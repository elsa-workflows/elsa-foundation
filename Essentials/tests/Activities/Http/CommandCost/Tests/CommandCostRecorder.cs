using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>One EF command as it started: its statements, its transaction, the engine phase it ran in and its caller.</summary>
/// <param name="Dispatch">The command kind of the scheduler work item being dispatched, or <c>-</c> outside a dispatch.</param>
/// <param name="Checkpoint">The checkpoint being committed and what the persistence policy decided for it, or <c>-</c>.</param>
/// <param name="Store">The first Elsa frame on the stack: the store or boundary that issued the command.</param>
/// <param name="Caller">The first Elsa frame that is not persistence or coalescing infrastructure.</param>
internal sealed record CommandCostEntry(
    int Ordinal,
    IReadOnlyList<SqlStatement> Statements,
    int? Transaction,
    bool InDrain,
    string Dispatch,
    string Checkpoint,
    string Store,
    string Caller,
    string CallerLocation,
    string Frames,
    string Sql);

/// <summary>Transaction and engine-phase counts at one point of a measurement, to subtract one point from another.</summary>
internal sealed record CommandCostCounters(
    int Commands,
    int TransactionsStarted,
    int TransactionsCommitted,
    int TransactionsRolledBack,
    int SavepointsCreated,
    int SavepointsReleased,
    int Drains,
    int Dispatches,
    int CheckpointsImmediate,
    int CheckpointsDeferred,
    int CheckpointsSkipped)
{
    public static readonly CommandCostCounters Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public CommandCostCounters Plus(CommandCostCounters other, int sign = 1) => new(
        Commands + sign * other.Commands,
        TransactionsStarted + sign * other.TransactionsStarted,
        TransactionsCommitted + sign * other.TransactionsCommitted,
        TransactionsRolledBack + sign * other.TransactionsRolledBack,
        SavepointsCreated + sign * other.SavepointsCreated,
        SavepointsReleased + sign * other.SavepointsReleased,
        Drains + sign * other.Drains,
        Dispatches + sign * other.Dispatches,
        CheckpointsImmediate + sign * other.CheckpointsImmediate,
        CheckpointsDeferred + sign * other.CheckpointsDeferred,
        CheckpointsSkipped + sign * other.CheckpointsSkipped);
}

/// <summary>
/// Records every command and transaction event of the runtime's EF context, and attributes each command to the engine
/// phase it ran in through the runtime's own tracing spans. Each recorder owns a private activity source, so two
/// measurements never see each other's spans. Capturing a stack with file information is slow; this is a measurement
/// tool and is never composed into a production path.
/// </summary>
internal sealed partial class CommandCostRecorder : DbCommandInterceptor, IDbTransactionInterceptor, IDisposable
{
    private const string CheckpointNameTag = "measure.checkpoint.name";

