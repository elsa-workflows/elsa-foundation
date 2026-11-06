using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Pure candidate submission rules; no identity, persistence or delivery authority is created here.</summary>
internal static class MessageSubmissionRules
{
    internal const string FingerprintVersion = "elsa.message-submission/v1";
    internal const int MaximumSubmissionBytes = 1024 * 1024;
    internal const int MaximumPayloadBytes = MaximumSubmissionBytes;
    internal const int MaximumPayloadDepth = 64;
    internal const int MaximumBufferSeconds = 30 * 24 * 60 * 60;
    internal const int MaximumDeduplicationSeconds = 90 * 24 * 60 * 60;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static MessageSubmissionIntent CreateIntent(string name, string correlationKey, JsonElement payload,
        string? messageId = null, int? lifetimeSeconds = null)
    {
        ValidateIdentity(name, 256, "invalid-name");
        ValidateIdentity(correlationKey, 1024, "invalid-key");
        if (messageId is not null) ValidateIdentity(messageId, 256, "invalid-message-id");
        if (lifetimeSeconds is < 0 or > MaximumBufferSeconds)
            throw Invalid("invalid-lifetime");
        try
        {
            if (payload.ValueKind == JsonValueKind.Undefined) throw Invalid("invalid-payload");
            if (StrictUtf8.GetByteCount(payload.GetRawText()) > MaximumPayloadBytes)
                throw Invalid("payload-too-large");
            var detached = payload.Clone();
            using (var envelope = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(envelope, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
                    WriteTransportEnvelope(writer, name, correlationKey, messageId, lifetimeSeconds, detached);
                if (envelope.Length > MaximumSubmissionBytes) throw Invalid("submission-too-large");
            }
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
            {
                writer.WriteStartObject();
                writer.WriteString("version", FingerprintVersion);
                writer.WriteString("name", name);
                writer.WriteString("correlationKey", correlationKey);
                if (messageId is null) writer.WriteNull("messageId");
                else writer.WriteString("messageId", messageId);
                if (lifetimeSeconds is { } seconds) writer.WriteNumber("lifetimeSeconds", seconds);
                else writer.WriteNull("lifetimeSeconds");
                writer.WritePropertyName("payload");
                WriteCanonical(writer, detached, 0);
                writer.WriteEndObject();
            }
            var digest = SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)));
            return new(name, correlationKey, messageId, lifetimeSeconds, detached,
                FingerprintVersion + ":" + Convert.ToHexStringLower(digest));
        }
        catch (MessageSubmissionValidationException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            // DOM/encoding exceptions may contain submitted content. Keep only a fixed safe reason.
            throw Invalid("invalid-payload");
        }
    }

    internal static MessageAcceptanceTimes AtAcceptance(MessageSubmissionIntent intent, DateTimeOffset acceptedAt,
        MessageSubmissionPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        policy ??= new MessageSubmissionPolicy();
        if (acceptedAt == default || acceptedAt == DateTimeOffset.MaxValue) throw Invalid("invalid-acceptance-time");
        var at = acceptedAt.ToUniversalTime();
        var bufferSeconds = intent.LifetimeSeconds ?? policy.DefaultBufferLifetimeSeconds;
        if (bufferSeconds > policy.MaximumBufferLifetimeSeconds) throw Invalid("lifetime-exceeds-policy");
        try
        {
            DateTimeOffset? bufferUntil = bufferSeconds == 0 ? null : at.AddSeconds(bufferSeconds);
            DateTimeOffset? deduplicateUntil = intent.MessageId is null ? null : at.AddSeconds(policy.DeduplicationLifetimeSeconds);
            return new(at, bufferSeconds, policy, bufferUntil, deduplicateUntil);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid("acceptance-deadline-overflow");
        }
    }

    private static void ValidateIdentity(string? value, int maximumBytes, string reason)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid(reason);
        try
        {
            if (StrictUtf8.GetByteCount(value) > maximumBytes) throw Invalid(reason);
        }
        catch (EncoderFallbackException) { throw Invalid(reason); }
    }

    // Shared normalized transport budget: JavaScriptEncoder.Default, no whitespace, canonical payload,
    // and omitted optional fields stay absent. Fingerprint-only version/null markers do not consume it.
    // The HTTP adapter must separately bound the original request bytes before parsing.
    private static void WriteTransportEnvelope(Utf8JsonWriter writer, string name, string key, string? id,
        int? seconds, JsonElement payload)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("correlationKey", key);
        if (id is not null) writer.WriteString("messageId", id);
        if (seconds is { } lifetime) writer.WriteNumber("lifetimeSeconds", lifetime);
        writer.WritePropertyName("payload");
        WriteCanonical(writer, payload, 0);
        writer.WriteEndObject();
    }

    internal static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (depth >= MaximumPayloadDepth) throw Invalid("payload-too-deep");
                writer.WriteStartObject();
                string? previous = null;
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (previous is not null && StringComparer.Ordinal.Equals(previous, property.Name))
                        throw Invalid("duplicate-payload-member");
                    previous = property.Name;
                    _ = StrictUtf8.GetByteCount(property.Name);
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                if (depth >= MaximumPayloadDepth) throw Invalid("payload-too-deep");
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                _ = StrictUtf8.GetByteCount(text);
                writer.WriteStringValue(text);
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(value.GetBoolean());
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw Invalid("invalid-payload");
        }
    }

    internal static MessageSubmissionValidationException Invalid(string reason) => new(reason);
}

