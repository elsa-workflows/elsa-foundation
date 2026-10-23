using Elsa.Primitives.Models;
using Elsa.Serialization.Core.Options;

namespace Elsa.Serialization.Core;

public interface IObjectConverter
{
    T? ConvertTo<T>(object? value, ObjectConverterOptions? converterOptions = null);

    object? ConvertTo(object? value, Type targetType, ObjectConverterOptions? converterOptions = null);

    Result TryConvertTo(object? value, Type targetType, ObjectConverterOptions? converterOptions = null);

    Result TryConvertTo<T>(object? value, ObjectConverterOptions? converterOptions = null) => TryConvertTo(value, typeof(T), converterOptions);
}