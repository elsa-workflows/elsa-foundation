using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for a non-terminal child→parent structural notification (spec 126, seam C). Rides the
/// dedicated <see cref="WorkflowExecutionCommandKind.NotifyParentActivity"/> command and is handled by
/// <c>WorkflowNotifyParentActivitySchedulerWorkHandler</c>, which reactivates the target parent from its
/// pinned snapshot and dispatches <c>IRuntimeActivityChildNotificationHandler.OnChildNotifiedAsync</c>. The
/// target is always the notifying child's committed parent, resolved by the runtime at build time — there is
/// no caller-supplied target (spoof-proofing).
/// </summary>
public sealed class RuntimeNotifyParentCommandPayload
{
    public const string NotifyParentReason = "NotifyParentActivity";

    [JsonConstructor]
    public RuntimeNotifyParentCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string? parentActivityExecutionId,
        string? branchId,
        string notifyingChildActivityExecutionId,
        string notifyingChildExecutableNodeId,
        string? notifyingChildIterationId,
        string code,
        System.Text.Json.JsonElement? payload,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyingChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyingChildExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (parentActivityExecutionId is not null && string.IsNullOrWhiteSpace(parentActivityExecutionId))
            throw new ArgumentException("Parent activity execution ID cannot be blank when provided.", nameof(parentActivityExecutionId));
        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));
        if (notifyingChildIterationId is not null && string.IsNullOrWhiteSpace(notifyingChildIterationId))
            throw new ArgumentException("Notifying child iteration ID cannot be blank when provided.", nameof(notifyingChildIterationId));
        if (code.Length > RuntimeParentNotificationRequest.MaximumCodeLength)
            throw new ArgumentException($"A parent-notification code must be at most {RuntimeParentNotificationRequest.MaximumCodeLength} UTF-16 code units.", nameof(code));
        if (StringComparer.Ordinal.Equals(notifyingChildActivityExecutionId, activityExecutionId))
            throw new ArgumentException("A parent notification cannot target the notifying child itself.", nameof(notifyingChildActivityExecutionId));

        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        ActivityExecutionId = activityExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        BranchId = branchId;
        NotifyingChildActivityExecutionId = notifyingChildActivityExecutionId;
        NotifyingChildExecutableNodeId = notifyingChildExecutableNodeId;
        NotifyingChildIterationId = notifyingChildIterationId;
        Code = code;
        Payload = payload?.Clone();
        Reason = reason;
    }

    /// <summary>The pinned executable artifact the target parent is reconstructed from.</summary>
    public WorkflowExecutableIdentity PinnedExecutable { get; }

    /// <summary>The target parent's executable node id (used to reconstruct the parent activity).</summary>
    public string ExecutableNodeId { get; }

    /// <summary>The target parent's activity execution id — the notifying child's committed parent.</summary>
    public string ActivityExecutionId { get; }

    /// <summary>The target parent's own committed parent activity execution id, when known.</summary>
    public string? ParentActivityExecutionId { get; }

    /// <summary>The target parent's committed branch id, when known.</summary>
    public string? BranchId { get; }

    /// <summary>The activity execution id of the still-running child that raised the notification.</summary>
    public string NotifyingChildActivityExecutionId { get; }

    /// <summary>The executable node id of the notifying child.</summary>
    public string NotifyingChildExecutableNodeId { get; }

    /// <summary>The notifying child's engine iteration identity, or <c>null</c> for a single-run child.</summary>
    public string? NotifyingChildIterationId { get; }

    /// <summary>The opaque, consumer-defined notification code.</summary>
    public string Code { get; }

    /// <summary>The optional, opaque, consumer-defined notification payload.</summary>
    public System.Text.Json.JsonElement? Payload { get; }

    /// <summary>The checkpoint reason recorded for the notification evaluation.</summary>
    public string Reason { get; }
}
