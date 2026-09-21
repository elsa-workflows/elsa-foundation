using System.Collections;

namespace Elsa.Primitives.Extensions;

public static class ObjectExtensions
{
    public static object ConvertIEnumerableToArray(this object? obj)
    {
        if (obj == null)
            return null!;

        // If it's not an IEnumerable or it's a string or dictionary, return the original object.
        if (obj is not IEnumerable enumerable || obj is string || obj is IDictionary)
            return obj;

        // Async enumerables (e.g. EF Core query results that implement both IEnumerable<T>
        // and IAsyncEnumerable<T>) must not be materialized synchronously here; return as-is
        // so the caller can enumerate them appropriately.
        if (IsAsyncEnumerable(obj))
            return obj;

        // Use LINQ to convert the IEnumerable to an array.
        // For projection operators like Select, the element type is the LAST generic argument
        // (e.g., ListSelectIterator<TSource, TResult> where TResult is the element type)
        var elementType = obj.GetType().GetGenericArguments().LastOrDefault();

        if (elementType == null)
            return obj;

        var toArrayMethod = typeof(Enumerable).GetMethod("ToArray")!.MakeGenericMethod(elementType);
        return toArrayMethod.Invoke(null, [
            enumerable
        ])!;
    }

    /// <summary>
    /// Returns <c>true</c> when the object implements <see cref="IAsyncEnumerable{T}"/>.
    /// Used instead of matching provider-internal type names (e.g. EF Core's
    /// <c>AsyncIListEnumerableAdapter&lt;T&gt;</c>) so this primitives layer stays free of
    /// knowledge about specific data-access implementations.
    /// </summary>
    private static bool IsAsyncEnumerable(object obj) =>
        obj.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
}