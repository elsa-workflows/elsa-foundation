using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Elsa.Agent.Core.Models;
using EngineSchema = Json.Schema.JsonSchema;

namespace Elsa.Agent.JsonSchema;

internal sealed record AgentSchemaProblem(string Path, string Code, string Message);

internal sealed record AgentSchemaPreparation(PreparedAgentSchema? Schema, IReadOnlyList<AgentSchemaProblem> Problems);

/// <summary>A finite JSON profile. Unsupported vocabulary is refused rather than silently ignored.</summary>
internal sealed class AgentSchemaValidator
{
    internal const string Profile = AgentRuntimeIds.SchemaProfile;
    internal const string DialectUri = "https://json-schema.org/draft/2020-12/schema";
    internal const int MaximumSchemaBytes = 65_536;
    internal const int MaximumValueBytes = 1_048_576;
    internal const int MaximumSchemaNodes = 2_048;
    internal const int MaximumValueNodes = 16_384;
    internal const int MaximumDepth = 32;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "$schema", "$defs", "$ref", "type", "properties", "required", "additionalProperties", "items",
        "minItems", "maxItems", "minLength", "maxLength", "minimum", "maximum", "exclusiveMinimum",
        "exclusiveMaximum", "multipleOf", "enum", "const", "title", "description", "default",
        "minProperties", "maxProperties"
    };

    public AgentSchemaPreparation Prepare(JsonElement schema)
    {
        try
        {
            AgentJsonBounds.Check(schema, MaximumSchemaBytes, MaximumSchemaNodes);
            var remaining = MaximumSchemaNodes;
            var expanded = Expand(schema, schema, "$", 0, new(StringComparer.Ordinal), ref remaining);
            var json = JsonSerializer.SerializeToElement(expanded);
            AgentJsonBounds.Check(json, MaximumSchemaBytes, MaximumSchemaNodes);
            var built = EngineSchema.Build(json, new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new()
            });
            return new(new PreparedAgentSchema(json, built), []);
        }
        catch (AgentSchemaException exception)
        {
            return new(null, [new(exception.Path, exception.Code, exception.Message)]);
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Library messages can contain schema or instance values. Never expose them as diagnostics.
            return new(null, [new("$", "agent.schema.invalid", "The schema is invalid for the supported JSON profile.")]);
        }
    }

    private static JsonNode? Expand(JsonElement schema, JsonElement root, string path, int depth,
        HashSet<string> references, ref int remaining)
    {
        if (depth > MaximumDepth || --remaining < 0)
            throw new AgentSchemaException(path, "agent.schema.limit", "The expanded schema exceeds the profile limits.");
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return JsonValue.Create(schema.GetBoolean());
        if (schema.ValueKind != JsonValueKind.Object)
            throw new AgentSchemaException(path, "agent.schema.invalid", "A schema must be an object or boolean.");

        foreach (var property in schema.EnumerateObject())
            if (!Keywords.Contains(property.Name))
                throw new AgentSchemaException(Path(path, property.Name), "agent.schema.keyword", "This schema keyword is not supported by the profile.");

        // Validate unused definitions too. References are resolved locally and removed before the engine sees them.
        if (schema.TryGetProperty("$defs", out var definitions))
        {
            if (definitions.ValueKind != JsonValueKind.Object)
                throw new AgentSchemaException(Path(path, "$defs"), "agent.schema.invalid", "Definitions must be an object.");
            foreach (var definition in definitions.EnumerateObject())
                Expand(definition.Value, root, Path(Path(path, "$defs"), definition.Name), depth + 1, references, ref remaining);
        }
        if (schema.TryGetProperty("$schema", out var dialect) &&
            (dialect.ValueKind != JsonValueKind.String || dialect.GetString() != DialectUri))
            throw new AgentSchemaException(Path(path, "$schema"), "agent.schema.dialect", "Only the declared draft 2020-12 dialect is supported.");

        if (schema.TryGetProperty("$ref", out var reference))
        {
            if (reference.ValueKind != JsonValueKind.String || reference.GetString() is not { } pointer ||
                !(pointer == "#" || pointer.StartsWith("#/", StringComparison.Ordinal)))
                throw new AgentSchemaException(Path(path, "$ref"), "agent.schema.reference", "Only local JSON Pointer references are supported.");
            if (schema.EnumerateObject().Any(x => x.Name is not ("$ref" or "$defs" or "$schema" or "title" or "description")))
                throw new AgentSchemaException(path, "agent.schema.reference-sibling", "Validation keywords beside a reference are unsupported in this profile.");
            if (!references.Add(pointer))
                throw new AgentSchemaException(path, "agent.schema.reference-cycle", "Cyclic schema references are unsupported in this profile.");
            try { return Expand(Resolve(root, pointer, path), root, path, depth + 1, references, ref remaining); }
            finally { references.Remove(pointer); }
        }

        var result = new JsonObject();
        foreach (var property in schema.EnumerateObject())
        {
            var memberPath = Path(path, property.Name);
            switch (property.Name)
            {
                case "$defs":
                    break;
                case "properties":
                    if (property.Value.ValueKind != JsonValueKind.Object)
                        throw new AgentSchemaException(memberPath, "agent.schema.invalid", "Properties must be an object.");
                    var properties = new JsonObject();
                    foreach (var member in property.Value.EnumerateObject())
                        properties[member.Name] = Expand(member.Value, root, Path(memberPath, member.Name), depth + 1, references, ref remaining);
                    result[property.Name] = properties;
                    break;
                case "items":
                case "additionalProperties":
                    result[property.Name] = Expand(property.Value, root, memberPath, depth + 1, references, ref remaining);
                    break;
                default:
                    result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                    break;
            }
        }
        return result;
    }

    private static JsonElement Resolve(JsonElement root, string reference, string path)
    {
        var current = root;
        if (reference == "#") return current;
        foreach (var segment in reference[2..].Split('/'))
        {
            // Percent-encoded URI fragments and malformed escapes are explicitly outside this profile.
            if (segment.Contains('%') || segment.Replace("~0", "", StringComparison.Ordinal).Replace("~1", "", StringComparison.Ordinal).Contains('~'))
                throw new AgentSchemaException(path, "agent.schema.reference", "The local reference encoding is unsupported.");
            var key = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out var next))
                throw new AgentSchemaException(path, "agent.schema.reference", "The local reference does not identify a schema.");
            current = next;
        }
        return current;
    }

    internal static string Path(string parent, string member) => parent + "/" + member.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

internal sealed class PreparedAgentSchema(JsonElement schema, EngineSchema engine)
{
    public JsonElement Schema { get; } = schema.Clone();

    public IReadOnlyList<AgentSchemaProblem> Validate(JsonElement instance)
    {
        try
        {
            AgentJsonBounds.Check(instance, AgentSchemaValidator.MaximumValueBytes, AgentSchemaValidator.MaximumValueNodes);
            var result = engine.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.Flag, Culture = CultureInfo.InvariantCulture });
            return result.IsValid ? [] : [new("$", "agent.value.invalid", "The value does not match the declared schema.")];
        }
        catch (AgentSchemaException exception)
        {
            return [new(exception.Path, exception.Code, exception.Message)];
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return [new("$", "agent.value.invalid", "The value cannot be evaluated under the declared schema profile.")];
        }
    }
}

internal sealed class AgentSchemaException(string path, string code, string message) : Exception(message)
{
    public string Path { get; } = path;
    public string Code { get; } = code;
}
