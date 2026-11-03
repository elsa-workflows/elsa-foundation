using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Primitives.Models;

namespace Elsa.Agent.Specialists.Design.Services;

internal sealed record SpecialistSchemaCompilation(
    PreparedSpecialistSchema? Inputs,
    PreparedSpecialistSchema? Result,
    IReadOnlyList<SpecialistSchemaProblem> Problems);

internal sealed class SpecialistSchemaCompiler(SpecialistSchemaValidator validator)
{
    public SpecialistSchemaCompilation Compile(ActivityContract contract, SpecialistManifest manifest)
    {
        var problems = new List<SpecialistSchemaProblem>();
        var inputs = Build(contract.Inputs.Select(x => new Member(x.ReferenceKey, x.Name, x.Type, x.IsRequired, x.IsNullable)),
            manifest.InputConstraints ?? new Dictionary<string, JsonElement>(), "/inputConstraints", problems);
        var result = Build(contract.Outputs.Select(x => new Member(x.ReferenceKey, x.Name, x.Type, x.IsRequired, x.IsNullable)),
            manifest.ResultConstraints ?? new Dictionary<string, JsonElement>(), "/resultConstraints", problems);
        return new(inputs, result, problems);
    }

    private PreparedSpecialistSchema? Build(IEnumerable<Member> members,
        IReadOnlyDictionary<string, JsonElement> constraints, string path, List<SpecialistSchemaProblem> problems)
    {
        var fields = members.ToArray();
        var keys = fields.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in constraints.Keys.Where(x => !keys.Contains(x)))
            problems.Add(new(SpecialistSchemaValidator.Path(path, key), "specialist.schema.unknown-member", "A constraint must identify an existing public member key."));
        if (fields.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Name)) ||
            fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != fields.Length ||
            fields.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
        {
            problems.Add(new(path, "specialist.schema.member", "Public member keys and JSON names must be nonempty and unique."));
            return null;
        }
        if (path == "/resultConstraints" && fields.Any(member =>
        {
            var segments = member.Name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return member.Name == "$" || segments.Length == 0 || fields.Any(other => other.Key != member.Key && StringComparer.OrdinalIgnoreCase.Equals(other.Name, segments[0]));
        }))
        {
            problems.Add(new(path, "specialist.schema.projection-name", "Output names must not overlap another output's dotted-path root ignoring case, use the reserved whole-result name, or collapse to an empty path."));
            return null;
        }
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var member in fields)
        {
            var memberPath = SpecialistSchemaValidator.Path(path, member.Key);
            try
            {
                var schema = TypeSchema(member.Type, member.Nullable, memberPath);
                if (constraints.TryGetValue(member.Key, out var constraint))
                {
                    var prepared = validator.Prepare(constraint);
                    if (prepared.Schema is null)
                    {
                        problems.AddRange(prepared.Problems.Select(x => x with { Path = memberPath + x.Path[1..] }));
                        continue;
                    }
                    if (prepared.Schema.Schema.ValueKind != JsonValueKind.Object)
                        throw Refinement(memberPath);
                    schema = Merge(schema, JsonNode.Parse(prepared.Schema.Schema.GetRawText())!.AsObject(), memberPath);
                }
                ValidateNativeRefinements(schema, member.Type, member.Nullable, memberPath);
                properties[member.Name] = schema;
                if (member.Required) required.Add(member.Name);
            }
            catch (SpecialistSchemaException exception)
            {
                problems.Add(new(exception.Path, exception.Code, exception.Message));
            }
        }
        if (problems.Any(x => x.Path.StartsWith(path, StringComparison.Ordinal))) return null;
        var root = new JsonObject
        {
            ["$schema"] = SpecialistSchemaValidator.DialectUri,
            ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false
        };
        var compiled = validator.Prepare(JsonSerializer.SerializeToElement(root));
        problems.AddRange(compiled.Problems.Select(x => x with { Path = path + x.Path[1..] }));
        return compiled.Schema;
    }

    private static JsonObject TypeSchema(TypeReference type, bool nullable, string path)
    {
        JsonObject scalar = type.Alias switch
        {
            "String" => new() { ["type"] = "string" },
            "Boolean" => new() { ["type"] = "boolean" },
            "Int32" => new() { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue },
            "Int64" => new() { ["type"] = "integer", ["minimum"] = long.MinValue, ["maximum"] = long.MaxValue },
            "Double" => new() { ["type"] = "number", ["minimum"] = double.MinValue, ["maximum"] = double.MaxValue },
            "Decimal" => new() { ["type"] = "number", ["minimum"] = decimal.MinValue, ["maximum"] = decimal.MaxValue },
            "DateTime" or "DateTimeOffset" or "TimeSpan" or "Guid" => new() { ["type"] = "string" },
            "Object" or "JsonElement" or "System.Text.Json.JsonElement" => new(),
            _ => throw new SpecialistSchemaException(path, "specialist.schema.type", "This type alias is outside the specialist JSON profile; use a JSON type or an explicit conversion boundary.")
        };
        var schema = type.CollectionKind switch
        {
            CollectionKind.Single => scalar,
            CollectionKind.Array or CollectionKind.List => new JsonObject { ["type"] = "array", ["items"] = scalar },
            CollectionKind.Dictionary => new JsonObject { ["type"] = "object", ["additionalProperties"] = scalar },
            _ => throw new SpecialistSchemaException(path, "specialist.schema.collection", "This collection shape is outside the specialist JSON profile.")
        };
        if (type.CollectionKind == CollectionKind.Single && type.Alias is "Object" or "JsonElement" or "System.Text.Json.JsonElement")
            schema["type"] = new JsonArray(nullable
                ? ["object", "array", "string", "number", "boolean", "null"]
                : new JsonNode?[] { "object", "array", "string", "number", "boolean" });
        if (nullable && schema["type"] is JsonValue value)
            schema["type"] = new JsonArray(value.GetValue<string>(), "null");
        return schema;
    }

    private JsonObject Merge(JsonObject baseline, JsonObject constraint, string path)
    {
        var result = (JsonObject)baseline.DeepClone();
        if (constraint["type"] is { } targetType && baseline["type"] is { } sourceType)
        {
            var source = Types(sourceType);
            if (Types(targetType).Any(x => !source.Contains(x) && !(x == "integer" && source.Contains("number"))))
                throw Refinement(path);
        }
        foreach (var pair in constraint)
        {
            if (pair.Key == "$schema") continue;
            var effectiveTypes = (constraint["type"] ?? result["type"]) is { } declared ? Types(declared) : null;
            var applicable = pair.Key switch
            {
                "minLength" or "maxLength" => new[] { "string" },
                "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" or "multipleOf" => ["number", "integer"],
                "items" or "minItems" or "maxItems" => ["array"],
                "properties" or "required" or "additionalProperties" or "minProperties" or "maxProperties" => ["object"],
                _ => []
            };
            if (effectiveTypes is not null && applicable.Length != 0 && !applicable.Any(effectiveTypes.Contains)) throw Refinement(path);
            if (pair.Key is "minimum" or "exclusiveMinimum" && baseline["minimum"] is { } minimum &&
                !Within("minimum", minimum, pair.Value)) throw Refinement(path);
            if (pair.Key is "maximum" or "exclusiveMaximum" && baseline["maximum"] is { } maximum &&
                !Within("maximum", maximum, pair.Value)) throw Refinement(path);
            if (pair.Key is "items" or "additionalProperties" && baseline[pair.Key] is JsonObject nested)
            {
                if (pair.Value is JsonValue boolean && boolean.TryGetValue<bool>(out var allowed))
                {
                    if (allowed) throw Refinement(path);
                    result[pair.Key] = false;
                }
                else if (pair.Value is JsonObject next) result[pair.Key] = Merge(nested, next, path);
                else throw Refinement(path);
            }
            else if (pair.Key == "properties" && pair.Value is JsonObject properties && baseline["additionalProperties"] is JsonObject item)
            {
                var mapped = new JsonObject();
                foreach (var property in properties)
                    mapped[property.Key] = property.Value is JsonObject value ? Merge(item, value, path) : throw Refinement(path);
                result[pair.Key] = mapped;
            }
            else result[pair.Key] = pair.Value?.DeepClone();
        }
        ValidateRefinement(result, path);
        return result;
    }

    private void ValidateRefinement(JsonObject schema, string path)
    {
        foreach (var lower in new[] { "minimum", "exclusiveMinimum" })
        foreach (var upper in new[] { "maximum", "exclusiveMaximum" })
        {
            if (schema[lower] is not { } min || schema[upper] is not { } max) continue;
            if (!Within("minimum", min, max) ||
                (lower == "exclusiveMinimum" || upper == "exclusiveMaximum") && Within("maximum", min, max))
                throw Refinement(path);
        }
        foreach (var (lower, upper) in new[] { ("minimum", "maximum"), ("minLength", "maxLength"), ("minItems", "maxItems"), ("minProperties", "maxProperties") })
            if (schema[lower] is { } min && schema[upper] is { } max && !Within("minimum", min, max)) throw Refinement(path);
        var prepared = validator.Prepare(JsonSerializer.SerializeToElement(schema));
        if (prepared.Schema is null) throw Refinement(path);
        if (schema["enum"] is JsonArray enumeration)
            foreach (var item in enumeration)
                if (prepared.Schema.Validate(JsonSerializer.SerializeToElement(item)).Count != 0) throw Refinement(path);
        if (schema.TryGetPropertyValue("const", out var constant) &&
            prepared.Schema.Validate(JsonSerializer.SerializeToElement(constant)).Count != 0) throw Refinement(path);
    }

    private static void ValidateNativeRefinements(JsonObject schema, TypeReference type, bool nullable, string path)
    {
        bool Representable(JsonNode? node)
        {
            var value = JsonSerializer.SerializeToElement(node);
            return value.ValueKind == JsonValueKind.Null ? nullable : SpecialistWireTypes.CanConvert(value, type.Alias, type.CollectionKind);
        }
        if (schema["enum"] is JsonArray enumeration && enumeration.Any(x => !Representable(x))) throw Refinement(path);
        if (schema.TryGetPropertyValue("const", out var constant) && !Representable(constant)) throw Refinement(path);
        if (type.CollectionKind is CollectionKind.Array or CollectionKind.List or CollectionKind.Dictionary)
        {
            var itemType = new TypeReference(type.Alias);
            var itemNullable = type.Alias is "Object" or "JsonElement" or "System.Text.Json.JsonElement";
            var keyword = type.CollectionKind == CollectionKind.Dictionary ? "additionalProperties" : "items";
            if (schema[keyword] is JsonObject item) ValidateNativeRefinements(item, itemType, itemNullable, path);
            if (type.CollectionKind == CollectionKind.Dictionary && schema["properties"] is JsonObject properties)
                foreach (var property in properties)
                    if (property.Value is JsonObject value) ValidateNativeRefinements(value, itemType, itemNullable, path);
        }
    }

    private static HashSet<string> Types(JsonNode node) => node is JsonArray array
        ? array.Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
        : new([node.GetValue<string>()], StringComparer.Ordinal);

    private bool Within(string keyword, JsonNode bound, JsonNode? value)
    {
        // Let the pinned engine compare JSON numbers exactly; double rounding can widen Int64 by one.
        var guard = validator.Prepare(JsonSerializer.SerializeToElement(new JsonObject { [keyword] = bound.DeepClone() }));
        return guard.Schema is not null && guard.Schema.Validate(JsonSerializer.SerializeToElement(value)).Count == 0;
    }
    private static SpecialistSchemaException Refinement(string path) => new(path, "specialist.schema.refinement", "The constraint must be a compatible refinement of the public type.");
    private sealed record Member(string Key, string Name, TypeReference Type, bool Required, bool Nullable);
}
