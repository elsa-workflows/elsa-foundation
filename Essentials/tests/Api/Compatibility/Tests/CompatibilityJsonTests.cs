using Elsa.Api.Compatibility.Testing.Serialization;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class CompatibilityJsonTests
{
    [Fact]
    public void Object_property_order_is_canonical_and_array_order_is_preserved()
    {
        var first = new Dictionary<string, object?> { ["z"] = 1, ["a"] = new[] { 2, 1 } };
        var second = new Dictionary<string, object?> { ["a"] = new[] { 2, 1 }, ["z"] = 1 };

        Assert.Equal(CompatibilityJson.Serialize(first), CompatibilityJson.Serialize(second));
        Assert.Contains("\"a\"", CompatibilityJson.Serialize(first), StringComparison.Ordinal);
        Assert.True(CompatibilityJson.Serialize(first).IndexOf("\"a\"", StringComparison.Ordinal)
                    < CompatibilityJson.Serialize(first).IndexOf("\"z\"", StringComparison.Ordinal));
    }
}
