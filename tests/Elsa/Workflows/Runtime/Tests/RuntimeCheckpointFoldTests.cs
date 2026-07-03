using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Unit coverage for RuntimeCheckpointFold (W9, E3-6/RT-10): last-writer-wins collapse for Upsert/Delete,
// Append accumulation, and outbox exclusion from the folded state.
public sealed class RuntimeCheckpointFoldTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fold_KeepsLastWorkflowAndSchedulerState()
    {
        var first = NewChangeSet(WorkflowState(WorkflowExecutionStatus.Running));
        var second = NewChangeSet(WorkflowState(WorkflowExecutionStatus.Completed));

        var folded = RuntimeCheckpointFold.Fold([first, second]);

        Assert.NotNull(folded.WorkflowExecution);
        Assert.Equal(WorkflowExecutionStatus.Completed, folded.WorkflowExecution!.State.Status);
    }

    [Fact]
    public void Fold_CollapsesActivityUpsertsToLastWriterPerStateId()
    {
        var running = NewChangeSet(activities: [ActivityChange("actexec-1", ActivityExecutionStatus.Running)]);
        var completed = NewChangeSet(activities:
        [
            ActivityChange("actexec-1", ActivityExecutionStatus.Completed),
            ActivityChange("actexec-2", ActivityExecutionStatus.Running),
        ]);

        var folded = RuntimeCheckpointFold.Fold([running, completed]);

        Assert.Equal(2, folded.ActivityExecutions.Count);
        var first = folded.ActivityExecutions.Single(c => c.StateId == "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Completed, first.State.Status);
    }

    [Fact]
    public void Fold_LastDeleteWinsOverEarlierUpsert()
    {
        var upsert = NewChangeSet(activities: [ActivityChange("actexec-1", ActivityExecutionStatus.Running)]);
        var delete = NewChangeSet(activities: [ActivityChange("actexec-1", ActivityExecutionStatus.Completed, RuntimeStateChangeOperation.Delete)]);

        var folded = RuntimeCheckpointFold.Fold([upsert, delete]);

        var change = Assert.Single(folded.ActivityExecutions);
        Assert.Equal(RuntimeStateChangeOperation.Delete, change.Operation);
    }

    [Fact]
    public void Fold_DoesNotCollapseAppendBookmarks()
    {
        var first = NewChangeSet(bookmarks: [BookmarkChange("bookmark-1", RuntimeStateChangeOperation.Append)]);
        var second = NewChangeSet(bookmarks: [BookmarkChange("bookmark-1", RuntimeStateChangeOperation.Append)]);

        var folded = RuntimeCheckpointFold.Fold([first, second]);

        Assert.Equal(2, folded.Bookmarks.Count);
    }

    [Fact]
    public void Fold_DropsPostCommitOutbox()
    {
        var withOutbox = NewChangeSet().WithPostCommitOutbox([OutboxChange("outbox-1")]);

        var folded = RuntimeCheckpointFold.Fold([withOutbox]);

        Assert.Empty(folded.PostCommitOutbox);
    }

    private static RuntimeCheckpointStateChangeSet NewChangeSet(
        WorkflowExecutionState? workflowState = null,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>>? activities = null,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>>? bookmarks = null) =>
        new(
            workflowExecution: workflowState is null
                ? null
                : new RuntimeStateChange<WorkflowExecutionState>(workflowState.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, workflowState, Empty),
            scheduler: null,
            activityExecutions: activities ?? [],
            bookmarks: bookmarks ?? [],
            durableValues: [],
            incidents: [],
            operational: []);

    private static RuntimeStateChange<ActivityExecutionState> ActivityChange(
        string activityExecutionId,
        ActivityExecutionStatus status,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert) =>
        new(
            StateId: activityExecutionId,
            Operation: operation,
            State: new ActivityExecutionState(
                Execution: new ActivityExecution(
                    ActivityExecutionId: activityExecutionId,
                    WorkflowExecutionId: "wfexec-1",
                    ExecutableNodeId: "node-1",
                    AuthoredActivityId: "authored-1",
                    ActivityType: "test/activity",
                    ActivityTypeVersion: "1.0.0"),
                Status: status,
                SubStatus: null,
                ScheduledAt: Now,
                StartedAt: Now,
                CompletedAt: status == ActivityExecutionStatus.Completed ? Now : null,
                SchedulingActivityExecutionId: null,
                ParentActivityExecutionId: null,
                BranchId: "branch-1",
                IterationId: null,
                CallStackDepth: 0,
                BookmarkIds: [],
                IncidentIds: [],
                FaultCount: 0,
                AggregateFaultCount: 0,
                Metadata: Empty),
            Metadata: Empty);

    private static RuntimeStateChange<BookmarkState> BookmarkChange(string bookmarkId, RuntimeStateChangeOperation operation) =>
        new(
            StateId: bookmarkId,
            Operation: operation,
            State: new BookmarkState(
                BookmarkId: bookmarkId,
                WorkflowExecutionId: "wfexec-1",
                ActivityExecutionId: "actexec-1",
                ExecutableNodeId: "node-1",
                ResumeTargetId: "node-resume-1",
                StimulusType: "delivery-status",
                StimulusHash: "sha256:test",
                Payload: JsonSerializer.SerializeToElement(new { expected = "delivered" }),
                Metadata: Empty,
                CreatedAt: Now,
                ExpiresAt: null),
            Metadata: Empty);

    private static RuntimeStateChange<RuntimePostCommitOutboxItem> OutboxChange(string outboxItemId)
    {
        var intent = new RuntimePostCommitIntent(
            intentId: "intent-1",
            workflowExecutionId: "wfexec-1",
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: Now,
            activityExecutionId: null,
            idempotencyKey: "idem-1",
            payload: null);
        var item = new RuntimePostCommitOutboxItem(
            outboxItemId: outboxItemId,
            intent: intent,
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: Now,
            availableAt: Now,
            retryPolicy: RuntimePostCommitRetryPolicy.None,
            metadata: Empty);
        return new RuntimeStateChange<RuntimePostCommitOutboxItem>(outboxItemId, RuntimeStateChangeOperation.Upsert, item, Empty);
    }

    private static WorkflowExecutionState WorkflowState(WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: status == WorkflowExecutionStatus.Completed ? Now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: Empty);

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
}