    /// <summary>
    /// Frames that only carry a command to the database: EF stores and their helpers, store decorators, the coalescing
    /// store overlays and paging extensions. The caller of a command is the first frame past them. The EF checkpoint
    /// participants are the exception: inside a commit they decide which rows are read and written, so they own those
    /// statements rather than the lease wrapper that happens to sit above them.
    /// </summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "Elsa.Persistence.EntityFramework",
        "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore"
    ];

    private static readonly string[] InfrastructureSuffixes = ["Store", "Decorator", "Boundary", "Extensions"];

    private readonly ConcurrentQueue<CommandCostEntry> _commands = new();
    private readonly ConcurrentDictionary<DbTransaction, int> _transactions = new(ReferenceEqualityComparer.Instance);
    private readonly ActivitySource _source = new($"Elsa.CommandCost.{Guid.NewGuid():N}");
    private readonly ActivityListener _listener;
    private int _commandCount;
    private int _transactionsStarted;
    private int _transactionsCommitted;
    private int _transactionsRolledBack;
    private int _savepointsCreated;
    private int _savepointsReleased;
    private int _drains;
    private int _dispatches;
    private int _checkpointsImmediate;
    private int _checkpointsDeferred;
    private int _checkpointsSkipped;

    public CommandCostRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, _source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>A tracer for the measured host: the runtime's own spans, on this recorder's source, with the checkpoint name added.</summary>
    public IWorkflowEngineTracer Tracer => new NamingTracer(new ActivitySourceWorkflowEngineTracer(_source));

    public IReadOnlyList<CommandCostEntry> Commands => _commands.ToArray();

    public CommandCostCounters Counters => new(
        Volatile.Read(ref _commandCount),
        Volatile.Read(ref _transactionsStarted),
        Volatile.Read(ref _transactionsCommitted),
        Volatile.Read(ref _transactionsRolledBack),
        Volatile.Read(ref _savepointsCreated),
        Volatile.Read(ref _savepointsReleased),
        Volatile.Read(ref _drains),
        Volatile.Read(ref _dispatches),
        Volatile.Read(ref _checkpointsImmediate),
        Volatile.Read(ref _checkpointsDeferred),
        Volatile.Read(ref _checkpointsSkipped));

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) => Record(command, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, result));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) => Record(command, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, result));

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) => Record(command, result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, result));

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) => Started(result);

    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Started(result));

    public DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result) => Started(result);

    public ValueTask<DbTransaction> TransactionUsedAsync(DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Started(result));

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Ended(transaction, ref _transactionsCommitted);

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Ended(transaction, ref _transactionsCommitted);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Ended(transaction, ref _transactionsRolledBack);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Ended(transaction, ref _transactionsRolledBack);
        return Task.CompletedTask;
    }

    public void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) => Interlocked.Increment(ref _savepointsCreated);

    public Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _savepointsCreated);
        return Task.CompletedTask;
    }

    public void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) => Interlocked.Increment(ref _savepointsReleased);

    public Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _savepointsReleased);
        return Task.CompletedTask;
    }

    private DbTransaction Started(DbTransaction transaction)
    {
        _transactions.GetOrAdd(transaction, _ => Interlocked.Increment(ref _transactionsStarted));
        return transaction;
    }

    /// <summary>
    /// Counts a transaction's end and forgets its object. Npgsql hands out the same <c>NpgsqlTransaction</c> for every
    /// transaction on one physical connection, so an id keyed by the object must end with the transaction, or the next
    /// transaction on that connection would inherit it.
    /// </summary>
    private void Ended(DbTransaction transaction, ref int counter)
    {
        _transactions.TryRemove(transaction, out _);
        Interlocked.Increment(ref counter);
    }

    private T Record<T>(DbCommand command, T result)
    {
        var (store, caller, location, frames) = Attribute(new StackTrace(1, fNeedFileInfo: true));
        var (inDrain, dispatch, checkpoint) = EnginePhase();
        int? transaction = command.Transaction is { } current ? _transactions.GetOrAdd(current, _ => Interlocked.Increment(ref _transactionsStarted)) : null;
        _commands.Enqueue(new CommandCostEntry(
            Interlocked.Increment(ref _commandCount),
            SqlStatements.Parse(command.CommandText),
            transaction,
            inDrain,
            dispatch,
            checkpoint,
            store,
            caller,
            location,
            frames,
            command.CommandText));
        return result;
    }

    private (bool InDrain, string Dispatch, string Checkpoint) EnginePhase()
    {
        string? dispatch = null;
        string? checkpoint = null;
        var inDrain = false;
        for (var activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (!ReferenceEquals(activity.Source, _source))
                continue;

            switch (activity.OperationName)
            {
                case WorkflowEngineTelemetry.CheckpointCommitSpanName:
                    checkpoint ??= $"{activity.GetTagItem(CheckpointNameTag)}/{activity.GetTagItem(WorkflowEngineTelemetry.CheckpointPersistenceDecisionTag) ?? "undecided"}";
                    break;
                case WorkflowEngineTelemetry.DispatchSpanName:
                    dispatch ??= activity.GetTagItem(WorkflowEngineTelemetry.CommandKindTag) as string;
                    break;
                case WorkflowEngineTelemetry.DrainSpanName:
                    inDrain = true;
                    break;
            }
        }

        return (inDrain, dispatch ?? "-", checkpoint ?? "-");
    }

    private void OnStopped(Activity activity)
    {
        switch (activity.OperationName)
        {
            case WorkflowEngineTelemetry.DrainSpanName:
                Interlocked.Increment(ref _drains);
                break;
            case WorkflowEngineTelemetry.DispatchSpanName:
                Interlocked.Increment(ref _dispatches);
                break;
            case WorkflowEngineTelemetry.CheckpointCommitSpanName:
                switch (activity.GetTagItem(WorkflowEngineTelemetry.CheckpointPersistenceDecisionTag) as string)
                {
                    case nameof(RuntimeCheckpointPersistenceMode.Deferred):
                        Interlocked.Increment(ref _checkpointsDeferred);
                        break;
                    case nameof(RuntimeCheckpointPersistenceMode.Skip):
                        Interlocked.Increment(ref _checkpointsSkipped);
                        break;
                    default:
                        Interlocked.Increment(ref _checkpointsImmediate);
                        break;
                }
                break;
        }
    }

    private static (string Store, string Caller, string Location, string Frames) Attribute(StackTrace trace)
    {
        string? store = null;
        string? caller = null;
        var location = "-";
        var names = new List<string>();
        foreach (var frame in trace.GetFrames())
        {
            if (frame.GetMethod() is not { DeclaringType: { } type } method)
                continue;
            var owner = Owner(type);
            if (owner.FullName is not { } fullName || !fullName.StartsWith("Elsa.", StringComparison.Ordinal) ||
                fullName.StartsWith("Elsa.Activities.Http.CommandCost.", StringComparison.Ordinal))
                continue;

            var name = $"{owner.Name}.{LogicalMethodName(type, method)}";
            if (names.Count == 0 || names[^1] != name)
                names.Add(name);
            store ??= name;
            if (caller is null && !IsInfrastructure(owner))
            {
                caller = name;
                location = Location(frame);
            }
        }

        return (store ?? "?", caller ?? "?", location, string.Join(" < ", names.Take(14)));
    }

    private static Type Owner(Type type)
    {
        while (type.DeclaringType is { } declaring && (type.Name.StartsWith('<') || type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
            type = declaring;
        return type;
    }

    private static string LogicalMethodName(Type type, MethodBase method)
    {
        var generated = string.Join(' ', GeneratedNames(type).Append(method.Name));
        var outer = OuterMethod().Match(generated);
        if (!outer.Success)
            return method.Name;
        var local = LocalFunction().Match(generated);
        return local.Success ? $"{outer.Groups["name"].Value}.{local.Groups["name"].Value}" : outer.Groups["name"].Value;
    }

    private static IEnumerable<string> GeneratedNames(Type type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null && current.Name.StartsWith('<'); current = current.DeclaringType)
            names.Push(current.Name);
        return names;
    }

    private static bool IsInfrastructure(Type owner) =>
        !(owner.Name.StartsWith("EfRuntimeCheckpoint", StringComparison.Ordinal) && owner.Name != "EfRuntimeCheckpointParticipantRows") &&
        (InfrastructureNamespaces.Any(prefix => owner.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
         owner.Name.StartsWith("Coalescing", StringComparison.Ordinal) ||
         InfrastructureSuffixes.Any(suffix => owner.Name.EndsWith(suffix, StringComparison.Ordinal)));

    private static string Location(StackFrame frame)
    {
        if (frame.GetFileName() is not { } file)
            return "-";
        var relative = file.IndexOf("/Essentials/", StringComparison.Ordinal);
        return $"{(relative >= 0 ? file[(relative + 1)..] : Path.GetFileName(file))}:{frame.GetFileLineNumber()}";
    }

    [GeneratedRegex("<(?<name>[A-Za-z_][A-Za-z0-9_]*)>")]
    private static partial Regex OuterMethod();

    [GeneratedRegex(@"g__(?<name>[A-Za-z_][A-Za-z0-9_]*)\|")]
    private static partial Regex LocalFunction();

    /// <summary>The runtime's tracer, adding the checkpoint's name to its commit span so a command can say which checkpoint it served.</summary>
    private sealed class NamingTracer(IWorkflowEngineTracer inner) : IWorkflowEngineTracer
    {
        public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request) => inner.StartDrainCycle(request);

        public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem) => inner.StartDispatch(workItem);

        public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem) => inner.StartActivityExecution(workItem);

        public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit) =>
            inner.StartCheckpointCommit(commit)?.SetTag(CheckpointNameTag, commit.Checkpoint.Name);
    }
}
