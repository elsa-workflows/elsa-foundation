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
        ActivitySchedulingProvenance? schedulingProvenance = null,
        LoopIterationScopeRequest? iterationFrame = null);

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

    /// <summary>
    /// Requests that the workflow instance name be set to <paramref name="instanceName"/>. Used by the
    /// <c>SetName</c> leaf control activity. The engine drains this on the leaf execution path and folds the
    /// new name into the activity-completed checkpoint's workflow-execution state change (under the
    /// <see cref="Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.InstanceName"/> system-metadata
    /// key), mirroring how <see cref="SetCorrelationId"/> persists the correlation id. A null or blank value
    /// clears the instance name.
    /// </summary>
    void SetInstanceName(string? instanceName);

    bool InstanceNameAssignmentRequested { get; }

    string? RequestedInstanceName { get; }

    /// <summary>
    /// Requests that the workflow output named <paramref name="outputName"/> be set to
    /// <paramref name="value"/>. Used by the <c>SetOutput</c> leaf control activity. The engine drains this on
    /// the leaf execution path and folds the value into the activity-completed checkpoint as an
    /// <see cref="Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OutputName"/>-tagged durable value
    /// — the same durable/output channel activity outputs use — so the workflow output is durably persisted.
    /// A blank name is ignored. A later assignment of the same name overwrites the earlier one.
    /// </summary>
    void SetWorkflowOutput(string outputName, object? value);

    bool WorkflowOutputAssignmentRequested { get; }

    IReadOnlyDictionary<string, object?> RequestedWorkflowOutputs { get; }
}
