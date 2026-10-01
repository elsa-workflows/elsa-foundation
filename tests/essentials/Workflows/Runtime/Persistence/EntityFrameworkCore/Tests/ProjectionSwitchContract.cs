using System.Data.Common;
using System.Globalization;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ProviderFailures;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// How an activation projection switch of either EF projection store, and the trigger-binding store's read of a
/// projection's state, retry (#2265): what they read again, what they report once their attempts run out, and what they
/// must not retry. Every scenario starts with <see cref="Candidate"/> prepared, with one row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Scenarios"/> run on every provider. The switch scenarios inject the race at a fixed point: a save that fails,
/// or a state moved inside the switch's own transaction. The state-read scenarios, for the trigger-binding store alone
/// (the schedule store answers from the state and reads no rows), let another context commit between two of the read's
/// statements, which run outside any transaction.
/// </para>
/// <para>
/// <see cref="InterleavedSwitchScenarios"/> let another context commit inside a switch's transaction. SQLite begins it with
/// <c>BEGIN IMMEDIATE</c>, so that cannot happen there, and these run on PostgreSQL.
/// </para>
/// </remarks>
internal static class ProjectionSwitchContract
{
    public const string TriggerBindings = "trigger-bindings";
    public const string RecurringSchedules = "recurring-schedules";
    private const string Candidate = "activation-a";
    private const string Other = "activation-b";
    private static readonly string[] BothStores = [TriggerBindings, RecurringSchedules];

    private static readonly Dictionary<string, Func<ProjectionStoreHarness, Task>> Switches = new()
    {
        ["a-switch-that-lost-its-write-reads-again-and-switches"] = async harness =>
        {
            var saves = new FailingSaveInterceptor(LostRace, failures: 1);

            Assert.Null((await SwitchAsync(harness, saves)).Failure);

            Assert.Equal(2, saves.Attempts);
            await AssertServesAsync(harness);
        },
        ["a-switch-that-keeps-losing-its-write-reports-it-changed-concurrently-and-rolls-back"] = async harness =>
        {
            var saves = new FailingSaveInterceptor(LostRace);

            var (failure, tracked) = await SwitchAsync(harness, saves);

            Assert.Equal($"{harness.Projection(Candidate)} changed concurrently; retry the operation.", Assert.IsType<InvalidOperationException>(failure).Message);
            Assert.IsType<DbUpdateConcurrencyException>(failure.InnerException);
            Assert.Equal((EfWriteRetry.DefaultMaxAttempts, 0), (saves.Attempts, tracked));
            await AssertPreparedAsync(harness);
        },
        // A deadlock victim's transaction is gone, so the switch reads again in a new one. Two switches that update the same
        // pair of projections in opposite orders, such as a completion and a failed sequence's restore, can deadlock.
        ["a-switch-chosen-as-a-deadlock-victim-retries-and-switches"] = async harness =>
        {
            var saves = FailingSaveInterceptor.WrappedDeadlock(failures: 1);

            Assert.Null((await SwitchAsync(harness, saves)).Failure);

            Assert.Equal(2, saves.Attempts);
            await AssertServesAsync(harness);
        },
        ["a-switch-that-keeps-deadlocking-reports-the-transient-conflict-and-rolls-back"] = async harness =>
        {
            var saves = FailingSaveInterceptor.WrappedDeadlock();

            var (failure, tracked) = await SwitchAsync(harness, saves);

            Assert.Equal($"{harness.Projection(Candidate)} encountered a transient write conflict; retry the operation.", Assert.IsType<InvalidOperationException>(failure).Message);
            Assert.Equal((EfWriteRetry.DefaultMaxAttempts, 0), (saves.Attempts, tracked));
            await AssertPreparedAsync(harness);
        },
        // The direction a wider retry would hide: a provider failure that is no write conflict is reported at once.
        ["a-switch-whose-provider-fails-otherwise-is-not-retried"] = async harness =>
        {
            var saves = FailingSaveInterceptor.WrappedProviderFailure();

            var (failure, tracked) = await SwitchAsync(harness, saves);

            Assert.Equal($"{harness.Projection(Candidate)} could not be committed.", Assert.IsType<InvalidOperationException>(failure).Message);
            Assert.Equal((1, 0), (saves.Attempts, tracked));
            await AssertPreparedAsync(harness);
        },
        // The state moves after the switch read it and before it reads the rows. The switch reads again before it writes
        // anything: a switch that wrote first would lose its write to the revision and also converge, with two saves.
        ["a-switch-whose-state-moved-while-it-read-reads-again-before-it-writes"] = async harness =>
        {
            var moves = MoveStatesBeforeRows(harness, times: 1);
            var saves = new SaveCounter();

            Assert.Null((await SwitchAsync(harness, moves, saves)).Failure);

            Assert.Equal((1, 1), (moves.Interleaved, saves.Saves));
            await AssertServesAsync(harness);
        },
        ["a-switch-whose-state-keeps-moving-while-it-reads-reports-it-changed-concurrently-and-rolls-back"] = async harness =>
        {
            var moves = MoveStatesBeforeRows(harness);
            var saves = new SaveCounter();

            var (failure, tracked) = await SwitchAsync(harness, moves, saves);

            Assert.Equal($"{harness.Projection(Candidate)} changed concurrently; retry the operation.", Assert.IsType<InvalidOperationException>(failure).Message);
            Assert.Null(failure.InnerException);
            Assert.Equal((EfWriteRetry.DefaultMaxAttempts, 0, 0), (moves.Interleaved, saves.Saves, tracked));
            await AssertPreparedAsync(harness);
        },
        // The direction a retry must not hide: rows that disagree with a state nothing moved are corrupt, reported as such
        // at once, not retried as a concurrent switch until the attempts run out.
        ["a-switch-whose-rows-disagree-with-a-state-that-stood-still-reports-the-projection-corrupt"] = async harness =>
        {
            await harness.DeleteRowsAsync(Candidate);

            var (failure, _) = await SwitchAsync(harness);

            Assert.StartsWith($"{harness.Projection(Candidate)} does not match its", Assert.IsType<InvalidDataException>(failure).Message, StringComparison.Ordinal);
        }
    };

