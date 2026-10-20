using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime callback payload for an activity-owned child fault (the parent-side counterpart of
/// <see cref="ActivityChildCompletedContext"/>). Carries the identity of the faulted child execution and the
/// incident recorded for it, so the parent composite can recover which branch faulted without holding mutable
/// state across the stateless child-fault re-construction.
/// </summary>
public sealed class ActivityChildFaultedContext
{
    public ActivityChildFaultedContext(
        IActivityExecutionContext parentContext,
        string faultedChildActivityExecutionId,
        string faultedChildExecutableNodeId,
        string? incidentId = null,
        string? faultedChildIterationId = null,
        string? faultCode = null,
        string? faultCategory = null)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedChildExecutableNodeId);

        if (incidentId is not null && string.IsNullOrWhiteSpace(incidentId))
            throw new ArgumentException("Incident id cannot be blank when provided.", nameof(incidentId));

        if (faultedChildIterationId is not null && string.IsNullOrWhiteSpace(faultedChildIterationId))
            throw new ArgumentException("Faulted child iteration id cannot be blank when provided.", nameof(faultedChildIterationId));

        ParentContext = parentContext;
        FaultedChildActivityExecutionId = faultedChildActivityExecutionId;
        FaultedChildExecutableNodeId = faultedChildExecutableNodeId;
        IncidentId = incidentId;
        FaultedChildIterationId = faultedChildIterationId;
        FaultCode = string.IsNullOrWhiteSpace(faultCode) ? null : faultCode;
        FaultCategory = string.IsNullOrWhiteSpace(faultCategory) ? null : faultCategory;
    }

    public IActivityExecutionContext ParentContext { get; }
    public string FaultedChildActivityExecutionId { get; }
    public string FaultedChildExecutableNodeId { get; }

    /// <summary>The id of the incident recorded for the faulted child, when known.</summary>
    public string? IncidentId { get; }

    /// <summary>
    /// The engine iteration identity the faulted child carried, or <c>null</c> when the child was not
    /// scheduled as a loop iteration. Mirrors <see cref="ActivityChildCompletedContext.CompletedChildIterationId"/>.
    /// </summary>
    public string? FaultedChildIterationId { get; }

    /// <summary>
    /// The code of the faulted child's committed normalized fault, or <c>null</c> when the runtime has none to give.
    /// </summary>
    public string? FaultCode { get; }

    /// <summary>
    /// The author-chosen category of the faulted child's committed fault (<see cref="ActivityFault.Category"/>), or
    /// <c>null</c> when the fault is unclassified. A parent tells an uncertain outcome (<c>outcome-unknown</c>) apart
    /// from a plain failure by it.
    /// </summary>
    public string? FaultCategory { get; }
}
