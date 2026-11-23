using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonCanonicalFormTests
{
    [Fact]
    public void Optional_values_keep_omission_distinct_from_explicit_null()
    {
        using var nil = JsonDocument.Parse("null");
        using var schema = JsonDocument.Parse("{\"maximum\":1.0,\"title\":\"\\u0061\"}");
        using var same = JsonDocument.Parse("{\"title\":\"a\",\"maximum\":1e0}");
        Assert.True(JsonCanonicalForm.Equals((JsonElement?)null, null));
        Assert.False(JsonCanonicalForm.Equals((JsonElement?)null, nil.RootElement));
        Assert.True(JsonCanonicalForm.Equals((JsonElement?)nil.RootElement, nil.RootElement));
        Assert.True(JsonCanonicalForm.Equals((JsonElement?)schema.RootElement, same.RootElement));
    }

    [Theory]
    [InlineData("{\"total\":1.0,\"name\":\"Ren\\u00e9\"}", "{\"name\":\"René\",\"total\":1}", true)]
    [InlineData("9007199254740993", "9007199254740992", false)]
    [InlineData("{}", "{\"x\":null}", false)]
    [InlineData("[1,2]", "[2,1]", false)]
    [InlineData("[1.0,2e0]", "[1,2]", true)]
    [InlineData("1e400", "10e399", true)]
    [InlineData("-0.000", "0e99999999999999999999999999999", true)]
    [InlineData("1e99999999999999999999999999999", "10e99999999999999999999999999998", true)]
    [InlineData("0.10000000000000000000000000001", "0.1", false)]
    public void Equality_and_hashes_use_the_same_exact_value_contract(string left, string right, bool equal)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        Assert.Equal(equal, JsonCanonicalForm.Equals(a.RootElement, b.RootElement));
        Assert.Equal(equal, JsonCanonicalForm.EqualsJson(left, right));
        Assert.Equal(equal, JsonCanonicalForm.EqualsUtf8(System.Text.Encoding.UTF8.GetBytes(left), System.Text.Encoding.UTF8.GetBytes(right)));
        Assert.Equal(equal, JsonCanonicalForm.EqualsNodes(JsonNode.Parse(left), JsonNode.Parse(right)));
        Assert.Equal(equal, JsonHashInput.Hash(a.RootElement) == JsonHashInput.Hash(b.RootElement));
    }

    [Fact]
    public void Node_equality_keeps_null_roots_and_absent_object_members_separate()
    {
        Assert.True(JsonCanonicalForm.EqualsNodes(null, JsonNode.Parse("null")));
        Assert.True(JsonCanonicalForm.EqualsJson(null, null));
        Assert.False(JsonCanonicalForm.EqualsJson(null, "null"));
        Assert.False(JsonCanonicalForm.EqualsNodes(JsonNode.Parse("{}"), JsonNode.Parse("{\"x\":null}")));
    }
}
