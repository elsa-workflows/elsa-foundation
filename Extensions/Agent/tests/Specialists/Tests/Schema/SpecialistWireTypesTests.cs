using System.Text.Json;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Schema;

public sealed class SpecialistWireTypesTests
{
    [Theory]
    [InlineData("Guid", "\"not-a-guid\"", false)]
    [InlineData("Guid", "\"e8484364-13ad-49d7-bd65-12c51b99e7f1\"", true)]
    [InlineData("DateTimeOffset", "\"yesterday\"", false)]
    [InlineData("DateTimeOffset", "\"2026-10-31T12:30:00Z\"", true)]
    [InlineData("TimeSpan", "\"01:30:00\"", true)]
    [InlineData("TimeSpan", "\"one hour\"", false)]
    [InlineData("Int64", "9223372036854775807", true)]
    [InlineData("Int64", "9223372036854775808", false)]
    [InlineData("Int64", "1e3", false)]
    [InlineData("Decimal", "79228162514264337593543950336", false)]
    [InlineData("Double", "1e999", false)]
    public void Checks_actual_native_conversion_without_rewriting_values(string alias, string raw, bool valid)
    {
        var value = JsonDocument.Parse("{\"Value\":" + raw + "}").RootElement;
        var problems = SpecialistWireTypes.Validate(value, [new("Value", alias, CollectionKind.Single, true, false)]);
        Assert.Equal(valid, problems.Count == 0);
        Assert.DoesNotContain(problems, x => x.Message.Contains("yesterday", StringComparison.Ordinal));
    }

    [Fact]
    public void Collection_elements_and_whole_value_nullability_are_checked_separately()
    {
        var members = new[] { new SpecialistJsonMember("Values", "Guid", CollectionKind.List, true, true) };
        Assert.Empty(SpecialistWireTypes.Validate(JsonDocument.Parse("{\"Values\":null}").RootElement, members));
        Assert.NotEmpty(SpecialistWireTypes.Validate(JsonDocument.Parse("{\"Values\":[null]}").RootElement, members));
        Assert.NotEmpty(SpecialistWireTypes.Validate(JsonDocument.Parse("{\"Values\":[\"invalid\"]}").RootElement, members));
        Assert.NotEmpty(SpecialistWireTypes.Validate(JsonDocument.Parse("{}").RootElement, members));
    }
}
