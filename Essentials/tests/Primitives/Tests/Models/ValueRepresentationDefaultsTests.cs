using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Primitives.Tests.Models;

public class ValueRepresentationDefaultsTests
{
    [Theory]
    [InlineData("JsonElement")]
    [InlineData("JsonElement?")]
    [InlineData("System.Text.Json.JsonElement")]
    [InlineData("System.Text.Json.JsonElement?")]
    [InlineData("JsonNode")]
    [InlineData("System.Text.Json.Nodes.JsonNode")]
    [InlineData("JsonObject")]
    [InlineData("System.Text.Json.Nodes.JsonObject")]
    [InlineData("Any")]
    [InlineData("Elsa.Any")]
    public void Json_and_any_aliases_infer_a_structured_value(string alias) =>
        Assert.Equal(ValueRepresentation.StructuredValue, ValueRepresentationDefaults.Infer(new ValueTypeDescriptor(alias)));

    [Theory]
    [InlineData("Customer")]
    [InlineData("Int32?")]
    [InlineData("Acme.Text.Json.JsonElement")]
    public void Other_single_aliases_stay_typed_values(string alias) =>
        Assert.Equal(ValueRepresentation.TypedValue, ValueRepresentationDefaults.Infer(new ValueTypeDescriptor(alias)));
}
