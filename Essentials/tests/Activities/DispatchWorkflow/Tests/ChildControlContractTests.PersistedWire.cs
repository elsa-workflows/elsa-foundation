using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed partial class ChildControlContractTests
{
    public static IEnumerable<object[]> IncompleteControlWire =>
        from mutation in new[] { "missing-notifications", "null-notifications", "missing-failure", "missing-payload",
            "duplicate-notifications", "duplicate-failure", "duplicate-payload" }
        from web in new[] { false, true }
        select new object[] { mutation, web };

    [Theory]
    [MemberData(nameof(IncompleteControlWire))]
    public void Persisted_terminal_control_cannot_default_or_shadow_its_authored_content(string mutation, bool web)
    {
        var options = web ? Web : new JsonSerializerOptions();
        var original = Batch(terminal: true, failure: "ORDER",
            notifications: [new("escalation", JsonSerializer.SerializeToElement(new { code = "WARNING" }))]);
        var node = JsonSerializer.SerializeToNode(original, options)!.AsObject();
        string Name(string value) => web ? char.ToLowerInvariant(value[0]) + value[1..] : value;
        var notifications = Name("Notifications");
        var failure = Name("AuthoredFailureCode");
        var payload = Name("Payload");
        string wire;
        switch (mutation)
        {
            case "missing-notifications": Assert.True(node.Remove(notifications)); break;
            case "null-notifications": node[notifications] = null; break;
            case "missing-failure": Assert.True(node.Remove(failure)); break;
            case "missing-payload": Assert.True(node[notifications]![0]!.AsObject().Remove(payload)); break;
        }
        wire = node.ToJsonString();
        if (mutation == "duplicate-notifications")
            wire = wire[..^1] + $",\"{notifications}\":null}}";
        if (mutation == "duplicate-failure")
            wire = wire[..^1] + $",\"{failure}\":null}}";
        if (mutation == "duplicate-payload")
            wire = wire.Replace($"\"{payload}\":{{\"code\":\"WARNING\"}}",
                $"\"{payload}\":{{\"code\":\"WARNING\"}},\"{payload}\":null", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuntimeChildControlBatch>(wire, options));
    }

    [Theory]
    [InlineData("AuthoredFailureCode")]
    [InlineData("Notifications")]
    [InlineData("Payload")]
    public void Web_record_member_case_shadows_cannot_erase_authored_control(string member)
    {
        var wire = JsonSerializer.Serialize(Batch(terminal: true, failure: "ORDER",
            notifications: [new("notice", JsonSerializer.SerializeToElement(new { code = "WARNING" }))]), Web);
        wire = member == "Payload"
            ? wire.Replace("\"payload\":{\"code\":\"WARNING\"}",
                "\"payload\":{\"code\":\"WARNING\"},\"Payload\":null", StringComparison.Ordinal)
            : wire[..^1] + $",\"{member}\":null}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuntimeChildControlBatch>(wire, Web));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("web")]
    [InlineData("snake")]
    public void Explicit_nullable_control_and_opaque_payload_members_round_trip(string policy)
    {
        var options = policy switch
        {
            "web" => Web,
            "snake" => new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true },
            _ => new JsonSerializerOptions()
        };
        var empty = new RuntimeChildControlBatch(1, "consumer", 1, Identity.ChildWorkflowExecutionId,
            "root", "source", 0, true);
        var restored = JsonSerializer.Deserialize<RuntimeChildControlBatch>(JsonSerializer.Serialize(empty, options), options)!;
        Assert.Empty(restored.Notifications);
        Assert.Null(restored.AuthoredFailureCode);
        using var payload = JsonDocument.Parse("{\"code\":\"one\",\"Code\":\"two\",\"value\":null}");
        var populated = Batch(terminal: true, notifications: [new("notice", payload.RootElement), new("nullable")]);
        var wire = JsonSerializer.Serialize(populated, options);
        restored = JsonSerializer.Deserialize<RuntimeChildControlBatch>(wire, options)!;
        Assert.Equal("one", restored.Notifications[0].Payload!.Value.GetProperty("code").GetString());
        Assert.Equal("two", restored.Notifications[0].Payload!.Value.GetProperty("Code").GetString());
        Assert.Null(restored.Notifications[1].Payload);
        Assert.Equal(wire, JsonSerializer.Serialize(restored, options));
    }
}
