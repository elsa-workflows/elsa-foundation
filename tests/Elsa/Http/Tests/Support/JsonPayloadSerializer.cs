using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Http.Tests.Support;

/// <summary>
/// Minimal System.Text.Json-backed <see cref="IPayloadSerializer"/> for unit tests.
/// </summary>
public sealed class JsonPayloadSerializer : IPayloadSerializer
{
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

    public string Serialize(object payload) => JsonSerializer.Serialize(payload, options);
    public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, options);
    public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, options)!;
    public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, options)!;
    public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(options)!;
    public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, options)!;
    public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(options)!;
    public JsonSerializerOptions GetOptions() => options;
}
