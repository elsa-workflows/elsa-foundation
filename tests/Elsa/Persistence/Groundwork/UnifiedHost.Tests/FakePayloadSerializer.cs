using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Minimal <see cref="IPayloadSerializer"/> double. The design draft/version read stores depend on the host's
/// payload serializer; these composition tests only need a functioning serializer, not the production
/// polymorphic <c>ActivityNode</c> handling (covered by the serializer's own tests).
/// </summary>
internal sealed class FakePayloadSerializer : IPayloadSerializer
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
