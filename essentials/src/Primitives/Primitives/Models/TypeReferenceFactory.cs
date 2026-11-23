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
    /// (<see cref="CollectionKind.Array"/>/<see cref="CollectionKind.List"/>/<see cref="CollectionKind.HashSet"/>/
    /// <see cref="CollectionKind.Dictionary"/>,
    /// else <see cref="CollectionKind.Single"/>) and resolves the element type's alias via
    /// <paramref name="aliasResolver"/>. The element/collection inspection is name-based so it works on
    /// reflection-only types loaded in a <see cref="System.Reflection.MetadataLoadContext"/>.
    /// </summary>
    /// <remarks>
    /// A collection whose element is itself a collection recurses: the result names the nested
    /// <see cref="TypeReference.Element"/>, and its alias is the innermost element's (spec 1228 FR-022).
    /// </remarks>
    public static TypeReference FromClrType(Type type, Func<Type, string> aliasResolver)
    {
        var (elementType, kind) = Decompose(type);
        if (kind != CollectionKind.Single && Decompose(elementType).Kind != CollectionKind.Single)
        {
            var element = FromClrType(elementType, aliasResolver);
            return new TypeReference(element.Alias, kind, element);
        }

        return new TypeReference(aliasResolver(elementType), kind);
    }

    /// <summary>
    /// Closes a <see cref="TypeReference"/> back into a concrete CLR type: resolves the element alias via
    /// <paramref name="aliasResolver"/>, then applies the collection shape
    /// (<see cref="CollectionKind.Single"/>→<c>T</c>, <see cref="CollectionKind.Array"/>→<c>T[]</c>,
    /// <see cref="CollectionKind.List"/>→<c>List&lt;T&gt;</c>, <see cref="CollectionKind.HashSet"/>→<c>HashSet&lt;T&gt;</c>,
    /// <see cref="CollectionKind.Dictionary"/>→<c>Dictionary&lt;string,T&gt;</c>).
    /// The inverse of <see cref="FromClrType"/>.
    /// </summary>
    public static Type Resolve(TypeReference reference, Func<string, Type> aliasResolver)
    {
        var elementType = reference.Element is { } element
            ? Resolve(element, aliasResolver)
            : aliasResolver(reference.Alias);
        return Close(elementType, reference.CollectionKind);
    }

    /// <summary>
    /// Closes a <see cref="TypeReference"/> around an already resolved innermost element type, applying every nested
    /// collection shape from the inside out. For a reference without <see cref="TypeReference.Element"/> this is
    /// <see cref="Close(Type, CollectionKind)"/>.
    /// </summary>
    public static Type Close(Type innermostElementType, TypeReference reference)
    {
        var elementType = reference.Element is { } element
            ? Close(innermostElementType, element)
            : innermostElementType;
        return Close(elementType, reference.CollectionKind);
    }

    /// <summary>Applies a <see cref="CollectionKind"/> shape to an element type.</summary>
    public static Type Close(Type elementType, CollectionKind kind) => kind switch
    {
        CollectionKind.Array => elementType.MakeArrayType(),
        CollectionKind.List => typeof(List<>).MakeGenericType(elementType),
        CollectionKind.HashSet => typeof(HashSet<>).MakeGenericType(elementType),
        CollectionKind.Dictionary => typeof(Dictionary<,>).MakeGenericType(typeof(string), elementType),
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

            if (definitionName is "List`1" or "IList`1" or "ICollection`1" or "IReadOnlyList`1" or "IReadOnlyCollection`1" or "IEnumerable`1")
                return (element, CollectionKind.List);

            if (definitionName == typeof(HashSet<>).Name)
                return (element, CollectionKind.HashSet);
        }

        if (type.IsGenericType && type.GetGenericArguments() is [var key, var value] &&
            key.FullName == typeof(string).FullName)
        {
            var definitionName = type.GetGenericTypeDefinition().Name;
            if (definitionName is "Dictionary`2" or "IDictionary`2" or "IReadOnlyDictionary`2")
                return (value, CollectionKind.Dictionary);
        }

        return (type, CollectionKind.Single);
    }
}
