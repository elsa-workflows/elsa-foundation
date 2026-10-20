using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request raised by a structural activity to notify its <b>nearest</b> committed ancestor that matches
/// a <see cref="RuntimeAncestorSelector"/>: a consumer key, or an activity type (optionally one authored node of it).
/// The runtime resolves the target by walking the committed
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
        : this(new RuntimeAncestorSelector(ConsumerKey: ancestorConsumerKey), code, payload)
    {
    }

    public RuntimeAncestorNotificationRequest(RuntimeAncestorSelector selector, string code, JsonElement? payload = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (selector.ConsumerKey is null == selector.ActivityTypeKey is null)
            throw new ArgumentException("An ancestor selector names exactly one of a consumer key or an activity type key.", nameof(selector));
        if (selector.AuthoredActivityId is not null && selector.ActivityTypeKey is null)
            throw new ArgumentException("An ancestor selector names an authored activity id only together with an activity type key.", nameof(selector));
        ValidateKey(selector.ConsumerKey, "consumer key");
        ValidateKey(selector.ActivityTypeKey, "activity type key");
        ValidateKey(selector.AuthoredActivityId, "authored activity id");

        // Reuses the parent-notification bounds for the code and payload.
        var notification = new RuntimeParentNotificationRequest(code, payload);
        Selector = selector;
        Code = notification.Code;
        Payload = notification.Payload;
    }

    /// <summary>Which ancestor to notify.</summary>
    public RuntimeAncestorSelector Selector { get; }

    /// <summary>The runtime consumer key of the ancestor to notify, e.g. <c>elsa.graph-activity</c>; <c>null</c> when it is selected by type.</summary>
    public string? AncestorConsumerKey => Selector.ConsumerKey;

    /// <summary>The opaque, consumer-defined notification code. Non-empty, bounded.</summary>
    public string Code { get; }

    /// <summary>The optional, opaque, consumer-defined notification payload. Size-bounded; <c>null</c> when omitted.</summary>
    public JsonElement? Payload { get; }

    private static void ValidateKey(string? value, string name)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"An ancestor {name} cannot be blank.", nameof(value));
        if (value.Length > MaximumConsumerKeyLength)
            throw new ArgumentException($"An ancestor {name} must be at most {MaximumConsumerKeyLength} UTF-16 code units.", nameof(value));
    }
}
