using System.Text.Json;
using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeOperationalStatePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_operational_state_smoke() => RuntimeOperationalStateProviderSmoke.RunAsync(fixture, c => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(c).Options), RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeOperationalStateSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_operational_state_smoke() => RuntimeOperationalStateProviderSmoke.RunAsync(fixture, c => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(c).Options), RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeOperationalStateMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_operational_state_smoke() => RuntimeOperationalStateProviderSmoke.RunAsync(fixture, c => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(c).Options), RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeOperationalStateProviderSmoke
{
    private const string SigningKey = "ef-runtime-r14-r15-provider-signing-key-32-bytes";

    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, RuntimeDbContext> createContext, string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        RuntimePostCommitOutboxClaim firstOutboxClaim;
        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var accessor = new FixedAccessor(scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey }));
            var checkpointStore = new EfRuntimeCheckpointCommitStore(
                context,
                accessor,
                rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
            var checkpoint = EmptyCheckpointCommit($"checkpoint-{Guid.NewGuid():N}");
            var checkpointResult = await checkpointStore.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate));
            var checkpointReplay = await checkpointStore.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Empty(checkpointResult.PendingPostCommitWorkIds);
            Assert.Empty(checkpointReplay.PendingPostCommitWorkIds);
            var values = new EfDurableValueStateStore(context, accessor, codec);
            var scheduler = new EfSchedulerStateStore(context, accessor);
            var liveness = new EfExecutionLivenessStateStore(context, accessor, codec);
            var holds = new EfWorkflowHoldStateStore(context, accessor);
            var executions = new EfWorkflowExecutionStateStore(context, accessor, codec);
            var incidents = new EfIncidentStateStore(context, accessor);
            var attention = new EfWorkflowRuntimeAttentionQuery(context, accessor);
            var checkpointNow = DateTimeOffset.UtcNow;
            var checkpointLease = new RuntimeExecutionLease(
                "checkpoint-lease",
                "workflow-a",
                "checkpoint-owner",
                checkpointNow,
                checkpointNow.AddMinutes(5),
                1);
            await liveness.SaveAsync(new ExecutionLivenessState(
                "ownership:workflow-a",
                "workflow-a",
                checkpointLease,
                null,
                null,
                null));
            var nonemptyCheckpoint = new RuntimeCheckpointCommit(
                $"checkpoint-nonempty-{Guid.NewGuid():N}",
                new RuntimeCheckpoint(
                    "checkpoint-workflow-a",
                    "NativeCheckpoint",
                    "workflow-a",
                    checkpointNow,
                    [],
                    new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(
                    new RuntimeStateChange<WorkflowExecutionState>(
                        "workflow-a",
                        RuntimeStateChangeOperation.Upsert,
                        Execution("workflow-a", scope, WorkflowExecutionStatus.Running),
                        new Dictionary<string, string>()),
                    new RuntimeStateChange<SchedulerState>(
                        "workflow-a",
                        RuntimeStateChangeOperation.Upsert,
                        new SchedulerState("workflow-a", 3),
                        new Dictionary<string, string>()),
                    [], [], [], [], []),
                [],
                new Dictionary<string, string>())
            {
                ExpectedFence = checkpointLease.ToFence()
            };
            var nonemptyResult = await checkpointStore.CommitAsync(nonemptyCheckpoint, new(RuntimeCheckpointPersistenceMode.Immediate));
            var nonemptyReplay = await checkpointStore.CommitAsync(nonemptyCheckpoint, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Empty(nonemptyResult.PendingPostCommitWorkIds);
            Assert.Empty(nonemptyReplay.PendingPostCommitWorkIds);
            Assert.Equal("workflow-a", (await executions.FindAsync("workflow-a"))?.WorkflowExecutionId);
            Assert.Equal(0, (await context.WorkflowRunHealthStates.SingleAsync()).IncidentCount);
            Assert.Equal(3, (await scheduler.FindAsync("workflow-a"))!.Version);
            Assert.Equal(2, (await liveness.FindVersionedAsync("workflow-a", "ownership:workflow-a"))!.Revision);
            var checkpointIncident = new IncidentState("native-checkpoint-incident", "workflow-a", null, null,
                IncidentSeverity.Error, IncidentStatus.Open, null, "provider-checkpoint", "native health fold",
                checkpointNow, null);
            var incidentCommit = EmptyCheckpointCommit($"checkpoint-incident-{Guid.NewGuid():N}");
            incidentCommit = incidentCommit with
            {
                StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [],
                    [new RuntimeStateChange<IncidentState>(checkpointIncident.IncidentId,
                        RuntimeStateChangeOperation.Append, checkpointIncident, new Dictionary<string, string>())], [])
            };
            await checkpointStore.CommitAsync(incidentCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            await checkpointStore.CommitAsync(incidentCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(1, (await context.WorkflowRunHealthStates.SingleAsync()).IncidentCount);
            Assert.Equal("native-checkpoint-incident", (await incidents.FindAsync("workflow-a", "native-checkpoint-incident"))!.IncidentId);
            var outbox = new EfRuntimePostCommitOutboxStore(context, accessor);
            var futureAt = DateTimeOffset.UtcNow.AddDays(3);
            var futureCommit = EmptyCheckpointCommit($"checkpoint-outbox-{Guid.NewGuid():N}");
            var futureIntent = new RuntimePostCommitIntent(
                "native-folded-intent", "workflow-a", "provider-smoke.outbox", futureAt, null, null, null);
            var futureId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(futureCommit.CommitId, futureIntent.IntentId);
            var futureItem = new RuntimePostCommitOutboxItem(
                futureId, futureIntent, RuntimePostCommitOutboxStatus.Pending, futureAt, futureAt);
            var futureDispatch = PendingDispatch("workflow-a", "native-checkpoint-dispatch", scope, futureAt);
            var workId = $"native-checkpoint-work-{Guid.NewGuid():N}";
            var queue = new EfSchedulerWorkQueueStore(context, accessor,
                new HmacRuntimeRecoveryContinuationCodec(Options.Create(
                    new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));
            await queue.EnqueueAsync(new RuntimeSchedulerWorkItem(
                workId, "workflow-a", "command", WorkflowExecutionCommandKind.ScheduleActivity,
                "envelope", $"enqueue-{workId}", futureAt, futureAt, 1));
            var queueClaim = (await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-a", "checkpoint-worker", futureAt, TimeSpan.FromMinutes(1))))!;
            futureCommit = futureCommit with
            {
                Checkpoint = futureCommit.Checkpoint with { OccurredAt = futureAt },
                PostCommitIntents = [futureIntent],
                StateChanges = futureCommit.StateChanges.WithWorkflowDispatches([
                    new RuntimeStateChange<WorkflowDispatchRecord>(
                        futureDispatch.DispatchId, RuntimeStateChangeOperation.Upsert,
                        futureDispatch, new Dictionary<string, string>())]).WithPostCommitOutbox([
                    new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                        futureId, RuntimeStateChangeOperation.Upsert, futureItem, new Dictionary<string, string>())])
                    .WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(queueClaim)])
            };
            var staged = await checkpointStore.CommitAsync(futureCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            var replay = await checkpointStore.CommitAsync(futureCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal([futureId], staged.PendingPostCommitWorkIds);
            Assert.Equal([futureId], replay.PendingPostCommitWorkIds);
            Assert.Equal([workId], staged.ConsumedSchedulerWorkItemIds);
            Assert.Equal([workId], replay.ConsumedSchedulerWorkItemIds);
            Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("workflow-a", 10))).Items);
            Assert.Equal(futureId, (await outbox.FindAsync(futureId))!.OutboxItemId);
            Assert.Equal(futureDispatch.DispatchId,
                (await new EfWorkflowDispatchStore(context, accessor).FindAsync(futureDispatch.DispatchId))!.DispatchId);
            var testScope = new WorkflowTestScope(
                $"native-checkpoint-scope-{Guid.NewGuid():N}", futureAt.AddHours(1), scope,
                new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue));
            var testScopes = new EfWorkflowTestScopeStore(context, accessor,
                new HmacRuntimeRecoveryContinuationCodec(Options.Create(
                    new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));
            await testScopes.CreateAsync(testScope, futureAt);
            var testDispatch = PendingDispatch("workflow-a", "native-checkpoint-test-dispatch", scope, futureAt, testScope);
            var testCommit = EmptyCheckpointCommit($"checkpoint-test-scope-{Guid.NewGuid():N}");
            testCommit = testCommit with
            {
                Checkpoint = testCommit.Checkpoint with { OccurredAt = futureAt },
                StateChanges = testCommit.StateChanges.WithWorkflowDispatches([
                    new RuntimeStateChange<WorkflowDispatchRecord>(
                        testDispatch.DispatchId, RuntimeStateChangeOperation.Upsert,
                        testDispatch, new Dictionary<string, string>())])
            };
            await checkpointStore.CommitAsync(testCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            await checkpointStore.CommitAsync(testCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(1, (await context.WorkflowTestScopes.AsNoTracking().SingleAsync(row =>
                row.ScopeId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(testScope.ScopeId))).Revision);
            Assert.Equal(testDispatch.DispatchId,
                (await new EfWorkflowDispatchStore(context, accessor).FindAsync(testDispatch.DispatchId))!.DispatchId);
            var durableId = $"native-checkpoint-value-{Guid.NewGuid():N}";
            await values.SaveAsync(Value(durableId, "workflow-a"));
            var durableReplacement = new DurableValueState(
                durableId, "workflow-a", durableId,
                new RuntimeValueTypeDescriptor("json", null, null),
                DurableValueLifecycle.Result, DurableValueStorage.Inline,
                JsonDocument.Parse("43").RootElement, null, null, futureAt,
                new Dictionary<string, string>());
            var durableCommit = EmptyCheckpointCommit($"checkpoint-durable-{Guid.NewGuid():N}");
            durableCommit = durableCommit with
            {
                Checkpoint = durableCommit.Checkpoint with { OccurredAt = futureAt },
                StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [],
                    [new RuntimeStateChange<DurableValueState>(durableId,
                        RuntimeStateChangeOperation.Upsert, durableReplacement,
                        new Dictionary<string, string>())], [], [])
            };
            await checkpointStore.CommitAsync(durableCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            await checkpointStore.CommitAsync(durableCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal("43", (await values.FindAsync("workflow-a", durableId))!.InlineValue!.Value.GetRawText());
            Assert.Equal(2, (await context.DurableValueStates.AsNoTracking().SingleAsync(row =>
                row.DurableValueId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(durableId))).Revision);
            var cleanupBookmarkId = $"native-cleanup-bookmark-{Guid.NewGuid():N}";
            var cleanupTimerId = $"native-cleanup-timer-{Guid.NewGuid():N}";
            var cleanupWorkId = $"native-cleanup-work-{Guid.NewGuid():N}";
            var bookmarks = new EfBookmarkStateStore(context, accessor);
            var timers = new EfDurableTimerStore(context, accessor, codec);
            await bookmarks.SaveAsync(new BookmarkState(
                cleanupBookmarkId, "workflow-a", "activity-cleanup", "node-cleanup", "resume-cleanup",
                "stimulus", "native-cleanup", JsonDocument.Parse("42").RootElement,
                new Dictionary<string, string>(), futureAt, futureAt.AddHours(1)));
            await timers.SaveAsync(new DurableTimer(
                cleanupTimerId, "workflow-a", "Delay", "stimulus-cleanup",
                futureAt.AddMinutes(10), futureAt));
            await queue.EnqueueAsync(new RuntimeSchedulerWorkItem(
                cleanupWorkId, "workflow-a", "command", WorkflowExecutionCommandKind.ScheduleActivity,
                "envelope", $"enqueue-{cleanupWorkId}", futureAt, futureAt, 1));
            var cleanupCommit = EmptyCheckpointCommit($"checkpoint-cleanup-{Guid.NewGuid():N}");
            cleanupCommit = cleanupCommit with
            {
                Checkpoint = cleanupCommit.Checkpoint with { OccurredAt = futureAt },
                StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
                    null, null, null,
                    [new ActivityScopeCleanupRequest("workflow-a", "scope-cleanup", ["scope-cleanup"],
                        [cleanupBookmarkId], [cleanupTimerId], [cleanupWorkId])], null)
            };
            await checkpointStore.CommitAsync(cleanupCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            await checkpointStore.CommitAsync(cleanupCommit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Null(await bookmarks.FindAsync("workflow-a", cleanupBookmarkId));
            Assert.Null(await timers.FindAsync("workflow-a", cleanupTimerId));
            Assert.DoesNotContain((await queue.ListAsync(new RuntimeSchedulerWorkQuery("workflow-a", 10))).Items,
                item => item.WorkItemId == cleanupWorkId);
            var outboxNow = DateTimeOffset.UtcNow;
            var outboxItem = OutboxPending($"outbox-{Guid.NewGuid():N}", "workflow-a", outboxNow);
            await outbox.SavePendingAsync(outboxItem);
            await outbox.SavePendingAsync(outboxItem);
            Assert.Equal(outboxItem.OutboxItemId, Assert.Single(await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(outboxNow, 10))).OutboxItemId);
            firstOutboxClaim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "provider-owner-a", outboxNow, TimeSpan.FromMinutes(1), 1)));

            await using (var rollback = await context.Database.BeginTransactionAsync())
            {
                await outbox.SavePendingAsync(OutboxPending("outbox-rolled-back", "workflow-a", outboxNow));
                await rollback.RollbackAsync();
            }
            Assert.Null(await outbox.FindAsync("outbox-rolled-back"));

            var value = Value("aa", "workflow-a");
            await values.SaveAsync(value);
            await values.SaveAsync(Value("aG", "workflow-a"));
            await scheduler.SaveAsync(new SchedulerState("workflow-a", 4));
            var livenessState = new ExecutionLivenessState("state-a", "workflow-a", null, null, null, null);
            Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, (await liveness.TrySaveAsync(livenessState, 0)).Status);
            await holds.SaveAsync(new WorkflowHoldState(
                "global-control",
                activeHolds: [WorkflowHold.ForWorkflowExecution("embedded", "workflow-a", DateTimeOffset.UtcNow, "provider-smoke", "provider smoke hold")]));
            Assert.Equal("aG", (await values.ListPageAsync(new DurableValueStatePageQuery("workflow-a", 1))).Items.Single().DurableValueId);
            Assert.Equal(4, (await scheduler.FindAsync("workflow-a"))!.Version);
            Assert.Equal(1, (await liveness.FindVersionedAsync("workflow-a", "state-a"))!.Revision);
            Assert.Single(await holds.ListForWorkflowExecutionAsync("workflow-a"));

            await executions.SaveAsync(Execution("workflow-faulted", scope, WorkflowExecutionStatus.Faulted));
            await executions.SaveAsync(Execution("workflow-blocked", scope, WorkflowExecutionStatus.Running));
            Assert.True(await incidents.TryAddAsync(Incident("incident-blocking", "workflow-blocked")));
            var attentionSnapshot = await attention.QueryAsync(new(
                new AttentionQueryContext(new ClaimsPrincipal(), scope),
                10));
            Assert.Equal(3, attentionSnapshot.TotalCount);
            Assert.Equal(
                [WorkflowRuntimeAttentionKind.BlockingIncident, WorkflowRuntimeAttentionKind.FaultedExecution,
                    WorkflowRuntimeAttentionKind.OpenIncident],
                attentionSnapshot.Records.Select(x => x.Kind));

            await using var transaction = await context.Database.BeginTransactionAsync();
            await values.SaveAsync(Value("rolled-back", "workflow-a"));
            await incidents.TryAddAsync(Incident("rolled-back-incident", "workflow-blocked"));
            await transaction.RollbackAsync();
        }

        await using (var fresh = createContext(connectionString))
        {
            var outbox = new EfRuntimePostCommitOutboxStore(fresh, new FixedAccessor(scope));
            var reclaimed = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "provider-owner-b", DateTimeOffset.UtcNow.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => outbox.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(
                    firstOutboxClaim,
                    new RuntimePostCommitOutboxDeliveryResult(
                        firstOutboxClaim.OutboxItemId,
                        RuntimePostCommitOutboxStatus.Delivered,
                        DateTimeOffset.UtcNow.AddMinutes(2)))).AsTask());
            await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                reclaimed,
                new RuntimePostCommitOutboxDeliveryResult(
                    reclaimed.OutboxItemId,
                    RuntimePostCommitOutboxStatus.Delivered,
                    DateTimeOffset.UtcNow.AddMinutes(2))));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outbox.FindAsync(reclaimed.OutboxItemId))!.Status);

            var values = new EfDurableValueStateStore(fresh, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));
            Assert.Null(await values.FindAsync("workflow-a", "rolled-back"));
            var liveness = new EfExecutionLivenessStateStore(fresh, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));
            var holds = new EfWorkflowHoldStateStore(fresh, new FixedAccessor(scope));
            var incidents = new EfIncidentStateStore(fresh, new FixedAccessor(scope));
            var attention = new EfWorkflowRuntimeAttentionQuery(fresh, new FixedAccessor(scope));
            Assert.NotNull(await liveness.FindAsync("workflow-a", "state-a"));
            Assert.Single(await holds.ListForWorkflowExecutionAsync("workflow-a"));
            Assert.Null(await incidents.FindAsync("workflow-blocked", "rolled-back-incident"));
            Assert.Equal(3, (await attention.QueryAsync(new(
                new AttentionQueryContext(new ClaimsPrincipal(), scope),
                10))).TotalCount);
            await using var left = createContext(connectionString);
            await using var right = createContext(connectionString);
            var scopeKey = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(scope);
            var scopeHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash(scope);
            var durableValueKey = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("aa");
            var durableValueHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("aa");
            var leftRow = await left.DurableValueStates.SingleAsync(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash && row.DurableValueId == durableValueKey && row.DurableValueIdHash == durableValueHash);
            var rightRow = await right.DurableValueStates.SingleAsync(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash && row.DurableValueId == durableValueKey && row.DurableValueIdHash == durableValueHash);
            leftRow.Revision++;
            await left.SaveChangesAsync();
            rightRow.Revision++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.SaveChangesAsync());
        }
    }

    private static DurableValueState Value(string valueId, string workflowId) => new(valueId, workflowId, valueId, new RuntimeValueTypeDescriptor("json", null, null), DurableValueLifecycle.Result, DurableValueStorage.Inline, JsonDocument.Parse("42").RootElement, null, null, DateTimeOffset.UtcNow, new Dictionary<string, string>());
    private static WorkflowExecutionState Execution(string id, string tenantId, WorkflowExecutionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            id,
            new WorkflowExecutableIdentity($"artifact-{id}", $"definition-{id}", "version-1", "1.0.0", "hash-1"),
            status,
            null,
            now,
            now,
            now,
            status.IsTerminal() ? now : null,
            null,
            null,
            tenantId,
            new Dictionary<string, string>());
    }

    private static IncidentState Incident(string incidentId, string workflowExecutionId) => new(
        incidentId,
        workflowExecutionId,
        null,
        null,
        IncidentSeverity.Critical,
        IncidentStatus.Blocking,
        null,
        "ProviderSmokeFailure",
        "provider smoke detail",
        DateTimeOffset.UtcNow,
        null);

    private static RuntimePostCommitOutboxItem OutboxPending(string outboxItemId, string workflowExecutionId, DateTimeOffset recordedAt) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            $"intent-{outboxItemId}",
            workflowExecutionId,
            "provider-smoke.outbox",
            recordedAt,
            null,
            null,
            null),
        RuntimePostCommitOutboxStatus.Pending,
        recordedAt,
        recordedAt);

    private static WorkflowDispatchRecord PendingDispatch(
        string parent,
        string activity,
        string tenant,
        DateTimeOffset createdAt,
        WorkflowTestScope? testScope = null)
    {
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{activity}", "definition-child", "version-child", "1", $"hash-{activity}"),
            new WorkflowExecutableSourceProvenance($"source-{activity}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            tenant,
            testScope?.Partition ?? new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            testScope is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            createdAt,
            createdAt,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" },
            testScope: testScope);
    }

    private static RuntimeCheckpointCommit EmptyCheckpointCommit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint(
            $"checkpoint-{commitId}",
            "EmptyCheckpoint",
            "workflow-a",
            DateTimeOffset.UtcNow,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>());
    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope)); }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }
}
