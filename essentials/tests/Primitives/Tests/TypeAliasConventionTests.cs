using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Primitives.Tests;

public class TypeAliasConventionTests
{
    [Theory]
    [InlineData("Any")]
    [InlineData("Elsa.Any")]
    [InlineData("Object")]
    [InlineData("System.Object")]
    [InlineData("JsonElement")]
    [InlineData("JsonElement?")]
    [InlineData("System.Text.Json.JsonElement")]
    [InlineData("System.Text.Json.JsonElement?")]
    [InlineData("JsonNode")]
    [InlineData("System.Text.Json.Nodes.JsonNode")]
    [InlineData("any")]
    public void Names_of_the_dynamic_kind_are_one_kind(string alias)
    {
        Assert.True(TypeAliasConvention.IsDynamicKind(alias));
    }

    [Theory]
    [InlineData("JsonObject")]
    [InlineData("System.Text.Json.Nodes.JsonObject")]
    [InlineData("JsonArray")]
    [InlineData("System.Text.Json.Nodes.JsonArray")]
    [InlineData("String")]
    [InlineData("Int32")]
    [InlineData("System.String[]")]
    [InlineData("")]
    [InlineData(null)]
    public void Shaped_json_and_other_types_are_not_the_dynamic_kind(string? alias)
    {
        Assert.False(TypeAliasConvention.IsDynamicKind(alias));
    }

    [Theory]
    [InlineData("JsonObject", true)]
    [InlineData("System.Text.Json.Nodes.JsonObject?", true)]
    [InlineData("JsonArray", true)]
    [InlineData("System.Text.Json.Nodes.JsonArray", true)]
    [InlineData("JsonElement", false)]
    [InlineData("Any", false)]
    public void JsonObject_and_JsonArray_are_shaped_json(string alias, bool expected)
    {
        Assert.Equal(expected, TypeAliasConvention.IsShapedJson(alias));
    }
}
