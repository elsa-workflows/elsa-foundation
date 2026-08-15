using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Api.Compatibility.Testing.Serialization;

/// <summary>Serializes compatibility evidence with deterministic object-property ordering.</summary>
public static class CompatibilityJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new EndpointIdentityJsonConverter());
        return options;
    }

    public static string Serialize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options);
        return node is null ? "null" : Canonicalize(node);
    }

    public static byte[] SerializeUtf8<T>(T value) =>
        System.Text.Encoding.UTF8.GetBytes(Serialize(value));

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("The JSON document contained null.");

    public static string Canonicalize(string json) =>
        Canonicalize(JsonNode.Parse(json) ?? throw new InvalidDataException("The JSON document was empty."));

    public static string Canonicalize(JsonNode node) => Sort(node).ToJsonString(Options);

    private static JsonNode Sort(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(property => property.Key, StringComparer.Ordinal)
            .ToDictionary(property => property.Key, property => property.Value is null ? null : Sort(property.Value), StringComparer.Ordinal)),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : Sort(item)).ToArray()),
        _ => node.DeepClone()
    };

    private sealed class EndpointIdentityJsonConverter : JsonConverter<EndpointIdentity>
    {
        public override EndpointIdentity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            var separator = value?.IndexOf(' ') ?? -1;
            if (separator <= 0 || separator == value!.Length - 1)
                throw new JsonException("Endpoint identities must use the 'METHOD /route' form.");
            return new EndpointIdentity(value[(separator + 1)..], value[..separator]);
        }

        public override void Write(Utf8JsonWriter writer, EndpointIdentity value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
