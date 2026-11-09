using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// <see cref="EventReceived"/> is the event's payload (#3107): any delivered JSON value reads as a trigger, and the
/// trigger writes back exactly what was delivered, so the engine's snapshot round-trip leaves it unchanged.
/// </summary>
public sealed class EventReceivedTests
{
    [Theory]
    [InlineData("""{"orderId":"1"}""")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("""{"EventName":"order.approved"}""")]
    public void Any_json_value_reads_as_the_payload_and_writes_back_unchanged(string json)
    {
        var trigger = JsonDocument.Parse(json).RootElement.Deserialize<EventReceived>();

        Assert.NotNull(trigger);
        Assert.Equal(json, trigger.Payload?.GetRawText());
        Assert.Equal(json, JsonSerializer.SerializeToElement(trigger).GetRawText());
    }

    [Fact]
    public void Json_null_reads_as_a_trigger_without_a_payload()
    {
        var trigger = JsonDocument.Parse("null").RootElement.Deserialize<EventReceived>();

        Assert.NotNull(trigger);
        Assert.Null(trigger.Payload);
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(trigger).ValueKind);
    }

    [Fact]
    public void A_trigger_without_a_payload_round_trips()
    {
        var element = JsonSerializer.SerializeToElement(new EventReceived());

        Assert.Equal(new EventReceived(), element.Deserialize<EventReceived>());
    }

    /// <summary>Waits parked before #3107 registered this alias; it must not change, or they would no longer resume.</summary>
    [Fact]
    public void The_trigger_type_alias_is_unchanged()
    {
        Assert.Equal("Elsa.Activities.Primitives.Activities.EventReceived", TypeAliasConvention.CanonicalAlias(typeof(EventReceived)));
    }
}
