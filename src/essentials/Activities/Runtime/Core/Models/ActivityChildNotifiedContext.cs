using System.Text.Json;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime callback payload for a non-terminal child→parent structural notification (spec 126, seam C).
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
        JsonElement? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyingChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyingChildExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (notifyingChildIterationId is not null && string.IsNullOrWhiteSpace(notifyingChildIterationId))
            throw new ArgumentException("Notifying child iteration id cannot be blank when provided.", nameof(notifyingChildIterationId));

        NotifyingChildActivityExecutionId = notifyingChildActivityExecutionId;
        NotifyingChildExecutableNodeId = notifyingChildExecutableNodeId;
        Code = code;
        NotifyingChildIterationId = notifyingChildIterationId;
        Payload = payload?.Clone();
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
}
