using System.Text.Json;
using Elsa.Activities.Runtime.Core.Serialization;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The default conversion contract and exact-segment JSON selection agree on names and duplicate keys.
/// A differently cased key remains data and produces an unmatched-member diagnostic.
/// </summary>
public sealed partial class ActivityValueJsonTests
{
    [Fact]
    public void With_a_duplicate_key_both_hydration_and_element_lookup_take_the_last_value()
    {
        var order = Parse("""{"customerName":"first","customerName":"second","total":1}""");

        Assert.Equal("second", Hydrate(order).Order!.CustomerName);
        Assert.True(order.TryGetProperty("customerName", out var selected));
        Assert.Equal("second", selected.GetString());
    }

    [Fact]
    public void Hydration_and_element_lookup_both_match_exact_names()
    {
        var order = Parse("""{"CustomerName":"pascal","total":1}""");

        var warnings = new Elsa.Workflows.Runtime.Services.Values.ExecutionWarningCollector();
        Assert.False(ActivityValueJsonProfile.CreateOptions().PropertyNameCaseInsensitive);
        Assert.Null(Hydrate(order, warnings).Order!.CustomerName);
        Assert.Equal("json-unmatched-member", Assert.Single(warnings.Warnings).Code);
        Assert.False(order.TryGetProperty("customerName", out _));
        Assert.True(order.TryGetProperty("CustomerName", out _));
    }

    [Fact]
    public void With_two_spellings_hydration_and_element_lookup_take_the_exact_one()
    {
        var order = Parse("""{"customerName":"camel","CustomerName":"pascal","total":1}""");

        var warnings = new Elsa.Workflows.Runtime.Services.Values.ExecutionWarningCollector();
        Assert.Equal("camel", Hydrate(order, warnings).Order!.CustomerName);
        Assert.Equal("json-unmatched-member", Assert.Single(warnings.Warnings).Code);
        Assert.True(order.TryGetProperty("customerName", out var camel));
        Assert.Equal("camel", camel.GetString());
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(" padded ")]
    public void Element_lookup_reads_a_literal_name_as_written(string name)
    {
        var value = Parse($$"""{"a":{"b":"nested"},{{JsonSerializer.Serialize(name)}}:"literal","[0]":"bracket"}""");

        Assert.True(value.TryGetProperty(name, out var selected));
        Assert.Equal("literal", selected.GetString());
    }

    [Fact]
    public void Element_lookup_does_not_trim_or_index()
    {
        var value = Parse("""{"name":"value","items":["first"]}""");

        Assert.False(value.TryGetProperty(" name", out _));
        Assert.False(value.GetProperty("items").ValueKind == JsonValueKind.Object);
        Assert.Throws<InvalidOperationException>(() => value.GetProperty("items").TryGetProperty("0", out _));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
