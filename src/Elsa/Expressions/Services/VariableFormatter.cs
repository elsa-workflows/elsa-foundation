using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Models;
using System.Collections;
using System.ComponentModel;
using System.Text.Json;

namespace Elsa.Expressions.Services
{
    public sealed class VariableDefaultValueFormatter : IVariableDefaultValueFormatter
    {
        /// <summary>
        /// Formats the specified value as a string.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>A string representation of the value.</returns>        
        public ArgumentValue? Format(object? value)
        {
            if (value == null)
                return null;

            if (value is string s)
                return new(s);

            var sourceType = value.GetType();
            var underlyingSourceType = Nullable.GetUnderlyingType(sourceType) ?? sourceType;

            if (underlyingSourceType == typeof(string))
                return new(value as string);

            // Byte arrays are base64-encoded for serialization
            if (value is byte[] byteArray)
                return new(Convert.ToBase64String(byteArray));

            // Memory-like types are preserved via TypeDescriptor
            if (IsMemoryLike(sourceType))
            {
                var sourceTypeConverter = TypeDescriptor.GetConverter(underlyingSourceType);
                if (sourceTypeConverter.CanConvertTo(typeof(string)))
                    return new((string?)sourceTypeConverter.ConvertTo(value, typeof(string)));

                return new(value.ToString());
            }

            // Serialize arrays and collections to JSON instead of "T[] Array" or "(Collection)".
            // Fixes GitHub issue #7019.
            if (value is IEnumerable)
            {
                return new(JsonSerializer.Serialize(value));
            }

            var converter = TypeDescriptor.GetConverter(underlyingSourceType);

            if (converter.CanConvertTo(typeof(string)))
                return new((string?)converter.ConvertTo(value, typeof(string)));

            return new(value.ToString());
        }

        private static bool IsMemoryLike(Type type)
        {
            if (!type.IsGenericType)
                return false;

            var genericTypeDef = type.GetGenericTypeDefinition();
            return genericTypeDef == typeof(Memory<>) || genericTypeDef == typeof(ReadOnlyMemory<>);
        }
    }
}
