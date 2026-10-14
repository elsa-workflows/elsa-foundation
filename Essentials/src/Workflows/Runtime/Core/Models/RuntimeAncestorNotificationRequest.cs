using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request raised by a structural activity to notify its <b>nearest</b> committed ancestor whose
/// executable node has the given consumer key. The runtime resolves the target by walking the committed
/// <c>ParentActivityExecutionId</c> chain upward from the requester's parent; the requester never names an
/// execution. The notification is delivered through the same notify-parent work item and handler as
/// <see cref="RuntimeParentNotificationRequest"/>, and it is enqueued ahead of the requester's own completion so the
/// ancestor evaluates it before the requester's direct parent sees that completion.
/// </summary>
public sealed class RuntimeAncestorNotificationRequest
{
    /// <summary>The maximum length of an <see cref="AncestorConsumerKey"/>.</summary>
    public const int MaximumConsumerKeyLength = 256;

    public RuntimeAncestorNotificationRequest(string ancestorConsumerKey, string code, JsonElement? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ancestorConsumerKey);
        if (ancestorConsumerKey.Length > MaximumConsumerKeyLength)
            throw new ArgumentException($"An ancestor consumer key must be at most {MaximumConsumerKeyLength} UTF-16 code units.", nameof(ancestorConsumerKey));

        // Reuses the parent-notification bounds for the code and payload.
        var notification = new RuntimeParentNotificationRequest(code, payload);
        AncestorConsumerKey = ancestorConsumerKey;
        Code = notification.Code;
        Payload = notification.Payload;
    }

    /// <summary>The runtime consumer key of the ancestor to notify, e.g. <c>elsa.graph-activity</c>.</summary>
    public string AncestorConsumerKey { get; }

    /// <summary>The opaque, consumer-defined notification code. Non-empty, bounded.</summary>
    public string Code { get; }

    /// <summary>The optional, opaque, consumer-defined notification payload. Size-bounded; <c>null</c> when omitted.</summary>
    public JsonElement? Payload { get; }
}
