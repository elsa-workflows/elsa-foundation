using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Jobs.Core;

/// <summary>Version-one durable identity framing of every immutable request field.</summary>
public static class JobRequestDigest
{
    public static string Compute(JobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JobDigestFrame.Compute("elsa.jobs.request.v1\n",
            JsonSerializer.SerializeToElement(request with { NotBefore = request.NotBefore?.ToUniversalTime() }));
    }
}

/// <summary>Version-one durable identity framing of a schema/value payload; null has its own framed digest.</summary>
public static class JobPayloadDigest
{
    public static string Compute(JobPayload? payload) =>
        JobDigestFrame.Compute("elsa.jobs.payload.v1\n", JsonSerializer.SerializeToElement(payload));
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
