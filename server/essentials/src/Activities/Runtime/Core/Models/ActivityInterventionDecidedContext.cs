namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime callback payload for an operator's intervention decision (spec 260 R-5): the incident the decision resolved,
/// the opaque decision code the requester offered, the faulted activity the incident belongs to, and the requester's
/// direct child on the path to it. For a direct child's fault the via child is the faulted activity itself; for a
/// descendant's fault (spec 258 R-3) it is the composite the fault came through.
/// </summary>
public sealed class ActivityInterventionDecidedContext
{
    public ActivityInterventionDecidedContext(
        string incidentId,
        string decision,
        string faultedActivityExecutionId,
        string faultedExecutableNodeId,
        string viaChildActivityExecutionId,
        string viaChildExecutableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viaChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viaChildExecutableNodeId);

        IncidentId = incidentId;
        Decision = decision;
        FaultedActivityExecutionId = faultedActivityExecutionId;
        FaultedExecutableNodeId = faultedExecutableNodeId;
        ViaChildActivityExecutionId = viaChildActivityExecutionId;
        ViaChildExecutableNodeId = viaChildExecutableNodeId;
    }

    /// <summary>The incident the operator's decision resolved.</summary>
    public string IncidentId { get; }

    /// <summary>The decision code, one of those the requester offered.</summary>
    public string Decision { get; }

    /// <summary>The faulted activity execution the incident belongs to.</summary>
    public string FaultedActivityExecutionId { get; }

    /// <summary>The executable node id of <see cref="FaultedActivityExecutionId"/>.</summary>
    public string FaultedExecutableNodeId { get; }

    /// <summary>The requester's direct child on the path to the faulted activity.</summary>
    public string ViaChildActivityExecutionId { get; }

    /// <summary>The executable node id of <see cref="ViaChildActivityExecutionId"/>.</summary>
    public string ViaChildExecutableNodeId { get; }
}
