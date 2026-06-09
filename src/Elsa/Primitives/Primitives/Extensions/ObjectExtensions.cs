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

        // If this is an async enumerable, return as-is.
        if (obj.GetType().Name == "AsyncIListEnumerableAdapter`1")
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
}