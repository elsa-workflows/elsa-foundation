using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

// The carried value, including its whitespace and number/escape spellings, is the preservation boundary.
internal sealed class RawJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonElement.ParseValue(ref reader);

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
}
