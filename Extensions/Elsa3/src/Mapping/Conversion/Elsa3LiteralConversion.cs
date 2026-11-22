using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>One-time translation of Elsa 3 literals into the target's exact JSON contract.</summary>
internal static class Elsa3LiteralConversion
{
    internal sealed record Note(string Code, string Path, string Message, string Guidance, Elsa3MigrationDiagnosticSeverity Severity);

    internal static JsonElement Rewrite(JsonElement value, Type type, Elsa3ConversionOptions options, Action<Note> report,
        JsonConversionContract? contract = null, string path = "$")
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        contract ??= JsonConversionContract.For(type);
        if (value.ValueKind == JsonValueKind.Null || type == typeof(object) || type == typeof(JsonElement) ||
            typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(type)) return value.Clone();
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
            return RewriteDate(value, type, options, report, path);
        if (type.IsEnum && value.ValueKind == JsonValueKind.String && Enum.TryParse(type, value.GetString(), true, out var enumeration))
            return JsonSerializer.SerializeToElement(enumeration, type, JsonBaseline.Options);

        var info = contract.Options.GetTypeInfo(type);
        if (value.ValueKind == JsonValueKind.Object && info.PolymorphismOptions is { } polymorphism &&
            value.TryGetProperty(polymorphism.TypeDiscriminatorPropertyName, out var discriminator))
        {
            var derived = polymorphism.DerivedTypes.FirstOrDefault(candidate =>
                candidate.TypeDiscriminator is string name && discriminator.ValueKind == JsonValueKind.String && discriminator.GetString() == name ||
                candidate.TypeDiscriminator is int number && discriminator.ValueKind == JsonValueKind.Number && discriminator.TryGetInt32(out var actual) && actual == number);
            if (derived.DerivedType is { } selectedType)
            {
                contract = ChildContract(selectedType, contract);
                info = contract.Options.GetTypeInfo(selectedType);
            }
        }
        if (info.Kind == JsonTypeInfoKind.Dictionary && value.ValueKind == JsonValueKind.Object)
        {
            var elementType = type.GetInterfaces().Append(type).FirstOrDefault(item => item.IsGenericType &&
                item.GetGenericTypeDefinition() is var definition && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)))?.GetGenericArguments()[1];
            return elementType is null ? value.Clone() : RewriteObject(value, property =>
                (property.Name, Rewrite(property.Value, elementType, options, report, ChildContract(elementType, contract), MemberPath(path, property.Name))));
        }
        if (info.Kind == JsonTypeInfoKind.Enumerable && value.ValueKind == JsonValueKind.Array)
        {
            var elementType = type.IsArray ? type.GetElementType() : type.GetInterfaces().Append(type)
                .FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (elementType is null) return value.Clone();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    writer.WriteRawValue(Rewrite(item, elementType, options, report, ChildContract(elementType, contract), $"{path}[{index++}]").GetRawText());
                writer.WriteEndArray();
            }
            using var document = JsonDocument.Parse(stream.ToArray());
            return JsonCanonicalForm.Equals(value, document.RootElement) ? value.Clone() : document.RootElement.Clone();
        }
        if (info.Kind != JsonTypeInfoKind.Object || value.ValueKind != JsonValueKind.Object)
        {
            if (info.Kind == JsonTypeInfoKind.None && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                report(CoverageUnavailable(path, type, info.Converter.GetType()));
            return value.Clone();
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        return RewriteObject(value, property =>
        {
            var exact = info.Properties.FirstOrDefault(member => member.Name == property.Name && !member.IsExtensionData);
            var matches = exact is null ? info.Properties.Where(member => !member.IsExtensionData &&
                member.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)).ToArray() : [exact];
            if (matches.Length > 1 || matches.Length == 1 && !usedNames.Add(matches[0].Name))
                throw new LiteralRefusal(Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous, MemberPath(path, property.Name),
                    "Two literal members would bind the same JSON member, or the target has multiple case-only matches.",
                    "Write each literal key once using the exact JSON member name of the target type.");
            if (matches.Length == 0) return (property.Name, property.Value.Clone());
            var member = matches[0];
            if (member.Name != property.Name)
                report(new(Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten, MemberPath(path, property.Name),
                    $"Literal key '{property.Name}' was rewritten to the target JSON name '{member.Name}'.",
                    "Use the target's exact JSON member names in new workflows.", Elsa3MigrationDiagnosticSeverity.Info));
            var clrMember = member.AttributeProvider as MemberInfo;
            var memberSelection = clrMember?.GetCustomAttribute<JsonConversionAttribute>(true) is not null;
            if (member.CustomConverter is { } customConverter && !memberSelection)
            {
                report(CoverageUnavailable(MemberPath(path, member.Name), member.PropertyType, customConverter.GetType()));
                return (member.Name, property.Value.Clone());
            }
            var underlying = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            var selected = memberSelection || underlying.GetCustomAttribute<JsonConversionAttribute>(true) is not null
                ? JsonConversionContract.For(member.PropertyType, clrMember) : contract;
            return (member.Name, Rewrite(property.Value, member.PropertyType, options, report, selected, MemberPath(path, member.Name)));
        });
    }

    private static JsonElement RewriteObject(JsonElement value, Func<JsonProperty, (string Name, JsonElement Value)> convert)
    {
        using var stream = new MemoryStream();
        var changed = false;
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                var mapped = convert(property);
                changed |= property.Name != mapped.Name || property.Value.GetRawText() != mapped.Value.GetRawText();
                writer.WritePropertyName(mapped.Name);
                writer.WriteRawValue(mapped.Value.GetRawText());
            }
            writer.WriteEndObject();
        }
        if (!changed) return value.Clone();
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement RewriteDate(JsonElement value, Type type, Elsa3ConversionOptions options, Action<Note> report, string path)
    {
        if (value.ValueKind != JsonValueKind.String) return value.Clone();
        var text = value.GetString()!;
        var separator = text.IndexOf('T');
        var hasOffset = separator >= 0 && (text.EndsWith('Z') || text.AsSpan(separator).IndexOfAny('+', '-') >= 0);
        if (hasOffset)
        {
            if (!value.TryGetDateTimeOffset(out _)) throw RefuseDate(path, "The literal is not a valid ISO 8601 instant.");
            if (type != typeof(DateTimeOffset)) report(WallClock(path));
            return value.Clone();
        }
        if (options.DateInterpretation == Elsa3DateInterpretation.Refuse)
            throw RefuseDate(path, $"An offset-less literal bound to {type.Name} has no established instant.");
        if (!value.TryGetDateTime(out var local)) throw RefuseDate(path, "The literal is not a valid ISO 8601 date and time.");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        string interpreted;
        if (options.DateInterpretation == Elsa3DateInterpretation.SourceTimeZone)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(options.SourceTimeZone!);
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
                throw RefuseDate(path, "The literal falls in a daylight-saving gap or a repeated local hour; add an explicit offset to choose an instant.");
            try { interpreted = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", System.Globalization.CultureInfo.InvariantCulture); }
            catch (ArgumentException) { throw RefuseDate(path, "The interpreted date is outside the supported instant range."); }
        }
        else interpreted = local.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", System.Globalization.CultureInfo.InvariantCulture);
        report(new(Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted, path,
            options.DateInterpretation == Elsa3DateInterpretation.SourceTimeZone
                ? $"The literal '{text}' was interpreted as '{interpreted}' in source time zone '{options.SourceTimeZone}', with its offset on that date."
                : $"The literal '{text}' was interpreted as '{interpreted}' in UTC. Its instant differs from Elsa 3 if that server was not on UTC.",
            "Keep the same date interpretation when converting this workflow again.",
            options.DateInterpretation == Elsa3DateInterpretation.SourceTimeZone ? Elsa3MigrationDiagnosticSeverity.Info : Elsa3MigrationDiagnosticSeverity.Warning));
        return JsonSerializer.SerializeToElement(interpreted, JsonBaseline.Options);
    }

    internal static Note WallClock(string path) => new(Elsa3ActivityDiagnosticCodes.LiteralDateWallClock, path,
        "Elsa 4 normalizes DateTime instants and offset-carrying DateOnly values to UTC; the wall-clock hour or date can differ from Elsa 3. An expression's date value is not known during import.",
        "Check the converted date behaviour against the Elsa 3 server's time zone.", Elsa3MigrationDiagnosticSeverity.Warning);

    private static LiteralRefusal RefuseDate(string path, string message) => new(Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing, path, message,
        "Set the Elsa 3 converter's date interpretation to SourceTimeZone with the server's IANA zone, or add Z or an explicit offset to the value; choose AssumeUtc only to accept UTC behaviour.");
    private static string MemberPath(string path, string name) => $"{path}[{JsonSerializer.Serialize(name, JsonBaseline.Options)}]";
    private static JsonConversionContract ChildContract(Type type, JsonConversionContract parent)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.GetCustomAttribute<JsonConversionAttribute>(true) is not null ? JsonConversionContract.For(type) : parent;
    }
    private static Note CoverageUnavailable(string path, Type type, Type converter) => new("json-unmatched-coverage-unavailable", path,
        $"Literal keys for {type.Name} are owned by converter {converter.FullName}; their shape was preserved without inferred rewrites.",
        "Check the literal against that converter's published JSON contract.", Elsa3MigrationDiagnosticSeverity.Info);

    internal sealed class LiteralRefusal(string code, string path, string message, string guidance) : Exception(message)
    {
        internal string Code { get; } = code;
        internal string Path { get; } = path;
        internal string Guidance { get; } = guidance;
    }
}
