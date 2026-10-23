using System.Text.Json;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime callback payload for a non-terminal child→parent structural notification (seam C).
/// Carries the notifying child's identity and the opaque code/payload it raised, so the parent composite can
/// decide how to route the signal (e.g. an interrupting escalation tears the child down via a seam-A subtree
/// cancellation; a non-interrupting one keeps routing). The notifying child keeps running regardless of how
/// this callback resolves; a notification whose child has since completed or faulted still delivers.
/// </summary>
public sealed class ActivityChildNotifiedContext
{
    public ActivityChildNotifiedContext(
        string notifyingChildActivityExecutionId,
        string notifyingChildExecutableNodeId,
        string code,
        string? notifyingChildIterationId = null,
        JsonElement? payload = null,
        string? viaChildActivityExecutionId = null,
        string? viaChildExecutableNodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyingChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyingChildExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (notifyingChildIterationId is not null && string.IsNullOrWhiteSpace(notifyingChildIterationId))
            throw new ArgumentException("Notifying child iteration id cannot be blank when provided.", nameof(notifyingChildIterationId));
        if (viaChildActivityExecutionId is null != viaChildExecutableNodeId is null)
            throw new ArgumentException("A via child is named by both its activity execution id and its executable node id, or not at all.", nameof(viaChildActivityExecutionId));

        NotifyingChildActivityExecutionId = notifyingChildActivityExecutionId;
        NotifyingChildExecutableNodeId = notifyingChildExecutableNodeId;
        Code = code;
        NotifyingChildIterationId = notifyingChildIterationId;
        Payload = payload?.Clone();
        ViaChildActivityExecutionId = string.IsNullOrWhiteSpace(viaChildActivityExecutionId) ? notifyingChildActivityExecutionId : viaChildActivityExecutionId;
        ViaChildExecutableNodeId = string.IsNullOrWhiteSpace(viaChildExecutableNodeId) ? notifyingChildExecutableNodeId : viaChildExecutableNodeId;
    }

    /// <summary>The activity execution id of the still-running child that raised the notification.</summary>
    public string NotifyingChildActivityExecutionId { get; }

    /// <summary>The executable node id of the notifying child.</summary>
    public string NotifyingChildExecutableNodeId { get; }

    /// <summary>The opaque, consumer-defined notification code.</summary>
    public string Code { get; }

    /// <summary>
    /// The notifying child's engine iteration identity (<c>ActivitySchedulingProvenance.IterationId</c>),
    /// or <c>null</c> when the child was not scheduled as a loop iteration. A multi-instance host resolves
    /// which instance signalled by <c>(NotifyingChildExecutableNodeId, NotifyingChildIterationId)</c>.
    /// </summary>
    public string? NotifyingChildIterationId { get; }

    /// <summary>The optional, opaque, consumer-defined notification payload; <c>null</c> when omitted.</summary>
    public JsonElement? Payload { get; }

    /// <summary>
    /// The receiver's direct child on the path from the notifying activity. For a notification from a direct child,
    /// and for a parent notification, it is the notifying child itself; for an ancestor notification it is the
    /// composite the notification passed through, so the receiver can tell which of its children it came from.
    /// </summary>
    public string ViaChildActivityExecutionId { get; }

    /// <summary>The executable node id of <see cref="ViaChildActivityExecutionId"/>.</summary>
    public string ViaChildExecutableNodeId { get; }
}
