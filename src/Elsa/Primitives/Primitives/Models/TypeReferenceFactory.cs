namespace Elsa.Primitives.Models;

/// <summary>
/// Shared "CLR <see cref="Type"/> → <see cref="TypeReference"/>" decomposition. Both the runtime
/// <c>VariableMapper</c> (alias via <c>IWellKnownTypeRegistry</c>) and the reflection-only activity
/// descriptor scanner (alias via simple type name) use this so the (alias, collection kind) split
/// stays consistent across the two call sites rather than being duplicated.
/// </summary>
public static class TypeReferenceFactory
{
    /// <summary>
    /// Decomposes a CLR type into a <see cref="TypeReference"/>: detects the collection shape
    /// (<see cref="CollectionKind.Array"/>/<see cref="CollectionKind.List"/>/<see cref="CollectionKind.HashSet"/>,
    /// else <see cref="CollectionKind.Single"/>) and resolves the element type's alias via
    /// <paramref name="aliasResolver"/>. The element/collection inspection is name-based so it works on
    /// reflection-only types loaded in a <see cref="System.Reflection.MetadataLoadContext"/>.
    /// </summary>
    public static TypeReference FromClrType(Type type, Func<Type, string> aliasResolver)
    {
        var (elementType, kind) = Decompose(type);
        return new TypeReference(aliasResolver(elementType), kind);
    }

    /// <summary>
    /// Closes a <see cref="TypeReference"/> back into a concrete CLR type: resolves the element alias via
    /// <paramref name="aliasResolver"/>, then applies the collection shape
    /// (<see cref="CollectionKind.Single"/>→<c>T</c>, <see cref="CollectionKind.Array"/>→<c>T[]</c>,
    /// <see cref="CollectionKind.List"/>→<c>List&lt;T&gt;</c>, <see cref="CollectionKind.HashSet"/>→<c>HashSet&lt;T&gt;</c>).
    /// The inverse of <see cref="FromClrType"/>.
    /// </summary>
    public static Type Resolve(TypeReference reference, Func<string, Type> aliasResolver)
    {
        var elementType = aliasResolver(reference.Alias);
        return Close(elementType, reference.CollectionKind);
    }

    /// <summary>Applies a <see cref="CollectionKind"/> shape to an element type.</summary>
    public static Type Close(Type elementType, CollectionKind kind) => kind switch
    {
        CollectionKind.Array => elementType.MakeArrayType(),
        CollectionKind.List => typeof(List<>).MakeGenericType(elementType),
        CollectionKind.HashSet => typeof(HashSet<>).MakeGenericType(elementType),
        _ => elementType
    };

    /// <summary>
    /// Splits a CLR type into its element type and <see cref="CollectionKind"/> without resolving an alias.
    /// </summary>
    public static (Type ElementType, CollectionKind Kind) Decompose(Type type)
    {
        if (type.IsArray)
            return (type.GetElementType()!, CollectionKind.Array);

        if (type.IsGenericType && type.GetGenericArguments() is [var element])
        {
            var definitionName = type.GetGenericTypeDefinition().Name;

            if (definitionName == typeof(List<>).Name)
                return (element, CollectionKind.List);

            if (definitionName == typeof(HashSet<>).Name)
                return (element, CollectionKind.HashSet);
        }

        return (type, CollectionKind.Single);
    }
}
