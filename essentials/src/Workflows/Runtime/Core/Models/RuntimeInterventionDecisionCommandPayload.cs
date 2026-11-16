using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload that delivers an operator's intervention decision to the structural parent that requested the
/// intervention (spec 260 R-5). Rides the dedicated <see cref="WorkflowExecutionCommandKind.DeliverInterventionDecision"/>
/// command, enqueued by the <c>DecideIntervention/1</c> alteration checkpoint, and is handled by
/// <c>WorkflowInterventionDecisionSchedulerWorkHandler</c>, which reactivates the requester from its pinned snapshot and
/// dispatches <c>IRuntimeActivityInterventionDecisionHandler.OnInterventionDecidedAsync</c>. The requester is the one the
/// incident's outcome recorded, never chosen by the operator.
/// </summary>
public sealed class RuntimeInterventionDecisionCommandPayload : IActivityCommandPayload
{
    public const string DeliverInterventionDecisionReason = "DeliverInterventionDecision";

    [JsonConstructor]
    public RuntimeInterventionDecisionCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string? parentActivityExecutionId,
        string? branchId,
        string incidentId,
        string decision,
        string faultedActivityExecutionId,
        string faultedExecutableNodeId,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (parentActivityExecutionId is not null && string.IsNullOrWhiteSpace(parentActivityExecutionId))
            throw new ArgumentException("Parent activity execution ID cannot be blank when provided.", nameof(parentActivityExecutionId));
        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));
        if (!RuntimeChildFaultInterventionRequest.IsValidDecisionCode(decision))
            throw new ArgumentException($"Intervention decision '{decision}' is not a valid decision code.", nameof(decision));
        if (StringComparer.Ordinal.Equals(faultedActivityExecutionId, activityExecutionId))
            throw new ArgumentException("An intervention decision cannot be delivered to the faulted activity itself.", nameof(faultedActivityExecutionId));

        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        ActivityExecutionId = activityExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        BranchId = branchId;
        IncidentId = incidentId;
        Decision = decision;
        FaultedActivityExecutionId = faultedActivityExecutionId;
        FaultedExecutableNodeId = faultedExecutableNodeId;
        Reason = reason;
    }

    /// <summary>The pinned executable artifact the requester is reconstructed from.</summary>
    public WorkflowExecutableIdentity PinnedExecutable { get; }

    /// <summary>The requester's executable node id.</summary>
    public string ExecutableNodeId { get; }

    /// <summary>The requester's activity execution id, as the incident's wait-for-intervention outcome recorded it.</summary>
    public string ActivityExecutionId { get; }

    /// <summary>The requester's own committed parent activity execution id, when known.</summary>
    public string? ParentActivityExecutionId { get; }

    /// <summary>The requester's committed branch id, when known.</summary>
    public string? BranchId { get; }

    /// <summary>The incident the decision resolved.</summary>
    public string IncidentId { get; }

    /// <summary>The decision code, one the requester offered on the incident.</summary>
    public string Decision { get; }

    /// <summary>The faulted activity execution the incident belongs to.</summary>
    public string FaultedActivityExecutionId { get; }

    /// <summary>The executable node id of <see cref="FaultedActivityExecutionId"/>.</summary>
    public string FaultedExecutableNodeId { get; }

    /// <summary>The checkpoint reason recorded for the decision evaluation.</summary>
    public string Reason { get; }
}
