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
        return $"sha256:{Elsa.Serialization.Core.JsonHashInput.HashUtf8(stream.ToArray())}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        writer.WriteRawValue(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(value), skipInputValidation: true);
    }
}
