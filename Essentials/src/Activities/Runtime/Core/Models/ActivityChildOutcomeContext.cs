using System.Collections.Frozen;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Callback payload for a structural parent evaluating an intentional child fault together with ordered
/// notifications from that same child evaluation. The fault is an immutable, normalized, masked snapshot.
/// </summary>
public sealed class ActivityChildOutcomeContext
{
    public ActivityChildOutcomeContext(
        IActivityExecutionContext parentContext,
        string faultedChildActivityExecutionId,
        string faultedChildExecutableNodeId,
        string? faultedChildIterationId,
        ActivityFault fault,
        string incidentId,
        IReadOnlyList<ActivityChildNotifiedContext> notifications)
        : this(parentContext, faultedChildActivityExecutionId, faultedChildExecutableNodeId, faultedChildIterationId,
            fault, incidentId, notifications, FrozenDictionary<string, string>.Empty)
    {
    }

    public ActivityChildOutcomeContext(
        IActivityExecutionContext parentContext,
        string faultedChildActivityExecutionId,
        string faultedChildExecutableNodeId,
        string? faultedChildIterationId,
        ActivityFault fault,
        string incidentId,
        IReadOnlyList<ActivityChildNotifiedContext> notifications,
        IReadOnlyDictionary<string, string> childSchedulingMetadata)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedChildExecutableNodeId);
        ArgumentNullException.ThrowIfNull(fault);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(childSchedulingMetadata);

        if (faultedChildIterationId is not null && string.IsNullOrWhiteSpace(faultedChildIterationId))
            throw new ArgumentException("Faulted child iteration id cannot be blank when provided.", nameof(faultedChildIterationId));

        if (notifications.Count == 0)
            throw new ArgumentException("A child outcome requires at least one notification.", nameof(notifications));

        if (notifications.Any(notification => notification is null))
            throw new ArgumentException("Child outcome notifications cannot contain null entries.", nameof(notifications));

        if (notifications.Any(notification =>
                !StringComparer.Ordinal.Equals(notification.NotifyingChildActivityExecutionId, faultedChildActivityExecutionId) ||
                !StringComparer.Ordinal.Equals(notification.NotifyingChildExecutableNodeId, faultedChildExecutableNodeId) ||
                !StringComparer.Ordinal.Equals(notification.NotifyingChildIterationId, faultedChildIterationId)))
            throw new ArgumentException("Every child outcome notification must match the faulted child's execution, node, and iteration identities.", nameof(notifications));

        ParentContext = parentContext;
        FaultedChildActivityExecutionId = faultedChildActivityExecutionId;
        FaultedChildExecutableNodeId = faultedChildExecutableNodeId;
        FaultedChildIterationId = faultedChildIterationId;
        Fault = fault;
        IncidentId = incidentId;
        Notifications = Array.AsReadOnly(notifications.ToArray());
        ChildSchedulingMetadata = childSchedulingMetadata.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IActivityExecutionContext ParentContext { get; }
    public string FaultedChildActivityExecutionId { get; }
    public string FaultedChildExecutableNodeId { get; }
    public string? FaultedChildIterationId { get; }
    public ActivityFault Fault { get; }
    public string IncidentId { get; }
    public IReadOnlyList<ActivityChildNotifiedContext> Notifications { get; }

    /// <summary>Immutable metadata from the faulted child's committed scheduling provenance, independent of the delivery command.</summary>
    public IReadOnlyDictionary<string, string> ChildSchedulingMetadata { get; }
}
