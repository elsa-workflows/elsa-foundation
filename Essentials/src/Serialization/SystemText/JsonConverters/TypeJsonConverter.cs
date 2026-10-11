using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// Serializes <see cref="Type"/> objects to a stable alias (FR-004 / FR-004a, research D8 revised).
/// </summary>
/// <remarks>
/// <para>
/// Write: the element/type alias always comes from <see cref="IWellKnownTypeRegistry.GetAliasOrDefault"/>, which
/// yields a registered alias or a deterministic convention alias (bare primitive, else dotted <c>FullName</c>) —
/// NEVER an assembly-qualified name. The <c>[]</c> / <c>List&lt;&gt;</c> / <c>HashSet&lt;&gt;</c> shape encoding is preserved.
/// </para>
/// <para>
/// Read: the element/type alias is resolved via the registry only — there is NO <c>Type.GetType</c> fallback.
/// An unresolved alias resolves to <c>typeof(object)</c> (logged at warning level) rather than throwing, so a
/// definition referencing an unknown alias still loads (FR-018 spirit; mirrors <c>WorkflowExecutableCompiler</c>
/// and <c>VariableMapper</c>).
/// </para>
/// </remarks>
public sealed class TypeJsonConverter(IWellKnownTypeRegistry wellKnownTypeRegistry, ILogger<TypeJsonConverter> logger) : JsonConverter<Type>
{
    // Parameterless-logger overload keeps existing test/DI call sites that construct the converter with only the
    // registry working without each having to thread a logger through.
    public TypeJsonConverter(IWellKnownTypeRegistry wellKnownTypeRegistry)
        : this(wellKnownTypeRegistry, NullLogger<TypeJsonConverter>.Instance)
    {
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(Type) || typeToConvert.FullName == "System.RuntimeType";
    }

    /// <inheritdoc />
    public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var typeAlias = reader.GetString()!;

        // Handle array types.
        if (typeAlias.EndsWith("[]"))
            return ResolveElement(typeAlias[..^2]).MakeArrayType();

        // Handle list types.
        if (typeAlias.StartsWith("List<") && typeAlias.EndsWith(">"))
            return typeof(List<>).MakeGenericType(ResolveElement(typeAlias[5..^1]));

        // Handle hash set types.
        if (typeAlias.StartsWith("HashSet<") && typeAlias.EndsWith(">"))
            return typeof(HashSet<>).MakeGenericType(ResolveElement(typeAlias[8..^1]));

        return ResolveElement(typeAlias);
    }

    // Registry-only resolution (FR-004a). Unknown alias → object (logged), never Type.GetType, never throw.
    private Type ResolveElement(string alias)
    {
        if (wellKnownTypeRegistry.TryGetType(alias, out var type))
            return type;

        logger.LogWarning("Could not resolve unknown type alias '{Alias}'; falling back to object.", alias);
        return typeof(object);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        // Handle array types.
        if (value.IsArray)
        {
            var elementTypeAlias = wellKnownTypeRegistry.GetAliasOrDefault(value.GetElementType()!);
            writer.WriteStringValue($"{elementTypeAlias}[]");
            return;
        }

        // Handle hash set types (checked before the general enumerable case below, since HashSet<T>
        // is itself an IEnumerable<T> and would otherwise be written as List<>).
        if (value is { IsGenericType: true, GenericTypeArguments.Length: 1 }
            && value.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var elementTypeAlias = wellKnownTypeRegistry.GetAliasOrDefault(value.GenericTypeArguments[0]);
            writer.WriteStringValue($"HashSet<{elementTypeAlias}>");
            return;
        }

        // Handle collection types.
        if (value is { IsGenericType: true, GenericTypeArguments.Length: 1 })
        {
            var elementType = value.GenericTypeArguments[0];
            var typedEnumerable = typeof(IEnumerable<>).MakeGenericType(elementType);

            if (typedEnumerable.IsAssignableFrom(value))
            {
                writer.WriteStringValue($"List<{wellKnownTypeRegistry.GetAliasOrDefault(elementType)}>");
                return;
            }
        }

        writer.WriteStringValue(wellKnownTypeRegistry.GetAliasOrDefault(value));
    }
}
