using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimeCheckpointWriterTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    static GroundworkV2RuntimeCheckpointWriterTests()
    {
        Json.Converters.Add(new JsonStringEnumConverter());
    }

    [Fact]
    public async Task Scheduler_claim_renewal_with_the_same_owner_and_token_is_consumed()
    {
        var source = new MemorySource();
        var unit = ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind;
        var workItemId = "work-item-renewed";
        var physicalId = GroundworkV2SchedulerWorkStorageConventions.PhysicalId("workflow-1", workItemId);
        source.ReplaceRow(
            "tenant-a",
            unit,
            physicalId,
            SchedulerWorkValues("workflow-1", workItemId, "owner-a", 1, DateTimeOffset.UtcNow.AddMinutes(1)),
            version: 1);
        source.BeforeCommit = _ => source.ReplaceRow(
            "tenant-a",
            unit,
            physicalId,
            SchedulerWorkValues("workflow-1", workItemId, "owner-a", 1, DateTimeOffset.UtcNow.AddMinutes(2)),
            version: 2);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null,
            consumedSchedulerWorkItems: [new ConsumedSchedulerWorkItem("workflow-1", workItemId, "owner-a", 1)]);

        var result = await NewWriter(source).CommitAsync(
            NewCommit("consume-renewed") with { StateChanges = changes },
            Immediate());

        Assert.Equal([workItemId], result.ConsumedSchedulerWorkItemIds);
        Assert.Null(source.Find(unit, physicalId, "tenant-a"));
        Assert.NotNull(source.Find(
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
            "consume-renewed",
            "tenant-a"));
    }

    [Fact]
    public async Task Scheduler_work_reassigned_to_another_workflow_after_pre_read_is_not_consumed()
    {
        var source = new MemorySource();
        var unit = ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind;
        var workItemId = "work-item-reassigned";
        var physicalId = GroundworkV2SchedulerWorkStorageConventions.PhysicalId("workflow-1", workItemId);
        source.ReplaceRow(
            "tenant-a",
            unit,
            physicalId,
            SchedulerWorkValues("workflow-1", workItemId, "owner-a", 1, DateTimeOffset.UtcNow.AddMinutes(1)),
            version: 1);
        source.BeforeCommit = _ =>
        {
            var reassigned = SchedulerWorkValues(
                "workflow-2",
                workItemId,
                "owner-a",
                1,
                DateTimeOffset.UtcNow.AddMinutes(1));
            var values = reassigned.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            // Keep the foreign content/projections internally consistent while occupying the
            // original physical alias, exercising collision refusal in the clean-break writer.
            values[ElsaRuntimeV2StorageManifest.IdField] = physicalId;
            source.ReplaceRow("tenant-a", unit, physicalId, new StorageValues(values), version: 2);
        };
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null,
            consumedSchedulerWorkItems: [new ConsumedSchedulerWorkItem("workflow-1", workItemId, "owner-a", 1)]);

        var exception = await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            NewWriter(source).CommitAsync(
                NewCommit("consume-reassigned") with { StateChanges = changes },
                Immediate()).AsTask());

        Assert.Equal(workItemId, exception.WorkItemId);
        Assert.Equal("workflow-2", source.Find(unit, physicalId, "tenant-a")!.Values.Values[ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField]);
        Assert.Null(source.Find(
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
            "consume-reassigned",
            "tenant-a"));
    }

    [Fact]
    public async Task Failed_batch_rolls_back_and_marker_remains_reusable()
    {
        var source = new MemorySource { FailCommitBeforeApply = true };
        var writer = NewWriter(source);
        var commit = NewCommit("rollback");

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, Immediate()).AsTask());
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "rollback", "tenant-a"));

        source.FailCommitBeforeApply = false;
        var result = await writer.CommitAsync(commit, Immediate());
        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Equal(2, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Replay_is_idempotent_and_conflicting_payload_is_rejected()
    {
        var source = new MemorySource();
        var writer = NewWriter(source);
        var commit = NewCommit("replay");

        await writer.CommitAsync(commit, Immediate());
        await writer.CommitAsync(commit, Immediate());
        Assert.Equal(1, source.UnitOfWorkCount);

        var conflicting = commit with
        {
            Checkpoint = commit.Checkpoint with { Name = "different" }
        };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(
            () => writer.CommitAsync(conflicting, Immediate()).AsTask());
        Assert.Equal(1, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Ambiguous_acknowledgement_reconciles_through_the_marker()
    {
        var source = new MemorySource { ThrowAfterApply = true };
        var writer = NewWriter(source);

        var result = await writer.CommitAsync(NewCommit("ambiguous"), Immediate());

        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Equal(1, source.UnitOfWorkCount);
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "ambiguous", "tenant-a"));
    }

    [Fact]
    public async Task Marker_and_rows_are_isolated_by_the_explicit_scope()
    {
        var source = new MemorySource();
        var tenantA = NewWriter(source, "tenant-a");
        var tenantB = NewWriter(source, "tenant-b");
        var commit = NewCommit("scoped");

        await tenantA.CommitAsync(commit, Immediate());
        await tenantB.CommitAsync(commit, Immediate());

        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "scoped", "tenant-a"));
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "scoped", "tenant-b"));
        Assert.Equal(2, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Unsupported_provider_is_refused_before_opening_a_unit_of_work()
    {
        var source = new MemorySource { AdvertiseAtomicCommit = false };
        var writer = NewWriter(source);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => writer.CommitAsync(NewCommit("unsupported"), Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "unsupported", "tenant-a"));
    }

    [Fact]
    public async Task Scheduler_consumption_requires_compare_and_delete_before_provider_io()
    {
        var source = new MemorySource { AdvertiseCompareAndDelete = false };
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null,
            consumedSchedulerWorkItems: [new ConsumedSchedulerWorkItem("workflow-1", "work-item-1", "owner-a", 1)]);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            NewWriter(source).CommitAsync(
                NewCommit("unsupported-compare-delete") with { StateChanges = changes },
                Immediate()).AsTask());

        Assert.Contains("compare-and-delete", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.UnitOfWorkCount);
        Assert.Null(source.Find(
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
            "unsupported-compare-delete",
            "tenant-a"));
    }

    [Fact]
    public async Task Workflow_execution_write_requires_the_root_write_lease_before_opening_a_unit_of_work()
    {
        var source = new MemorySource();
        var writer = new GroundworkV2RuntimeCheckpointWriter(
            source,
            new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var execution = NewExecution("workflow-1");
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(execution.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
            null,
            [], [], [], [], []);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CommitAsync(NewCommit("lease-required") with { StateChanges = changes }, Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Lost_root_write_lease_aborts_before_the_checkpoint_unit_of_work()
    {
        var source = new MemorySource();
        var writer = new GroundworkV2RuntimeCheckpointWriter(
            source,
            new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            rootWriteLeaseManager: new TestRootWriteLeaseManager(throwLost: true));
        var execution = NewExecution("workflow-1");
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(execution.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
            null,
            [], [], [], [], []);

        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() =>
            writer.CommitAsync(NewCommit("lease-lost") with { StateChanges = changes }, Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Unavailable_root_write_lease_aborts_before_the_checkpoint_unit_of_work()
    {
        var source = new MemorySource();
        var writer = new GroundworkV2RuntimeCheckpointWriter(
            source,
            new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            rootWriteLeaseManager: new TestRootWriteLeaseManager(throwUnavailable: true));
        var execution = NewExecution("workflow-1");
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(execution.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
            null,
            [], [], [], [], []);

        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            writer.CommitAsync(NewCommit("lease-unavailable") with { StateChanges = changes }, Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Alteration_terminal_write_preserves_the_full_job_and_uses_revision_cas()
    {
        var source = new MemorySource();
        var now = DateTimeOffset.UtcNow;
        var job = new WorkflowAlterationJobState(
            "job-1",
            "plan-1",
            "workflow-1",
            "tenant-a",
            3,
            WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", now.AddMinutes(5)),
            2,
            [],
            null,
            null,
            now.AddMinutes(-1),
            now.AddSeconds(-30),
            null,
            7,
            null);
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
            job.JobId,
            job,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField] = job.JobId,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobPlanIdField] = job.PlanId,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCaptureOrdinalField] = job.CaptureOrdinal,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField] = job.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField] = null
            });
        var completedAt = now;
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "cancelled", null, now);
        var terminal = new WorkflowAlterationJobTerminalChange("job-1", "claim", WorkflowAlterationJobStatus.Succeeded, [outcome], "alteration-terminal", completedAt);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null,
            consumedSchedulerWorkItems: null,
            alterationJobTerminalChange: terminal);

        await NewWriter(source).CommitAsync(NewCommit("alteration-terminal") with { StateChanges = changes }, Immediate());
        AssertProjectionFieldsDeclared(source.AllStaged);

        var stored = source.Find(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind, job.JobId, "tenant-a");
        Assert.NotNull(stored);
        var projected = JsonSerializer.Deserialize<WorkflowAlterationJobState>(
            (string)stored!.Values.Values[ElsaRuntimeV2StorageManifest.ContentField]!, Json);
        Assert.Equal(job.PlanId, projected!.PlanId);
        Assert.Equal(job.CaptureOrdinal, projected.CaptureOrdinal);
        Assert.Equal(job.Claim, projected.Claim);
        Assert.Equal(terminal.Status, projected.Status);
        Assert.Equal(8, projected.Revision);
    }

    [Fact]
    public async Task Dispatch_transition_uses_revision_cas_and_rolls_back_on_successor_race()
    {
        var source = new MemorySource();
        var pending = NewDispatch("workflow-1", WorkflowDispatchStatus.Pending);
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
            pending.DispatchId,
            pending,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                [ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField] = pending.ParentWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField] = pending.ChildWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StatusField] = pending.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.TestScopeIdField] = null,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField] = pending.CreatedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField] = pending.DispatchId
            });
        var started = WorkflowDispatchLifecycle.Transition(pending, WorkflowDispatchStatus.Started, pending.UpdatedAt.AddSeconds(1));
        source.BeforeCommit = _ => source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
            pending.DispatchId,
            started,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                [ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField] = pending.ParentWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField] = pending.ChildWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StatusField] = started.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.TestScopeIdField] = null,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField] = started.CreatedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField] = started.DispatchId
            },
            version: 2);
        var completed = WorkflowDispatchLifecycle.Transition(pending, WorkflowDispatchStatus.Completed, pending.UpdatedAt.AddSeconds(2));
        var changes = new RuntimeCheckpointStateChangeSet(
            null,
            null,
            [], [], [], [], [],
            workflowDispatches: [new RuntimeStateChange<WorkflowDispatchRecord>(completed.DispatchId, RuntimeStateChangeOperation.Upsert, completed, new Dictionary<string, string>())]);
        var commit = NewCommit("dispatch-race") with
        {
            Checkpoint = NewCommit("dispatch-race").Checkpoint with { WorkflowExecutionId = completed.ChildWorkflowExecutionId },
            StateChanges = changes
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewWriter(source).CommitAsync(commit, Immediate()).AsTask());
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "dispatch-race", "tenant-a"));
    }

    [Fact]
    public async Task Equivalent_pending_outbox_replay_does_not_overwrite_existing_delivery_row()
    {
        var source = new MemorySource();
        var now = DateTimeOffset.UtcNow;
        var intent = new RuntimePostCommitIntent("intent-1", "workflow-1", "test.intent", now, null, "idempotency-1", null);
        var item = new RuntimePostCommitOutboxItem("outbox-1", intent, RuntimePostCommitOutboxStatus.Pending, now, null);
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
            item.OutboxItemId,
            item,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = item.Intent.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxStatusField] = (int)item.Status,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField] = DateTimeOffset.MinValue,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField] = DateTimeOffset.MinValue,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField] = item.RecordedAt,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField] = item.OutboxItemId,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField] = item.Intent.Kind
            });
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            postCommitOutbox: [new RuntimeStateChange<RuntimePostCommitOutboxItem>(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())]);

        await NewWriter(source).CommitAsync(NewCommit("outbox-equivalent") with { StateChanges = changes }, Immediate());
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "outbox-equivalent", "tenant-a"));
        Assert.Equal(1, source.Find(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind, item.OutboxItemId, "tenant-a")!.Version);
    }

    [Fact]
    public async Task Duplicate_pending_outbox_intent_with_different_recorded_time_is_rejected()
    {
        var source = new MemorySource();
        var now = DateTimeOffset.UtcNow;
        var firstIntent = new RuntimePostCommitIntent(
            "intent-time",
            "workflow-1",
            "test.intent",
            now,
            null,
            "idempotency-time",
            null);
        var conflictingIntent = new RuntimePostCommitIntent(
            "intent-time",
            "workflow-1",
            "test.intent",
            now.AddSeconds(1),
            null,
            "idempotency-time",
            null);
        var first = new RuntimePostCommitOutboxItem(
            "outbox-time",
            firstIntent,
            RuntimePostCommitOutboxStatus.Pending,
            now,
            null);
        var conflicting = new RuntimePostCommitOutboxItem(
            "outbox-time",
            conflictingIntent,
            RuntimePostCommitOutboxStatus.Pending,
            now,
            null);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            postCommitOutbox:
            [
                new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                    first.OutboxItemId,
                    RuntimeStateChangeOperation.Upsert,
                    first,
                    new Dictionary<string, string>()),
                new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                    conflicting.OutboxItemId,
                    RuntimeStateChangeOperation.Upsert,
                    conflicting,
                    new Dictionary<string, string>())
            ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source)
                .CommitAsync(NewCommit("outbox-time-conflict") with { StateChanges = changes }, Immediate())
                .AsTask());
        Assert.Contains("conflicting intent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_scope_cleanup_is_staged_in_the_same_checkpoint_unit_of_work()
    {
        var source = new MemorySource();
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: [new ActivityScopeCleanupRequest("workflow-1", "scope-1", ["scope-1"], [], [], [])],
            workflowDispatchCancellations: null);

        await NewWriter(source).CommitAsync(NewCommit("cleanup") with { StateChanges = changes }, Immediate());
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "cleanup", "tenant-a"));
    }

    [Fact]
    public async Task Boundary_validation_rejects_cross_workflow_and_reserved_ownership_before_provider_io()
    {
        var source = new MemorySource();
        var writer = NewWriter(source);
        var foreignExecution = NewExecution("workflow-foreign");
        var foreignChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                foreignExecution.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                foreignExecution,
                new Dictionary<string, string>()),
            null,
            [], [], [], [], []);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CommitAsync(NewCommit("foreign-workflow") with { StateChanges = foreignChanges }, Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);

        var ownershipChanges = new RuntimeCheckpointStateChangeSet(
            null,
            null,
            [], [], [],
            [],
            [new RuntimeStateChange<ExecutionLivenessState>(
                "ownership:workflow-1",
                RuntimeStateChangeOperation.Upsert,
                NewLiveness("lease", "owner", 1),
                new Dictionary<string, string>())]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CommitAsync(NewCommit("reserved-ownership") with { StateChanges = ownershipChanges }, Immediate()).AsTask());
        Assert.Equal(0, source.UnitOfWorkCount);
    }

    [Fact]
    public async Task Test_scope_admission_is_deduplicated_when_execution_and_dispatch_share_a_scope()
    {
        var source = new MemorySource();
        var now = DateTimeOffset.UtcNow;
        var scope = new WorkflowTestScope("scope-deduplicated", now.AddMinutes(5), "tenant-a", new WorkflowExecutionPartition("partition-a"));
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
            scope.ScopeId,
            new WorkflowTestScopeRecord(scope, WorkflowTestScopeState.Open, now.AddMinutes(-1)),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                [ElsaRuntimeV2StorageManifest.StateField] = WorkflowTestScopeState.Open.ToString(),
                [ElsaRuntimeV2StorageManifest.ScopeIdField] = scope.ScopeId,
                [ElsaRuntimeV2StorageManifest.ExpiresAtField] = scope.ExpiresAt
            });

        var execution = NewExecution("workflow-1") with { TestScope = scope, RunKind = WorkflowRunKind.TestRun };
        var dispatch = NewDispatch("workflow-1", WorkflowDispatchStatus.Pending, scope);
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>("workflow-1", RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
            null,
            [], [], [], [], [],
            workflowDispatches: [new RuntimeStateChange<WorkflowDispatchRecord>(dispatch.DispatchId, RuntimeStateChangeOperation.Upsert, dispatch, new Dictionary<string, string>())]);

        await NewWriter(source).CommitAsync(NewCommit("scope-deduplicated") with { StateChanges = changes }, Immediate());

        Assert.Equal(
            1,
            source.LastUnitOfWork!.Staged.Count(write => write.Unit.Id.Value == ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind));
    }

    [Fact]
    public async Task A_stale_execution_fence_is_rejected_before_any_checkpoint_row_is_staged()
    {
        var source = new MemorySource();
        source.SeedLiveness(NewLiveness("lease-current", "owner-current", 2));
        var writer = NewWriter(source);
        var commit = NewCommit("stale") with
        {
            ExpectedFence = new RuntimeExecutionFence("lease-old", "owner-old", 1)
        };

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => writer.CommitAsync(commit, Immediate()).AsTask());
        Assert.Empty(source.LastUnitOfWork!.Staged);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "stale", "tenant-a"));
    }

    [Fact]
    public async Task A_fence_change_after_pre_read_is_translated_to_stale_fence_and_rolls_back()
    {
        var source = new MemorySource();
        source.SeedLiveness(NewLiveness("lease-a", "owner-a", 1), version: 1);
        source.BeforeCommit = _ => source.SeedLiveness(NewLiveness("lease-b", "owner-b", 2), version: 2);
        var commit = NewCommit("fence-race") with
        {
            ExpectedFence = new RuntimeExecutionFence("lease-a", "owner-a", 1)
        };

        var exception = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() =>
            NewWriter(source).CommitAsync(commit, Immediate()).AsTask());

        Assert.Equal(2, exception.CurrentFencingToken);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "fence-race", "tenant-a"));
    }

    [Fact]
    public async Task Test_scope_admission_is_a_cas_and_a_concurrent_close_rolls_back_the_checkpoint()
    {
        var source = new MemorySource();
        var now = DateTimeOffset.UtcNow;
        var scope = new WorkflowTestScope("scope-1", now.AddMinutes(5), "tenant-a", new WorkflowExecutionPartition("partition-a"));
        var open = new WorkflowTestScopeRecord(scope, WorkflowTestScopeState.Open, now.AddMinutes(-1));
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
            scope.ScopeId,
            open,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                [ElsaRuntimeV2StorageManifest.StateField] = WorkflowTestScopeState.Open.ToString(),
                [ElsaRuntimeV2StorageManifest.ScopeIdField] = scope.ScopeId,
                [ElsaRuntimeV2StorageManifest.ExpiresAtField] = scope.ExpiresAt
            });
        var execution = NewExecution("workflow-1") with { TestScope = scope, RunKind = WorkflowRunKind.TestRun };
        source.BeforeCommit = _ =>
        {
            var closing = new WorkflowTestScopeRecord(
                scope,
                WorkflowTestScopeState.Closing,
                open.CreatedAt,
                now,
                closeReason: WorkflowTestScopeCloseReason.ExplicitTeardown);
            source.SeedRow("tenant-a", ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind, scope.ScopeId, closing, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                [ElsaRuntimeV2StorageManifest.StateField] = WorkflowTestScopeState.Closing.ToString(),
                [ElsaRuntimeV2StorageManifest.ScopeIdField] = scope.ScopeId,
                [ElsaRuntimeV2StorageManifest.ExpiresAtField] = scope.ExpiresAt
            }, version: 2);
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(NewCommit("scope-race") with
            {
                StateChanges = new RuntimeCheckpointStateChangeSet(
                    new RuntimeStateChange<WorkflowExecutionState>("workflow-1", RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
                    null, [], [], [], [], [])
            }, Immediate()).AsTask());

        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, "workflow-1", "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "scope-race", "tenant-a"));
    }

    [Fact]
    public async Task A_scheduler_claim_reclaim_race_rolls_back_and_reports_claim_lost()
    {
        var source = new MemorySource();
        var workItemId = "work-item-1";
        var unit = ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind;
        source.SeedRow("tenant-a", unit, workItemId, new
        {
            workItemId,
            workflowExecutionId = "workflow-1",
            claimOwnerId = "owner-a",
            fencingToken = 1L
        }, SchedulerWorkProjections("workflow-1", "owner-a", 1));
        source.BeforeCommit = _ => source.SeedRow("tenant-a", unit, workItemId, new
        {
            workItemId,
            workflowExecutionId = "workflow-1",
            claimOwnerId = "owner-b",
            fencingToken = 2L
        }, SchedulerWorkProjections("workflow-1", "owner-b", 2), version: 2);

        var changes = new RuntimeCheckpointStateChangeSet(
            null,
            null,
            [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null,
            consumedSchedulerWorkItems: [new ConsumedSchedulerWorkItem("workflow-1", workItemId, "owner-a", 1)]);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            NewWriter(source).CommitAsync(NewCommit("consume-race") with { StateChanges = changes }, Immediate()).AsTask());

        Assert.NotNull(source.Find(unit, workItemId, "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "consume-race", "tenant-a"));
    }

    [Fact]
    public async Task A_scheduler_claim_without_owner_and_token_projections_fails_closed()
    {
        var source = new MemorySource();
        var unit = ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind;
        source.SeedRow(
            "tenant-a",
            unit,
            "work-item-without-claim",
            new { workflowExecutionId = "workflow-1" },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = "workflow-1",
                [ElsaRuntimeV2StorageManifest.CollectionField] = unit,
                [ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField] = "order-1"
            });
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null,
            consumedSchedulerWorkItems: [new ConsumedSchedulerWorkItem("workflow-1", "work-item-without-claim", "owner-a", 1)]);

        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            NewWriter(source).CommitAsync(NewCommit("consume-without-claim") with { StateChanges = changes }, Immediate()).AsTask());

        Assert.Empty(source.LastUnitOfWork!.Staged);
        Assert.NotNull(source.Find(unit, "work-item-without-claim", "tenant-a"));
    }

    [Fact]
    public async Task A_dispatch_cancellation_race_is_cas_protected_and_does_not_overwrite_successor_state()
    {
        var source = new MemorySource();
        var pending = NewDispatch("workflow-1", WorkflowDispatchStatus.Pending);
        var unit = ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind;
        source.SeedRow("tenant-a", unit, pending.DispatchId, pending, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] = unit,
            [ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField] = pending.ParentWorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField] = pending.ChildWorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.StatusField] = pending.Status.ToString(),
            [ElsaRuntimeV2StorageManifest.TestScopeIdField] = null,
            [ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField] = pending.CreatedAt,
            [ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField] = pending.DispatchId
        });
        source.BeforeCommit = _ =>
        {
            var started = pending.TransitionTo(WorkflowDispatchStatus.Started, pending.UpdatedAt.AddSeconds(1));
            source.SeedRow("tenant-a", unit, pending.DispatchId, started, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = unit,
                [ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField] = started.ParentWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField] = started.ChildWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StatusField] = started.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.TestScopeIdField] = null,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField] = started.CreatedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField] = started.DispatchId
            }, version: 2);
        };
        var cancellation = new WorkflowDispatchCancellationRequest(
            pending.DispatchId,
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId,
            pending.ChildWorkflowExecutionId,
            DateTimeOffset.UtcNow);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: [cancellation]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(NewCommit("dispatch-race") with { StateChanges = changes }, Immediate()).AsTask());

        var stored = source.Find(unit, pending.DispatchId, "tenant-a");
        Assert.NotNull(stored);
        Assert.Contains("Started", (string)stored!.Values.Values[ElsaRuntimeV2StorageManifest.ContentField]!);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "dispatch-race", "tenant-a"));
    }

    [Fact]
    public async Task Hierarchy_uses_provenance_scope_and_marks_only_the_scope_root()
    {
        var source = new MemorySource();
        var activityId = "activity-root";
        var inspection = NewInspection(activityId);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: [new RuntimeStateChange<ActivityExecutionInspectionProjection>(activityId, RuntimeStateChangeOperation.Upsert, inspection, new Dictionary<string, string>())],
            postCommitOutbox: null,
            activityScopeCleanups: null,
            workflowDispatchCancellations: null);

        await NewWriter(source).CommitAsync(NewCommit("hierarchy-provenance") with { StateChanges = changes }, Immediate());

        var hierarchy = source.LastUnitOfWork!.Staged.Single(write =>
            write.Unit.Id.Value == ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var content = (string)hierarchy.Values!.Values[ElsaRuntimeV2StorageManifest.ContentField]!;
        Assert.Contains("activity-root", content);
        Assert.True((bool)hierarchy.Values.Values[ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyIsScopeRootField]!);
    }

    [Fact]
    public async Task Exact_uow_refuses_an_undeclared_hierarchy_unit_open()
    {
        var source = new MemorySource { OmitHierarchyFromAdmission = true };
        var activityId = "activity-undeclared";
        var inspection = NewInspection(activityId);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: [new RuntimeStateChange<ActivityExecutionInspectionProjection>(activityId, RuntimeStateChangeOperation.Upsert, inspection, new Dictionary<string, string>())]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(NewCommit("undeclared-hierarchy") with { StateChanges = changes }, Immediate()).AsTask());

        Assert.Contains("was not admitted", exception.Message, StringComparison.Ordinal);
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "undeclared-hierarchy", "tenant-a"));
    }

    [Fact]
    public async Task Bookmark_lookup_projections_are_delimiter_safe_hashes()
    {
        var source = new MemorySource();
        var bookmark = new BookmarkState(
            "bookmark-1",
            "workflow-1",
            "activity-1",
            "node",
            "resume",
            "type:with:delimiters",
            "hash|with|delimiters",
            null,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            null);
        var changes = new RuntimeCheckpointStateChangeSet(
            null, null, [],
            [new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())],
            [], [], []);

        await NewWriter(source).CommitAsync(NewCommit("bookmark-lookup") with { StateChanges = changes }, Immediate());

        var row = source.LastUnitOfWork!.Staged.Single(write => write.Unit.Id.Value == ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind);
        var pair = (string)row.Values!.Values[ElsaRuntimeV2StorageManifest.StimulusLookupKeyField]!;
        var type = (string)row.Values.Values[ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField]!;
        Assert.NotEqual(bookmark.StimulusHash, pair);
        Assert.NotEqual(bookmark.StimulusType, type);
        Assert.Equal(64, pair.Length);
        Assert.Equal(64, type.Length);
    }

    [SkippableFact]
    [Trait("Category", "Sqlite")]
    public async Task Bookmark_checkpoint_upsert_and_delete_share_the_store_composite_identity()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-bookmark-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            Skip.If(
                !connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
                "The installed SQLite Groundwork package does not evidence AtomicCommit; run with the preview.3 candidate for this vertical gate.");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var accessor = new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var store = new GroundworkV2BookmarkStateStore(source, accessor);
            var otherWorkflowBookmark = new BookmarkState(
                "bookmark-shared",
                "workflow-2",
                "activity-2",
                "node",
                "resume",
                "signal",
                "hash-2",
                null,
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow,
                null);
            await store.SaveAsync(otherWorkflowBookmark);

            var bookmark = new BookmarkState(
                "bookmark-shared",
                "workflow-1",
                "activity-1",
                "node",
                "resume",
                "signal",
                "hash-1",
                null,
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow,
                null);
            var upsert = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())],
                [],
                [],
                []);
            var writer = new GroundworkV2RuntimeCheckpointWriter(source, accessor);
            await writer.CommitAsync(NewCommit("bookmark-composite-upsert") with { StateChanges = upsert }, Immediate());

            var session = source.Open(
                ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
                StorageAccess.Scoped(new StorageScope("tenant-a")));
            Assert.NotNull(session.Read(GroundworkRuntimeRowStore.Key(CompositeBookmarkId(bookmark.WorkflowExecutionId, bookmark.BookmarkId))));
            Assert.Null(session.Read(GroundworkRuntimeRowStore.Key(bookmark.BookmarkId)));
            Assert.NotNull(await store.FindAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId));
            Assert.NotNull(await store.FindAsync(otherWorkflowBookmark.WorkflowExecutionId, otherWorkflowBookmark.BookmarkId));

            var delete = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId, RuntimeStateChangeOperation.Delete, bookmark, new Dictionary<string, string>())],
                [],
                [],
                []);
            await writer.CommitAsync(NewCommit("bookmark-composite-delete") with { StateChanges = delete }, Immediate());

            Assert.Null(await store.FindAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId));
            Assert.NotNull(await store.FindAsync(otherWorkflowBookmark.WorkflowExecutionId, otherWorkflowBookmark.BookmarkId));
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [Fact]
    public async Task The_workflow_row_precedes_the_create_only_marker()
    {
        var source = new MemorySource();
        var writer = NewWriter(source);
        var execution = new WorkflowExecutionState(
            "workflow-1",
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>("workflow-1", RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [],
            []);

        await writer.CommitAsync(NewCommit("ordered") with { StateChanges = stateChanges }, Immediate());

        var staged = source.LastUnitOfWork!.Staged;
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, staged[0].Unit.Id.Value);
        Assert.Equal(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, staged[^1].Unit.Id.Value);
        Assert.Equal(RowWriteMode.Insert, staged[^1].Mode);
        Assert.Equal(WritePreconditionKind.CreateOnly, staged[^1].Options.Precondition.Kind);
        Assert.Contains(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
            source.LastUnitOfWork.AdmittedUnitIds);
    }

    [SkippableFact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_exact_uow_commits_marker_and_replays_through_the_native_provider()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            Skip.If(
                !connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
                "The installed SQLite Groundwork package does not evidence AtomicCommit; run with the preview.2 candidate/local package for this vertical gate.");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var writer = new GroundworkV2RuntimeCheckpointWriter(
                source,
                new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            var commit = NewCommit("sqlite-native");

            await writer.CommitAsync(commit, Immediate());
            await writer.CommitAsync(commit, Immediate());

            Assert.Equal(1, source.UnitOfWorkCount);
            Assert.Equal(BatchWriteOptions.Exact, source.LastOptions);
            Assert.Equal(
                [
                    ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
                    ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
                    ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
                    ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
                    ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                    ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                    ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
                    ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
                    ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind,
                    ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind
                ],
                source.LastUnitIds);
            Assert.NotNull(source.Open(
                ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
                StorageAccess.Scoped(new StorageScope("tenant-a"))).Read(GroundworkRuntimeRowStore.Key(commit.CommitId)));
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_checkpoint_writer_and_dispatch_store_share_the_same_physical_identity()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-dispatch-identity-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            Skip.If(
                !connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
                "The installed SQLite Groundwork package does not evidence AtomicCommit.");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var access = new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var dispatch = NewDispatch("workflow-1", WorkflowDispatchStatus.Pending);
            var changes = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [],
                [],
                [],
                workflowDispatches: [new RuntimeStateChange<WorkflowDispatchRecord>(
                    dispatch.DispatchId,
                    RuntimeStateChangeOperation.Upsert,
                    dispatch,
                    new Dictionary<string, string>())]);

            await new GroundworkV2RuntimeCheckpointWriter(source, access)
                .CommitAsync(NewCommit("dispatch-identity") with { StateChanges = changes }, Immediate());

            var store = new GroundworkV2WorkflowDispatchStore(source, access);
            var found = await store.FindAsync(dispatch.DispatchId);
            Assert.NotNull(found);
            Assert.Equal(dispatch.DispatchId, found!.DispatchId);
            Assert.True(WorkflowDispatchLifecycle.RecordsEqual(dispatch, found));
            Assert.Equal(
                dispatch.DispatchId,
                source.Open(
                        ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                        StorageAccess.Scoped(new StorageScope("tenant-a")))
                    .Read(GroundworkRuntimeRowStore.Key(dispatch.DispatchId))!
                    .Values.Values[ElsaRuntimeV2StorageManifest.IdField]);
            Assert.True(WorkflowDispatchLifecycle.RecordsEqual(dispatch, await store.SaveAsync(dispatch)));
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_native_activity_execution_write_accepts_all_declared_projections()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-activity-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            Skip.If(
                !connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
                "The installed SQLite Groundwork package does not evidence AtomicCommit; run with the preview.2 candidate/local package for this vertical gate.");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var writer = new GroundworkV2RuntimeCheckpointWriter(
                source,
                new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            var activity = NewActivity("activity-native");
            var changes = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [new RuntimeStateChange<ActivityExecutionState>(
                    activity.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    activity,
                    new Dictionary<string, string>())],
                [], [], [], []);

            await writer.CommitAsync(NewCommit("sqlite-activity") with { StateChanges = changes }, Immediate());

            var row = source.Open(
                    ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind,
                    StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2ActivityExecutionStorageConventions.PhysicalId(
                        activity.Execution.WorkflowExecutionId,
                        activity.Execution.ActivityExecutionId)));
            Assert.NotNull(row);
            Assert.Equal(
                activity.Execution.ActivityExecutionId,
                row!.Values.Values[ElsaRuntimeV2StorageManifest.ActivityExecutionIdField]);
            Assert.Equal(
                activity.Execution.WorkflowExecutionId,
                row.Values.Values[ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField]);
            Assert.Equal(
                activity.Status.ToString(),
                row.Values.Values[ElsaRuntimeV2StorageManifest.StatusField]);
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_native_scheduler_consume_uses_claim_compare_and_delete()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-scheduler-consume-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            Skip.If(
                !connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)) ||
                !connection.Capabilities.Any(capability => capability.Id.Equals(BatchWriteCapabilities.CompareAndDelete)),
                "The installed SQLite Groundwork package does not evidence atomic compare-and-delete; run with the W5 Feedz candidate.");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var access = StorageAccess.Scoped(new StorageScope("tenant-a"));
            var workItemId = "sqlite-work-item";
            var physicalId = GroundworkV2SchedulerWorkStorageConventions.PhysicalId("workflow-1", workItemId);
            var values = SchedulerWorkValues(
                "workflow-1",
                workItemId,
                "owner-a",
                1,
                DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Equal(
                WriteOutcomeStatus.Inserted,
                source.Open(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, access).Insert(values).Status);
            var changes = new RuntimeCheckpointStateChangeSet(
                null, null, [], [], [], [], [],
                workflowDispatches: null,
                activityExecutionInspections: null,
                postCommitOutbox: null,
                activityScopeCleanups: null,
                workflowDispatchCancellations: null,
                consumedSchedulerWorkItems: [new ConsumedSchedulerWorkItem("workflow-1", workItemId, "owner-a", 1)]);
            var commit = NewCommit("sqlite-scheduler-consume") with { StateChanges = changes };

            var result = await new GroundworkV2RuntimeCheckpointWriter(
                    source,
                    new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))))
                .CommitAsync(commit, Immediate());

            Assert.Equal([workItemId], result.ConsumedSchedulerWorkItemIds);
            Assert.Null(source.Open(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, access)
                .Read(GroundworkRuntimeRowStore.Key(physicalId)));
            Assert.NotNull(source.Open(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, access)
                .Read(GroundworkRuntimeRowStore.Key(commit.CommitId)));
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [Fact]
    public async Task Every_checkpoint_projection_key_is_declared_by_its_target_unit()
    {
        var source = new MemorySource();
        var now = DateTimeOffset.UtcNow;
        var activity = NewActivity("activity-projection-audit");
        var inspection = NewInspection(activity.Execution.ActivityExecutionId);
        var bookmark = new BookmarkState(
            "bookmark-projection-audit",
            "workflow-1",
            activity.Execution.ActivityExecutionId,
            "node",
            "resume",
            "stimulus-type",
            "stimulus-hash",
            null,
            new Dictionary<string, string>(),
            now,
            null);
        var durableValue = new DurableValueState(
            "durable-projection-audit",
            "workflow-1",
            "value",
            new RuntimeValueTypeDescriptor("reference", "test.value", null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement(new { value = "test" }),
            null,
            activity.Execution.ActivityExecutionId,
            now,
            new Dictionary<string, string>());
        var incident = new IncidentState(
            "incident-projection-audit",
            "workflow-1",
            activity.Execution.ActivityExecutionId,
            "node",
            IncidentSeverity.Error,
            IncidentStatus.Blocking,
            null,
            "TestFailure",
            "projection audit",
            now,
            null,
            new Dictionary<string, string>());
        var operational = new ExecutionLivenessState(
            "operational-projection-audit",
            "workflow-1",
            new RuntimeExecutionLease(
                "lease-projection-audit",
                "workflow-1",
                "owner-projection-audit",
                now.AddMinutes(-1),
                now.AddMinutes(5),
                1),
            null,
            null,
            null);
        var dispatch = NewDispatch("workflow-1", WorkflowDispatchStatus.Pending);
        var intent = new RuntimePostCommitIntent(
            "intent-projection-audit",
            "workflow-1",
            "test.intent",
            now,
            null,
            "idempotency-projection-audit",
            null);
        var outbox = new RuntimePostCommitOutboxItem(
            "outbox-projection-audit",
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            now,
            null);
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                "workflow-1",
                RuntimeStateChangeOperation.Upsert,
                NewExecution("workflow-1"),
                new Dictionary<string, string>()),
            new RuntimeStateChange<SchedulerState>(
                "workflow-1",
                RuntimeStateChangeOperation.Upsert,
                new SchedulerState("workflow-1", 1, pendingWork: []),
                new Dictionary<string, string>()),
            [new RuntimeStateChange<ActivityExecutionState>(
                activity.Execution.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                activity,
                new Dictionary<string, string>())],
            [new RuntimeStateChange<BookmarkState>(
                bookmark.BookmarkId,
                RuntimeStateChangeOperation.Upsert,
                bookmark,
                new Dictionary<string, string>())],
            [new RuntimeStateChange<DurableValueState>(
                durableValue.DurableValueId,
                RuntimeStateChangeOperation.Upsert,
                durableValue,
                new Dictionary<string, string>())],
            [new RuntimeStateChange<IncidentState>(
                incident.IncidentId,
                RuntimeStateChangeOperation.Append,
                incident,
                new Dictionary<string, string>())],
            [new RuntimeStateChange<ExecutionLivenessState>(
                operational.OperationalStateId,
                RuntimeStateChangeOperation.Upsert,
                operational,
                new Dictionary<string, string>())],
            workflowDispatches: [new RuntimeStateChange<WorkflowDispatchRecord>(
                dispatch.DispatchId,
                RuntimeStateChangeOperation.Upsert,
                dispatch,
                new Dictionary<string, string>())],
            activityExecutionInspections: [new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                inspection.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                inspection,
                new Dictionary<string, string>())],
            postCommitOutbox: [new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                outbox.OutboxItemId,
                RuntimeStateChangeOperation.Upsert,
                outbox,
                new Dictionary<string, string>())]);

        await NewWriter(source).CommitAsync(NewCommit("projection-audit") with { StateChanges = changes }, Immediate());

        var expectedUnits = new[]
        {
            ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
            ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind,
            ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind,
            ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
            ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
            ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind,
            ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
            ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
            ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
            ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
            ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind,
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind
        };
        var stagedUnitIds = source.AllStaged.Select(write => write.Unit.Id.Value).ToHashSet(StringComparer.Ordinal);
        Assert.All(expectedUnits, unitId => Assert.Contains(unitId, stagedUnitIds));
        AssertProjectionFieldsDeclared(source.AllStaged);
    }

    [Fact]
    public async Task Run_health_folds_multiple_incidents_once_and_preserves_started_at_on_update()
    {
        var source = new MemorySource();
        var writer = NewWriter(source);
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var workflow = NewExecution("workflow-health") with
        {
            RunKind = WorkflowRunKind.PublishedRun,
            StartedAt = startedAt
        };
        var firstIncident = NewIncident("incident-health-1", workflow.WorkflowExecutionId, startedAt);
        var secondIncident = NewIncident("incident-health-2", workflow.WorkflowExecutionId, startedAt.AddSeconds(1));
        var initialChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                workflow.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                workflow,
                new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [
                new RuntimeStateChange<IncidentState>(firstIncident.IncidentId, RuntimeStateChangeOperation.Append, firstIncident, new Dictionary<string, string>()),
                new RuntimeStateChange<IncidentState>(secondIncident.IncidentId, RuntimeStateChangeOperation.Append, secondIncident, new Dictionary<string, string>())
            ],
            []);
        var initialCommit = NewCommit("health-fold", workflow.WorkflowExecutionId) with { StateChanges = initialChanges };

        await writer.CommitAsync(initialCommit, Immediate());

        var healthUnit = ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind;
        var healthEntry = source.Find(healthUnit, workflow.WorkflowExecutionId, "tenant-a");
        Assert.NotNull(healthEntry);
        var health = DeserializeRunHealth(healthEntry!);
        Assert.Equal(2, health.IncidentCount);
        Assert.Equal(1, health.IncidentBearingCount);
        Assert.Equal(startedAt, health.StartedAt);
        var healthWrites = source.AllStaged.Count(write => write.Unit.Id.Value == healthUnit);
        Assert.Equal(1, healthWrites);

        await writer.CommitAsync(initialCommit, Immediate());
        Assert.Equal(healthWrites, source.AllStaged.Count(write => write.Unit.Id.Value == healthUnit));

        var resolutionTime = startedAt.AddMinutes(1);
        var resolvedIncident = new IncidentState(
            firstIncident.IncidentId,
            firstIncident.WorkflowExecutionId,
            firstIncident.ActivityExecutionId,
            firstIncident.ExecutableNodeId,
            firstIncident.Severity,
            IncidentStatus.Resolved,
            new IncidentResolutionOutcome("test.resolve", resolutionTime, null, "test"),
            firstIncident.FailureType,
            firstIncident.Message,
            firstIncident.CreatedAt,
            resolutionTime,
            firstIncident.Metadata);
        var updatedWorkflow = workflow with
        {
            Status = WorkflowExecutionStatus.Completed,
            StartedAt = startedAt.AddHours(1),
            UpdatedAt = resolutionTime
        };
        var updateChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                workflow.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                updatedWorkflow,
                new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [new RuntimeStateChange<IncidentState>(
                resolvedIncident.IncidentId,
                RuntimeStateChangeOperation.Upsert,
                resolvedIncident,
                new Dictionary<string, string>())],
            []);

        await writer.CommitAsync(
            NewCommit("health-resolution", workflow.WorkflowExecutionId) with { StateChanges = updateChanges },
            Immediate());

        healthEntry = source.Find(healthUnit, workflow.WorkflowExecutionId, "tenant-a");
        Assert.NotNull(healthEntry);
        health = DeserializeRunHealth(healthEntry!);
        Assert.Equal(2, health.IncidentCount);
        Assert.Equal(1, health.IncidentBearingCount);
        Assert.Equal(startedAt, health.StartedAt);
        Assert.Equal(WorkflowExecutionStatus.Completed, health.Status);
        Assert.Equal(2, source.AllStaged.Count(write => write.Unit.Id.Value == healthUnit));
    }

    [Fact]
    public async Task Run_health_projection_rolls_back_with_workflow_and_incidents()
    {
        var source = new MemorySource { FailCommitBeforeApply = true };
        var workflow = NewExecution("workflow-health-rollback") with { RunKind = WorkflowRunKind.PublishedRun };
        var incident = NewIncident("incident-health-rollback", workflow.WorkflowExecutionId, workflow.StartedAt ?? DateTimeOffset.UtcNow);
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                workflow.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                workflow,
                new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [new RuntimeStateChange<IncidentState>(incident.IncidentId, RuntimeStateChangeOperation.Append, incident, new Dictionary<string, string>())],
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(
                NewCommit("health-rollback", workflow.WorkflowExecutionId) with { StateChanges = changes },
                Immediate()).AsTask());

        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, workflow.WorkflowExecutionId, "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, incident.IncidentId, "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind, workflow.WorkflowExecutionId, "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "health-rollback", "tenant-a"));
    }

    [Fact]
    public async Task Run_health_concurrent_revision_change_rejects_the_entire_checkpoint()
    {
        var source = new MemorySource();
        var workflow = NewExecution("workflow-health-race") with
        {
            RunKind = WorkflowRunKind.PublishedRun,
            Status = WorkflowExecutionStatus.Running
        };
        var initialChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                workflow.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                workflow,
                new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [],
            []);
        await NewWriter(source).CommitAsync(
            NewCommit("health-race-initial", workflow.WorkflowExecutionId) with { StateChanges = initialChanges },
            Immediate());

        var healthUnit = ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind;
        var initialHealth = source.Find(healthUnit, workflow.WorkflowExecutionId, "tenant-a")!;
        source.BeforeCommit = _ =>
        {
            source.BeforeCommit = null;
            source.ReplaceRow(
                "tenant-a",
                healthUnit,
                workflow.WorkflowExecutionId,
                initialHealth.Values,
                initialHealth.Version!.Value + 1);
        };
        var completed = workflow with { Status = WorkflowExecutionStatus.Completed };
        var updateChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                completed.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                completed,
                new Dictionary<string, string>()),
            null,
            [],
            [],
            [],
            [],
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(
                NewCommit("health-race-update", completed.WorkflowExecutionId) with { StateChanges = updateChanges },
                Immediate()).AsTask());

        Assert.Equal(
            WorkflowExecutionStatus.Running,
            DeserializeRunHealth(source.Find(healthUnit, workflow.WorkflowExecutionId, "tenant-a")!).Status);
        Assert.Null(source.Find(
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
            "health-race-update",
            "tenant-a"));
    }

    [Fact]
    public async Task Run_health_rejects_an_existing_workflow_without_its_projection()
    {
        var source = new MemorySource();
        var workflow = NewExecution("workflow-health-missing") with { RunKind = WorkflowRunKind.PublishedRun };
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
            workflow.WorkflowExecutionId,
            new { workflowExecutionId = workflow.WorkflowExecutionId },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = workflow.WorkflowExecutionId
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(
                NewCommit("health-missing", workflow.WorkflowExecutionId) with
                {
                    StateChanges = new RuntimeCheckpointStateChangeSet(
                        new RuntimeStateChange<WorkflowExecutionState>(
                            workflow.WorkflowExecutionId,
                            RuntimeStateChangeOperation.Upsert,
                            workflow,
                            new Dictionary<string, string>()),
                        null,
                        [],
                        [],
                        [],
                        [],
                        [])
                },
                Immediate()).AsTask());

        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind, workflow.WorkflowExecutionId, "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "health-missing", "tenant-a"));
    }

    [Fact]
    public async Task Run_health_rejects_a_new_workflow_with_an_orphan_projection()
    {
        var source = new MemorySource();
        var workflow = NewExecution("workflow-health-orphan") with { RunKind = WorkflowRunKind.PublishedRun };
        var startedAt = workflow.StartedAt ?? DateTimeOffset.UtcNow;
        source.SeedRow(
            "tenant-a",
            ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind,
            workflow.WorkflowExecutionId,
            new
            {
                workflowExecutionId = workflow.WorkflowExecutionId,
                definitionId = workflow.PinnedExecutable.DefinitionId,
                runKind = workflow.RunKind,
                startedAt,
                status = workflow.Status,
                incidentCount = 0L,
                incidentBearingCount = 0L
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField] = workflow.PinnedExecutable.DefinitionId,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunKindField] = (int)workflow.RunKind,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField] = startedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField] = (int)workflow.Status,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentCountField] = 0L,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentBearingCountField] = 0L
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewWriter(source).CommitAsync(
                NewCommit("health-orphan", workflow.WorkflowExecutionId) with
                {
                    StateChanges = new RuntimeCheckpointStateChangeSet(
                        new RuntimeStateChange<WorkflowExecutionState>(
                            workflow.WorkflowExecutionId,
                            RuntimeStateChangeOperation.Upsert,
                            workflow,
                            new Dictionary<string, string>()),
                        null,
                        [],
                        [],
                        [],
                        [],
                        [])
                },
                Immediate()).AsTask());

        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, workflow.WorkflowExecutionId, "tenant-a"));
        Assert.Null(source.Find(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "health-orphan", "tenant-a"));
        Assert.NotNull(source.Find(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind, workflow.WorkflowExecutionId, "tenant-a"));
    }

    private static void AssertProjectionFieldsDeclared(IEnumerable<RowWrite> writes)
    {
        Assert.All(
            writes.Where(write => write.Values is not null),
            write => Assert.All(
                write.Values!.Values.Keys,
                field => Assert.Contains(
                    write.Unit.Columns,
                    column => StringComparer.Ordinal.Equals(column.Name, field))));
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public void Sqlite_native_exact_uow_enforces_admission_and_if_version_outcomes()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-checkpoint-uow-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind);
            connection.Schema.Apply(unit);
            var access = StorageAccess.Scoped(new StorageScope("tenant-a"));
            var values = GroundworkRuntimeRowStore.Values(
                "native-uow",
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                "{}",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind
                });

            using (var unitOfWork = connection.BeginUnitOfWork(access, BatchWriteOptions.Exact, [unit]))
            {
                unitOfWork.OpenSession(unit);
                unitOfWork.Stage(RowWrite.Insert(unit, values, WriteOptions.CreateOnly));
                Assert.True(unitOfWork.CommitWithOutcomes().IsSuccessful);
            }

            var entry = connection.OpenSession(unit, access).Read(GroundworkRuntimeRowStore.Key("native-uow"));
            Assert.NotNull(entry);
            Assert.NotNull(entry!.Version);

            using (var staleUnitOfWork = connection.BeginUnitOfWork(access, BatchWriteOptions.Exact, [unit]))
            {
                staleUnitOfWork.Stage(RowWrite.ConditionalUpsert(unit, values, WriteOptions.IfVersion(entry.Version!.Value - 1)));
                var exception = Assert.Throws<BatchWriteException>(staleUnitOfWork.CommitWithOutcomes);
                Assert.Contains(exception.Outcomes, outcome => outcome.Outcome.Status == WriteOutcomeStatus.ConcurrencyConflict);
            }

            var unchanged = connection.OpenSession(unit, access).Read(GroundworkRuntimeRowStore.Key("native-uow"));
            Assert.Equal(entry.Version, unchanged?.Version);
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    private static GroundworkV2RuntimeCheckpointWriter NewWriter(MemorySource source, string scope = "tenant-a") =>
        new(
            source,
            new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope))),
            rootWriteLeaseManager: new TestRootWriteLeaseManager());

    private static RuntimeCheckpointPersistenceDecision Immediate() =>
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private static RuntimeCheckpointCommit NewCommit(string commitId, string workflowExecutionId = "workflow-1") =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint-{commitId}",
                "runtime",
                workflowExecutionId,
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

    private static IncidentState NewIncident(
        string incidentId,
        string workflowExecutionId,
        DateTimeOffset createdAt) =>
        new(
            incidentId,
            workflowExecutionId,
            null,
            "node",
            IncidentSeverity.Error,
            IncidentStatus.Blocking,
            null,
            "TestFailure",
            "test failure",
            createdAt,
            null,
            new Dictionary<string, string>());

    private static RunHealthProjection DeserializeRunHealth(StoredEntry entry) =>
        JsonSerializer.Deserialize<RunHealthProjection>(
            (string)entry.Values.Values[ElsaRuntimeV2StorageManifest.ContentField]!,
            Json) ?? throw new InvalidDataException("Run-health projection content was empty.");

    private sealed record RunHealthProjection(
        string WorkflowExecutionId,
        string DefinitionId,
        WorkflowRunKind RunKind,
        DateTimeOffset? StartedAt,
        WorkflowExecutionStatus Status,
        long IncidentCount,
        long IncidentBearingCount);

    // Independent test oracle for the persisted identity. The production convention is intentionally
    // internal; this keeps the checkpoint-writer/store compatibility assertion from merely reusing it.
    private static string CompositeBookmarkId(string workflowExecutionId, string bookmarkId) =>
        $"{workflowExecutionId.Length}:{workflowExecutionId}{bookmarkId.Length}:{bookmarkId}";

    private static WorkflowExecutionState NewExecution(string workflowExecutionId) =>
        new(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());

    private static ActivityExecutionState NewActivity(string activityExecutionId) =>
        new(
            new ActivityExecution(
                activityExecutionId,
                "workflow-1",
                "node",
                "authored",
                "Activity",
                "1"),
            ActivityExecutionStatus.Completed,
            null,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            1,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

    private static ActivityExecutionInspectionProjection NewInspection(string activityId, string? executionScopeId = null) =>
        new(
            activityId,
            "workflow-1",
            "node",
            "authored",
            "Activity",
            "1",
            ActivityExecutionStatus.Completed,
            null,
            7,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            "checkpoint",
            "checkpoint",
            DateTimeOffset.UtcNow,
            new ActivitySchedulingProvenance(null, null, null, null, null, null, executionScopeId ?? activityId, null, new Dictionary<string, string>()),
            [], [], [], [], new Dictionary<string, string>());

    private static ExecutionLivenessState NewLiveness(string leaseId, string ownerId, long token) =>
        new(
            "ownership:workflow-1",
            "workflow-1",
            new RuntimeExecutionLease(
                leaseId,
                "workflow-1",
                ownerId,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                token),
            null,
            null,
            null);

    private static IReadOnlyDictionary<string, object?> SchedulerWorkProjections(
        string workflowExecutionId,
        string claimOwnerId,
        long fencingToken) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = workflowExecutionId,
            [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
            [ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField] = "order-1",
            [ElsaRuntimeV2StorageManifest.SchedulerWorkClaimOwnerIdField] = claimOwnerId,
            [ElsaRuntimeV2StorageManifest.SchedulerWorkFencingTokenField] = fencingToken
        };

    private static StorageValues SchedulerWorkValues(
        string workflowExecutionId,
        string workItemId,
        string claimOwnerId,
        long fencingToken,
        DateTimeOffset visibleAfter)
    {
        var claimedAt = visibleAfter.AddMinutes(-1);
        using var payload = JsonDocument.Parse($"{{\"workItemId\":\"{workItemId}\"}}");
        var item = new RuntimeSchedulerWorkItem(
            workItemId,
            workflowExecutionId,
            $"command-{workItemId}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"envelope-{workItemId}",
            $"{workflowExecutionId}:{workItemId}",
            claimedAt,
            claimedAt,
            1,
            payload.RootElement.Clone());
        var envelope = GroundworkV2SchedulerWorkStorageConventions.NewEnvelope(item) with
        {
            ClaimOwnerId = claimOwnerId,
            ClaimToken = fencingToken,
            ClaimedAt = claimedAt,
            VisibleAfter = visibleAfter
        };
        return GroundworkV2SchedulerWorkStorageConventions.Values(envelope);
    }

    private static WorkflowDispatchRecord NewDispatch(
        string parentWorkflowExecutionId,
        WorkflowDispatchStatus status,
        WorkflowTestScope? testScope = null)
    {
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, "activity-1");
        var now = DateTimeOffset.UtcNow;
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentWorkflowExecutionId,
            "activity-1",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            new WorkflowExecutableSourceProvenance("source", "kind", "source-id", null, "definition", "version", "1", null, null),
            WorkflowDispatchMode.WaitForCompletion,
            status,
            null,
            "tenant-a",
            new WorkflowExecutionPartition("partition-a"),
            testScope is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            WorkflowExecutionAuthoritySnapshot.CreateRoot("tester"),
            [],
            now,
            now,
            testScope: testScope);
    }

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class TestRootWriteLeaseManager(bool throwLost = false, bool throwUnavailable = false) : IWorkflowExecutableRootWriteLeaseManager
    {
        private readonly bool throwLost = throwLost;
        private readonly bool throwUnavailable = throwUnavailable;

        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) =>
            throwLost
                ? ValueTask.FromException(new WorkflowExecutableRootWriteLeaseLostException(artifactId, leaseId))
                : throwUnavailable
                    ? ValueTask.FromException(new WorkflowExecutableRootWriteLeaseUnavailableException(artifactId, leaseId))
                    : write(cancellationToken);
    }

    private sealed class NativeSessionSource(IStorageProviderConnection connection) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        public int UnitOfWorkCount { get; private set; }
        public BatchWriteOptions? LastOptions { get; private set; }
        public IReadOnlyList<string> LastUnitIds { get; private set; } = [];

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null)
        {
            UnitOfWorkCount++;
            LastOptions = options;
            LastUnitIds = unitIds.ToArray();
            return connection.BeginUnitOfWork(access, options, unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class MemorySource : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        private readonly MemoryBacking backing = new();
        public bool AdvertiseAtomicCommit { get; init; } = true;
        public bool AdvertiseCompareAndDelete { get; init; } = true;
        public bool OmitHierarchyFromAdmission { get; init; }
        public bool FailCommitBeforeApply { get; set; }
        public bool ThrowAfterApply { get; set; }
        public Action<MemoryUnitOfWork>? BeforeCommit { get; set; }
        public int UnitOfWorkCount { get; private set; }
        public MemoryUnitOfWork? LastUnitOfWork { get; private set; }
        public List<RowWrite> AllStaged { get; } = [];

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null)
        {
            var capabilities = new List<CapabilityDescriptor>();
            if (AdvertiseAtomicCommit)
                capabilities.AddRange(WellKnownCapabilities.All);
            if (AdvertiseCompareAndDelete)
                capabilities.Add(BatchWriteCapabilities.CompareAndDeleteDescriptor);
            return capabilities;
        }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new MemorySession(ElsaRuntimeV2StorageManifest.Require(unitId), access, backing);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null)
        {
            UnitOfWorkCount++;
            var admittedUnitIds = OmitHierarchyFromAdmission
                ? unitIds.Where(unitId => unitId != ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind).ToArray()
                : unitIds;
            LastUnitOfWork = new MemoryUnitOfWork(access, backing, this, admittedUnitIds);
            return LastUnitOfWork;
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);

        public StoredEntry? Find(string unitId, string id, string scope) =>
            backing.Read(scope, unitId, id);

        public void SeedRow(string scope, string unitId, string id, object content, IReadOnlyDictionary<string, object?>? projections = null, long version = 1)
        {
            var values = GroundworkRuntimeRowStore.Values(
                id,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                JsonSerializer.Serialize(content, content.GetType(), Json),
                projections);
            backing.Write(scope, unitId, id, values, version);
        }

        public void ReplaceRow(string scope, string unitId, string id, StorageValues values, long version) =>
            backing.Write(scope, unitId, id, values, version);

        public void SeedLiveness(ExecutionLivenessState state, long version = 1)
        {
            var operationalStateId = state.OperationalStateId;
            var identity = $"{state.WorkflowExecutionId.Length}:{state.WorkflowExecutionId}{operationalStateId}";
            var content = JsonSerializer.Serialize(new
            {
                collection = ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
                workflowExecutionId = state.WorkflowExecutionId,
                hasOperationalOwner = true,
                state
            }, Json);
            var values = GroundworkRuntimeRowStore.Values(
                identity,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                content,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                    [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
                    [ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField] = operationalStateId,
                    [ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField] = null,
                    [ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField] = null,
                    [ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField] = true,
                    [ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField] = state.ExecutionLease!.OwnerId,
                    [ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField] = state.ExecutionLease.AcquiredAt,
                    [ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField] = state.ExecutionLease.ExpiresAt,
                    [ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField] = null,
                    [ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField] = null
                });
            backing.Write("tenant-a", ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, identity, values, version);
        }

        public sealed class MemoryBacking
        {
            private readonly Dictionary<(string Scope, string Unit, string Id), StoredEntry> rows = [];

            public StoredEntry? Read(string scope, string unit, string id) =>
                rows.GetValueOrDefault((scope, unit, id));

            public void Write(string scope, string unit, string id, StorageValues values, long version) =>
                rows[(scope, unit, id)] = new StoredEntry(values, version);

            public void Delete(string scope, string unit, string id) => rows.Remove((scope, unit, id));
        }

        private sealed class MemorySession(StorageUnit unit, StorageAccess access, MemoryBacking backing) : IStorageSession
        {
            public StorageUnit Unit { get; } = unit;
            public StorageAccess Access { get; } = access;

            public StoredEntry? Read(StorageKey key) =>
                backing.Read(Access.Scope!.Value, Unit.Id.Value, (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!);

            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
            public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
        }

        public sealed class MemoryUnitOfWork(
            StorageAccess access,
            MemoryBacking backing,
            MemorySource owner,
            IReadOnlyList<string> admittedUnitIds) : IUnitOfWork
        {
            public List<RowWrite> Staged { get; } = [];
            public IReadOnlyList<string> AdmittedUnitIds { get; } = admittedUnitIds.ToArray();
            private bool rolledBack;

            public IStorageSession OpenSession(StorageUnit unit)
            {
                if (!AdmittedUnitIds.Contains(unit.Id.Value, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Unit '{unit.Id.Value}' was not admitted to the exact UOW.");
                return new MemorySession(unit, access, backing);
            }

            public void Stage(RowWrite write)
            {
                if (rolledBack)
                    throw new InvalidOperationException("The unit of work has rolled back.");
                if (!AdmittedUnitIds.Contains(write.Unit.Id.Value, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Unit '{write.Unit.Id.Value}' was not admitted to the exact UOW.");
                Staged.Add(write);
                owner.AllStaged.Add(write);
            }

            public BatchWriteSummary Commit() => CommitWithOutcomes().Summary;

            public BatchWriteReport CommitWithOutcomes()
            {
                if (owner.FailCommitBeforeApply)
                    throw new InvalidOperationException("simulated atomic failure");
                owner.BeforeCommit?.Invoke(this);
                var failures = Staged
                    .Select(write => (Write: write, Status: FailureStatus(write)))
                    .Where(item => item.Status is not null)
                    .ToDictionary(item => item.Write, item => item.Status!.Value);
                if (failures.Count > 0)
                {
                    return new BatchWriteReport(Staged.Select(write => new RowWriteOutcome(
                        write,
                        new WriteOutcome(
                            failures.GetValueOrDefault(write, SuccessfulStatus(write)),
                            failures.ContainsKey(write) ? null : 1))).ToArray());
                }
                foreach (var write in Staged)
                    Apply(write);
                if (owner.ThrowAfterApply)
                {
                    owner.ThrowAfterApply = false;
                    throw new InvalidOperationException("simulated ambiguous acknowledgement");
                }
                return new BatchWriteReport(Staged.Select(write => new RowWriteOutcome(write, new WriteOutcome(
                    SuccessfulStatus(write), 1))).ToArray());
            }

            public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(CommitWithOutcomes());

            public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(CommitWithOutcomes().Summary);

            public void Rollback()
            {
                rolledBack = true;
                Staged.Clear();
            }

            public void Dispose() { }

            private void Apply(RowWrite write)
            {
                var id = RowId(write);
                var existing = backing.Read(access.Scope!.Value, write.Unit.Id.Value, id);
                if (write.Options.Precondition.Kind == WritePreconditionKind.CreateOnly && existing is not null)
                    throw new InvalidOperationException("create-only conflict");
                if (write.Mode is RowWriteMode.Delete or RowWriteMode.CompareAndDelete)
                {
                    backing.Delete(access.Scope.Value, write.Unit.Id.Value, id);
                    return;
                }

                var version = (existing?.Version ?? 0) + 1;
                backing.Write(access.Scope.Value, write.Unit.Id.Value, id, write.Values!, version);
            }

            private WriteOutcomeStatus? FailureStatus(RowWrite write)
            {
                var id = RowId(write);
                var existing = backing.Read(access.Scope!.Value, write.Unit.Id.Value, id);
                WriteOutcomeStatus? preconditionFailure = write.Options.Precondition.Kind switch
                {
                    WritePreconditionKind.Unconditional => null,
                    WritePreconditionKind.CreateOnly when existing is not null => WriteOutcomeStatus.ConcurrencyConflict,
                    WritePreconditionKind.IfVersion when existing?.Version != write.Options.Precondition.Version => WriteOutcomeStatus.ConcurrencyConflict,
                    WritePreconditionKind.CreateOnly or WritePreconditionKind.IfVersion => null,
                    _ => WriteOutcomeStatus.ConcurrencyConflict
                };
                if (preconditionFailure is not null || write.Mode != RowWriteMode.CompareAndDelete)
                    return preconditionFailure;
                if (existing is null)
                    return WriteOutcomeStatus.NotFound;
                return write.ExpectedValues.Any(expected =>
                        !existing.Values.Values.TryGetValue(expected.Key, out var actual) ||
                        !Equals(actual, expected.Value))
                    ? WriteOutcomeStatus.ComparisonMismatch
                    : null;
            }

            private static string RowId(RowWrite write) =>
                write.Mode is RowWriteMode.Delete or RowWriteMode.CompareAndDelete
                    ? (string)write.Key!.Values[ElsaRuntimeV2StorageManifest.IdField]!
                    : (string)write.Values!.Values[ElsaRuntimeV2StorageManifest.IdField]!;

            private static WriteOutcomeStatus SuccessfulStatus(RowWrite write) =>
                write.Mode is RowWriteMode.Delete or RowWriteMode.CompareAndDelete
                    ? WriteOutcomeStatus.Deleted
                    : WriteOutcomeStatus.Upserted;
        }
    }
}
