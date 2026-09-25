using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Modularity.Planning.Bridge;

public enum SettingReviewJsonType
{
    Boolean,
    Number,
    String,
    Null,
    Object,
    Array
}

public sealed record SettingReviewField
{
    internal SettingReviewField(
        string featureId,
        string pointer,
        ImmutableArray<string> pointerSegments,
        SettingReviewJsonType expectedType,
        bool portable)
    {
        FeatureId = featureId;
        Pointer = pointer;
        PointerSegments = pointerSegments;
        ExpectedType = expectedType;
        Portable = portable;
    }

    public string FeatureId { get; }
    public string Pointer { get; }
    public ImmutableArray<string> PointerSegments { get; }
    public SettingReviewJsonType ExpectedType { get; }
    public bool Portable { get; }

    public bool MatchesValueKind(JsonValueKind valueKind) => ExpectedType switch
    {
        SettingReviewJsonType.Boolean => valueKind is JsonValueKind.True or JsonValueKind.False,
        SettingReviewJsonType.Number => valueKind is JsonValueKind.Number,
        SettingReviewJsonType.String => valueKind is JsonValueKind.String,
        SettingReviewJsonType.Null => valueKind is JsonValueKind.Null,
        SettingReviewJsonType.Object => valueKind is JsonValueKind.Object,
        SettingReviewJsonType.Array => valueKind is JsonValueKind.Array,
        _ => false
    };
}

public sealed class SettingReviewDocument
{
    internal SettingReviewDocument(ImmutableArray<SettingReviewField> fields) => Fields = fields;

    public ImmutableArray<SettingReviewField> Fields { get; }

    /// <summary>Finds a declaration only when both the feature ID and pointer match exactly.</summary>
    public bool TryGetExact(string featureId, string pointer, out SettingReviewField field)
    {
        var match = Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.FeatureId, featureId, StringComparison.Ordinal) &&
            string.Equals(candidate.Pointer, pointer, StringComparison.Ordinal));

        field = match!;
        return match is not null;
    }

    /// <summary>
    /// Requires an exact, portable declaration whose expected JSON type matches the observed value.
    /// The caller remains responsible for requiring leaf reviews for nonempty objects and arrays.
    /// </summary>
    public SettingReviewField RequirePortableValue(string featureId, string pointer, JsonValueKind actualValueKind)
    {
        if (!TryGetExact(featureId, pointer, out var field) || !field.Portable || !field.MatchesValueKind(actualValueKind))
            throw new SettingReviewException(
                "bridge-portable-unsafe",
                "The setting is unclassified, not approved for portability, or has a different JSON type than reviewed.");

        return field;
    }
}

public sealed class SettingReviewException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Reads the optional local setting-review document without depending on host source parsing.</summary>
public static class SettingReviewReader
{
    private static readonly StringComparer s_featureComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer s_identityComparer = StringComparer.OrdinalIgnoreCase;

