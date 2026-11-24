using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Options;
using Elsa3.Mapping.Services;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>Why a variable is refused, and how to fix it in the Elsa 3 workflow; <paramref name="Code"/> is the diagnostic code.</summary>
internal sealed record Elsa3VariableRefusal(string Message, string Guidance, string Code = Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible);

/// <summary>Something a migrator should know about a variable that converts: what changed, and what to do about it.</summary>
internal sealed record Elsa3VariableNotice(string Message, string Guidance);

/// <summary>
/// Maps an Elsa 3 variable declaration to an Elsa 4 <see cref="VariableDefinition"/> (spec 264). Elsa 3 stored a
/// starting value as text and converted it to the variable's type when the workflow started; the converter does that
/// conversion here and carries the result as a <c>Literal</c>, as Elsa 4's own authoring does. No Elsa 3 value means no
/// starting value. A collection's starting value is always a JSON array and a dictionary's a JSON object: text is never
/// split on commas.
/// </summary>
internal static class Elsa3VariableMapper
{
    // The Elsa 3 name of its untyped kind, and the Elsa 4 alias it becomes: Any, the dynamic kind (spec 1228 FR-019).
    private const string Elsa3ObjectTypeName = "Object";
    private const string AnyAlias = TypeAliasConvention.AnyAlias;

