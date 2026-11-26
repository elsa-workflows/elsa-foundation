using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnInputModelSourceJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("null")]
    [InlineData("\"hello\"")]
    [InlineData("18446744073709551615")]
    [InlineData("[]")]
    [InlineData("{\"x\":1}")]
    public void Literal_values_keep_their_defined_JSON_value_across_the_model_envelope_round_trip(string json)
    {
        using var document = JsonDocument.Parse(json);
        var source = new BpmnModelInputSource("Literal", value: document.RootElement);
        var wire = JsonSerializer.Serialize(source, Web);
        var retained = JsonSerializer.Deserialize<BpmnModelInputSource>(wire, Web)!;
        Assert.True(retained.Value.HasValue);
        Assert.True(JsonElement.DeepEquals(document.RootElement, retained.Value.Value));
        Assert.Equal(wire, JsonSerializer.Serialize(retained, Web));
    }
    [Fact]
    public void Declared_input_sources_keep_the_value_member_absent()
    {
        var source = new BpmnModelInputSource("Input", "ExactCaseName");
        var wire = JsonSerializer.SerializeToElement(source, Web);
        Assert.False(wire.TryGetProperty("value", out _));
        var retained = wire.Deserialize<BpmnModelInputSource>(Web)!;
        Assert.Equal("ExactCaseName", retained.Name); Assert.False(retained.Value.HasValue);
    }
    [Theory]
    [InlineData("{\"kind\":\"Literal\"}")]
    [InlineData("{\"kind\":\"Input\",\"name\":\"x\",\"value\":null}")]
    public void A_missing_literal_or_an_input_with_a_value_is_refused(string json) =>
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<BpmnModelInputSource>(json, Web));
}
