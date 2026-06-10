using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeActivityCompletionPropagationContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchedulerState_CarriesCompletionDrainWorkSeparatelyFromActivitySchedulingAndContinuations()
    {
        var scheduler = new SchedulerState(
            workflowExecutionId: "wfexec-1",
            version: 5,
            pendingWork:
            [
                new ScheduledActivityWorkItem(
                    WorkItemId: "activity-work-1",
                    WorkflowExecutionId: "wfexec-1",
                    ExecutableNodeId: "node-next",
                    ActivityExecutionId: "actexec-next",
                    SchedulingActivityExecutionId: "actexec-parent",
                    BranchId: "branch-a",
                    IterationId: null,
                    EnqueuedAt: _now.AddSeconds(2),
                    Reason: "ActivityScheduled")
            ],
            pendingCompletionWork:
            [
                NewActivityCompleted(sequence: 1)
            ],
            pendingContinuations:
            [
                new SchedulerContinuationWorkItem(
                    workItemId: "continuation-1",
                    workflowExecutionId: "wfexec-1",
                    activityExecutionId: "actexec-wait",
                    branchId: "branch-a",
                    kind: SchedulerContinuationKind.VolatileWaitCompleted,
                    volatileWaitId: "wait-1",
                    enqueuedAt: _now.AddSeconds(1),
                    reason: "VolatileWaitCompleted")
            ]);

        Assert.Single(scheduler.PendingWork);
        Assert.Single(scheduler.PendingContinuations);
        var completion = Assert.Single(scheduler.PendingCompletionWork);
        Assert.Equal(SchedulerCompletionKind.ActivityCompleted, completion.Kind);
        Assert.Equal("actexec-child", completion.SubjectActivityExecutionId);
    }

    [Fact]
    public void CompletionDrainWork_ModelsParentEvaluationWithCompletedChildIdentity()
    {
        var workItem = new SchedulerCompletionWorkItem(
            workItemId: "completion-2",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-parent",
            completedChildActivityExecutionId: "actexec-child",
            branchId: "branch-a",
            kind: SchedulerCompletionKind.ParentCompletionEvaluation,
            sequence: 2,
            enqueuedAt: _now,
            reason: "ParentCompletionEvaluation",
            outcomeNames: ["Done"],
            requiredCompletedActivityExecutionIds: ["actexec-child", "actexec-sibling"],
            metadata: new Dictionary<string, string> { ["Join"] = "all" });

        Assert.Equal("actexec-parent", workItem.SubjectActivityExecutionId);
        Assert.Equal("actexec-child", workItem.CompletedChildActivityExecutionId);
        Assert.Equal(2, workItem.Sequence);
        Assert.Equal(["Done"], workItem.OutcomeNames);
        Assert.Equal(["actexec-child", "actexec-sibling"], workItem.RequiredCompletedActivityExecutionIds);
        Assert.Equal("all", workItem.Metadata["Join"]);
    }

    [Fact]
    public void CompletionDrainWork_ModelsContinuationSchedulingWithoutOrdinaryActivityScheduling()
    {
        var workItem = new SchedulerCompletionWorkItem(
            workItemId: "completion-3",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-parent",
            completedChildActivityExecutionId: null,
            branchId: "branch-a",
            kind: SchedulerCompletionKind.ContinuationScheduling,
            sequence: 3,
            enqueuedAt: _now,
            reason: "ContinuationScheduling",
            outcomeNames: ["Done"]);

        Assert.Equal(SchedulerCompletionKind.ContinuationScheduling, workItem.Kind);
        Assert.Null(workItem.CompletedChildActivityExecutionId);
        Assert.Equal(["Done"], workItem.OutcomeNames);
    }

    [Fact]
    public void ParentCompletionEvaluation_RequiresCompletedChildIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SchedulerCompletionWorkItem(
            workItemId: "completion-2",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-parent",
            completedChildActivityExecutionId: null,
            branchId: "branch-a",
            kind: SchedulerCompletionKind.ParentCompletionEvaluation,
            sequence: 2,
            enqueuedAt: _now,
            reason: "ParentCompletionEvaluation"));

        Assert.Contains("completed child", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SchedulerCompletionKind.ActivityCompleted)]
    [InlineData(SchedulerCompletionKind.ContinuationScheduling)]
    public void NonParentEvaluationWork_RejectsCompletedChildIdentity(SchedulerCompletionKind kind)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SchedulerCompletionWorkItem(
            workItemId: "completion-1",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-child",
            completedChildActivityExecutionId: "actexec-other",
            branchId: "branch-a",
            kind: kind,
            sequence: 1,
            enqueuedAt: _now,
            reason: "Completion"));

        Assert.Contains("only valid", exception.Message);
    }

    [Fact]
    public void CompletionDrainWork_RejectsBlankJoinPrerequisites()
    {
        Assert.Throws<ArgumentException>(() => new SchedulerCompletionWorkItem(
            workItemId: "completion-2",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-parent",
            completedChildActivityExecutionId: "actexec-child",
            branchId: "branch-a",
            kind: SchedulerCompletionKind.ParentCompletionEvaluation,
            sequence: 2,
            enqueuedAt: _now,
            reason: "ParentCompletionEvaluation",
            requiredCompletedActivityExecutionIds: ["actexec-child", " "]));
    }

    [Fact]
    public void ParentCompletionEvaluation_RequiresJoinPrerequisitesToIncludeCompletedChild()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SchedulerCompletionWorkItem(
            workItemId: "completion-2",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-parent",
            completedChildActivityExecutionId: "actexec-child",
            branchId: "branch-a",
            kind: SchedulerCompletionKind.ParentCompletionEvaluation,
            sequence: 2,
            enqueuedAt: _now,
            reason: "ParentCompletionEvaluation",
            requiredCompletedActivityExecutionIds: ["actexec-sibling"]));

        Assert.Contains("completed child", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SchedulerCompletionKind.ActivityCompleted)]
    [InlineData(SchedulerCompletionKind.ContinuationScheduling)]
    public void NonParentEvaluationWork_RejectsJoinPrerequisites(SchedulerCompletionKind kind)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SchedulerCompletionWorkItem(
            workItemId: "completion-1",
            workflowExecutionId: "wfexec-1",
            subjectActivityExecutionId: "actexec-child",
            completedChildActivityExecutionId: null,
            branchId: "branch-a",
            kind: kind,
            sequence: 1,
            enqueuedAt: _now,
            reason: "Completion",
            requiredCompletedActivityExecutionIds: ["actexec-child"]));

        Assert.Contains("only valid", exception.Message);
    }

    [Fact]
    public void SchedulerState_RecordCopyNormalizesCompletionWorkAssignments()
    {
        var scheduler = new SchedulerState(
            workflowExecutionId: "wfexec-1",
            version: 1);
        var completionWork = new List<SchedulerCompletionWorkItem> { NewActivityCompleted(sequence: 1) };

        var copied = scheduler with { PendingCompletionWork = completionWork };
        completionWork.Clear();

        Assert.Single(copied.PendingCompletionWork);
    }

    private SchedulerCompletionWorkItem NewActivityCompleted(long sequence) => new(
        workItemId: $"completion-{sequence}",
        workflowExecutionId: "wfexec-1",
        subjectActivityExecutionId: "actexec-child",
        completedChildActivityExecutionId: null,
        branchId: "branch-a",
        kind: SchedulerCompletionKind.ActivityCompleted,
        sequence: sequence,
        enqueuedAt: _now,
        reason: "ActivityCompleted",
        outcomeNames: ["Done"]);
}
