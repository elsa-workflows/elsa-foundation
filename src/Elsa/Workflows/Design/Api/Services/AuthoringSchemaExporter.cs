using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// Exports JSON Schemas for authoring contract types so headless clients can generate valid
/// request payloads without reverse-engineering the wire format. Schemas are generated from a
/// serializer configuration identical to the FastEndpoints wire options, so property names,
/// dictionary keys, and enum values in the schema match actual traffic.
/// </summary>
internal static class AuthoringSchemaExporter
{
    // Duplicates the wire options of Elsa.Api.FastEndpoints' SerializationFastEndpointConfigurator
    // (internal to that assembly, deliberately not exposed): camelCase properties, camelCase
    // dictionary keys, camelCase string enums, case-insensitive reads. Must stay in sync with it.
    private static readonly JsonSerializerOptions WireOptions = CreateWireOptions();

    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true
    };

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // The schema exporter requires a resolver; the wire options rely on the same default.
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// Exports the JSON Schema of <paramref name="type"/> as serialized on the wire. Opaque members
    /// (<c>object</c>, <c>JsonElement</c>) intentionally export as unconstrained schemas.
    /// </summary>
    internal static JsonElement ExportSchema(Type type)
    {
        var node = WireOptions.GetJsonSchemaAsNode(type, ExporterOptions);
        return JsonSerializer.SerializeToElement(node, WireOptions);
    }

    /// <summary>
    /// Computes the canonical <c>sha256:&lt;lowercase hex&gt;</c> fingerprint of a snapshot,
    /// following the descriptor-document fingerprint pattern of the design APIs.
    /// </summary>
    internal static string ComputeFingerprint<TSnapshot>(TSnapshot snapshot)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }
}
