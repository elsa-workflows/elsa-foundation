using Elsa.Expressions.Core.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.JsonConverters
{
    /// <summary>
    /// Prevents System.Text.Json from trying to serialize the compiled delegate.
    /// Always emits null and cannot rehydrate a Func.
    /// </summary>
    public sealed class FuncExpressionValueConverter : JsonConverter<Func<IExpressionExecutionContext, ValueTask<object>>>
    {
        public override Func<IExpressionExecutionContext, ValueTask<object>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Skip whatever value is in the JSON (probably null).
            if (reader.TokenType != JsonTokenType.Null)
                reader.Skip();

            // We can't deserialize a delegate, so return null.
            return null!;
        }

        public override void Write(Utf8JsonWriter writer, Func<IExpressionExecutionContext, ValueTask<object>> value, JsonSerializerOptions options)
        {
            // Emit a JSON null instead of trying to serialize the delegate
            writer.WriteNullValue();
        }
    }
}
