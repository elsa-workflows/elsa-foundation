using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RuntimeArtifactJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("The persisted Runtime JSON payload was empty.");
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(WorkflowExecutable)) return;
            info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(WorkflowExecutable.Nodes), StringComparison.OrdinalIgnoreCase)));
            info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(WorkflowExecutable.NodesById), StringComparison.OrdinalIgnoreCase)));
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
        options.Converters.Add(new JsonStringEnumConverter());
        // Runtime artifact identities are opaque UTF-16 values. Encoding string values as framed UTF-16
        // preserves accepted lone surrogates in JSON before the payload reaches a provider.
        options.Converters.Add(new LosslessUtf16StringConverter());
        return options;
    }

    private sealed class LosslessUtf16StringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Decode(ref reader);

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Decode(ref reader);

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EfRelationalIdentity.Encode(value));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(EfRelationalIdentity.Encode(value));

        private static string Decode(ref Utf8JsonReader reader) =>
            EfRelationalIdentity.Decode(reader.GetString() ?? throw new JsonException("A runtime artifact string was null."));
    }
}
