using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Options;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Maps an Elsa 3 variable declaration to an Elsa 4 <see cref="VariableDefinition"/>. Elsa 3 stored a starting value as
/// text and converted it to the variable's type when the workflow started (spec 264 R-2); Elsa 4's object converter
/// keeps that rule, so the converter applies it here and carries the result as a <c>Literal</c>, as Elsa 4's own
/// authoring does. No Elsa 3 value means no starting value.
/// </summary>
internal static class Elsa3VariableMapper
{
    /// <summary>The guidance of <see cref="Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible"/>.</summary>
    public const string UnconvertibleGuidance =
        "In the Elsa 3 workflow, change the variable's starting value to one its type accepts, or change its type to Object to keep the value as text. Elsa 3 started such a variable with its type's default or empty.";

    private static readonly ObjectConverterOptions StrictConversion = new(StrictMode: true);

    /// <summary>Maps a variable whose starting value <see cref="TryMap"/> accepts; synthesized variables have none.</summary>
    public static VariableDefinition Map(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter, Elsa3Variable source) =>
        TryMap(wellKnownTypeRegistry, objectConverter, source, out var variable, out var failure)
            ? variable
            : throw new InvalidOperationException(failure);

    /// <summary>
    /// Maps <paramref name="source"/>, or returns <c>false</c> with the reason when its starting value cannot be
    /// converted to its type by Elsa 3's rule.
    /// </summary>
    public static bool TryMap(
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IObjectConverter objectConverter,
        Elsa3Variable source,
        out VariableDefinition variable,
        out string? failure)
    {
        var clrType = LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.TypeName ?? "Object");
        var type = TypeReferenceFactory.FromClrType(clrType, wellKnownTypeRegistry.GetAliasOrDefault);
        if (source.IsArray && type.CollectionKind == CollectionKind.Single)
        {
            type = type with { CollectionKind = CollectionKind.Array };
            clrType = clrType.MakeArrayType();
        }

        var storageDriverType = !string.IsNullOrWhiteSpace(source.StorageDriverTypeName)
            ? wellKnownTypeRegistry.GetAliasOrDefault(LegacyClrTypeResolver.Resolve(wellKnownTypeRegistry, source.StorageDriverTypeName))
            : null;

        variable = new VariableDefinition(source.Id, source.Name, type, storageDriverType, Default: null);
        failure = null;
        if (source.Value is null)
            return true;

        // A type Elsa 4's registry does not know runs as Object, which keeps the text, where Elsa 3 converted it to that type.
        if (!IsObjectAlias(source.TypeName) && (clrType == typeof(object) || !wellKnownTypeRegistry.TryGetTypeOrDefault(type.Alias, out _)))
        {
            failure = $"Variable '{source.Name}' has a starting value, but its type '{source.TypeName}' is not known in Elsa 4, so the value cannot start as it did in Elsa 3.";
            return false;
        }

        // Strict: Elsa 3 silently started a value it could not parse ("abc" for an Int32) as the type's default. That is
        // an authoring mistake, so it is refused, like malformed JSON, where Elsa 3 logged a warning (spec 264 clarification).
        var converted = objectConverter.TryConvertTo(source.Value, clrType, StrictConversion);
        if (!converted.IsSuccess || !TrySerialize(converted.ValueOrDefault, out var json))
        {
            failure = $"Variable '{source.Name}' has the starting value '{Echo(source.Value)}', which cannot be converted to its type '{source.TypeName}'.";
            return false;
        }

        variable = variable with { Default = new ArgumentValue(json, Elsa3TypedValues.Literal) };
        return true;
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

    private static bool IsObjectAlias(string? typeName) =>
        string.IsNullOrWhiteSpace(typeName) ||
        string.Equals(typeName, "Object", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(typeName, "System.Object", StringComparison.Ordinal);

    private static string Echo(string value) => value.Length <= 80 ? value : $"{value[..80]}…";
}
