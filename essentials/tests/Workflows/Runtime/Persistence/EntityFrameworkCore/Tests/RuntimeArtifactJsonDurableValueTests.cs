using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Primitives.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeArtifactJsonDurableValueTests
{
    [Theory]
    [InlineData("External", true, DurableValueStorage.External)]
    [InlineData("Custom", true, DurableValueStorage.Custom)]
    [InlineData("external", true, DurableValueStorage.Inline)]
    [InlineData("Unknown", true, DurableValueStorage.Inline)]
    [InlineData("External", false, DurableValueStorage.Inline)]
    public void Minimum_storage_metadata_only_applies_to_a_materialized_JSON_null(string marker, bool isNull, DurableValueStorage expected)
    {
        var state = new DurableValueState("id", "workflow", "output:result", new("String", null, null), DurableValueLifecycle.Instance,
            DurableValueStorage.Inline, isNull ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(42), null, null,
            DateTimeOffset.UnixEpoch, new Dictionary<string, string> { [RuntimeMetadataKeys.InputName] = "result", [RuntimeMetadataKeys.NullMinimumStorage] = marker });
        foreach (var bounded in new[] { false, true })
        {
            var reloaded = Read(RuntimeArtifactJson.Serialize(state), bounded);
            var envelope = RuntimeInputBindingStateProjection.ProjectWorkflowInputEnvelopes([reloaded])["result"];
            Assert.Equal(expected, envelope.Policy.Storage);
            Assert.Equal(isNull ? ValuePresence.ExplicitNull : ValuePresence.Present, envelope.Presence);
        }
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_inline_and_external_rows_keep_the_persistence_profile_and_opaque_utf16(bool external)
    {
        var metadata = new Dictionary<string, string> { ["key-\ud802"] = "value-\ud803" };
        var value = new DurableValueState("id-\ud800", "workflow-\ud801", "output:Value", new("Any", "Single", null),
            DurableValueLifecycle.Instance, external ? DurableValueStorage.External : DurableValueStorage.Inline,
            external ? null : JsonSerializer.SerializeToElement(42), external ? new("store-\ud804", "locator-\ud805", metadata) : null,
            "body-\ud806", DateTimeOffset.UnixEpoch, metadata);
        var json = Invoke<string>("Serialize", value);
        var restored = Invoke<DurableValueState>("Deserialize", json);
        Assert.Equal(value.DurableValueId, restored.DurableValueId);
        Assert.Equal(value.WorkflowExecutionId, restored.WorkflowExecutionId);
        Assert.Equal(value.SourceActivityExecutionId, restored.SourceActivityExecutionId);
        Assert.Equal(metadata["key-\ud802"], restored.Metadata["key-\ud802"]);
        if (external) Assert.Equal(value.ExternalReference!.Locator, restored.ExternalReference!.Locator);
        else Assert.Equal(42, restored.InlineValue!.Value.GetInt32());
        Assert.Equal(json, Invoke<string>("Serialize", restored));
    }

    // Exercise the actual EF serialization profile without adding an internals grant.
    private static T Invoke<T>(string name, object argument) => (T)typeof(EfDurableValueStateStore).Assembly
        .GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimeArtifactJson", throwOnError: true)!
        .GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method => method.Name == name)
        .MakeGenericMethod(typeof(DurableValueState)).Invoke(null, [argument])!;
}
