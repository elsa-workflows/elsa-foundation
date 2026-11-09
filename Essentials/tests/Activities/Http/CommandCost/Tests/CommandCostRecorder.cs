using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>One EF command as it started: its statements, its transaction, the engine phase it ran in and its caller.</summary>
/// <param name="Dispatch">The command kind of the scheduler work item being dispatched, or <c>-</c> outside a dispatch.</param>
/// <param name="Checkpoint">The checkpoint being committed and what the persistence policy decided for it, or <c>-</c>.</param>
/// <param name="Store">The first Elsa frame on the stack: the store or boundary that issued the command.</param>
/// <param name="Caller">The first Elsa frame that is not persistence or coalescing infrastructure.</param>
/// <param name="Attribution">How the frames were obtained; see <see cref="CommandCostAttribution"/>.</param>
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
    string Attribution,
    string Sql);

/// <summary>Transaction and engine-phase counts at one point of a measurement, to subtract one point from another.</summary>
internal sealed record CommandCostCounts(
    int Commands,
    int TransactionsStarted,
    int TransactionsCommitted,
    int TransactionsRolledBack,
    int SavepointsCreated,
    int SavepointsReleased,
    int SavepointsRolledBack,
    int Drains,
    int Dispatches,
    int CheckpointsImmediate,
    int CheckpointsDeferred,
    int CheckpointsSkipped)
{
    public static readonly CommandCostCounts Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Statements EF sends around savepoints without passing through the command interceptor.</summary>
    public int SavepointStatements => SavepointsCreated + SavepointsReleased + SavepointsRolledBack;

    public CommandCostCounts Plus(CommandCostCounts other, int sign = 1) => new(
        Commands + sign * other.Commands,
        TransactionsStarted + sign * other.TransactionsStarted,
        TransactionsCommitted + sign * other.TransactionsCommitted,
        TransactionsRolledBack + sign * other.TransactionsRolledBack,
        SavepointsCreated + sign * other.SavepointsCreated,
        SavepointsReleased + sign * other.SavepointsReleased,
        SavepointsRolledBack + sign * other.SavepointsRolledBack,
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
internal sealed class CommandCostRecorder : DbCommandInterceptor, IDbTransactionInterceptor, ISaveChangesInterceptor, IDbConnectionInterceptor, IDisposable
{
    private const string CheckpointNameTag = "measure.checkpoint.name";

    private readonly ConcurrentQueue<CommandCostEntry> _commands = new();
    private readonly ConcurrentDictionary<DbTransaction, int> _transactions = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<DbContext, CommandCostAttribution> _saving = new(ReferenceEqualityComparer.Instance);
    private readonly AsyncLocal<CommandCostFlowMemory?> _flow = new();
    private readonly ActivitySource _source = new($"Elsa.CommandCost.{Guid.NewGuid():N}");
    private readonly ActivityListener _listener;
    private int _commandCount;
    private int _transactionsStarted;
    private int _transactionsCommitted;
    private int _transactionsRolledBack;
    private int _savepointsCreated;
    private int _savepointsReleased;
    private int _savepointsRolledBack;
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
    public IWorkflowEngineTracer Tracer => new NamingTracer(this, new ActivitySourceWorkflowEngineTracer(_source));

    /// <summary>
    /// Gives the calling asynchronous flow a memory of whole and stitched stacks to stitch truncated ones onto, unless it
    /// has one. Call it from the method that starts the work; the memory flows into everything that method awaits.
    /// </summary>
    public void BeginFlow() => _flow.Value ??= new CommandCostFlowMemory();

    public IReadOnlyList<CommandCostEntry> Commands => _commands.ToArray();

    public CommandCostCounts Counts => new(
        Volatile.Read(ref _commandCount),
        Volatile.Read(ref _transactionsStarted),
        Volatile.Read(ref _transactionsCommitted),
        Volatile.Read(ref _transactionsRolledBack),
        Volatile.Read(ref _savepointsCreated),
        Volatile.Read(ref _savepointsReleased),
        Volatile.Read(ref _savepointsRolledBack),
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

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) => Record(command, eventData, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, eventData, result));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) => Record(command, eventData, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, eventData, result));

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) => Record(command, eventData, result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, eventData, result));

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

    public void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) => Interlocked.Increment(ref _savepointsRolledBack);

    public Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _savepointsRolledBack);
        return Task.CompletedTask;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) => Saving(eventData, result);

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Saving(eventData, result));

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result) => Saved(eventData, result);

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Saved(eventData, result));

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Saved(eventData, 0);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Saved(eventData, 0);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData) => Saved(eventData, 0);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Saved(eventData, 0);
        return Task.CompletedTask;
    }

    public InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) => Opening(result);

    public ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Opening(result));

    /// <summary>
    /// Remembers the stack a connection is opened on, before the open's asynchronous hop. Taking a new physical connection
    /// from an empty PostgreSQL pool is real I/O, so the command that needed the connection runs after it on a stack that
    /// may have lost its callers, with no earlier whole stack in its flow to be stitched onto.
    /// </summary>
    private InterceptionResult Opening(InterceptionResult result)
    {
        Complete(CommandCostAttribution.Capture(new StackTrace(1, fNeedFileInfo: true)));
        return result;
    }

    /// <summary>
    /// Captures the stack <c>SaveChanges</c> was entered on, before EF's first asynchronous hop inside it. On PostgreSQL the
    /// savepoint EF sets inside an open transaction is a real round trip, so the batched writes after it run on a stack
    /// without a single Elsa frame; they are attributed to this call site instead.
    /// </summary>
    private InterceptionResult<int> Saving(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            _saving[context] = Complete(CommandCostAttribution.Capture(new StackTrace(1, fNeedFileInfo: true)));
        return result;
    }

    private int Saved(DbContextEventData eventData, int result)
    {
        if (eventData.Context is { } context)
            _saving.TryRemove(context, out _);
        return result;
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

    private T Record<T>(DbCommand command, CommandEventData eventData, T result)
    {
        var direct = CommandCostAttribution.Capture(new StackTrace(1, fNeedFileInfo: true));
        var attribution = !direct.HasCaller && eventData.Context is { } context && _saving.TryGetValue(context, out var save) && save.HasCaller
            ? save with { Source = "save-changes" }
            : Complete(direct);
        var (inDrain, dispatch, checkpoint) = EnginePhase();
        int? transaction = command.Transaction is { } current ? _transactions.GetOrAdd(current, _ => Interlocked.Increment(ref _transactionsStarted)) : null;
        _commands.Enqueue(new CommandCostEntry(
            Interlocked.Increment(ref _commandCount),
            SqlStatements.Parse(command.CommandText),
            transaction,
            inDrain,
            dispatch,
            checkpoint,
            attribution.Store,
            attribution.Caller,
            attribution.CallerLocation,
            attribution.Text,
            attribution.Source,
            command.CommandText));
        return result;
    }

    /// <summary>
    /// A whole stack is remembered for the flow; a stack that lost its callers is stitched onto the flow's latest whole one,
    /// or, when no whole one matches, its latest stitched one, and the result is remembered as a stitched stack.
    /// </summary>
    private CommandCostAttribution Complete(CommandCostAttribution attribution)
    {
        if (attribution.HasCaller)
        {
            _flow.Value?.Remember(attribution);
            return attribution;
        }

        if (_flow.Value is not { } flow || flow.Stitch(attribution) is not { } stitched)
            return attribution;
        flow.Remember(stitched);
        return stitched;
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

    /// <summary>
    /// The runtime's tracer, adding the checkpoint's name to its commit span so a command can say which checkpoint it
    /// served. Each span also gives its flow a stack memory when it has none, which covers work that does not start in
    /// the measured call's own flow, such as a request TestServer runs without the caller's execution context.
    /// </summary>
    private sealed class NamingTracer(CommandCostRecorder recorder, IWorkflowEngineTracer inner) : IWorkflowEngineTracer
    {
        public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request)
        {
            recorder.BeginFlow();
            return inner.StartDrainCycle(request);
        }

        public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem)
        {
            recorder.BeginFlow();
            return inner.StartDispatch(workItem);
        }

        public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem)
        {
            recorder.BeginFlow();
            return inner.StartActivityExecution(workItem);
        }

        public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit)
        {
            recorder.BeginFlow();
            return inner.StartCheckpointCommit(commit)?.SetTag(CheckpointNameTag, commit.Checkpoint.Name);
        }
    }
}
