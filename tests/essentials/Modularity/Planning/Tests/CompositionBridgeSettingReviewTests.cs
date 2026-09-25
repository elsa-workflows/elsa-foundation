using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;

namespace Elsa.Modularity.Planning.Tests;

public sealed class CompositionBridgeSettingReviewTests
{
    [Fact]
    public void Reads_exact_safe_flag_and_limit_reviews()
    {
        var review = SettingReviewReader.Parse("""
            {
              "schemaVersion": "1",
              "fields": [
                { "featureId": "A", "pointer": "/Flag", "type": "boolean", "portable": true },
                { "featureId": "A", "pointer": "/Limit", "type": "number", "portable": true }
              ]
            }
            """);

        Assert.Equal(2, review.Fields.Length);
        Assert.True(review.TryGetExact("A", "/Flag", out var flag));
        Assert.Equal("A", flag.FeatureId);
        Assert.Equal(new[] { "Flag" }, flag.PointerSegments.ToArray());
        Assert.Equal(SettingReviewJsonType.Boolean, flag.ExpectedType);
        Assert.True(flag.Portable);
        Assert.True(review.TryGetExact("A", "/Limit", out var limit));
        Assert.Equal(SettingReviewJsonType.Number, limit.ExpectedType);
        Assert.True(review.RequirePortableValue("A", "/Flag", JsonValueKind.False).Portable);
        Assert.True(review.RequirePortableValue("A", "/Limit", JsonValueKind.Number).Portable);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[],\"copyAll\":true}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"Flag\",\"type\":\"boolean\",\"portable\":true}]}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"/Bad~2Escape\",\"type\":\"boolean\",\"portable\":true}]}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"\",\"type\":\"object\",\"portable\":true}]}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A;Password=x\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true}]}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"bool\",\"portable\":true}]}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":\"true\"}]}", "bridge-source-invalid")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true},{\"featureId\":\"a\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true}]}", "bridge-source-duplicate")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true},{\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true}]}", "bridge-source-duplicate")]
    [InlineData("{\"schemaVersion\":\"1\",\"fields\":[{\"featureId\":\"A\",\"pointer\":\"/Flag\",\"type\":\"boolean\",\"portable\":true},{\"featureId\":\"A\",\"pointer\":\"/flag\",\"type\":\"boolean\",\"portable\":true}]}", "bridge-source-duplicate")]
    [InlineData("{\"schemaVersion\":\"1\",\"schemaVersion\":\"1\",\"fields\":[]}", "bridge-source-duplicate")]
    public void Rejects_unsafe_or_ambiguous_review_input(string json, string expectedCode)
    {
        var exception = Assert.Throws<SettingReviewException>(() => SettingReviewReader.Parse(json));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void Refuses_unclassified_nonportable_and_type_mismatched_values()
    {
        var review = SettingReviewReader.Parse("""
            {
              "schemaVersion": "1",
              "fields": [
                { "featureId": "A", "pointer": "/Flag", "type": "boolean", "portable": true },
                { "featureId": "A", "pointer": "/LocalOnly", "type": "string", "portable": false }
              ]
            }
            """);

        Assert.Equal("bridge-portable-unsafe", Assert.Throws<SettingReviewException>(
            () => review.RequirePortableValue("A", "/Unknown", JsonValueKind.String)).Code);
        Assert.Equal("bridge-portable-unsafe", Assert.Throws<SettingReviewException>(
            () => review.RequirePortableValue("A", "/LocalOnly", JsonValueKind.String)).Code);
        Assert.Equal("bridge-portable-unsafe", Assert.Throws<SettingReviewException>(
            () => review.RequirePortableValue("A", "/Flag", JsonValueKind.String)).Code);
    }

    [Fact]
    public void Unescapes_rfc6901_pointer_segments_for_exact_lookup()
    {
        var review = SettingReviewReader.Parse("""
            {
              "schemaVersion": "1",
              "fields": [
                { "featureId": "A", "pointer": "/a~1b/~0key", "type": "string", "portable": true }
              ]
            }
            """);

        Assert.True(review.TryGetExact("A", "/a~1b/~0key", out var field));
        Assert.Equal(new[] { "a/b", "~key" }, field.PointerSegments.ToArray());
    }
}