    public static SettingReviewDocument Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = SelectionJsonReader.ParseStrict(json);
        }
        catch (SelectionDocumentException exception)
        {
            var code = exception.Code == "duplicate-json-key" ? "bridge-source-duplicate" : "bridge-source-invalid";
            throw new SettingReviewException(code, "The setting-review document is malformed or contains duplicate JSON properties.");
        }
        catch (ArgumentNullException)
        {
            throw Invalid("The setting-review document is missing.");
        }

        using (document)
        {
            var root = RequireObject(document.RootElement, "The setting-review document must be a JSON object.");
            RequireFields(root, "schemaVersion", "fields");
            if (RequiredString(root, "schemaVersion") != "1")
                throw Invalid("The setting-review schema version is unsupported.");

            var fields = RequiredArray(root, "fields").EnumerateArray()
                .Select(ReadField)
                .ToImmutableArray();
            ValidateUniqueFields(fields);
            return new SettingReviewDocument(fields);
        }
    }

    private static SettingReviewField ReadField(JsonElement element)
    {
        var row = RequireObject(element, "Each setting-review field must be a JSON object.");
        RequireFields(row, "featureId", "pointer", "type", "portable");

        var featureId = RequiredString(row, "featureId");
        if (!SelectionValueRules.IsSafeReference(featureId))
            throw Invalid("A setting-review feature ID must be a safe identity label.");

        var pointer = RequiredString(row, "pointer");
        var pointerSegments = ParsePointer(pointer);
        var expectedType = ParseType(RequiredString(row, "type"));
        var portable = RequiredBoolean(row, "portable");

        return new SettingReviewField(featureId, pointer, pointerSegments, expectedType, portable);
    }

    private static ImmutableArray<string> ParsePointer(string pointer)
    {
        if (pointer.Length == 0 || pointer[0] != '/')
            throw Invalid("A setting-review pointer must be a non-root RFC 6901 JSON pointer.");

        var segments = pointer[1..].Split('/', StringSplitOptions.None);
        var decoded = ImmutableArray.CreateBuilder<string>(segments.Length);
        foreach (var segment in segments)
            decoded.Add(UnescapePointerSegment(segment));

        return decoded.MoveToImmutable();
    }

    private static string UnescapePointerSegment(string segment)
    {
        if (!segment.Contains('~'))
            return segment;

        var result = new System.Text.StringBuilder(segment.Length);
        for (var index = 0; index < segment.Length; index++)
        {
            var character = segment[index];
            if (character != '~')
            {
                result.Append(character);
                continue;
            }

            if (++index >= segment.Length || segment[index] is not ('0' or '1'))
                throw Invalid("A setting-review pointer contains an invalid RFC 6901 escape.");

            result.Append(segment[index] == '0' ? '~' : '/');
        }

        return result.ToString();
    }

    private static SettingReviewJsonType ParseType(string value) => value switch
    {
        "boolean" => SettingReviewJsonType.Boolean,
        "number" => SettingReviewJsonType.Number,
        "string" => SettingReviewJsonType.String,
        "null" => SettingReviewJsonType.Null,
        "object" => SettingReviewJsonType.Object,
        "array" => SettingReviewJsonType.Array,
        _ => throw Invalid("A setting-review field has an unsupported JSON type.")
    };

    private static void ValidateUniqueFields(ImmutableArray<SettingReviewField> fields)
    {
        var featureSpellings = new Dictionary<string, string>(s_featureComparer);
        var identities = new HashSet<string>(s_identityComparer);

        foreach (var field in fields)
        {
            if (featureSpellings.TryGetValue(field.FeatureId, out var spelling) &&
                !string.Equals(spelling, field.FeatureId, StringComparison.Ordinal))
                throw Duplicate("Feature IDs that differ only by case are ambiguous in setting review.");

            featureSpellings.TryAdd(field.FeatureId, field.FeatureId);

            // The pointer is included verbatim. Escaping is validated, so each JSON pointer has one canonical form.
            var identity = $"{field.FeatureId}\0{field.Pointer}";
            if (!identities.Add(identity))
                throw Duplicate("A setting-review field identity is repeated or differs only by case.");
        }
    }

    private static void RequireFields(JsonElement element, params string[] allowed)
    {
        var allowedFields = new HashSet<string>(allowed, StringComparer.Ordinal);
        if (element.EnumerateObject().Any(property => !allowedFields.Contains(property.Name)))
            throw Invalid("The setting-review document contains an unknown field.");
    }

    private static JsonElement RequireObject(JsonElement element, string message) =>
        element.ValueKind is JsonValueKind.Object ? element : throw Invalid(message);

    private static JsonElement RequiredArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            throw Invalid("The setting-review document is missing a required field.");
        return value.ValueKind is JsonValueKind.Array ? value : throw Invalid("The setting-review fields value must be an array.");
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not JsonValueKind.String)
            throw Invalid("A setting-review identity or type must be a string.");
        var text = value.GetString();
        return string.IsNullOrEmpty(text) ? throw Invalid("A setting-review identity or type must not be empty.") : text;
    }

    private static bool RequiredBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid("A setting-review portable classification must be a JSON boolean.");
        return value.GetBoolean();
    }

    private static SettingReviewException Invalid(string message) => new("bridge-source-invalid", message);

    private static SettingReviewException Duplicate(string message) => new("bridge-source-duplicate", message);
}
