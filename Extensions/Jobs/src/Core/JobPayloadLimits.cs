using System.Text.Json;

namespace Elsa.Jobs.Core;

/// <summary>Shared durable byte/depth bounds for the complete schema/value JSON envelope, independent of host serializer options.</summary>
public static class JobPayloadLimits
{
    public const int MaximumBytes = 1_048_576;
    public const int MaximumProgressBytes = 16_384;
    public const int MaximumDepth = 32;
    private static readonly JsonSerializerOptions PayloadJson = CreateOptions();

    /// <summary>Counts fixed durable JSON bytes, including schema/escaping, and enforces depth headroom for surrounding request/receipt/model envelopes.</summary>
    /// <exception cref="JobException">The payload exceeds the supported JSON format/depth; the original serialization cause is retained as <see cref="Exception.InnerException"/>.</exception>
    public static int GetByteCount(JobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try { return JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJson).Length; }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new JobException("jobs.payload.invalid", "The Jobs payload cannot be represented by the supported durable JSON format.", error);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { MaxDepth = MaximumDepth };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
