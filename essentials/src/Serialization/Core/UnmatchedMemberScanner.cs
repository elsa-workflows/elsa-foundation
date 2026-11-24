using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>A diagnostic about a typed copy of carried JSON. Its path may contain user data and needs the owner's protection policy.</summary>
public sealed record JsonConversionDiagnostic(string Code, string Path, Type TargetType, string? Converter = null);

/// <summary>Optional matching information supplied by a custom JSON converter for unmatched-member diagnostics.</summary>
public interface IJsonConversionMemberMatcher
{
    /// <summary>Reports whether the converter consumes a JSON key, and the type used for its value.</summary>
    bool TryGetMemberType(string name, out Type memberType);
}

/// <summary>Inspects the effective serializer contract once before conversion, without performing another deserialization.</summary>
public static class UnmatchedMemberScanner
{
    /// <summary>Finds unmatched members; dictionaries and extension data intentionally consume arbitrary keys.</summary>
    public static IReadOnlyList<JsonConversionDiagnostic> Scan(JsonElement value, Type type, JsonConversionContract contract) =>
        Scan(value, type, contract, new JsonConversionContract.Operation());

    internal static IReadOnlyList<JsonConversionDiagnostic> Scan(JsonElement value, Type type, JsonConversionContract contract, JsonConversionContract.Operation operation)
    {
        var result = new List<JsonConversionDiagnostic>();
        Visit(value, type, contract, "$", result, operation);
        return result;
    }

    private static void Visit(JsonElement value, Type type, JsonConversionContract contract, string path, List<JsonConversionDiagnostic> result, JsonConversionContract.Operation operation, JsonConverter? customConverter = null)
    {
        var temporalType = Nullable.GetUnderlyingType(type) ?? type;
        type = temporalType;
        if (value.ValueKind != JsonValueKind.Null && (temporalType == typeof(DateTime) || temporalType == typeof(DateTimeOffset)))
        {
            if (value.ValueKind != JsonValueKind.String) throw new JsonException($"{temporalType.Name} requires a JSON string.", path, null, null);
            if (!StrictDateTimeConverters.HasOffset(value.GetString()!)) throw new JsonConversionException("json-datetime-offset-missing", $"{temporalType.Name} requires Z or an explicit offset.", path);
            if (!value.TryGetDateTimeOffset(out _)) throw new JsonException($"Invalid ISO 8601 {temporalType.Name}.", path, null, null);
        }
        if (type == typeof(JsonElement) || type == typeof(object) || typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(type)) return;
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return;
        var info = operation.GetOptions(contract).GetTypeInfo(type);
        if (customConverter is not null || info.Kind == JsonTypeInfoKind.None)
        {
            var converter = customConverter ?? info.Converter;
            if (converter is IJsonConversionMemberMatcher matcher && value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    var memberPath = MemberPath(path, property.Name);
                    if (matcher.TryGetMemberType(property.Name, out var memberType)) Visit(property.Value, memberType, contract, memberPath, result, operation);
                    else Unmatched(memberPath, type, contract, result);
                }
            }
            else result.Add(new("json-unmatched-coverage-unavailable", path, type, converter.GetType().FullName));
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Dictionary)
        {
            var valueType = type.GetInterfaces().Append(type).FirstOrDefault(x => x.IsGenericType && (x.GetGenericTypeDefinition() == typeof(IDictionary<,>) || x.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))?.GetGenericArguments()[1];
            if (valueType is not null && value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject()) Visit(property.Value, valueType, operation.ChildContract(valueType, contract), MemberPath(path, property.Name), result, operation);
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Enumerable && value.ValueKind == JsonValueKind.Array)
        {
            var itemType = type.IsArray ? type.GetElementType() : type.GetInterfaces().Append(type)
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (itemType is null) return;
            var index = 0;
            foreach (var item in value.EnumerateArray()) Visit(item, itemType, operation.ChildContract(itemType, contract), $"{path}[{index++}]", result, operation);
            return;
        }
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            result.Add(new("json-unmatched-coverage-unavailable", path, type));
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        var discriminator = info.PolymorphismOptions?.TypeDiscriminatorPropertyName;
        if (discriminator is not null && value.TryGetProperty(discriminator, out var token))
        {
            var selected = info.PolymorphismOptions!.DerivedTypes.FirstOrDefault(x =>
                x.TypeDiscriminator is string text && token.ValueKind == JsonValueKind.String && token.GetString() == text ||
                x.TypeDiscriminator is int number && token.ValueKind == JsonValueKind.Number && token.TryGetInt32(out var actual) && actual == number);
            if (selected.DerivedType is not null) info = operation.GetOptions(contract).GetTypeInfo(selected.DerivedType);
        }
        var comparison = contract.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == discriminator) continue;
            JsonPropertyInfo? member = null;
            for (var index = 0; index < info.Properties.Count; index++)
            {
                var candidate = info.Properties[index];
                if (candidate.IsExtensionData || candidate.Set is null && candidate.Get is null ||
                    !candidate.Name.Equals(property.Name, comparison)) continue;
                member = candidate;
                break;
            }
            if (member is not null)
            {
                // Scalar values have no members to inspect. Instant tokens are checked here;
                // other scalar fitting still belongs to the actual deserializer below the scan.
                var scalar = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
                if (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) &&
                    scalar != typeof(DateTime) && scalar != typeof(DateTimeOffset)) continue;
                var selection = member.AttributeProvider as System.Reflection.MemberInfo;
                var scoped = JsonConversionContract.HasSelection(member.PropertyType, selection);
                // A member converter may consume a shape unrelated to its CLR properties.
                Visit(property.Value, member.PropertyType, scoped ? JsonConversionContract.For(member.PropertyType, selection) : contract, MemberPath(path, property.Name), result, operation, scoped ? null : member.CustomConverter);
                continue;
            }
            if (info.Properties.Any(x => x.IsExtensionData)) continue;
            Unmatched(MemberPath(path, property.Name), type, contract, result);
        }
    }

    private static string MemberPath(string path, string name) => path + "[" + JsonSerializer.Serialize(name, JsonBaseline.Options) + "]";
    private static void Unmatched(string path, Type type, JsonConversionContract contract, List<JsonConversionDiagnostic> result)
    {
        var diagnostic = new JsonConversionDiagnostic(contract.RefuseUnmatchedMembers ? "json-unmatched-member-refused" : "json-unmatched-member", path, type);
        if (contract.RefuseUnmatchedMembers) throw new JsonConversionException(diagnostic.Code, $"A JSON member matches no member of {type.FullName}.", path);
        result.Add(diagnostic);
    }
}
