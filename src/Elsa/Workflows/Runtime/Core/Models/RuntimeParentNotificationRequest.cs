using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request raised by a still-running structural child to notify its own committed parent
/// (spec 126, seam C). Staged during any of the child's own evaluations (invoke, child-completion /
/// child-fault of its own children) and committed atomically with the child's <c>Defer</c>/<c>Complete</c>
/// state as a durable post-commit work item. The child cannot name a target: the notification always and
/// only reaches the notifying child's committed parent, derived by the runtime — this is the seam's
/// spoof-proofing core.
/// </summary>
public sealed class RuntimeParentNotificationRequest
{
    /// <summary>The maximum length of a notification <see cref="Code"/> (mirrors the post-commit intent kind discipline).</summary>
    public const int MaximumCodeLength = 128;

    /// <summary>The maximum UTF-8 byte length of a serialized notification <see cref="Payload"/> document.</summary>
    public const int MaximumPayloadByteLength = 8 * 1024;

    public RuntimeParentNotificationRequest(string code, JsonElement? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code.Length > MaximumCodeLength)
            throw new ArgumentException($"A parent-notification code must be at most {MaximumCodeLength} UTF-16 code units.", nameof(code));

        if (payload is { } document)
        {
            var byteLength = Encoding.UTF8.GetByteCount(document.GetRawText());
            if (byteLength > MaximumPayloadByteLength)
                throw new ArgumentException($"A parent-notification payload must serialize to at most {MaximumPayloadByteLength} UTF-8 bytes.", nameof(payload));
        }

        Code = code;
        Payload = payload?.Clone();
    }

    /// <summary>The opaque, consumer-defined notification code (e.g. a BPMN escalation code). Non-empty, bounded.</summary>
    public string Code { get; }

    /// <summary>The optional, opaque, consumer-defined notification payload. Size-bounded; <c>null</c> when omitted.</summary>
    public JsonElement? Payload { get; }
}