    /// <summary>
    /// Elsa 3 types that stand for a resource (a file, a stream, raw bytes), not data. Elsa 4 has no counterpart yet, and
    /// holding one as <c>Any</c> would pretend the conversion works, so a variable of one is refused (#3194).
    /// </summary>
    private static readonly IReadOnlySet<string> ResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FormFile", "IFormFile", "Stream", "Byte[]"
    };

    /// <summary>
    /// Elsa 3 dictionary types Elsa 4 has no type for. Each becomes an <c>Any</c> variable whose starting value is a
    /// JSON object; the value is the example a refusal shows.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DictionaryTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ObjectDictionary"] = """{"key": "value"}""",
        ["HttpHeaders"] = """{"Accept": ["application/json"]}"""
    };

    /// <summary>
    /// Elsa 3's JSON and dynamic types, by their bare names. Each is JSON, so Any holds it faithfully: no type is missing.
    /// </summary>
    private static readonly IReadOnlySet<string> JsonTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "JsonElement", "JsonNode", "JsonObject", "JsonArray", "ExpandoObject"
    };

    /// <summary>Maps a variable whose starting value <see cref="TryMap"/> accepts; synthesized variables have none.</summary>
    public static VariableDefinition Map(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter, Elsa3Variable source, IVariableTypeDescriptorCatalog? typeDescriptors = null) =>
        TryMap(wellKnownTypeRegistry, objectConverter, source, out var variable, out var refusal, typeDescriptors)
            ? variable
            : throw new InvalidOperationException(refusal!.Message);

    /// <summary>
    /// The info a variable whose Elsa 3 type Elsa 4 does not register gets (#5666): it converts as Any, and keeps its type
    /// once an Elsa 4 feature registers the type. <c>null</c> for a type Elsa 4 knows, and for a shape that is Any
    /// whatever is registered (a nested collection, a collection of dictionaries).
    /// </summary>
    internal static Elsa3VariableNotice? DescribeUnregisteredType(IWellKnownTypeRegistry wellKnownTypeRegistry, Elsa3Variable source)
    {
        var typeName = string.IsNullOrWhiteSpace(source.TypeName) ? Elsa3ObjectTypeName : source.TypeName.Trim();
        if (Resolve(wellKnownTypeRegistry, typeName, source.IsArray).UnregisteredType is not { } unregistered)
            return null;

        var which = string.Equals(unregistered, typeName, StringComparison.Ordinal) ? "which" : $"whose element type '{unregistered}'";
        return new(
            $"Variable '{source.Name}' has the Elsa 3 type '{typeName}', {which} is not registered in Elsa 4, so its Elsa 4 type is Any. It stays Any until an Elsa 4 feature registers the type and the workflow is converted again.",
            "To keep the type, have an Elsa 4 feature register its CLR type (module types take dotted aliases), then convert the workflow again. " +
            "The converter looks the Elsa 3 name up in Elsa 4's type registry, as an alias or as the full name of a registered type, and otherwise loads it as an assembly-qualified CLR name.");
    }

    /// <summary>Maps <paramref name="source"/>, or returns <c>false</c> with the refusal when it cannot be converted: a resource type, or a starting value that cannot start as it did in Elsa 3.</summary>
    public static bool TryMap(
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IObjectConverter objectConverter,
        Elsa3Variable source,
        out VariableDefinition variable,
        out Elsa3VariableRefusal? refusal,
        IVariableTypeDescriptorCatalog? typeDescriptors = null,
        Elsa3ConversionOptions? options = null,
        Action<Elsa3LiteralConversion.Note>? report = null)
    {
        var typeName = string.IsNullOrWhiteSpace(source.TypeName) ? Elsa3ObjectTypeName : source.TypeName.Trim();
        var shape = Resolve(wellKnownTypeRegistry, typeName, source.IsArray);
        // Every Elsa 3 storage driver converts to Elsa 4's default storage (elsa.json); the normalizer reports a Memory or
        // custom driver as a correction (Elsa3StorageDriverCorrections).
        variable = new VariableDefinition(source.Id, source.Name, shape.Type, StorageDriverType: null, Default: null);
        refusal = null;

        // A resource (a file, a stream, raw bytes) has nothing faithful to hold, so it is refused with or without a starting
        // value, until its own spec gives it an Elsa 4 counterpart (#3194).
        if (shape.Kind == ShapeKind.Resource)
        {
            refusal = new(
                $"Variable '{source.Name}' has the type '{typeName}', a resource (a file, a stream or raw bytes) Elsa 4 cannot hold yet, so it cannot be converted.",
                "Elsa 4 has no counterpart for resource variables yet; they are tracked in #3194. Remove the variable from the Elsa 3 workflow, or convert this workflow once Elsa 4 supports it.");
            return false;
        }

        if (source.Value is null)
        {
            if (shape.Type.CollectionKind == CollectionKind.Single && !NullPolicyDefaults.AcceptsNull(shape.Type, wellKnownTypeRegistry, typeDescriptors) &&
                shape.ClrType is { } clrType && !NullPolicyDefaults.TryGetTypeDefault(clrType, out _))
            {
                refusal = new($"Variable '{source.Name}' of type '{shape.Type.Alias}' needs a declared starting value.",
                    "Declare a default or type the member String / Any before converting.");
                return false;
            }
            return true;
        }

        if (!TryStart(shape, source, typeName, options ?? new(), report ?? (_ => { }), out var json, out refusal))
            return false;

        variable = variable with { Default = ArgumentValue.FromClr(json, Elsa3TypedValues.Literal) };
        return true;
    }

    private static bool TryStart(
        VariableShape shape,
        Elsa3Variable source,
        string typeName,
        Elsa3ConversionOptions options,
        Action<Elsa3LiteralConversion.Note> report,
        out JsonElement json,
        out Elsa3VariableRefusal? refusal,
        IVariableTypeDescriptorCatalog? typeDescriptors = null)
    {
        var value = source.Value!;
        refusal = null;
        json = default;
        switch (shape.Kind)
        {
            // Elsa 3 kept an Object variable's text as it was, JSON or not: it becomes a JSON string with the same text.
            case ShapeKind.Text:
                json = JsonSerializer.SerializeToElement(value, JsonBaseline.Options);
                return true;

            case ShapeKind.Collection:
                if (!TryParse(value, JsonValueKind.Array, out var array))
                {
                    refusal = NotJsonArray(source, typeName, value, shape.ElementIsText);
                    return false;
                }
                if (!TryConvert(array, shape.ClrType!, options, report, out json, out var collectionRefusal))
                {
                    refusal = collectionRefusal ?? new(
                        $"Variable '{source.Name}' has the starting value '{Echo(value)}', whose items cannot be converted to '{shape.Type.Alias}'.",
                        $"In the Elsa 3 workflow, write every item as a JSON {shape.Type.Alias} value, for example {Example(shape.Type.Alias)}.");
                    return false;
                }
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
                try
                {
                    var scalarType = shape.ClrType!;
                    var scalarKind = Nullable.GetUnderlyingType(scalarType) ?? scalarType;
                    JsonElement scalar;
                    if (scalarKind == typeof(string)) scalar = JsonSerializer.SerializeToElement(value, JsonBaseline.Options);
                    else if (scalarKind == typeof(bool) && bool.TryParse(value, out var boolean)) scalar = JsonSerializer.SerializeToElement(boolean, JsonBaseline.Options);
                    else if (scalarKind.IsPrimitive && scalarKind != typeof(char) || scalarKind == typeof(decimal))
                    {
                        using var document = JsonDocument.Parse(value);
                        scalar = document.RootElement.Clone();
                    }
                    else if (JsonConversionContract.For(scalarType).Options.GetTypeInfo(scalarKind).Kind is
                        JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary or JsonTypeInfoKind.Enumerable)
                    {
                        using var document = JsonDocument.Parse(value);
                        scalar = document.RootElement.Clone();
                    }
                    else scalar = JsonSerializer.SerializeToElement(value, JsonBaseline.Options);
                    if (TryConvert(scalar, scalarType, options, report, out json, out var scalarRefusal)) return true;
                    refusal = scalarRefusal;
                }
                catch (JsonException) { json = default; }
                refusal ??= new(
                    $"Variable '{source.Name}' has the starting value '{Echo(value)}', which cannot be converted to its type '{typeName}'.",
                    $"In the Elsa 3 workflow, change the starting value to a valid {typeName} value, or change the type to Object to keep the value as text.");
                return false;

            // A type Elsa 4 does not know runs as Any. JSON keeps the shape Elsa 3 deserialized into that type; other text
            // Elsa 3 converted in a way the converter cannot know (an enum, a type converter), so it is refused.
            default:
                if (TryParse(value, JsonValueKind.Object, out json) || TryParse(value, JsonValueKind.Array, out json))
                    return true;
                refusal = new(
                    $"Variable '{source.Name}' has the starting value '{Echo(value)}', but its type '{typeName}' is not known in Elsa 4, and the value is not JSON, so it cannot start as it did in Elsa 3.",
                    "In the Elsa 3 workflow, write the starting value as a JSON object or array, change the variable's type to Object to keep the value as text, or remove the starting value.");
                return false;
        }
    }

    /// <summary>
    /// The Elsa 4 shape of an Elsa 3 type name. Elsa 3 writes a list as <c>List&lt;T&gt;</c> and an array as <c>T[]</c>:
    /// a collection whose element type Elsa 4 knows keeps that element type. Any other type Elsa 4 does not know, a nested
    /// collection included, becomes <c>Any</c>; a resource is refused.
    /// </summary>
    private static VariableShape Resolve(IWellKnownTypeRegistry registry, string typeName, bool isArray)
    {
        // Byte with the array flag is Byte[]: raw bytes, a resource.
        if (IsResource(typeName) || isArray && IsResource($"{typeName.Split(',')[0].Trim()}[]"))
            return new(ShapeKind.Resource, new TypeReference(AnyAlias), null);

        if (DictionaryTypes.TryGetValue(typeName, out var example))
            return new(ShapeKind.Dictionary, new TypeReference(AnyAlias), null, Example: example);

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
                return new(ShapeKind.Unknown, new TypeReference(AnyAlias), null, UnregisteredType: InnermostUnregistered(registry, elementName));
            return new(
                ShapeKind.Collection,
                new TypeReference(elementAlias, kind),
                TypeReferenceFactory.Close(elementType, kind),
                ElementIsText: elementType == typeof(string) || elementType == typeof(object));
        }

        if (IsObjectAlias(typeName))
            return new(ShapeKind.Text, new TypeReference(AnyAlias), typeof(object));

        if (TryResolveKnown(registry, typeName, out var scalarType, out _))
            return new(ShapeKind.Scalar, TypeReferenceFactory.FromClrType(scalarType, registry.GetAliasOrDefault), scalarType);

        // A type Elsa 4's registry does not know runs as Any.
        return new(ShapeKind.Unknown, new TypeReference(AnyAlias), null, UnregisteredType: IsUnregistered(registry, typeName) ? typeName : null);
    }

    /// <summary>
    /// The innermost element of a collection whose element Elsa 4 does not know, when that element is itself unregistered.
    /// <c>null</c> when only the nesting is unsupported (<c>List&lt;List&lt;String&gt;&gt;</c>, <c>ObjectDictionary[]</c>):
    /// registering a type would not change that.
    /// </summary>
    private static string? InnermostUnregistered(IWellKnownTypeRegistry registry, string elementName)
    {
        // Each step strips a List<> or [] from a non-empty name, so the loop ends.
        while (TryReadCollection(elementName, out var inner, out _))
            elementName = inner;
        return IsUnregistered(registry, elementName) ? elementName : null;
    }

    /// <summary>
    /// Whether <paramref name="typeName"/> is unregistered: not Object, an Elsa 3 dictionary or JSON type, or a type Elsa 4's
    /// registry knows.
    /// </summary>
    private static bool IsUnregistered(IWellKnownTypeRegistry registry, string typeName) =>
        !IsObjectAlias(typeName) && !DictionaryTypes.ContainsKey(typeName) && !JsonTypes.Contains(typeName) && !TryResolveKnown(registry, typeName, out _, out _);

    /// <summary>Whether <paramref name="typeName"/>, or the element of a collection of it, is a <see cref="ResourceTypes">resource</see>.</summary>
    private static bool IsResource(string typeName)
    {
        // A CLR generic name carries its type arguments after the arity: List`1[[A, Asm]] or List`1[A]. Any resource
        // argument makes it a resource. Each part is shorter than the name, so the recursion ends.
        var arity = typeName.IndexOf('`');
        var arguments = arity >= 0 ? typeName.IndexOf('[', arity) : -1;
        if (arguments >= 0 && typeName[(arguments + 1)..].TrimStart('[').Split(["],[", ","], StringSplitOptions.TrimEntries).Any(IsResource))
            return true;

        // A CLR name may be namespace- and assembly-qualified: "System.IO.Stream, System.Runtime" → "Stream". A generic
        // argument keeps its closing brackets ("Stream]]"); "[]" is an array and stays.
        var name = typeName.Split(',')[0].Trim();
        while (name.EndsWith(']') && !name.EndsWith("[]", StringComparison.Ordinal))
            name = name[..^1];
        var simple = name[(name.LastIndexOf('.') + 1)..];
        if (ResourceTypes.Contains(simple))
            return true;
        return TryReadCollection(name, out var elementName, out _) && IsResource(elementName);
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

    private static bool TryConvert(JsonElement value, Type type, Elsa3ConversionOptions options, Action<Elsa3LiteralConversion.Note> report,
        out JsonElement json, out Elsa3VariableRefusal? refusal)
    {
        refusal = null;
        try
        {
            json = Elsa3LiteralConversion.Rewrite(value, type, options, report);
            var contract = JsonConversionContract.For(type);
            var converted = contract.Read(json, type, diagnostic => report(new(diagnostic.Code, diagnostic.Path,
                $"JSON conversion of a starting value to {diagnostic.TargetType.Name} reported {diagnostic.Code}" +
                (diagnostic.Converter is null ? "." : $" using {diagnostic.Converter}."),
                "Check the literal against the target's exact JSON member names and scoped conversion contract.",
                diagnostic.Code == "json-unmatched-coverage-unavailable" ? Elsa3MigrationDiagnosticSeverity.Info : Elsa3MigrationDiagnosticSeverity.Warning)));
            // Validate a typed copy; preserve every carried object member, including unknown keys.
            if (json.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) json = contract.Write(converted, type);
            return true;
        }
        catch (Elsa3LiteralConversion.LiteralRefusal failure)
        {
            json = default;
            refusal = new(failure.Message, failure.Guidance, failure.Code);
            return false;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            json = default;
            return false;
        }
    }

    private static string Example(string alias) => alias switch
    {
        "String" => """["a", "b"]""",
        "Boolean" => "[true, false]",
        AnyAlias => """[{"key": "value"}]""",
        _ => "[1, 2]"
    };

    private static bool IsObjectAlias(string? typeName) =>
        string.IsNullOrWhiteSpace(typeName) ||
        string.Equals(typeName, Elsa3ObjectTypeName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(typeName, "System.Object", StringComparison.Ordinal);

    private static string Echo(string value) => value.Length <= 80 ? value : $"{value[..80]}…";

    private enum ShapeKind { Text, Scalar, Collection, Dictionary, Unknown, Resource }

    /// <param name="ClrType">The CLR type the starting value converts to; <c>null</c> when it is kept as parsed JSON (a dictionary) or the type is unknown.</param>
    /// <param name="UnregisteredType">For an unknown shape, the Elsa 3 type name Elsa 4 does not register; <c>null</c> when only the nesting is unsupported.</param>
    private sealed record VariableShape(ShapeKind Kind, TypeReference Type, Type? ClrType, bool ElementIsText = true, string? Example = null, string? UnregisteredType = null);
}
