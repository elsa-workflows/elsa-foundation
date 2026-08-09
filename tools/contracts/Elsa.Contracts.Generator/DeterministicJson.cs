using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Deterministic JSON writer for contract artifacts (spec 149 FR-002 / research R7): ordinal-sorted object
/// keys (recursively, including embedded payload elements), 2-space indent, LF line endings, UTF-8 without
/// BOM, invariant-culture value rendering, wire-form values (camelCase property names and string enums —
/// the same conventions the served catalog uses). Byte-identical output across OS/machine/culture is a
/// hard requirement: the CI check byte-compares committed artifacts against regenerated ones.
/// </summary>
public static class DeterministicJson
{
    public static readonly JsonSerializerOptions WireOptions = CreateWireOptions();

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }

    public static string Serialize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, WireOptions);
        var builder = new StringBuilder();
        WriteNode(builder, node, 0);
        builder.Append('\n');
        return builder.ToString();
    }

    public static byte[] SerializeToBytes<T>(T value) => new UTF8Encoding(false).GetBytes(Serialize(value));

    public static void WriteFile(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>Fingerprint of an artifact's bytes: <c>sha256:&lt;lowercase hex&gt;</c> (spec-148 convention).</summary>
    public static string Fingerprint(byte[] content) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

    private static void WriteNode(StringBuilder builder, JsonNode? node, int depth)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                return;
            case JsonObject jsonObject:
                WriteObject(builder, jsonObject, depth);
                return;
            case JsonArray jsonArray:
                WriteArray(builder, jsonArray, depth);
                return;
            default:
                // Scalars: reuse System.Text.Json's canonical rendering (culture-independent by design).
                builder.Append(node.ToJsonString());
                return;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonObject jsonObject, int depth)
    {
        if (jsonObject.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append("{\n");
        var first = true;
        foreach (var property in jsonObject.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!first)
                builder.Append(",\n");
            first = false;
            Indent(builder, depth + 1);
            builder.Append(JsonSerializer.Serialize(property.Key)).Append(": ");
            WriteNode(builder, property.Value, depth + 1);
        }

        builder.Append('\n');
        Indent(builder, depth);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JsonArray jsonArray, int depth)
    {
        if (jsonArray.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append("[\n");
        var first = true;
        foreach (var item in jsonArray)
        {
            if (!first)
                builder.Append(",\n");
            first = false;
            Indent(builder, depth + 1);
            WriteNode(builder, item, depth + 1);
        }

        builder.Append('\n');
        Indent(builder, depth);
        builder.Append(']');
    }

    private static void Indent(StringBuilder builder, int depth) => builder.Append(' ', depth * 2);
}