internal sealed class MessageSubmissionIntent
{
    internal MessageSubmissionIntent(string name, string correlationKey, string? messageId, int? lifetimeSeconds,
        JsonElement payload, string fingerprint)
    {
        Name = name; CorrelationKey = correlationKey; MessageId = messageId; LifetimeSeconds = lifetimeSeconds;
        Payload = payload; Fingerprint = fingerprint;
    }

    internal string Name { get; }
    internal string CorrelationKey { get; }
    internal string? MessageId { get; }
    internal int? LifetimeSeconds { get; }
    internal JsonElement Payload { get; }
    internal string Fingerprint { get; }
}

internal sealed class MessageSubmissionPolicy
{
    internal MessageSubmissionPolicy(int defaultBufferLifetimeSeconds = 24 * 60 * 60,
        int deduplicationLifetimeSeconds = 7 * 24 * 60 * 60,
        int maximumBufferLifetimeSeconds = MessageSubmissionRules.MaximumBufferSeconds)
    {
        if (maximumBufferLifetimeSeconds is < 1 or > MessageSubmissionRules.MaximumBufferSeconds ||
            defaultBufferLifetimeSeconds < 1 || defaultBufferLifetimeSeconds > maximumBufferLifetimeSeconds)
            throw MessageSubmissionRules.Invalid("invalid-buffer-policy");
        if (deduplicationLifetimeSeconds is < 1 or > MessageSubmissionRules.MaximumDeduplicationSeconds)
            throw MessageSubmissionRules.Invalid("invalid-deduplication-policy");
        DefaultBufferLifetimeSeconds = defaultBufferLifetimeSeconds;
        DeduplicationLifetimeSeconds = deduplicationLifetimeSeconds;
        MaximumBufferLifetimeSeconds = maximumBufferLifetimeSeconds;
    }

    internal int DefaultBufferLifetimeSeconds { get; }
    internal int DeduplicationLifetimeSeconds { get; }
    internal int MaximumBufferLifetimeSeconds { get; }
}

internal sealed class MessageAcceptanceTimes(DateTimeOffset acceptedAt, int bufferLifetimeSeconds,
    MessageSubmissionPolicy policy, DateTimeOffset? bufferUntil, DateTimeOffset? deduplicateUntil)
{
    internal DateTimeOffset AcceptedAt { get; } = acceptedAt;
    internal int BufferLifetimeSeconds { get; } = bufferLifetimeSeconds;
    internal MessageSubmissionPolicy Policy { get; } = policy;
    internal DateTimeOffset? BufferUntil { get; } = bufferUntil;
    internal DateTimeOffset? DeduplicateUntil { get; } = deduplicateUntil;
}

internal sealed class MessageSubmissionValidationException(string reasonCode)
    : ArgumentException("The message submission is invalid (" + reasonCode + ").")
{
    internal string ReasonCode { get; } = reasonCode;
}
