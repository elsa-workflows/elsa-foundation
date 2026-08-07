using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = PruneToleratedAbsencesFromRequired
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

    // The exporter marks every non-defaulted constructor parameter of a positional record as
    // "required", but the wire deserializer treats a missing nullable parameter exactly like an
    // explicit null. Keeping those members in "required" would make clients reject bodies the
    // server accepts, so "required" is pruned to members whose absence the contract cannot
    // represent: non-nullable and without a default.
    private static JsonNode PruneToleratedAbsencesFromRequired(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (schema is not JsonObject schemaObject ||
            !schemaObject.TryGetPropertyValue("required", out var requiredNode) ||
            requiredNode is not JsonArray required)
            return schema;

        var nullableMembers = context.TypeInfo.Properties
            .Where(property => property.IsSetNullable)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = required.Count - 1; index >= 0; index--)
        {
            if (required[index]?.GetValue<string>() is { } member && nullableMembers.Contains(member))
                required.RemoveAt(index);
        }

        if (required.Count == 0)
            schemaObject.Remove("required");

        return schemaObject;
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
