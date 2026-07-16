using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class WorkflowDispatchStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowDispatchIdentity DispatchIdentity = new("parent-1", "activity-1");

    [Fact]
    public void Identity_IsStableForReplayAndDistinctPerActivityExecution()
    {
        var first = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var replay = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var nextActivity = new WorkflowDispatchIdentity("parent-1", "activity-2");

        Assert.Equal(first.DispatchId, replay.DispatchId);
        Assert.Equal(first.ChildWorkflowExecutionId, replay.ChildWorkflowExecutionId);
        Assert.Equal(first.StartIntentId, replay.StartIntentId);
        Assert.Equal(first.StartIdempotencyKey, replay.StartIdempotencyKey);
        Assert.NotEqual(first.DispatchId, nextActivity.DispatchId);
        Assert.NotEqual(first.ChildWorkflowExecutionId, nextActivity.ChildWorkflowExecutionId);
        Assert.Equal(64, first.DispatchId.Split(':')[^1].Length);
        Assert.Equal(4, new[]
        {
            first.DispatchId,
            first.ChildWorkflowExecutionId,
            first.StartIntentId,
            first.StartIdempotencyKey
        }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Record_CanonicalizesSafeInputDescriptorsWithoutRawValues()
    {
        var record = NewRecord(inputDescriptors:
        [
            new WorkflowDispatchInputDescriptor("zeta", "System.Int32"),
            new WorkflowDispatchInputDescriptor("alpha", "System.String")
        ]);

        Assert.Equal(["alpha", "zeta"], record.InputDescriptors.Select(descriptor => descriptor.Name));
        Assert.DoesNotContain("raw-secret", JsonSerializer.Serialize(record), StringComparison.Ordinal);
    }

    [Fact]
    public void StateChangeSet_RejectsMismatchedDispatchIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RuntimeCheckpointStateChangeSet(
            workflowExecution: null,
            scheduler: null,
            activityExecutions: [],
            bookmarks: [],
            durableValues: [],
            incidents: [],
            operational: [],
            workflowDispatches: [Change(NewRecord(), stateId: "wrong-dispatch")]));

        Assert.Equal("workflowDispatches", exception.ParamName);
    }

    [Fact]
    public void CheckpointRequest_RejectsIntentOutsideDeterministicDispatchIdentity()
    {
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var record = NewRecord(
            dispatchId: identity.DispatchId,
            childWorkflowExecutionId: identity.ChildWorkflowExecutionId);
        var wrongIntent = new RuntimePostCommitIntent(
            intentId: "wrong-intent",
            workflowExecutionId: "parent-1",
            kind: "test.start-child",
            recordedAt: Now,
            activityExecutionId: "activity-1",
            idempotencyKey: identity.StartIdempotencyKey,
            payload: null);

        var exception = Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchCheckpointRequest(record, wrongIntent));

        Assert.Equal("startIntent", exception.ParamName);
    }

    [Fact]
    public async Task InMemoryCheckpoint_ProjectsDispatchAndConvergesEquivalentReplay()
    {
        var sharedState = new InMemoryRuntimeCheckpointStoreState();
        IWorkflowDispatchStore dispatchStore = new InMemoryWorkflowDispatchStore(sharedState);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            state: sharedState,
            workflowDispatchStore: dispatchStore);
        var commit = NewCommit("commit-1", NewRecord());

        await checkpointStore.CommitAsync(commit, Decision());
        await checkpointStore.CommitAsync(commit, Decision());

        var projected = await dispatchStore.FindAsync(DispatchIdentity.DispatchId);
        Assert.NotNull(projected);
        Assert.Equal(DispatchIdentity.ChildWorkflowExecutionId, projected.ChildWorkflowExecutionId);
        Assert.Single(checkpointStore.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpoint_RejectsConflictingRecordBeforeReplacingProjection()
    {
        var sharedState = new InMemoryRuntimeCheckpointStoreState();
        IWorkflowDispatchStore dispatchStore = new InMemoryWorkflowDispatchStore(sharedState);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            state: sharedState,
            workflowDispatchStore: dispatchStore);
        await checkpointStore.CommitAsync(NewCommit("commit-1", NewRecord()), Decision());
        var conflict = NewRecord(correlationId: "correlation-conflict");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            checkpointStore.CommitAsync(NewCommit("commit-2", conflict), Decision()).AsTask());

        Assert.Contains("conflicting immutable identity", exception.Message, StringComparison.Ordinal);
        Assert.Equal(DispatchIdentity.ChildWorkflowExecutionId, (await dispatchStore.FindAsync(DispatchIdentity.DispatchId))!.ChildWorkflowExecutionId);
        Assert.Single(checkpointStore.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpoint_AllowsMonotonicLifecycleTransitionWithoutChangingIdentity()
    {
        var sharedState = new InMemoryRuntimeCheckpointStoreState();
        IWorkflowDispatchStore dispatchStore = new InMemoryWorkflowDispatchStore(sharedState);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            state: sharedState,
            workflowDispatchStore: dispatchStore);
        var pending = NewRecord();
        await checkpointStore.CommitAsync(NewCommit("commit-1", pending), Decision());
        var started = NewRecord(
            status: WorkflowDispatchStatus.Started,
            updatedAt: Now.AddSeconds(1));

        await checkpointStore.CommitAsync(NewCommit("commit-2", started), Decision());

        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatchStore.FindAsync(DispatchIdentity.DispatchId))!.Status);
        Assert.Equal(2, checkpointStore.ListCommits().Count);
    }

    private static RuntimeCheckpointCommit NewCommit(string commitId, WorkflowDispatchRecord record) =>
        new(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{commitId}",
                Name: "ActivityCompleted",
                WorkflowExecutionId: record.ParentWorkflowExecutionId,
                OccurredAt: Now,
                ActivityExecutionIds: [record.ParentActivityExecutionId],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                workflowDispatches: [Change(record)]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static RuntimeStateChange<WorkflowDispatchRecord> Change(
        WorkflowDispatchRecord record,
        string? stateId = null) =>
        new(stateId ?? record.DispatchId, RuntimeStateChangeOperation.Upsert, record, new Dictionary<string, string>());

    private static RuntimeCheckpointPersistenceDecision Decision() =>
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private static WorkflowDispatchRecord NewRecord(
        IReadOnlyCollection<WorkflowDispatchInputDescriptor>? inputDescriptors = null,
        WorkflowDispatchStatus status = WorkflowDispatchStatus.Pending,
        DateTimeOffset? updatedAt = null,
        string? dispatchId = null,
        string? childWorkflowExecutionId = null,
        string? correlationId = "correlation-1") =>
        new(
            dispatchId: dispatchId ?? DispatchIdentity.DispatchId,
            parentWorkflowExecutionId: "parent-1",
            parentActivityExecutionId: "activity-1",
            childWorkflowExecutionId: childWorkflowExecutionId ?? DispatchIdentity.ChildWorkflowExecutionId,
            childExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            childSource: new WorkflowExecutableSourceProvenance(
                "source-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
                "definition-1", "version-1", "1.0.0", "publication-1", "slot-1"),
            mode: WorkflowDispatchMode.FireAndForget,
            status: status,
            correlationId: correlationId,
            tenantId: "tenant-1",
            partition: new WorkflowExecutionPartition("partition-1"),
            runKind: WorkflowRunKind.PublishedRun,
            authority: new WorkflowExecutionAuthoritySnapshot("parent-1", "initiator-1"),
            inputDescriptors: inputDescriptors ?? [new WorkflowDispatchInputDescriptor("message", "System.String")],
            createdAt: Now,
            updatedAt: updatedAt ?? Now,
            metadata: new Dictionary<string, string> { ["runtime.source"] = "DispatchWorkflow" });
}