    private static readonly Dictionary<string, Func<ProjectionStoreHarness, Task>> StateReads = new()
    {
        ["a-state-read-that-a-switch-interleaved-answers-after-it"] = async harness =>
        {
            var switches = await SwitchBackAndForthBeforeRowsAsync(harness, times: 1);
            await using var context = harness.Open(switches);

            Assert.Equal(WorkflowActivationProjectionState.Replaced, await harness.FindStateAsync(context, Candidate));

            Assert.Equal(1, switches.Interleaved);
        },
        ["a-state-read-that-switches-keep-interleaving-reports-it-changed-concurrently"] = async harness =>
        {
            var switches = await SwitchBackAndForthBeforeRowsAsync(harness);
            await using var context = harness.Open(switches);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.FindStateAsync(context, Candidate).AsTask());

            Assert.Equal($"{harness.Projection(Candidate)} changed concurrently; retry the operation.", failure.Message);
            Assert.Equal(EfWriteRetry.DefaultMaxAttempts, switches.Interleaved);
        },
        // The state read is of the first generation, its rows of the second. Both generations start at the creation
        // revision, so a read that compared revisions alone would report the new projection corrupt.
        ["a-state-read-across-a-projection-deleted-and-prepared-again-answers-for-the-new-one"] = async harness =>
        {
            var again = PrepareAgainAt(harness, inTransaction: false, ProjectionReadPoint.BeforeRows);
            await using var context = harness.Open(again);

            Assert.Equal(WorkflowActivationProjectionState.Prepared, await harness.FindStateAsync(context, Candidate));

            Assert.Equal(1, again.Interleaved);
            Assert.Equal([(2, false)], await harness.RowsAsync(Candidate));
        }
    };

    private static readonly Dictionary<string, Func<ProjectionStoreHarness, Task>> InterleavedSwitches = new()
    {
        // The switch read the first generation's state and the second's rows: comparing revisions alone, it would report
        // the new projection corrupt.
        ["a-switch-that-read-a-state-deleted-and-prepared-again-before-its-rows-switches-the-new-one"] = harness =>
            ASwitchAcrossAProjectionPreparedAgainAsync(harness, ProjectionReadPoint.BeforeRows),
        // The switch read the first generation's state and rows, then the second's state at the same revision: comparing
        // revisions alone, it would find nothing moved and write the first generation's content over the second's.
        ["a-switch-that-read-a-projection-deleted-and-prepared-again-switches-the-new-one"] = harness =>
            ASwitchAcrossAProjectionPreparedAgainAsync(harness, ProjectionReadPoint.BeforeStateAgain)
    };

    public static TheoryData<string, string> Scenarios => Cases((BothStores, Switches.Keys), ([TriggerBindings], StateReads.Keys));

    public static TheoryData<string, string> InterleavedSwitchScenarios => Cases((BothStores, InterleavedSwitches.Keys));

    /// <summary>Runs a scenario of either set against <paramref name="store"/>, over contexts <paramref name="open"/> starts with the given interceptors.</summary>
    public static async Task RunAsync(string store, string scenario, Func<IInterceptor[], RuntimeDbContext> open, string scope)
    {
        var harness = ProjectionStoreHarness.For(store, open, scope);
        await using (var context = harness.Open())
            await context.Database.EnsureCreatedAsync();
        await harness.PrepareAsync(Candidate);

        await (Switches.GetValueOrDefault(scenario) ?? StateReads.GetValueOrDefault(scenario) ?? InterleavedSwitches[scenario])(harness);
    }

    private static TheoryData<string, string> Cases(params (string[] Stores, IEnumerable<string> Scenarios)[] sets)
    {
        var data = new TheoryData<string, string>();
        foreach (var (stores, scenarios) in sets)
        foreach (var store in stores)
        foreach (var scenario in scenarios)
            data.Add(store, scenario);
        return data;
    }

    private static DbUpdateConcurrencyException LostRace() => new("Another writer moved a revision.");

    /// <summary>Switches <see cref="Candidate"/> on through a context that carries <paramref name="interceptors"/>: what it threw, and how many entries it still tracks.</summary>
    private static async Task<(Exception? Failure, int Tracked)> SwitchAsync(ProjectionStoreHarness harness, params IInterceptor[] interceptors)
    {
        await using var context = harness.Open(interceptors);
        var failure = await Record.ExceptionAsync(() => harness.ActivateAsync(context, Candidate).AsTask());
        return (failure, context.ChangeTracker.Entries().Count());
    }

    private static async Task ASwitchAcrossAProjectionPreparedAgainAsync(ProjectionStoreHarness harness, ProjectionReadPoint point)
    {
        var again = PrepareAgainAt(harness, inTransaction: true, point);
        await using var context = harness.Open(again);

        await harness.ActivateAsync(context, Candidate);

        Assert.Equal(1, again.Interleaved);
        await AssertServesAsync(harness, generation: 2);
    }

    /// <summary>Deletes <see cref="Candidate"/>'s projection and prepares it again with other content, once, at <paramref name="point"/>.</summary>
    private static InterleaveProjectionRead PrepareAgainAt(ProjectionStoreHarness harness, bool inTransaction, ProjectionReadPoint point) =>
        new(harness.StateTable, inTransaction, point, async _ =>
        {
            await harness.DeleteAsync(Candidate);
            await harness.PrepareAsync(Candidate, generation: 2);
        }, times: 1);

    /// <summary>Moves the states inside a switch's own transaction, between its read of a state and of its rows.</summary>
    private static InterleaveProjectionRead MoveStatesBeforeRows(ProjectionStoreHarness harness, int times = int.MaxValue) =>
        new(harness.StateTable, inTransaction: true, ProjectionReadPoint.BeforeRows, harness.MoveStatesAsync, times);

    /// <summary>
    /// Serves <see cref="Candidate"/> beside a prepared <see cref="Other"/>, and switches the slot from one to the other,
    /// through a context of its own, each time a state read reaches the rows.
    /// </summary>
    private static async Task<InterleaveProjectionRead> SwitchBackAndForthBeforeRowsAsync(ProjectionStoreHarness harness, int times = int.MaxValue)
    {
        await harness.PrepareAsync(Other);
        await harness.ActivateAsync(Candidate);
        var serving = Candidate;
        return new(harness.StateTable, inTransaction: false, ProjectionReadPoint.BeforeRows, async _ =>
        {
            var replaced = serving;
            serving = serving == Candidate ? Other : Candidate;
            await harness.ActivateAsync(serving, replaced);
        }, times);
    }

    private static async Task AssertServesAsync(ProjectionStoreHarness harness, int generation = 1)
    {
        Assert.Equal(WorkflowActivationProjectionState.Active, await harness.FindStateAsync(Candidate));
        Assert.Equal([(generation, true)], await harness.RowsAsync(Candidate));
    }

    private static async Task AssertPreparedAsync(ProjectionStoreHarness harness)
    {
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await harness.FindStateAsync(Candidate));
        Assert.Equal([(1, false)], await harness.RowsAsync(Candidate));
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Saves { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Where <see cref="InterleaveProjectionRead"/> lets another writer in.</summary>
internal enum ProjectionReadPoint
{
    /// <summary>After a projection's state was read, before its rows are.</summary>
    BeforeRows,

    /// <summary>After a projection's rows were read, before its state is read again.</summary>
    BeforeStateAgain
}

/// <summary>
/// Runs <c>interleave</c> at <c>point</c> of a projection read, at most <c>times</c> times. A read of <c>stateTable</c> is
/// a state read and any other a rows read; only reads inside a transaction count, or only reads outside one, as
/// <c>inTransaction</c> says: a switch reads inside its own, a state read and a completion's reads before it switches
/// outside any.
/// </summary>
internal sealed class InterleaveProjectionRead(
    string stateTable,
    bool inTransaction,
    ProjectionReadPoint point,
    Func<DbCommand, Task> interleave,
    int times = int.MaxValue) : DbCommandInterceptor
{
    private bool? _previousWasStateRead;

    public int Interleaved { get; private set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if ((command.Transaction is not null) != inTransaction)
            return result;
        var stateRead = command.CommandText.Contains(stateTable, StringComparison.Ordinal);
        if (_previousWasStateRead == !stateRead && stateRead == (point == ProjectionReadPoint.BeforeStateAgain) && Interleaved < times)
        {
            Interleaved++;
            await interleave(command);
        }
        _previousWasStateRead = stateRead;
        return result;
    }
}

/// <summary>
/// One EF projection store over one database and persistence scope, as <see cref="ProjectionSwitchContract"/> drives it.
/// Each activation it prepares has one row, whose content carries a generation, so a projection prepared again with other
/// content can be told from the first.
/// </summary>
internal abstract class ProjectionStoreHarness(Func<IInterceptor[], RuntimeDbContext> open, string scope)
{
    protected static readonly DateTimeOffset NextOccurrence = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    public static ProjectionStoreHarness For(string store, Func<IInterceptor[], RuntimeDbContext> open, string scope) => store switch
    {
        ProjectionSwitchContract.TriggerBindings => new TriggerBindingHarness(open, scope),
        ProjectionSwitchContract.RecurringSchedules => new RecurringScheduleHarness(open, scope),
        _ => throw new ArgumentOutOfRangeException(nameof(store), store, null)
    };

    public abstract string StateTable { get; }

    /// <summary>How the store names an activation's projection in its failures.</summary>
    public abstract string Projection(string activationId);

    protected string ScopeKey { get; } = EfRelationalIdentity.Encode(scope);

    public RuntimeDbContext Open(params IInterceptor[] interceptors) => open(interceptors);

    public ValueTask ActivateAsync(RuntimeDbContext context, string activationId, string? replacedActivationId = null) =>
        ActivateAsync(Stores(context), activationId, replacedActivationId);

    public ValueTask<WorkflowActivationProjectionState> FindStateAsync(RuntimeDbContext context, string activationId) =>
        FindStateAsync(Stores(context), activationId);

    public Task PrepareAsync(string activationId, int generation = 1) => ThroughAsync(context => PrepareAsync(Stores(context), activationId, generation).AsTask());

    public Task ActivateAsync(string activationId, string? replacedActivationId = null) => ThroughAsync(context => ActivateAsync(context, activationId, replacedActivationId).AsTask());

    public Task<WorkflowActivationProjectionState> FindStateAsync(string activationId) => ThroughAsync(context => FindStateAsync(context, activationId).AsTask());

    public Task DeleteAsync(string activationId) => ThroughAsync(context => DeleteAsync(Stores(context), activationId).AsTask());

    /// <summary>The generation and serving flag of each of the activation's rows.</summary>
    public Task<(int Generation, bool IsActive)[]> RowsAsync(string activationId) => ThroughAsync(context => RowsAsync(Stores(context), activationId));

    /// <summary>Deletes the activation's rows and leaves its state as it stands.</summary>
    public Task DeleteRowsAsync(string activationId) => ThroughAsync(context => DeleteRowsAsync(context, EfRelationalIdentity.Encode(activationId)));

    /// <summary>Moves every projection state of the scope on a revision, switching nothing, inside the transaction <paramref name="command"/> runs in.</summary>
    public async Task MoveStatesAsync(DbCommand command)
    {
        await using var context = Open();
        context.Database.SetDbConnection(command.Connection);
        await context.Database.UseTransactionAsync(command.Transaction);
        await MoveRevisionsAsync(context);
    }

    protected abstract ValueTask PrepareAsync(ActivationStores stores, string activationId, int generation);
    protected abstract ValueTask ActivateAsync(ActivationStores stores, string activationId, string? replacedActivationId);
    protected abstract ValueTask<WorkflowActivationProjectionState> FindStateAsync(ActivationStores stores, string activationId);
    protected abstract ValueTask DeleteAsync(ActivationStores stores, string activationId);
    protected abstract Task<(int Generation, bool IsActive)[]> RowsAsync(ActivationStores stores, string activationId);
    protected abstract Task DeleteRowsAsync(RuntimeDbContext context, string encodedActivationId);
    protected abstract Task MoveRevisionsAsync(RuntimeDbContext context);

    private ActivationStores Stores(RuntimeDbContext context) => ActivationStores.EntityFramework(context, scope);

    private async Task ThroughAsync(Func<RuntimeDbContext, Task> run)
    {
        await using var context = Open();
        await run(context);
    }

    private async Task<T> ThroughAsync<T>(Func<RuntimeDbContext, Task<T>> run)
    {
        await using var context = Open();
        return await run(context);
    }

    private sealed class TriggerBindingHarness(Func<IInterceptor[], RuntimeDbContext> open, string scope) : ProjectionStoreHarness(open, scope)
    {
        private const string GenerationKey = "generation";

        public override string StateTable => RuntimeTriggerBindingEfModule.ProjectionStateTableName;

        public override string Projection(string activationId) => $"Trigger-binding activation projection '{activationId}'";

        protected override ValueTask PrepareAsync(ActivationStores stores, string activationId, int generation) =>
            stores.Bindings.PrepareActivationAsync(activationId,
            [
                new(WorkflowTriggerBinding.BuildId(activationId, "artifact-a", "node-a", "hash-a"), "artifact-a", "definition-a", "1", "artifact-hash",
                    "node-a", "Event", "hash-a", null, new Dictionary<string, string> { [GenerationKey] = generation.ToString(CultureInfo.InvariantCulture) },
                    DateTimeOffset.UnixEpoch, activationId, "slot-a")
            ]);

        protected override ValueTask ActivateAsync(ActivationStores stores, string activationId, string? replacedActivationId) =>
            stores.Bindings.ActivateAsync(activationId, replacedActivationId);

        protected override ValueTask<WorkflowActivationProjectionState> FindStateAsync(ActivationStores stores, string activationId) =>
            stores.Bindings.FindActivationStateAsync(activationId);

        protected override ValueTask DeleteAsync(ActivationStores stores, string activationId) => stores.Bindings.DeleteByActivationAsync(activationId);

        protected override async Task<(int Generation, bool IsActive)[]> RowsAsync(ActivationStores stores, string activationId) =>
            (await stores.Bindings.ListByActivationAsync(new WorkflowTriggerBindingActivationPageQuery(activationId))).Items
            .Select(binding => (int.Parse(binding.Metadata[GenerationKey], CultureInfo.InvariantCulture), binding.IsActive))
            .ToArray();

        protected override Task DeleteRowsAsync(RuntimeDbContext context, string encodedActivationId) =>
            context.WorkflowTriggerBindings.Where(x => x.ScopeKey == ScopeKey && x.ActivationId == encodedActivationId).ExecuteDeleteAsync();

        protected override Task MoveRevisionsAsync(RuntimeDbContext context) =>
            context.WorkflowTriggerBindingProjectionStates.Where(x => x.ScopeKey == ScopeKey).ExecuteUpdateAsync(state => state.SetProperty(x => x.Revision, x => x.Revision + 1));
    }

    private sealed class RecurringScheduleHarness(Func<IInterceptor[], RuntimeDbContext> open, string scope) : ProjectionStoreHarness(open, scope)
    {
        public override string StateTable => RuntimeOperationalStateEfModule.RecurringScheduleProjectionStateTableName;

        public override string Projection(string activationId) => $"Recurring-schedule activation projection '{activationId}'";

        // The generation is the interval in minutes, so a second generation differs in the content its fingerprints cover.
        protected override ValueTask PrepareAsync(ActivationStores stores, string activationId, int generation) =>
            stores.Schedules.PrepareActivationAsync(activationId,
            [
                new(RecurringTriggerSchedule.BuildId(activationId, "artifact-a", "node-a"), "artifact-a", "node-a", "Timer", "hash-a",
                    RecurringScheduleKind.Interval, $"PT{generation}M", NextOccurrence, DateTimeOffset.UnixEpoch, activationId, "slot-a")
            ]);

        protected override ValueTask ActivateAsync(ActivationStores stores, string activationId, string? replacedActivationId) =>
            stores.Schedules.ActivateAsync(activationId, replacedActivationId);

        protected override ValueTask<WorkflowActivationProjectionState> FindStateAsync(ActivationStores stores, string activationId) =>
            stores.Schedules.FindActivationStateAsync(activationId);

        protected override ValueTask DeleteAsync(ActivationStores stores, string activationId) => stores.Schedules.DeleteByActivationAsync(activationId);

        protected override async Task<(int Generation, bool IsActive)[]> RowsAsync(ActivationStores stores, string activationId) =>
            (await stores.Schedules.ListByActivationAsync(activationId))
            .Select(schedule => (int.Parse(schedule.Expression[2..^1], CultureInfo.InvariantCulture), schedule.IsActive))
            .ToArray();

        protected override Task DeleteRowsAsync(RuntimeDbContext context, string encodedActivationId) =>
            context.RecurringTriggerSchedules.Where(x => x.ScopeKey == ScopeKey && x.ActivationId == encodedActivationId).ExecuteDeleteAsync();

        protected override Task MoveRevisionsAsync(RuntimeDbContext context) =>
            context.RecurringTriggerScheduleProjectionStates.Where(x => x.ScopeKey == ScopeKey).ExecuteUpdateAsync(state => state.SetProperty(x => x.Revision, x => x.Revision + 1));
    }
}
