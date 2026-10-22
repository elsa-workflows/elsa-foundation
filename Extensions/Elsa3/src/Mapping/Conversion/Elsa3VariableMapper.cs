using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Options;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>Why a variable's starting value is refused, and how to fix it in the Elsa 3 workflow.</summary>
internal sealed record Elsa3VariableRefusal(string Message, string Guidance);

/// <summary>
/// Maps an Elsa 3 variable declaration to an Elsa 4 <see cref="VariableDefinition"/> (spec 264). Elsa 3 stored a
/// starting value as text and converted it to the variable's type when the workflow started; the converter does that
/// conversion here and carries the result as a <c>Literal</c>, as Elsa 4's own authoring does. No Elsa 3 value means no
/// starting value. A collection's starting value is always a JSON array and a dictionary's a JSON object: text is never
/// split on commas.
/// </summary>
internal static class Elsa3VariableMapper
{
    private const string ObjectAlias = "Object";
    private static readonly ObjectConverterOptions StrictConversion = new(StrictMode: true);

    /// <summary>
    /// Elsa 3 dictionary types Elsa 4 has no type for. Each becomes an <c>Object</c> variable whose starting value is a
    /// JSON object; the value is the example a refusal shows.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DictionaryTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ObjectDictionary"] = """{"key": "value"}""",
        ["HttpHeaders"] = """{"Accept": ["application/json"]}"""
    };

    /// <summary>Maps a variable whose starting value <see cref="TryMap"/> accepts; synthesized variables have none.</summary>
    public static VariableDefinition Map(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter, Elsa3Variable source) =>
        TryMap(wellKnownTypeRegistry, objectConverter, source, out var variable, out var refusal)
            ? variable
            : throw new InvalidOperationException(refusal!.Message);

    /// <summary>Maps <paramref name="source"/>, or returns <c>false</c> with the refusal when its starting value cannot start as it did in Elsa 3.</summary>
    public static bool TryMap(
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IObjectConverter objectConverter,
        Elsa3Variable source,
        out VariableDefinition variable,
        out Elsa3VariableRefusal? refusal)
    {
        var typeName = string.IsNullOrWhiteSpace(source.TypeName) ? ObjectAlias : source.TypeName.Trim();
        var shape = Resolve(wellKnownTypeRegistry, typeName, source.IsArray);
        var storageDriverType = !string.IsNullOrWhiteSpace(source.StorageDriverTypeName)
            ? wellKnownTypeRegistry.GetAliasOrDefault(LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.StorageDriverTypeName))
            : null;

        variable = new VariableDefinition(source.Id, source.Name, shape.Type, storageDriverType, Default: null);
        refusal = null;
        if (source.Value is null)
            return true;

        if (!TryStart(objectConverter, shape, source, typeName, out var json, out refusal))
            return false;

        variable = variable with { Default = new ArgumentValue(json, Elsa3TypedValues.Literal) };
        return true;
    }

    private static bool TryStart(
        IObjectConverter objectConverter,
        VariableShape shape,
        Elsa3Variable source,
        string typeName,
        out JsonElement json,
        out Elsa3VariableRefusal? refusal)
    {
        var value = source.Value!;
        refusal = null;
        json = default;
        switch (shape.Kind)
        {
            // Elsa 3 kept an Object variable's text as it was, JSON or not.
            case ShapeKind.Text:
                json = JsonSerializer.SerializeToElement(value);
                return true;

            case ShapeKind.Collection:
                if (!TryParse(value, JsonValueKind.Array, out var array))
                {
                    refusal = NotJsonArray(source, typeName, value, shape.ElementIsText);
                    return false;
                }
                if (shape.ClrType is { } collectionType && !TryConvertItems(array, collectionType, out json))
                {
                    refusal = new(
                        $"Variable '{source.Name}' has the starting value '{Echo(value)}', whose items cannot be converted to '{shape.Type.Alias}'.",
                        $"In the Elsa 3 workflow, write every item as a JSON {shape.Type.Alias} value, for example {Example(shape.Type.Alias)}.");
                    return false;
                }
                if (shape.ClrType is null)
                    json = array;
                return true;

            case ShapeKind.Dictionary:
                if (!TryParse(value, JsonValueKind.Object, out json))
                {
                    refusal = new(
                        $"Variable '{source.Name}' has the starting value '{Echo(value)}', which is not a JSON object.",
                        $"In the Elsa 3 workflow, write the '{typeName}' starting value as a JSON object, for example {shape.Example}.");
                    return false;
                }
                return true;

            case ShapeKind.Scalar:
                // Strict: Elsa 3 silently started a value it could not parse ("abc" for an Int32) as the type's default.
                // That is an authoring mistake, so it is refused (spec 264 clarification).
                var converted = objectConverter.TryConvertTo(value, shape.ClrType!, StrictConversion);
                if (converted.IsSuccess && TrySerialize(converted.ValueOrDefault, out json))
                    return true;
                refusal = new(
                    $"Variable '{source.Name}' has the starting value '{Echo(value)}', which cannot be converted to its type '{typeName}'.",
                    $"In the Elsa 3 workflow, change the starting value to a valid {typeName} value, or change the type to Object to keep the value as text.");
                return false;

            default:
                refusal = new(
                    $"Variable '{source.Name}' has a starting value, but its type '{typeName}' is not known in Elsa 4, so the value cannot start as it did in Elsa 3.",
                    "In the Elsa 3 workflow, remove the starting value, or change the variable's type to Object to keep the value as text.");
                return false;
        }
    }

    /// <summary>
    /// The Elsa 4 shape of an Elsa 3 type name. Elsa 3 writes a list as <c>List&lt;T&gt;</c> and an array as <c>T[]</c>:
    /// a collection whose element type Elsa 4 knows keeps that element type, any other becomes <c>Object</c>.
    /// </summary>
    private static VariableShape Resolve(IWellKnownTypeRegistry registry, string typeName, bool isArray)
    {
        if (DictionaryTypes.TryGetValue(typeName, out var example))
            return new(ShapeKind.Dictionary, new TypeReference(ObjectAlias), null, Example: example);

        var isCollection = TryReadCollection(typeName, out var elementName, out var kind);
        if (!isCollection && isArray)
        {
            elementName = typeName;
            kind = CollectionKind.Array;
            isCollection = true;
        }

        if (isCollection)
        {
            if (!TryResolveKnown(registry, elementName, out var elementType, out var elementAlias))
                return new(ShapeKind.Collection, new TypeReference(ObjectAlias), null);
            return new(
                ShapeKind.Collection,
                new TypeReference(elementAlias, kind),
                TypeReferenceFactory.Close(elementType, kind),
                ElementIsText: elementType == typeof(string) || elementType == typeof(object));
        }

        if (IsObjectAlias(typeName))
            return new(ShapeKind.Text, new TypeReference(ObjectAlias), typeof(object));

        if (TryResolveKnown(registry, typeName, out var scalarType, out _))
            return new(ShapeKind.Scalar, TypeReferenceFactory.FromClrType(scalarType, registry.GetAliasOrDefault), scalarType);

        // A type Elsa 4's registry does not know runs as Object; a starting value on it is refused.
        var unknown = LegacyClrTypeResolver.Resolve(registry, typeName);
        return new(ShapeKind.Unknown, TypeReferenceFactory.FromClrType(unknown, registry.GetAliasOrDefault), null);
    }

    private static bool TryReadCollection(string typeName, out string elementName, out CollectionKind kind)
    {
        if (typeName.StartsWith("List<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            elementName = typeName[5..^1].Trim();
            kind = CollectionKind.List;
            return elementName.Length > 0;
        }

        if (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            elementName = typeName[..^2].Trim();
            kind = CollectionKind.Array;
            return elementName.Length > 0;
        }

        elementName = string.Empty;
        kind = CollectionKind.Single;
        return false;
    }

    /// <summary>The CLR type and Elsa 4 alias of <paramref name="typeName"/>, when Elsa 4's registry knows it.</summary>
    private static bool TryResolveKnown(IWellKnownTypeRegistry registry, string typeName, out Type type, out string alias)
    {
        type = IsObjectAlias(typeName) ? typeof(object) : LegacyClrTypeResolver.Resolve(registry, typeName);
        alias = TypeReferenceFactory.FromClrType(type, registry.GetAliasOrDefault).Alias;
        return type != typeof(object) ? registry.TryGetTypeOrDefault(alias, out _) : IsObjectAlias(typeName);
    }

    private static Elsa3VariableRefusal NotJsonArray(Elsa3Variable source, string typeName, string value, bool elementIsText)
    {
        // Show the array the author most likely meant: the comma-separated items, as JSON.
        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var suggestion = items.Length == 0
            ? "[]"
            : elementIsText
                ? JsonSerializer.Serialize(items)
                : $"[{string.Join(", ", items)}]";
        return new(
            $"Variable '{source.Name}' has the starting value '{Echo(value)}', which is not a JSON array.",
            $"In the Elsa 3 workflow, write the '{typeName}' starting value as a JSON array, for example {suggestion}. Elsa 4 does not split text on commas.");
    }

    private static bool TryParse(string value, JsonValueKind kind, out JsonElement element)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            element = document.RootElement.Clone();
            return element.ValueKind == kind;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    private static bool TryConvertItems(JsonElement array, Type collectionType, out JsonElement json)
    {
        try
        {
            json = JsonSerializer.SerializeToElement(array.Deserialize(collectionType), collectionType);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            json = default;
            return false;
        }
    }

    private static bool TrySerialize(object? value, out JsonElement json)
    {
        try
        {
            json = JsonSerializer.SerializeToElement(value);
            return true;
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException or InvalidOperationException)
        {
            json = default;
            return false;
        }
    }

    private static string Example(string alias) => alias switch
    {
        "String" => """["a", "b"]""",
        "Boolean" => "[true, false]",
        "Object" => """[{"key": "value"}]""",
        _ => "[1, 2]"
    };

    private static bool IsObjectAlias(string? typeName) =>
        string.IsNullOrWhiteSpace(typeName) ||
        string.Equals(typeName, ObjectAlias, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(typeName, "System.Object", StringComparison.Ordinal);

    private static string Echo(string value) => value.Length <= 80 ? value : $"{value[..80]}…";

    private enum ShapeKind { Text, Scalar, Collection, Dictionary, Unknown }

    /// <param name="ClrType">The CLR type the starting value converts to; <c>null</c> when it is kept as parsed JSON.</param>
    private sealed record VariableShape(ShapeKind Kind, TypeReference Type, Type? ClrType, bool ElementIsText = true, string? Example = null);
}
