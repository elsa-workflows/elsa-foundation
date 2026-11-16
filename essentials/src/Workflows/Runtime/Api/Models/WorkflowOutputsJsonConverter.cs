using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Api.Models;

// Output names are authored identities. The API's property naming policy must not rename them.
internal sealed class WorkflowOutputsJsonConverter : JsonConverter<IReadOnlyDictionary<string, WorkflowOutputView>>
{
    public override IReadOnlyDictionary<string, WorkflowOutputView> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected workflow outputs.");
        var outputs = new Dictionary<string, WorkflowOutputView>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return outputs;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected an output name.");
            var name = reader.GetString()!;
            if (!reader.Read()) throw new JsonException("Expected an output value.");
            var value = JsonSerializer.Deserialize<WorkflowOutputView>(ref reader, options) ?? throw new JsonException("Expected an output value.");
            if (!outputs.TryAdd(name, value)) throw new JsonException("Duplicate output name.");
        }
        throw new JsonException("Expected the end of workflow outputs.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, WorkflowOutputView> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var output in value)
        {
            writer.WritePropertyName(output.Key);
            JsonSerializer.Serialize(writer, output.Value, options);
        }
        writer.WriteEndObject();
    }
}
