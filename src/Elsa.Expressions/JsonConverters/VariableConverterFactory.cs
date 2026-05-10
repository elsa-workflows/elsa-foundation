using Elsa.Expressions.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.JsonConverters
{
    /// <summary>
    /// Produces <see cref="VariableConverter"/> instances for <see cref="IVariable"/> and <see cref="IVariable{T}"/>.
    /// </summary>    
    public sealed class VariableConverterFactory(IVariableMapper variableMapper) : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert) => typeof(IVariable).IsAssignableFrom(typeToConvert);

        /// <inheritdoc />
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new VariableConverter(variableMapper);
    }
}
