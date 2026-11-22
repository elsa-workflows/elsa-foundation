using Elsa3.Mapping.Services;

namespace Elsa3.Mapping.Internal;

/// <summary>
/// The catalog of Elsa 3 types by what Elsa 3's Jint handed to scripts (data-model §3.1). Data beside the Elsa 3 type
/// map; curated, and extended as the corpus warrants. A type that is not listed is treated as JSON, so a custom class
/// (whose members a script read as JSON keys in Elsa 4) is never refused.
/// </summary>
internal static class Elsa3ScriptValueKinds
{
    private static readonly HashSet<string> DateTypes = new(StringComparer.OrdinalIgnoreCase) { "DateTime", "DateTimeOffset" };

    private static readonly HashSet<string> WrappedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TimeSpan", "Uri", "FormFile", "IFormFile", "HttpFile", "Stream", "MemoryStream", "FileStream", "Byte[]", "DateOnly",
        "TimeOnly", "CultureInfo", "TimeZoneInfo", "Version", "IPAddress", "Encoding",
    };

    private static readonly string[] CollectionMarkers =
        ["List", "Dictionary", "Collection", "Enumerable", "HashSet", "Queue", "Stack", "[]"];

    /// <summary>What a value of Elsa 3 type <paramref name="typeName"/> (an alias or a .NET type name) was in a script.</summary>
    public static Elsa3ScriptValueKind Classify(string? typeName, bool isArray = false)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return isArray ? Elsa3ScriptValueKind.Collection : Elsa3ScriptValueKind.Json;

        var name = SimpleName(typeName);
        if (WrappedTypes.Contains(name))
            return Elsa3ScriptValueKind.Object;
        if (isArray)
            return Elsa3ScriptValueKind.Collection;
        if (DateTypes.Contains(name))
            return Elsa3ScriptValueKind.Date;
        if (name.Equals("Guid", StringComparison.OrdinalIgnoreCase))
            return Elsa3ScriptValueKind.Guid;
        if (name.Contains("JsonObject", StringComparison.OrdinalIgnoreCase) || name.Contains("JsonArray", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("JsonNode", StringComparison.OrdinalIgnoreCase) || name.Contains("ExpandoObject", StringComparison.OrdinalIgnoreCase))
            return Elsa3ScriptValueKind.Json;
        return CollectionMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? Elsa3ScriptValueKind.Collection
            : Elsa3ScriptValueKind.Json;
    }

    // "System.Collections.Generic.List`1[[System.String, ...]]" → "List"; "List<String>" → "List";
    // "Elsa.Http.Models.FormFile, Elsa.Http" → "FormFile"; "System.Byte[]" → "Byte[]".
    private static string SimpleName(string typeName)
    {
        var name = typeName.Trim();
        var generic = name.IndexOfAny(['`', '<']);
        if (generic >= 0)
            return LastSegment(name[..generic]);

        var head = name.Split(',')[0].Trim();
        var array = head.EndsWith("[]", StringComparison.Ordinal);
        if (array)
            head = head[..^2];
        return LastSegment(head) + (array ? "[]" : string.Empty);
    }

    private static string LastSegment(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}
