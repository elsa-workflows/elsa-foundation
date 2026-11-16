using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Tests;

internal sealed class TestPayloadSerializer : IPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
    public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
    public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
    public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
    public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
    public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
    public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
    public JsonSerializerOptions GetOptions() => Options;
}
