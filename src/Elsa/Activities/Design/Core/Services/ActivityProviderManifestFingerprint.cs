using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

public static class ActivityProviderManifestFingerprint
{
    public static string Compute(ActivityProviderManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("providerKey", manifest.ProviderKey);
            writer.WriteString("schemaVersion", manifest.SchemaVersion);
            writer.WritePropertyName("payload");
            WriteCanonical(writer, manifest.Payload);
            writer.WriteEndObject();
        }
        return $"sha256:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
