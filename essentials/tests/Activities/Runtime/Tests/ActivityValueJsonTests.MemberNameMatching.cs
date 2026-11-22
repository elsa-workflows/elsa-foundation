using System.Text.Json;
using Elsa.Activities.Runtime.Core.Serialization;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Characterises, on main before #4078, how member names are matched by the input hydrator (the activity-value
/// profile) and by <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>, the API an exact-segment member
/// selector would use. They agree on duplicate keys (the last one wins) and on literal names with dots, digits or no
/// characters. They disagree on case: the profile matches case-insensitively, the element lookup does not, so an
/// ordinal selector reads absent where hydration of the whole value would have found the member.
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
    public void Hydration_matches_a_member_name_case_insensitively_and_element_lookup_does_not()
    {
        var order = Parse("""{"CustomerName":"pascal","total":1}""");

        Assert.True(ActivityValueJsonProfile.CreateOptions().PropertyNameCaseInsensitive);
        Assert.Equal("pascal", Hydrate(order).Order!.CustomerName);
        Assert.False(order.TryGetProperty("customerName", out _));
        Assert.True(order.TryGetProperty("CustomerName", out _));
    }

    [Fact]
    public void With_two_spellings_of_one_name_hydration_takes_the_last_and_element_lookup_takes_the_exact_one()
    {
        var order = Parse("""{"customerName":"camel","CustomerName":"pascal","total":1}""");

        Assert.Equal("pascal", Hydrate(order).Order!.CustomerName);
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
