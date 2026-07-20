using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Minimal <see cref="IPayloadSerializer"/> double for adapter tests. It proves the adapter <b>delegates</b>
/// authored-state (de)serialization to the host's payload serializer (rather than serializing State inline),
/// which is the contract that matters here; the production serializer's polymorphic <c>ActivityNode</c>
/// handling is exercised by the serializer's own tests and by composition-level tests.
/// </summary>
internal sealed class FakePayloadSerializer(Exception? serializationFailure = null) : IPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Serialize(object payload)
    {
        ThrowIfSerializationFails();
        return JsonSerializer.Serialize(payload, Options);
    }

    public JsonElement SerializeToElement(object payload)
    {
        ThrowIfSerializationFails();
        return JsonSerializer.SerializeToElement(payload, Options);
    }

    public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;

    public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;

    public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;

    public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;

    public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;

    public JsonSerializerOptions GetOptions() => Options;

    private void ThrowIfSerializationFails()
    {
        if (serializationFailure is not null)
            throw serializationFailure;
    }
}
