using Elsa.Primitives.Extensions;
using Elsa.Serialization.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// Serializes <see cref="Type"/> objects to a simple alias representing the type.
/// </summary>
/// <inheritdoc />
public sealed class TypeJsonConverter(IWellKnownTypeRegistry wellKnownTypeRegistry) : JsonConverter<Type>
{

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
        {
            var elementTypeAlias = typeAlias[..^2];
            var elementType = wellKnownTypeRegistry.TryGetType(elementTypeAlias, out var t) ? t : Type.GetType(elementTypeAlias)!;
            return elementType.MakeArrayType();
        }

        // Handle list types.
        if (typeAlias.StartsWith("List<") && typeAlias.EndsWith(">"))
        {
            var elementTypeAlias = typeAlias[5..^1];
            var elementType = wellKnownTypeRegistry.TryGetType(elementTypeAlias, out var t) ? t : Type.GetType(elementTypeAlias)!;
            return typeof(List<>).MakeGenericType(elementType);
        }

        // Handle hash set types.
        if (typeAlias.StartsWith("HashSet<") && typeAlias.EndsWith(">"))
        {
            var elementTypeAlias = typeAlias[8..^1];
            var elementType = wellKnownTypeRegistry.TryGetType(elementTypeAlias, out var t) ? t : Type.GetType(elementTypeAlias)!;
            return typeof(HashSet<>).MakeGenericType(elementType);
        }

        return wellKnownTypeRegistry.TryGetType(typeAlias, out var type) ? type : Type.GetType(typeAlias);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        // Handle array types.
        if (value.IsArray)
        {
            var elementType = value.GetElementType()!;
            var elementTypeAlias = wellKnownTypeRegistry.TryGetAlias(elementType, out var elementTypeAliasValue)
                ? elementTypeAliasValue
                : elementType.GetSimpleAssemblyQualifiedName();

            writer.WriteStringValue($"{elementTypeAlias}[]");
            return;
        }

        // Handle hash set types (checked before the general enumerable case below, since HashSet<T>
        // is itself an IEnumerable<T> and would otherwise be written as List<>).
        if (value is { IsGenericType: true, GenericTypeArguments.Length: 1 }
            && value.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var elementType = value.GenericTypeArguments.First();
            var elementTypeAlias = wellKnownTypeRegistry.TryGetAlias(elementType, out var hashSetElementAlias)
                ? hashSetElementAlias
                : elementType.GetSimpleAssemblyQualifiedName();

            writer.WriteStringValue($"HashSet<{elementTypeAlias}>");
            return;
        }

        // Handle collection types.
        if (value is { IsGenericType: true, GenericTypeArguments.Length: 1 })
        {
            var elementType = value.GenericTypeArguments.First();
            var typedEnumerable = typeof(IEnumerable<>).MakeGenericType(elementType);

            if (typedEnumerable.IsAssignableFrom(value) && wellKnownTypeRegistry.TryGetAlias(elementType, out var elementTypeAlias))
            {
                writer.WriteStringValue($"List<{elementTypeAlias}>");
                return;
            }
        }

        var typeAlias = wellKnownTypeRegistry.TryGetAlias(value, out var alias)
            ? alias
            : value.GetSimpleAssemblyQualifiedName();

        writer.WriteStringValue(typeAlias);
    }
}