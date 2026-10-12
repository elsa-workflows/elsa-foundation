using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The structural half of the checkpoint commit contract, run identically against every
/// <see cref="IRuntimeCheckpointCommitStore"/>. The application layer (<see cref="RuntimeCheckpointCommitter"/>) owns
/// these rules, so an invalid commit must be rejected with the same exception and message whichever store is registered,
/// and the store must never be reached. A valid commit carrying every validated state kind must commit on every store, so
/// the rules cannot quietly reject what a store accepts either.
/// </summary>
/// <remarks>
/// Adding a structural rule means adding a case to <see cref="InvalidCases"/>. Rules that read persisted state are covered
/// by <see cref="RuntimeCheckpointCommitStatefulContractTests"/>.
/// </remarks>
public sealed class RuntimeCheckpointCommitValidationContractTests
{
    private const string WaitingParentId = "workflow-parent";
    private const string WaitedActivityId = "activity-waited";
    private static readonly string WaitedChildId = new WorkflowDispatchIdentity(WaitingParentId, WaitedActivityId).ChildWorkflowExecutionId;

    public static TheoryData<string> StoreData => RuntimeCheckpointCommitContract.StoreData();

    public static TheoryData<string, string> InvalidCommitData => StoreCaseData(InvalidCases.Keys);

    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task A_valid_commit_carrying_every_validated_kind_commits(string store)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);

        var result = await backend.Committer.CommitAsync(new CommitParts().Build());

        Assert.True(result.Succeeded);
        Assert.Single(result.PendingPostCommitWorkIds);
        Assert.Equal(1, backend.Store.Calls);
        Assert.Equal(1, await backend.CountMarkersAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidCommitData))]
    public async Task An_invalid_commit_is_rejected_before_the_store_with_the_same_error(string store, string caseName)
    {
        var invalid = InvalidCases[caseName];
        var commit = invalid.Build();
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);

        var exception = await Assert.ThrowsAsync(invalid.ExceptionType, () => backend.Committer.CommitAsync(commit).AsTask());

        Assert.Equal(invalid.Message, exception.Message);
        Assert.Equal(0, backend.Store.Calls);
        Assert.Equal(0, await backend.CountMarkersAsync());
    }

    private sealed record InvalidCase(Func<RuntimeCheckpointCommit> Build, Type ExceptionType, string Message);

    /// <summary>A structural rule violation: the validator refuses it with the checkpoint validation type.</summary>
    private static InvalidCase Invalid(Func<RuntimeCheckpointCommit> build, string message) =>
        new(build, typeof(RuntimeCheckpointCommitValidationException), message);

    private static Func<RuntimeCheckpointCommit> Mutated(Action<CommitParts> mutate) => () =>
    {
        var parts = new CommitParts();
        mutate(parts);
        return parts.Build();
    };

    private static string OperationMessage(string kind, string allowed, RuntimeStateChangeOperation actual) =>
        $"A checkpoint commit can only carry {kind} {allowed} changes, not '{actual}'.";

    private static string WorkflowMessage(string subject) =>
        $"{subject} WorkflowExecutionId '{OtherWorkflowId}' must match the checkpoint workflow execution ID '{WorkflowId}'.";

    /// <summary>A waited child's terminal commit carrying a resume intent for <paramref name="intentTarget"/>.</summary>
    private static RuntimeCheckpointCommit ChildTerminalCommit(string intentTarget, params WorkflowDispatchRecord[] dispatches) =>
        Commit(
            WaitedChildId,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], dispatches.Select(DispatchChange).ToArray()),
            [Intent("intent-parent-resume", intentTarget)],
            "commit-child");

    private static WorkflowDispatchRecord WaitedDispatch(string parent, string activity, WorkflowDispatchStatus status) =>
        PendingDispatch(parent, activity, mode: WorkflowDispatchMode.WaitForCompletion).TransitionTo(status, OccurredAt.AddSeconds(1));

    private static string UnlinkedIntentMessage(string target) =>
        $"Post-commit outbox item '{RuntimePostCommitOutboxIdentity.CreateLogicalValue("commit-child", "intent-parent-resume")}' targets workflow execution '{target}', " +
        $"which is neither the checkpoint workflow execution '{WaitedChildId}' nor the parent of a terminal workflow dispatch for it in this commit.";

    private static string BlankArgumentMessage(string value, string parameterName)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        throw new InvalidOperationException("The value was expected to be blank.");
    }

    private static readonly IReadOnlyDictionary<string, InvalidCase> InvalidCases = new Dictionary<string, InvalidCase>
    {
        ["workflow-execution-delete"] = Invalid(
            Mutated(parts => parts.WorkflowExecution = parts.WorkflowExecution! with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("workflow execution", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["workflow-execution-state-id"] = Invalid(
            Mutated(parts => parts.WorkflowExecution = parts.WorkflowExecution! with { StateId = "workflow-stale" }),
            "Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId."),
        ["workflow-execution-other-workflow"] = Invalid(
            Mutated(parts => parts.WorkflowExecution = Change(OtherWorkflowId, Execution(OtherWorkflowId))),
            WorkflowMessage("Workflow execution state change")),

        ["scheduler-delete"] = Invalid(
            Mutated(parts => parts.Scheduler = parts.Scheduler! with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("scheduler", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["scheduler-state-id"] = Invalid(
            Mutated(parts => parts.Scheduler = parts.Scheduler! with { StateId = "workflow-stale" }),
            "Scheduler state change StateId must match SchedulerState.WorkflowExecutionId."),
        ["scheduler-other-workflow"] = Invalid(
            Mutated(parts => parts.Scheduler = Change(OtherWorkflowId, new SchedulerState(OtherWorkflowId, 1))),
            WorkflowMessage("Scheduler state change")),

        ["activity-execution-append"] = Invalid(
            Mutated(parts => parts.ActivityExecutions[0] = parts.ActivityExecutions[0] with { Operation = RuntimeStateChangeOperation.Append }),
            OperationMessage("activity execution", "'Upsert'", RuntimeStateChangeOperation.Append)),
        ["activity-execution-other-workflow"] = Invalid(
            Mutated(parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a", OtherWorkflowId))),
            WorkflowMessage("Activity execution state change")),
        ["activity-execution-value-flow-version"] = Invalid(
            Mutated(parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with
            {
                DocumentVersion = ActivityExecutionValueFlowDocumentVersions.Current + 1
            })),
            $"Activity invocation 'activity-a' carries value-flow document version {ActivityExecutionValueFlowDocumentVersions.Current + 1}; this runtime requires version {ActivityExecutionValueFlowDocumentVersions.Current}."),
        ["activity-execution-supersession"] = Invalid(
            Mutated(parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with { Status = ActivityExecutionStatus.Superseded })),
            "A superseded activity execution must record both its successor ID and supersession time."),
        ["activity-execution-scope-provenance"] = Invalid(
            Mutated(parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with
            {
                ExecutionScopeId = "scope-own",
                Provenance = Provenance("scope-scheduled")
            })),
            "Activity execution ExecutionScopeId must match its scheduling provenance when both are present."),
        ["activity-execution-attempt-provenance"] = Invalid(
            Mutated(parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with
            {
                Attempt = new ActivityExecutionAttemptLineage(2, "activity-first", "activity-first"),
                Provenance = Provenance(null, new ActivityExecutionAttemptLineage(1, "activity-first", null))
            })),
            "Activity execution Attempt must match its scheduling provenance when both are present."),

        ["inspection-delete"] = Invalid(
            Mutated(parts => parts.Inspections[0] = parts.Inspections[0] with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("activity execution inspection", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["inspection-other-workflow"] = Invalid(
            Mutated(parts => parts.Inspections[0] = Change("activity-a", Inspection("activity-a", OtherWorkflowId))),
            WorkflowMessage("Activity execution inspection state change")),
        ["inspection-scope-provenance"] = Invalid(
            Mutated(parts => parts.Inspections[0] = Change("activity-a", Inspection("activity-a") with { ExecutionScopeId = "scope-own" })),
            "Activity execution inspection ExecutionScopeId must match its scheduling provenance when both are present."),

        ["incident-delete"] = Invalid(
            Mutated(parts => parts.Incidents[0] = parts.Incidents[0] with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("incident", "'Append' or 'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["incident-other-workflow"] = Invalid(
            Mutated(parts => parts.Incidents[0] = Change("incident-a", Incident("incident-a", OtherWorkflowId), RuntimeStateChangeOperation.Append)),
            WorkflowMessage("Incident state change")),
        ["incident-duplicate-id"] = Invalid(
            Mutated(parts => parts.Incidents.Add(Change("incident-a", Incident("incident-a"), RuntimeStateChangeOperation.Upsert))),
            "Incident 'incident-a' occurs more than once in one checkpoint commit."),

        ["durable-value-append"] = Invalid(
            Mutated(parts => parts.DurableValues[0] = parts.DurableValues[0] with { Operation = RuntimeStateChangeOperation.Append }),
            OperationMessage("durable value", "'Upsert' or 'Delete'", RuntimeStateChangeOperation.Append)),
        ["durable-value-other-workflow"] = Invalid(
            Mutated(parts => parts.DurableValues[0] = Change("value-a", DurableValue("value-a", OtherWorkflowId))),
            WorkflowMessage("Durable value state change")),

        ["bookmark-append"] = Invalid(
            Mutated(parts => parts.Bookmarks[0] = parts.Bookmarks[0] with { Operation = RuntimeStateChangeOperation.Append }),
            OperationMessage("bookmark", "'Upsert' or 'Delete'", RuntimeStateChangeOperation.Append)),
        ["bookmark-other-workflow"] = Invalid(
            Mutated(parts => parts.Bookmarks[0] = Change("bookmark-a", Bookmark("bookmark-a", OtherWorkflowId))),
            WorkflowMessage("Bookmark state change")),

        ["operational-delete"] = Invalid(
            Mutated(parts => parts.Operational[0] = parts.Operational[0] with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("operational", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["operational-other-workflow"] = Invalid(
            Mutated(parts => parts.Operational[0] = Change("operational-a", Liveness("operational-a", OtherWorkflowId))),
            WorkflowMessage("Operational state change")),
        ["operational-reserved-ownership"] = Invalid(
            Mutated(parts => parts.Operational.Add(Change("ownership:workflow-a", Liveness("ownership:workflow-a")))),
            "Checkpoint operational changes cannot overwrite the reserved execution-ownership state."),

        // Outbox items reach a store folded from the commit's intents by the committer, so these cases carry the items
        // directly and no intents for the committer to fold over them.
        ["outbox-not-pending"] = Invalid(
            Mutated(parts => parts.UseOutbox(OutboxChange("outbox-a", Intent("intent-a"), RuntimePostCommitOutboxStatus.Delivered))),
            "Only pending post-commit outbox items can be saved as pending."),
        ["outbox-duplicate-conflict"] = Invalid(
            Mutated(parts => parts.UseOutbox(OutboxChange("outbox-a", Intent("intent-a")), OutboxChange("outbox-a", Intent("intent-b")))),
            "Post-commit outbox item 'outbox-a' occurs more than once with conflicting content."),
        ["outbox-delete"] = Invalid(
            Mutated(parts => parts.UseOutbox(OutboxChange("outbox-a", Intent("intent-a")) with { Operation = RuntimeStateChangeOperation.Delete })),
            OperationMessage("post-commit outbox", "'Upsert'", RuntimeStateChangeOperation.Delete)),

        // Work for another execution rides a checkpoint only as a waited child's resume of the parent its terminal
        // dispatch record, in the same commit, links it to.
        ["cross-execution-intent-without-dispatch"] = Invalid(
            () => ChildTerminalCommit(WaitingParentId),
            UnlinkedIntentMessage(WaitingParentId)),
        ["cross-execution-intent-dispatch-links-other-parent"] = Invalid(
            () => ChildTerminalCommit("workflow-unrelated", WaitedDispatch(WaitingParentId, WaitedActivityId, WorkflowDispatchStatus.Completed)),
            UnlinkedIntentMessage("workflow-unrelated")),
        ["cross-execution-intent-dispatch-links-other-child"] = Invalid(
            () => ChildTerminalCommit(WaitingParentId, WaitedDispatch(WaitingParentId, "activity-other", WorkflowDispatchStatus.Completed)),
            UnlinkedIntentMessage(WaitingParentId)),
        ["cross-execution-intent-dispatch-not-terminal"] = Invalid(
            () => ChildTerminalCommit(WaitingParentId, WaitedDispatch(WaitingParentId, WaitedActivityId, WorkflowDispatchStatus.Started)),
            UnlinkedIntentMessage(WaitingParentId)),

        ["cleanup-other-workflow"] = Invalid(
            Mutated(parts => parts.Cleanups[0] = parts.Cleanups[0] with { WorkflowExecutionId = OtherWorkflowId }),
            WorkflowMessage("Activity-scope cleanup")),
        ["cleanup-without-outer-scope"] = Invalid(
            Mutated(parts => parts.Cleanups[0] = parts.Cleanups[0] with { ActivityExecutionIds = ["activity-inner"] }),
            "Activity scope cleanup must include its outer execution scope."),
        ["cleanup-blank-scope"] = new(
            Mutated(parts => parts.Cleanups[0] = parts.Cleanups[0] with { ExecutionScopeId = " ", ActivityExecutionIds = [" "] }),
            typeof(ArgumentException),
            BlankArgumentMessage(" ", "cleanup.ExecutionScopeId")),
        ["cleanup-bookmark-also-changed"] = Invalid(
            Mutated(parts => parts.Cleanups[0] = parts.Cleanups[0] with { BookmarkIds = ["bookmark-a"] }),
            "Bookmark 'bookmark-a' cannot be both changed and deleted by activity-scope cleanup in one checkpoint commit."),
        ["cleanup-work-item-also-consumed"] = Invalid(
            Mutated(parts => parts.Consumed.Add(new ConsumedSchedulerWorkItem(WorkflowId, "work-cleanup", "owner-a", 1))),
            "Scheduler work item 'work-cleanup' cannot be both consumed and deleted by activity-scope cleanup in one checkpoint commit."),

        ["dispatch-delete"] = Invalid(
            Mutated(parts => parts.Dispatches[0] = parts.Dispatches[0] with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("workflow dispatch", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["dispatch-wrong-owner"] = Invalid(
            Mutated(parts => parts.Dispatches[0] = DispatchChange(PendingDispatch(OtherWorkflowId, DispatchActivityId))),
            $"Workflow dispatch '{new WorkflowDispatchIdentity(OtherWorkflowId, DispatchActivityId).DispatchId}' status 'Pending' must be committed by its parent workflow execution."),
        ["dispatch-duplicate-conflict"] = Invalid(
            Mutated(parts => parts.Dispatches.Add(DispatchChange(PendingDispatch(WorkflowId, DispatchActivityId, OccurredAt.AddSeconds(1))))),
            $"Workflow dispatch '{new WorkflowDispatchIdentity(WorkflowId, DispatchActivityId).DispatchId}' occurs more than once with conflicting state."),
        ["dispatch-cancellation-other-parent"] = Invalid(
            Mutated(parts => parts.Cancellations.Add(CancellationRequest(OtherWorkflowId, "activity-cancel"))),
            $"Workflow dispatch cancellation request '{new WorkflowDispatchIdentity(OtherWorkflowId, "activity-cancel").DispatchId}' must be committed by its parent workflow execution."),

        ["consumed-work-other-workflow"] = Invalid(
            Mutated(parts => parts.Consumed.Add(new ConsumedSchedulerWorkItem(OtherWorkflowId, "work-consumed", "owner-a", 1))),
            WorkflowMessage("Consumed scheduler work item")),
        ["consumed-work-non-positive-token"] = Invalid(
            Mutated(parts => parts.Consumed.Add(new ConsumedSchedulerWorkItem(WorkflowId, "work-consumed", "owner-a", 0))),
            "Consumed scheduler work item 'work-consumed' requires a positive fencing token."),

        ["alteration-terminal-other-commit"] = Invalid(
            Mutated(parts => parts.AlterationTerminal = new WorkflowAlterationJobTerminalChange(
                "job-a", "claim-a", WorkflowAlterationJobStatus.Succeeded, [], "commit-other", OccurredAt)),
            "Workflow alteration terminal evidence must reference its checkpoint commit ID."),
    };
}
