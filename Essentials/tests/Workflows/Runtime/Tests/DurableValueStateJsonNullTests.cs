using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class DurableValueStateJsonNullTests
{
    [Fact]
    public void Strict_unknown_and_required_constructor_settings_are_honored()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DurableValueState>("{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true }));
        var json = JsonNode.Parse(JsonSerializer.Serialize(State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(42), null), JsonSerializerOptions.Web))!;
        json["unknown"] = 1;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DurableValueState>(json.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }));
    }

    [Theory]
    [InlineData("durableValueId")]
    [InlineData("type")]
    [InlineData("metadata")]
    public void Nonnullable_constructor_properties_keep_host_null_enforcement(string property)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(42), null), JsonSerializerOptions.Web))!;
        json[property] = null;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DurableValueState>(json.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true }));
    }

    [Fact]
    public void Serialization_keeps_get_only_collection_and_scalar_ignore_behavior()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IgnoreReadOnlyProperties = true };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(42), null), options));
        Assert.False(json.RootElement.TryGetProperty("durableValueId", out _));
        Assert.True(json.RootElement.TryGetProperty("metadata", out _));
    }

    [Fact]
    public void Serialization_keeps_legacy_null_ignore_behavior()
    {
#pragma warning disable SYSLIB0020 // Verify compatibility with a caller's existing serializer configuration.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IgnoreNullValues = true };
#pragma warning restore SYSLIB0020
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement<object?>(null), null), options));
        Assert.False(json.RootElement.TryGetProperty("externalReference", out _));
        Assert.True(json.RootElement.TryGetProperty("inlineValue", out _));
    }

    [Fact]
    public void Serialization_keeps_nonnullable_property_enforcement()
    {
        var value = new DurableValueState(null!, "workflow", "output:Value", new("Any", "Single", null), DurableValueLifecycle.None,
            DurableValueStorage.None, null, null, null, DateTimeOffset.UnixEpoch, new Dictionary<string, string>());
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true }));
    }

    [Fact]
    public void Registered_payload_converter_is_used_in_both_directions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new NumberAsString());
        var value = State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(42), null);
        var restored = JsonSerializer.Deserialize<DurableValueState>(JsonSerializer.Serialize(value, options), options)!;
        Assert.Equal(42, restored.InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inline_json_null_is_present_after_round_trip_while_missing_payload_remains_invalid(bool web)
    {
        var options = new JsonSerializerOptions(web ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
        var value = State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement<object?>(null), null);
        var json = JsonSerializer.Serialize(value, options);
        var restored = JsonSerializer.Deserialize<DurableValueState>(json, options)!;
        Assert.True(restored.InlineValue.HasValue);
        Assert.Equal(JsonValueKind.Null, restored.InlineValue.Value.ValueKind);
        var missing = JsonNode.Parse(json)!.AsObject();
        missing.Remove(web ? "inlineValue" : "InlineValue");
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<DurableValueState>(missing.ToJsonString(), options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_external_null_inline_property_remains_absent(bool web)
    {
        var options = new JsonSerializerOptions(web ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
        var value = State(DurableValueStorage.External, null, new("store", "locator", new Dictionary<string, string>()));
        var restored = JsonSerializer.Deserialize<DurableValueState>(JsonSerializer.Serialize(value, options), options)!;
        Assert.Null(restored.InlineValue);
        Assert.Equal("locator", restored.ExternalReference!.Locator);
    }

    private static DurableValueState State(DurableValueStorage storage, JsonElement? value, DurableValueExternalReference? reference) =>
        new("id", "workflow", "output:Null", new("Any", "Single", null), DurableValueLifecycle.Instance, storage,
            value, reference, null, DateTimeOffset.UnixEpoch, new Dictionary<string, string>());

    private sealed class NumberAsString : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.ValueKind == JsonValueKind.String && document.RootElement.GetString()!.StartsWith("number:", StringComparison.Ordinal)
                ? JsonSerializer.SerializeToElement(int.Parse(document.RootElement.GetString()![7..], System.Globalization.CultureInfo.InvariantCulture))
                : document.RootElement.Clone();
        }
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
        {
            if (value.ValueKind == JsonValueKind.Number) writer.WriteStringValue("number:" + value.GetRawText());
            else value.WriteTo(writer);
        }
    }
}
