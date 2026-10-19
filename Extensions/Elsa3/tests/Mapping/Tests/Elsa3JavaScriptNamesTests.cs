using Elsa3.Mapping.Internal;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>Spec 247 research R-3: accessor names as Elsa 3 generated them.</summary>
public sealed class Elsa3JavaScriptNamesTests
{
    [Theory]
    [InlineData("httpRequestBody", "HttpRequestBody")]
    [InlineData("RequestBody", "RequestBody")]
    [InlineData("x1", "X1")]
    [InlineData("i", "I")]
    public void Pascalize_upper_cases_the_first_character_only(string name, string expected) =>
        Assert.Equal(expected, Elsa3JavaScriptNames.Pascalize(name));

    [Theory]
    [InlineData("orderId", true)]
    [InlineData("Order1", true)]
    [InlineData("", false)]
    [InlineData("a_b", false)]
    [InlineData("a$", false)]
    [InlineData("a b", false)]
    public void Only_letters_and_digits_get_an_accessor(string name, bool expected) =>
        Assert.Equal(expected, Elsa3JavaScriptNames.IsAccessorName(name));
}
