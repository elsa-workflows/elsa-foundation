using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned activity execution context extensions used by activities that schedule child executable nodes.
/// </summary>
public interface IRuntimeActivityExecutionContext : IActivityExecutionContext
{
    string WorkflowExecutionId { get; }
    WorkflowExecutableIdentity PinnedExecutable { get; }
    RuntimeSchedulerWorkItem SchedulerWorkItem { get; }
    ExecutableNode ExecutableNode { get; }
    ActivityExecutionState ActivityExecutionState { get; }

    void ScheduleChildActivity(
        string executableNodeId,
        string? schedulingActivityExecutionId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null);

    IReadOnlyCollection<RuntimeChildActivityScheduleRequest> GetChildActivityScheduleRequests();

    void CompleteCompositeActivity(IEnumerable<string>? outcomeNames = null);

    bool CompositeCompletionRequested { get; }
    IReadOnlyCollection<string> CompositeCompletionOutcomeNames { get; }

    void DeferCompositeCompletion();

    bool CompositeCompletionDeferred { get; }

    /// <summary>
    /// Requests that the whole workflow run end now with a successful outcome, regardless of any remaining
    /// scheduled work. Used by the <c>Finish</c>/<c>Complete</c> leaf control activity. The engine drains this
    /// on the leaf execution path and commits a terminal <c>WorkflowCompleted</c> checkpoint in place of the
    /// usual activity-completed propagation.
    /// </summary>
    void FinishWorkflow(IEnumerable<string>? outcomeNames = null);

    bool FinishWorkflowRequested { get; }

    IReadOnlyCollection<string> FinishWorkflowOutcomeNames { get; }

    /// <summary>
    /// Requests that the workflow instance correlation id be set to <paramref name="correlationId"/>. Used by
    /// the <c>Correlate</c> leaf control activity. The engine drains this on the leaf execution path and folds
    /// the new correlation id into the activity-completed checkpoint's workflow-execution state change. A null
    /// or blank value clears the correlation id.
    /// </summary>
    void SetCorrelationId(string? correlationId);

    bool CorrelationIdAssignmentRequested { get; }

    string? RequestedCorrelationId { get; }
}
