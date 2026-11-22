using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class DurableValueStateJsonNullTests
{
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
}
