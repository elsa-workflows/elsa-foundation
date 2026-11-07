using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Jobs.Core;

/// <summary>Version-one durable identity framing of every immutable request field.</summary>
public static class JobRequestDigest
{
    /// <summary>Computes the fixed version-one identity of every immutable accepted request field.</summary>
    /// <exception cref="JobException">The request cannot be serialized in its durable format; the original serialization cause is retained as <see cref="Exception.InnerException"/>.</exception>
    public static string Compute(JobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return JobDigestFrame.Compute("elsa.jobs.request.v1\n",
                JsonSerializer.SerializeToElement(request with { NotBefore = request.NotBefore?.ToUniversalTime() }));
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new JobException("jobs.request.invalid", "The Jobs request cannot be represented by its supported durable JSON format.", error);
        }
    }
}

/// <summary>Version-one durable identity framing of a schema/value payload; null has its own framed digest.</summary>
public static class JobPayloadDigest
{
    /// <summary>Computes the fixed version-one payload identity; absence and a present JSON null remain distinct.</summary>
    /// <exception cref="JobException">The payload cannot be serialized in its durable format; the original serialization cause is retained as <see cref="Exception.InnerException"/>.</exception>
    public static string Compute(JobPayload? payload)
    {
        try { return JobDigestFrame.Compute("elsa.jobs.payload.v1\n", JsonSerializer.SerializeToElement(payload)); }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new JobException("jobs.payload.invalid", "The Jobs payload cannot be represented by its supported durable JSON format.", error);
        }
    }
}

internal static class JobDigestFrame
{
    internal static string Compute(string prefix, JsonElement value)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
            Write(writer, value);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        hash.AppendData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
