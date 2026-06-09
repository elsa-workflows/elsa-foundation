using Elsa.Serialization.Core;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters
{
    /// <summary>
    /// A JSON converter factory that creates <see cref="PolymorphicObjectConverter"/> instances.
    /// </summary>
    /// <remarks>
    /// A JSON converter factory that creates <see cref="PolymorphicObjectConverter"/> instances.
    /// </remarks>
    public sealed class PolymorphicObjectConverterFactory(
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IEnumerable<IJsonIslandTypeHandler>? jsonIslandTypeHandlers = null) : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert.IsClass
                   && typeToConvert == typeof(object)
                   || typeToConvert == typeof(ExpandoObject)
                   || typeToConvert == typeof(Dictionary<string, object>))
                return true;

            if (typeToConvert.IsInterface
                   && typeToConvert == typeof(IDictionary<string, object>))
                return true;

            return false;
        }

        /// <inheritdoc />
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            if (typeof(IDictionary<string, object>).IsAssignableFrom(typeToConvert))
                return new PolymorphicDictionaryConverter(options, wellKnownTypeRegistry, jsonIslandTypeHandlers);

            return new PolymorphicObjectConverter(jsonIslandTypeHandlers ?? []);
        }
    }
}
