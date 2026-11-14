using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeArtifactJsonDurableValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Present_inline_json_null_round_trips_without_losing_presence(bool bounded)
    {
        var state = State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement<object?>(null));
        var json = RuntimeArtifactJson.Serialize(state);

        var restored = Read(json, bounded);

        Assert.True(restored.InlineValue.HasValue);
        Assert.Equal(JsonValueKind.Null, restored.InlineValue.Value.ValueKind);
        Assert.Equal(json, RuntimeArtifactJson.Serialize(restored));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_inline_content_round_trips_without_changing_the_wire_payload(bool bounded)
    {
        var json = RuntimeArtifactJson.Serialize(State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(new { answer = 42 })));

        var restored = Read(json, bounded);

        Assert.Equal(42, restored.InlineValue!.Value.GetProperty("answer").GetInt32());
        Assert.Equal(json, RuntimeArtifactJson.Serialize(restored));
    }

    [Theory]
    [InlineData(DurableValueStorage.None, false)]
    [InlineData(DurableValueStorage.None, true)]
    [InlineData(DurableValueStorage.External, false)]
    [InlineData(DurableValueStorage.External, true)]
    [InlineData(DurableValueStorage.Custom, false)]
    [InlineData(DurableValueStorage.Custom, true)]
    public void States_without_inline_content_keep_nullable_absence(DurableValueStorage storage, bool bounded)
    {
        var json = RuntimeArtifactJson.Serialize(State(storage, null));

        var restored = Read(json, bounded);

        Assert.False(restored.InlineValue.HasValue);
        Assert.Equal(storage, restored.Storage);
        Assert.Equal(json, RuntimeArtifactJson.Serialize(restored));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_missing_inline_member_is_still_refused(bool bounded)
    {
        var payload = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(42))))!.AsObject();
        Assert.True(payload.Remove("inlineValue"));

        Assert.Throws<ArgumentException>(() => Read(payload.ToJsonString(), bounded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void External_state_with_inline_content_is_still_refused(bool bounded)
    {
        var payload = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(DurableValueStorage.External, null)))!.AsObject();
        payload["inlineValue"] = 42;

        Assert.Throws<ArgumentException>(() => Read(payload.ToJsonString(), bounded));
    }

    private static DurableValueState Read(string json, bool bounded) => bounded
        ? RuntimeArtifactJson.DeserializeBounded<DurableValueState>(json, 128)
        : RuntimeArtifactJson.Deserialize<DurableValueState>(json);

    private static DurableValueState State(DurableValueStorage storage, JsonElement? inlineValue) => new(
        "durable-value", "workflow", "value",
        new RuntimeValueTypeDescriptor("json", null, null),
        storage == DurableValueStorage.None ? DurableValueLifecycle.None : DurableValueLifecycle.Instance,
        storage, inlineValue,
        storage == DurableValueStorage.External ? new DurableValueExternalReference("archive", "value", new Dictionary<string, string>()) : null,
        null, DateTimeOffset.UnixEpoch, new Dictionary<string, string> { ["source"] = "test" });
}
