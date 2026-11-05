using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeHistoryPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    [Trait("Feature", "RunHistory")]
    public Task PostgreSql_history_atomic_append_replay_cursor_purge_and_deletion_races() => RuntimeHistoryProviderContract.RunAsync(fixture,
        connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
        (connection, interceptor) => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptor).Options));
}
[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeHistorySqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    [Trait("Feature", "RunHistory")]
    public Task SqlServer_history_atomic_append_replay_cursor_purge_and_deletion_races() => RuntimeHistoryProviderContract.RunAsync(fixture,
        connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
        (connection, interceptor) => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(interceptor).Options));
}
[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeHistoryMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    [Trait("Feature", "RunHistory")]
    public Task MySql_history_atomic_append_replay_cursor_purge_and_deletion_races() => RuntimeHistoryProviderContract.RunAsync(fixture,
        connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
        (connection, interceptor) => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(interceptor).Options));
}

internal static class RuntimeHistoryProviderContract
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("provider-history", 1, true, ["ActivityCompleted", "StimulusReceived"]);
    private static readonly RuntimeCheckpointPersistenceDecision Decision = new(RuntimeCheckpointPersistenceMode.Immediate);
    private static IRuntimeRecoveryContinuationCodec Codec() => new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "runtime-history-provider-exact-signing-key-32" }));
    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, RuntimeDbContext> open,
        Func<string, IInterceptor, RuntimeDbContext> intercepted)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The requested relational provider is unavailable.");
        var scope = "history-" + Guid.NewGuid().ToString("N"); var run = "run-" + Guid.NewGuid().ToString("N"); var access = new Accessor(scope);
        var first = Commit(run, "first-" + Guid.NewGuid().ToString("N"), "A", true);
        var second = Commit(run, "second-" + Guid.NewGuid().ToString("N"), "a", true);
        await using (var seed = open(fixture.ConnectionString))
        {
            var chain = seed.Database.GetMigrations().ToArray();
            Assert.EndsWith("_RunHistory", chain[^1]);
            await seed.GetService<IMigrator>().MigrateAsync(chain[^2]);
            var legacyRun = run + "-legacy";
            var legacyCheckpoint = new RuntimeCheckpoint("legacy-" + run, "Legacy", legacyRun, At, [], new Dictionary<string, string>());
            var legacy = new RuntimeCheckpointCommit(legacyCheckpoint.CheckpointId, legacyCheckpoint, new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());
            var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(legacy);
            var oldState = new WorkflowExecutionState(legacyRun, new("artifact-legacy", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                null, At, At, At, null, null, null, scope, new Dictionary<string, string>());
            seed.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(oldState, scope, EfWorkflowExecutionStateStore.CreateId(scope, legacyRun), 1));
            var content = System.Text.Json.JsonSerializer.Serialize(new { legacy.CommitId, legacy.WorkflowExecutionId, OccurredAt = At, Fingerprint = fingerprint,
                PendingPostCommitWorkIds = Array.Empty<string>(), ConsumedSchedulerWorkItemIds = Array.Empty<string>() }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            seed.RuntimeCheckpointCommits.Add(new RuntimeCheckpointCommitEntity
            {
                Id = EfRuntimeOperationalStoreSupport.CompositeId(scope, legacy.CommitId), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
                WorkflowExecutionId = EfRelationalIdentity.Encode(legacyRun), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(legacyRun), WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(legacyRun),
                CommitId = EfRelationalIdentity.Encode(legacy.CommitId), CommitIdHash = EfRelationalIdentity.Hash(legacy.CommitId), OccurredAtUtcTicks = At.UtcTicks,
                Fingerprint = fingerprint, ContentJson = content, PendingPostCommitWorkIdsJson = "[]", ConsumedSchedulerWorkItemIdsJson = "[]", SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1
            });
            await seed.SaveChangesAsync(); seed.ChangeTracker.Clear();
            await InsertLegacyTimerAsync(seed, scope, legacyRun);
            await seed.Database.MigrateAsync();
            var oldMarker = await seed.RuntimeCheckpointCommits.AsNoTracking().SingleAsync(x => x.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, legacy.CommitId));
            Assert.Equal(content, oldMarker.ContentJson); Assert.Equal(fingerprint, oldMarker.Fingerprint);
            await new EfRuntimeCheckpointCommitStore(seed, access).CommitAsync(legacy, Decision);
            Assert.Null(await new EfRuntimeHistoryStore(seed, access, Codec()).FindHeadAsync(legacyRun));
            var timers = new EfDurableTimerStore(seed, access, Codec());
            Assert.Null((await timers.FindAsync(legacyRun, "legacy-timer"))!.HistoryResponsibility);
            Assert.False(await timers.HasPendingHistoryAsync(legacyRun));
            var state = new WorkflowExecutionState(run, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                null, At, At, At, null, null, null, scope, new Dictionary<string, string>())
            { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
            seed.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, scope, EfWorkflowExecutionStateStore.CreateId(scope, run), 1));
            await seed.SaveChangesAsync();
        }
        // Both legitimate writers see the first head missing. The native run lock plus head allocation CAS must
        // settle this at the head, including InnoDB's repeatable-read snapshot, before inserting event positions.
        await InterleavedAppendAsync(first, second);
        await using (var restarted = open(fixture.ConnectionString))
        {
            var writer = new EfRuntimeCheckpointCommitStore(restarted, access);
            await writer.CommitAsync(first, Decision); await writer.CommitAsync(second, Decision);
            await writer.CommitAsync(first with { CommitId = "new-physical-" + Guid.NewGuid().ToString("N") }, Decision);
            var reader = new EfRuntimeHistoryStore(restarted, access, Codec());
            var page = await reader.ReadPageAsync(new(run, 1, accessBinding: "profile"));
            Assert.Equal(2, page.Head!.LastSequence); Assert.NotNull(page.NextContinuationToken);
            Assert.Single((await reader.ReadPageAsync(new(run, activityExecutionId: "A"))).Items);
            Assert.Single((await reader.ReadPageAsync(new(run, activityExecutionId: "a"))).Items);
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new(run, 1, page.NextContinuationToken, accessBinding: "other")).AsTask());
            var exactKind = await reader.ReadPageAsync(new(run, name: "ActivityCompleted"));
            Assert.Equal(2, exactKind.ScannedCount);
        }
        var third = Commit(run, "third-" + Guid.NewGuid().ToString("N"), "third", false);
        var fourth = Commit(run, "fourth-" + Guid.NewGuid().ToString("N"), "fourth", false);
        await InterleavedAppendAsync(third, fourth);
        // Failure occurs after reservation and event staging but before immutable commit proof.
        await using (var failing = intercepted(fixture.ConnectionString, new FailMarker()))
            await Assert.ThrowsAnyAsync<Exception>(() => new EfRuntimeCheckpointCommitStore(failing, access)
                .CommitAsync(Commit(run, "rollback-" + Guid.NewGuid().ToString("N"), "rollback", false), Decision).AsTask());
        await using (var verify = open(fixture.ConnectionString))
        {
            var reader = new EfRuntimeHistoryStore(verify, access, Codec());
            Assert.Equal(4, (await reader.FindHeadAsync(run))!.LastSequence);
            Assert.Equal(4, (await reader.ReadPageAsync(new(run))).Items.Count);
            Assert.Equal(4, await reader.PurgeThroughAsync(run, 4));
            Assert.Empty((await reader.ReadPageAsync(new(run))).Items);
            await new EfRuntimeCheckpointCommitStore(verify, access).CommitAsync(first with { CommitId = "late-purged-replay-" + Guid.NewGuid().ToString("N") }, Decision);
            Assert.Equal(4, (await reader.FindHeadAsync(run))!.LastSequence);
        }
        // Ambiguous final acknowledgement reconciles through the same durable marker, retaining one event.
        var ambiguous = Commit(run, "ambiguous-" + Guid.NewGuid().ToString("N"), "after-purge", false);
        await using (var loss = intercepted(fixture.ConnectionString, new AcknowledgementLoss()))
            await new EfRuntimeCheckpointCommitStore(loss, access).CommitAsync(ambiguous, Decision);
        await using (var verify = open(fixture.ConnectionString))
        {
            var reader = new EfRuntimeHistoryStore(verify, access, Codec());
            Assert.Equal(5, (await reader.FindHeadAsync(run))!.LastSequence);
            Assert.Equal(5, Assert.Single((await reader.ReadPageAsync(new(run))).Items).Sequence);
        }
        // A terminal deletion and independently owned observation serialize through the same native root. Either
        // append happens first and is deleted, or deletion wins and observation is refused; no history can remain.
        WorkflowExecutionDeletionCandidate candidate;
        await using (var terminal = open(fixture.ConnectionString))
        {
            var id = EfWorkflowExecutionStateStore.CreateId(scope, run);
            var row = await terminal.WorkflowExecutionStates.SingleAsync(x => x.Id == id);
            var state = EfWorkflowExecutionStateStore.ReadChecked(row, scope, run) with { Status = WorkflowExecutionStatus.Completed, CompletedAt = At.AddHours(1) };
            EfWorkflowExecutionStateStore.CopyToEntity(row, state, scope, id, row.Revision + 1);
            await terminal.SaveChangesAsync(); terminal.ChangeTracker.Clear();
            candidate = (await new EfWorkflowExecutionStateStore(terminal, access, Codec()).ReadDeletionCandidateAsync(run))!;
        }
        await using (var deletion = open(fixture.ConnectionString))
        await using (var observation = open(fixture.ConnectionString))
        {
            var e = new RuntimeHistoryEvent("delivery-" + Guid.NewGuid().ToString("N"), run, 0, RuntimeHistoryEventCategory.OperationalLifecycle, "StimulusReceived", At)
            { Authority = RuntimeHistoryAuthority.DeliveryObservation };
            var deleted = new EfWorkflowExecutionStateStore(deletion, access, Codec()).TryDeleteTerminalAsync(candidate).AsTask();
            var observed = new EfRuntimeHistoryStore(observation, access, Codec()).RecordAsync(new(Policy, e)).AsTask();
            await Task.WhenAll(deleted, observed);
            Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deleted);
        }
        await using (var verify = open(fixture.ConnectionString))
        {
            var reader = new EfRuntimeHistoryStore(verify, access, Codec());
            Assert.Null(await reader.FindHeadAsync(run)); Assert.Empty((await reader.ReadPageAsync(new(run))).Items);
        }
        await SourceDischargeRaceAsync(sourceFirst: true);
        await SourceDischargeRaceAsync(sourceFirst: false);

        async Task SourceDischargeRaceAsync(bool sourceFirst)
        {
            var sourceRun = run + (sourceFirst ? "-source-first" : "-completion-first");
            var timerPolicy = new RuntimeHistoryCapturePolicy("provider-timer-history", 1, true, ["WorkflowCompleted", "WorkflowFaulted", "TimerScheduled", "TimerRetired"]);
            var lease = new RuntimeExecutionLease("lease-" + sourceRun, sourceRun, "owner", At.AddMinutes(-1), At.AddHours(1), 1);
            DurableTimer MakeTimer(string timerId)
            {
                var timer = new DurableTimer(timerId, sourceRun, "type", "hash", At.AddMinutes(1), At, ActivityExecutionId: "activity");
                return timer with { HistoryResponsibility = new(timerPolicy, [DurableTimerHistoryFacts.CreateScheduled(timer, timerPolicy)!.Event], false) };
            }
            await using (var seed = open(fixture.ConnectionString))
            {
                seed.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(new(RuntimeExecutionOwnershipStateId.For(sourceRun), sourceRun, lease, null, null, null), scope, 1));
                await seed.SaveChangesAsync(); seed.ChangeTracker.Clear();
                var store = new EfDurableTimerStore(seed, access, Codec(), new Clock()); var timer = await store.SaveAsync(MakeTimer("retired"));
                await store.RetireWithHistoryAsync(sourceRun, timer.TimerId, DurableTimerHistoryFacts.CreateCancelled(timer, At)!);
            }
            var completed = new WorkflowExecutionState(sourceRun, new("artifact-source", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Completed,
                null, At, At, At, At, null, null, scope, new Dictionary<string, string>())
            { HistoryCapturePolicy = timerPolicy, HistoryCaptureEnabledAtAdmission = true, SystemMetadata = new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = nameof(PersistenceMode.Ephemeral) } };
            var checkpoint = new RuntimeCheckpoint("completion-" + sourceRun, "WorkflowCompleted", sourceRun, At, [], new Dictionary<string, string>());
            var completion = new RuntimeCheckpointCommit("discard-" + sourceRun, checkpoint, new(new(sourceRun, RuntimeStateChangeOperation.Upsert, completed, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>()) { ExpectedFence = lease.ToFence() };
            var hold = new SourceOwnershipBarrier(holdAfterLock: true); var attempting = new SourceOwnershipBarrier(holdAfterLock: false);
            await using var firstContext = intercepted(fixture.ConnectionString, hold);
            await using var secondContext = intercepted(fixture.ConnectionString, attempting);
            var firstStore = new EfDurableTimerStore(firstContext, access, Codec(), new Clock());
            var secondStore = new EfDurableTimerStore(secondContext, access, Codec(), new Clock());
            var firstOperation = sourceFirst ? firstStore.SaveAsync(MakeTimer("new-active")).AsTask() : FirstCompletionAsync();
            async Task<DurableTimer> FirstCompletionAsync() { Assert.True(await firstStore.DischargeUnrecordedCompletionAsync(completion, 1)); return MakeTimer("unused"); }
            await hold.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var secondOperation = sourceFirst ? SecondCompletionAsync() : SecondSourceAsync();
            async Task SecondCompletionAsync() => await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => secondStore.DischargeUnrecordedCompletionAsync(completion, 1).AsTask());
            async Task SecondSourceAsync() => await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => secondStore.SaveAsync(MakeTimer("new-active")).AsTask());
            try { await attempting.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
            finally { hold.Release.TrySetResult(); }
            await firstOperation; await secondOperation;
            await using var verify = open(fixture.ConnectionString);
            var owner = new EfDurableTimerStore(verify, access, Codec(), new Clock());
            var pending = await owner.ListPendingHistoryAsync(new());
            if (sourceFirst)
            {
                Assert.Contains(pending.Items, timer => timer.WorkflowExecutionId == sourceRun && timer.TimerId == "retired" && timer.HistoryResponsibility!.NoRecordCompletion is null);
                Assert.NotNull(await owner.FindAsync(sourceRun, "new-active"));
            }
            else
            {
                Assert.DoesNotContain(pending.Items, timer => timer.WorkflowExecutionId == sourceRun);
                Assert.Null(await owner.FindAsync(sourceRun, "new-active"));
                var old = await owner.SaveAsync(MakeTimer("retired")); Assert.NotNull(old.HistoryResponsibility!.NoRecordCompletion);
                var fault = new RuntimeCheckpoint("first-fault-" + sourceRun, "WorkflowFaulted", sourceRun, At, [], new Dictionary<string, string>()) { HistoryStartsAtAdmission = true };
                var e = new RuntimeHistoryEvent("first-fault-event-" + sourceRun, sourceRun, 0, RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowFaulted", At, checkpointId: fault.CheckpointId) { Authority = RuntimeHistoryAuthority.CommittedTransition };
                var faulted = completed with { Status = WorkflowExecutionStatus.Faulted, CompletedAt = null };
                var commit = new RuntimeCheckpointCommit("fault-physical-" + sourceRun, fault, new(new(sourceRun, RuntimeStateChangeOperation.Upsert, faulted, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>())
                { ExpectedFence = lease.ToFence(), HistoryBatches = [new(timerPolicy, fault.CheckpointId, At, true, [new(e, 0)])] };
                await new EfRuntimeCheckpointCommitStore(verify, access, new Clock(), new RootLease()).CommitAsync(commit, Decision);
                Assert.True((await new EfRuntimeHistoryStore(verify, access, Codec()).FindHeadAsync(sourceRun))!.HasGaps);
            }
        }

        async Task InterleavedAppendAsync(RuntimeCheckpointCommit firstCommit, RuntimeCheckpointCommit waitingCommit)
        {
            var pause = new BeforeRootHistorySnapshot(scope, run);
            await using var waiting = intercepted(fixture.ConnectionString, pause);
            var delayed = new EfRuntimeCheckpointCommitStore(waiting, access).CommitAsync(waitingCommit, Decision).AsTask();
            try
            {
                await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(60));
                await using var winner = open(fixture.ConnectionString);
                await new EfRuntimeCheckpointCommitStore(winner, access).CommitAsync(firstCommit, Decision);
            }
            finally { pause.Release.TrySetResult(); }
            await delayed;
            Assert.Equal(1, pause.EstablishedSnapshots);
        }
    }
    private static async Task InsertLegacyTimerAsync(RuntimeDbContext context, string scope, string run)
    {
        var timer = new DurableTimer("legacy-timer", run, "type", "hash", At.AddMinutes(1), At);
        var framed = $"{run.Length}:{run}{timer.TimerId.Length}:{timer.TimerId}";
        var order = timer.DueTime.UtcTicks.ToString("D19", System.Globalization.CultureInfo.InvariantCulture) + "." +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(framed)));
        string[] names = ["Id", "ScopeKey", "ScopeKeyHash", "WorkflowExecutionId", "WorkflowExecutionIdHash", "WorkflowExecutionIdOrderKey", "TimerId", "TimerIdHash", "TimerIdOrderKey",
            "StimulusType", "StimulusHash", "DueTimeUtcTicks", "DueTimeOffsetMinutes", "CreatedAtUtcTicks", "CreatedAtOffsetMinutes", "ClaimOrderKey", "ClaimToken", "FailureCount", "ContentJson", "SchemaVersion", "Revision"];
        object[] values = [EfRuntimeOperationalStoreSupport.CompositeId(scope, run, timer.TimerId), EfRelationalIdentity.Encode(scope), EfRelationalIdentity.Hash(scope),
            EfRelationalIdentity.Encode(run), EfRelationalIdentity.Hash(run), EfRuntimeOperationalStoreSupport.Order(run), EfRelationalIdentity.Encode(timer.TimerId), EfRelationalIdentity.Hash(timer.TimerId), EfRuntimeOperationalStoreSupport.Order(timer.TimerId),
            EfRelationalIdentity.Encode(timer.StimulusType), EfRelationalIdentity.Encode(timer.StimulusHash), timer.DueTime.UtcTicks, 0, timer.CreatedAt.UtcTicks, 0, order, 0L, 0,
            RuntimeArtifactJson.Serialize(timer), RuntimeOperationalStateEfModule.SchemaVersion, 1L];
        var sql = context.GetService<ISqlGenerationHelper>();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "INSERT INTO " + sql.DelimitIdentifier(RuntimeOperationalStateEfModule.DurableTimerTableName) + " (" + string.Join(",", names.Select(name => sql.DelimitIdentifier(name))) + ") VALUES (" +
                string.Join(",", names.Select((_, index) => "@p" + index)) + ")";
            for (var index = 0; index < values.Length; index++) { var parameter = command.CreateParameter(); parameter.ParameterName = "@p" + index; parameter.Value = values[index]; command.Parameters.Add(parameter); }
            await command.ExecuteNonQueryAsync();
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static RuntimeCheckpointCommit Commit(string run, string id, string activity, bool initial)
    {
        var checkpoint = new RuntimeCheckpoint(id, "ActivityCompleted", run, At, [], new Dictionary<string, string>());
        var e = new RuntimeHistoryEvent(RuntimeHistoryIdentity.CreateId(id, "ActivityCompleted", activity), run, 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityCompleted", At,
            activity, "node", id) { Authority = RuntimeHistoryAuthority.CommittedTransition, ActivityAncestryKnown = true };
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []);
        return new(id, checkpoint, changes, [], new Dictionary<string, string>()) { HistoryBatches = [new(Policy, id, At, initial, [new(e, 0)])] };
    }
    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class RootLease : IWorkflowExecutableRootWriteLeaseManager
    { public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken); }
    private sealed class SourceOwnershipBarrier(bool holdAfterLock) : DbCommandInterceptor
    {
        private int entered;
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static bool IsOwnerLock(DbCommand command) => command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_runtime_execution_liveness_state", StringComparison.Ordinal);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (IsOwnerLock(command)) Attempted.TrySetResult(); return ValueTask.FromResult(result); }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (holdAfterLock && IsOwnerLock(command) && Interlocked.Exchange(ref entered, 1) == 0)
            { Acquired.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
    private sealed class BeforeRootHistorySnapshot(string scope, string run) : DbCommandInterceptor
    {
        private int entered;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int EstablishedSnapshots { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimeWorkflowExecutionEfModule.TableName, StringComparison.Ordinal) && Interlocked.Exchange(ref entered, 1) == 0)
            {
                // Establish a real pre-lock consistent read on the waiting transaction. InnoDB retains this older
                // head snapshot across the other writer's commit; PostgreSQL/SQL Server use their normal semantics.
                var sql = data.Context!.GetService<ISqlGenerationHelper>();
                await using var read = command.Connection!.CreateCommand(); read.Transaction = command.Transaction;
                read.CommandText = "SELECT " + sql.DelimitIdentifier("Revision") + " FROM " + sql.DelimitIdentifier(RuntimeHistoryEfModule.HeadTable) +
                    " WHERE " + sql.DelimitIdentifier("Id") + " = @historyHeadId";
                var parameter = read.CreateParameter(); parameter.ParameterName = "@historyHeadId";
                parameter.Value = EfRuntimeHistorySupport.Id("head", scope, run); read.Parameters.Add(parameter);
                _ = await read.ExecuteScalarAsync(cancellationToken); EstablishedSnapshots++;
                Ready.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    private sealed class FailMarker : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        private static void Check(DbCommand command)
        { if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.Ordinal)) throw new IOException("controlled marker failure"); }
    }
    private sealed class AcknowledgementLoss : DbTransactionInterceptor
    {
        private int remaining = 1;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        { if (Interlocked.Exchange(ref remaining, 0) == 1) throw new IOException("controlled commit acknowledgement loss"); return Task.CompletedTask; }
    }
}
