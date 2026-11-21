using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

internal static partial class RuntimeHistoryCheckpointProviderContract
{
    private static async Task TimerCapturedRoundtrip(Database database)
    {
        const string run = "timer-captured-roundtrip";
        var access = new Access();
        var at = database.Clock.GetUtcNow();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, DurableTimerHistoryFacts.Kinds);
        var timer = new DurableTimer("captured", run, "Timer", "stimulus", at.AddMinutes(1), at);
        var scheduled = DurableTimerHistoryFacts.CreateScheduled(timer, policy)!.Event;
        await using (var producer = database.Open())
        {
            await database.Owner(producer, "timer-owner").AcquireAsync(run);
            var store = new EfDurableTimerStore(producer, access, new Codec(), database.Clock);
            await store.SaveAsync(timer with { HistoryResponsibility = new(policy, [scheduled], false) });
        }
        await using (var restarted = database.Open())
        {
            var store = new EfDurableTimerStore(restarted, access, new Codec(), database.Clock);
            var retained = await store.FindAsync(run, timer.TimerId);
            Assert.NotNull(retained);
            Assert.Equal(timer.DueTime, retained.DueTime);
            Assert.Equal(scheduled.HistoryEventId, Assert.Single(retained.HistoryResponsibility!.PendingEvents).HistoryEventId);
            Assert.False(retained.HistoryResponsibility.Retired);
            await store.DeleteAsync(run, timer.TimerId);
        }
        await using var verification = database.Open();
        var current = new EfDurableTimerStore(verification, access, new Codec(), database.Clock);
        Assert.Null(await current.FindAsync(run, timer.TimerId));
        var retired = Assert.Single((await current.ListPendingHistoryAsync(new())).Items);
        Assert.True(retired.HistoryResponsibility!.Retired);
        Assert.Equal(2, retired.HistoryResponsibility.PendingEvents.Count);
        Assert.Equal(scheduled.HistoryEventId, retired.HistoryResponsibility.PendingEvents[0].HistoryEventId);
    }

    private static async Task TimerAlternateSchema(Database database)
    {
        const string run = "timer-alternate-schema";
        await using var canonical = database.Open();
        await database.Owner(canonical, "schema-owner").AcquireAsync(run);
        var at = database.Clock.GetUtcNow();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, DurableTimerHistoryFacts.Kinds);
        var timer = new DurableTimer("active", run, "Timer", "stimulus", at.AddMinutes(1), at);
        var source = new EfDurableTimerStore(canonical, new Access(), new Codec(), database.Clock);
        await source.SaveAsync(timer with { HistoryResponsibility = new(policy, [DurableTimerHistoryFacts.CreateScheduled(timer, policy)!.Event], false) });
        await canonical.Database.ExecuteSqlRawAsync("CREATE SCHEMA history_hidden");
        await canonical.Database.ExecuteSqlRawAsync("CREATE TABLE history_hidden.elsa_runtime_durable_timer (LIKE public.elsa_runtime_durable_timer INCLUDING ALL)");
        await using var hidden = new AlternateTimerSchemaContext(new DbContextOptionsBuilder<AlternateTimerSchemaContext>().UseNpgsql(database.Connection).Options);
        var store = new EfDurableTimerStore(hidden, new Access(), new Codec(), database.Clock);
        var supported = store.SupportsHistoryResponsibility;
        var failure = await Record.ExceptionAsync(() => store.DeleteAsync(run, "active").AsTask());
        var retained = await source.FindAsync(run, "active"); Assert.NotNull(retained);
        Assert.False(retained.HistoryResponsibility!.Retired); Assert.Single(retained.HistoryResponsibility.PendingEvents);
        Assert.IsType<RuntimeSchedulerWorkUnavailableException>(failure); Assert.False(supported);
    }

    private sealed class AlternateTimerSchemaContext(DbContextOptions<AlternateTimerSchemaContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model)
        {
            model.Entity<DurableTimerEntity>().ToTable("elsa_runtime_durable_timer", "history_hidden");
        }
    }

    private static async Task TimerCallerSnapshot(Database database, bool probe = false, bool native = false)
    {
        const string run = "timer-repeatable-read-run";
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, DurableTimerHistoryFacts.Kinds);
        var access = new Access();
        await using (var seed = database.Open()) await database.Owner(seed, "timer-owner").AcquireAsync(run);
        await using var caller = database.Open();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var key = EfRuntimeOperationalStoreSupport.CompositeId(Scope, run, Elsa.Workflows.Runtime.Services.Executions.RuntimeExecutionOwnershipStateId.For(run));
        var old = await caller.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.Id == key);
        Assert.Null(old.TimerHistoryOwnerJson); // Establish the caller's actual pre-owner InnoDB snapshot.
        var now = database.Clock.GetUtcNow();
        var timer = new DurableTimer("first", run, "Timer", "timer-stimulus", now.AddMinutes(1), now);
        await using (var producer = database.Open())
        {
            var source = new EfDurableTimerStore(producer, access, new Codec(), database.Clock);
            await source.SaveAsync(timer with { HistoryResponsibility = new(policy, [DurableTimerHistoryFacts.CreateScheduled(timer, policy)!.Event], false) });
        }
        Assert.Null((await caller.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.Id == key)).TimerHistoryOwnerJson);
        if (native)
        {
            var proposed = new WorkflowExecutionState(run, new("root", "definition", "version", "1", "hash"),
                WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, Scope, new Dictionary<string, string>())
                { HistoryCaptureEnabledAtAdmission = false };
            var nativeStates = new EfWorkflowExecutionStateStore(caller, access, new Codec());
            var nativeFailure = await Record.ExceptionAsync(() => nativeStates.SaveAsync(proposed).AsTask());
            if (nativeFailure is null) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            await using var nativeVerification = database.Open();
            Assert.Null(await new EfWorkflowExecutionStateStore(nativeVerification, access, new Codec()).FindAsync(run));
            Assert.IsType<NotSupportedException>(nativeFailure);
            Assert.True(await new EfDurableTimerStore(nativeVerification, access, new Codec(), database.Clock).HasPendingHistoryAsync(run));
            return;
        }
        if (probe)
        {
            bool? requires = null;
            var probeFailure = await Record.ExceptionAsync(async () => requires = await ((IRuntimeCheckpointCommitStore)database.Store(caller))
                .RequiresUnrecordedTimerDischargeAsync(run));
            await transaction.RollbackAsync();
            Assert.Null(requires); // Stale false absence must never reach the coalescing discard decision.
            Assert.IsType<RuntimeSchedulerWorkUnavailableException>(probeFailure);
            await using var verified = database.Open();
            Assert.True(await new EfDurableTimerStore(verified, access, new Codec(), database.Clock).HasPendingHistoryAsync(run));
            return;
        }
        var legacy = new EfDurableTimerStore(caller, access, new Codec(), database.Clock);
        var failure = await Record.ExceptionAsync(() => legacy.SaveAsync(timer with { TimerId = "legacy" }).AsTask());
        if (failure is null) await transaction.CommitAsync(); else await transaction.RollbackAsync();
        await using var current = database.Open(); var currentStore = new EfDurableTimerStore(current, access, new Codec(), database.Clock);
        Assert.Null(await currentStore.FindAsync(run, "legacy"));
        Assert.IsType<RuntimeSchedulerWorkUnavailableException>(failure);
        Assert.Single((await currentStore.ListPendingHistoryAsync(new())).Items);
    }
    private static async Task TimerPendingParticipants(Database database)
    {
        const string run = "timer-pending-native";
        await using var context = database.Open();
        var at = database.Clock.GetUtcNow();
        var state = new WorkflowExecutionState(run, new("root", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running, null, at, at, at, null, null, null, Scope, new Dictionary<string, string>());
        var pending = EfWorkflowExecutionStateStore.ToEntity(state, Scope, EfWorkflowExecutionStateStore.CreateId(Scope, run), 1);
        context.WorkflowExecutionStates.Add(pending);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await EfTimerHistoryOwner.CoordinateFirstNativeAsync(context, Scope, run, default);
            Assert.Equal(EntityState.Added, context.Entry(pending).State);
            Assert.False(await context.WorkflowExecutionStates.AsNoTracking().AnyAsync(row => row.Id == pending.Id));
            Assert.NotNull(await context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == EfTimerHistoryOwner.Id(Scope, run)));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.False(await context.WorkflowExecutionStates.AsNoTracking().AnyAsync(row => row.Id == pending.Id));
        Assert.Null(await context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == EfTimerHistoryOwner.Id(Scope, run)));
    }

    private static async Task TimerNativeAffectedRows(Database database, bool existingOwner)
    {
        const string run = "timer-changed-rows-native";
        var access = new Access(); var now = database.Clock.GetUtcNow();
        await using var context = database.Open();
        if (existingOwner) await database.Owner(context, "legacy-owner").AcquireAsync(run);
        var state = new WorkflowExecutionState(run, new("root", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, Scope, new Dictionary<string, string>())
            { HistoryCaptureEnabledAtAdmission = false };
        await new EfWorkflowExecutionStateStore(context, access, new Codec()).SaveAsync(state);
        await using var restarted = database.Open();
        var retained = await new EfWorkflowExecutionStateStore(restarted, access, new Codec()).FindAsync(run);
        Assert.NotNull(retained); Assert.False(retained.HistoryCaptureEnabledAtAdmission); Assert.Null(retained.HistoryCapturePolicy);
        Assert.False(await new EfDurableTimerStore(restarted, access, new Codec(), database.Clock).HasPendingHistoryAsync(run));
    }

    private static async Task TimerFirstOwnerRace(Database database, bool checkpoint = false)
    {
        const string run = "timer-initial-owner-race";
        var held = new TimerNativeInsertHold(); var access = new Access(); var now = database.Clock.GetUtcNow();
        await using var nativeContext = database.Open(held);
        var state = new WorkflowExecutionState(run, new("root", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, Scope, new Dictionary<string, string>())
            { HistoryCaptureEnabledAtAdmission = false };
        async Task SaveNative()
        {
            if (!checkpoint) { await new EfWorkflowExecutionStateStore(nativeContext, access, new Codec()).SaveAsync(state); return; }
            var commit = new RuntimeCheckpointCommit("unfenced-initial", new("unfenced-initial", "initial", run, now, [], new Dictionary<string, string>()),
                new(new(run, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>());
            Assert.Null(commit.ExpectedFence);
            await new EfRuntimeCheckpointCommitStore(nativeContext, access, database.Clock, new TimerRootLease()).CommitAsync(commit, Immediate);
        }
        var saving = SaveNative();
        Exception? sourceFailure = null;
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using var producer = database.Open();
            await producer.Database.OpenConnectionAsync();
            // The database's lock refusal completes the competing attempt while A remains held;
            // no sleep or timing-shaped assertion decides which writer won.
            await producer.Database.ExecuteSqlRawAsync("SET lock_timeout = '750ms'");
            sourceFailure = await Record.ExceptionAsync(async () =>
            {
                await database.Owner(producer, "first-timer-owner").AcquireAsync(run);
                var timer = new DurableTimer("first", run, "Timer", "timer-stimulus", now.AddMinutes(1), now);
                var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, DurableTimerHistoryFacts.Kinds);
                await new EfDurableTimerStore(producer, access, new Codec(), database.Clock).SaveAsync(timer with
                    { HistoryResponsibility = new(policy, [DurableTimerHistoryFacts.CreateScheduled(timer, policy)!.Event], false) });
            });
        }
        finally { held.Proceed.TrySetResult(); }
        await saving;
        await using var current = database.Open();
        var retained = await new EfWorkflowExecutionStateStore(current, access, new Codec()).FindAsync(run);
        Assert.NotNull(retained); Assert.False(retained.HistoryCaptureEnabledAtAdmission); Assert.Null(retained.HistoryCapturePolicy);
        Assert.Null(await new EfDurableTimerStore(current, access, new Codec(), database.Clock).FindAsync(run, "first"));
        Assert.NotNull(sourceFailure); Assert.True(IsOwnershipLockRefusal(sourceFailure), sourceFailure.ToString());
    }

    private static bool IsOwnershipLockRefusal(Exception error) => error is PostgresException { SqlState: "55P03" } ||
        error.InnerException is { } inner && IsOwnershipLockRefusal(inner) ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsOwnershipLockRefusal);

    private sealed class TimerRootLease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }

    private sealed class TimerNativeInsertHold : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Hold(DbCommand command, CancellationToken token)
        {
            if (!command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains(RuntimeWorkflowExecutionEfModule.TableName, StringComparison.Ordinal)) return;
            Entered.TrySetResult(); await Proceed.Task.WaitAsync(token);
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { await Hold(command, token); return result; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { await Hold(command, token); return result; }
    }

}
