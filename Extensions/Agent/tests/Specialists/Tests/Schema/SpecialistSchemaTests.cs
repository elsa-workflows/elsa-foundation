using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Agent.Specialists.JsonSchema;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Schema;

public sealed class SpecialistSchemaTests
{
    private const string ReviewSchema = """
        {"type":"object","properties":{"verdict":{"type":"string","enum":["approved","changes_requested"]},
        "comments":{"type":"string"}},"required":["verdict","comments"],"additionalProperties":false}
        """;

    [Theory]
    [InlineData("{\"verdict\":\"approved\",\"comments\":\"Good\"}", true)]
    [InlineData("{\"verdict\":\"changes_requested\",\"comments\":\"Revise\"}", true)]
    [InlineData("{\"verdict\":\"maybe\",\"comments\":\"Good\"}", false)]
    [InlineData("{\"verdict\":\"approved\"}", false)]
    [InlineData("{\"verdict\":\"approved\",\"comments\":null}", false)]
    [InlineData("{\"verdict\":\"approved\",\"comments\":\"Good\",\"undeclared\":true}", false)]
    public void Structured_review_enforces_required_types_enum_and_closed_members(string json, bool valid)
    {
        var prepared = Prepare(ReviewSchema);
        using var value = JsonDocument.Parse(json);
        Assert.Equal(valid, prepared.Validate(value.RootElement).Count == 0);
    }

    [Theory]
    [InlineData("{\"$ref\":\"https://example.invalid/private-schema\"}", "specialist.schema.reference")]
    [InlineData("{\"$ref\":\"file:///private/schema.json\"}", "specialist.schema.reference")]
    [InlineData("{\"$ref\":\"#/$defs/missing\"}", "specialist.schema.reference")]
    [InlineData("{\"$schema\":\"https://example.invalid/custom-dialect\"}", "specialist.schema.dialect")]
    [InlineData("{\"pattern\":\"(a+)+$\"}", "specialist.schema.keyword")]
    [InlineData("{\"oneOf\":[true,false]}", "specialist.schema.keyword")]
    [InlineData("{\"$dynamicRef\":\"#anchor\"}", "specialist.schema.keyword")]
    [InlineData("{\"type\":\"string\",\"type\":\"integer\"}", "specialist.json.duplicate")]
    [InlineData("{\"$defs\":{\"loop\":{\"$ref\":\"#/$defs/loop\"}},\"$ref\":\"#/$defs/loop\"}", "specialist.schema.reference-cycle")]
    public void Unsafe_or_unsupported_schema_is_refused_before_evaluation(string json, string code)
    {
        using var schema = JsonDocument.Parse(json);
        var result = new SpecialistSchemaValidator().Prepare(schema.RootElement);
        Assert.Null(result.Schema);
        Assert.Equal(code, Assert.Single(result.Problems).Code);
    }

    [Fact]
    public void Bounded_local_pointer_is_resolved_and_preserves_literal_reference_named_data()
    {
        var prepared = Prepare("""
            {"$schema":"https://json-schema.org/draft/2020-12/schema",
             "$defs":{"review/item":{"type":"object","properties":{"$ref":{"const":"literal-data"}},"required":["$ref"],"additionalProperties":false}},
             "type":"object","properties":{"review":{"$ref":"#/$defs/review~1item"}},"required":["review"],"additionalProperties":false}
            """);
        using var valid = JsonDocument.Parse("""{"review":{"$ref":"literal-data"}}""");
        using var invalid = JsonDocument.Parse("""{"review":{"$ref":"wrong"}}""");
        Assert.Empty(prepared.Validate(valid.RootElement));
        Assert.NotEmpty(prepared.Validate(invalid.RootElement));
        Assert.False(prepared.Schema.TryGetProperty("$defs", out _));
    }

    [Fact]
    public void Expanded_reference_graph_has_a_finite_budget()
    {
        var definitions = new JsonObject { ["d0"] = new JsonObject { ["type"] = "integer" } };
        for (var i = 1; i < 12; i++)
            definitions[$"d{i}"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["left"] = new JsonObject { ["$ref"] = $"#/$defs/d{i - 1}" },
                    ["right"] = new JsonObject { ["$ref"] = $"#/$defs/d{i - 1}" }
                }
            };
        var schema = JsonSerializer.SerializeToElement(new JsonObject { ["$defs"] = definitions, ["$ref"] = "#/$defs/d11" });
        var result = new SpecialistSchemaValidator().Prepare(schema);
        Assert.Null(result.Schema);
        Assert.Equal("specialist.schema.limit", Assert.Single(result.Problems).Code);
    }

    [Fact]
    public void Invalid_values_never_echo_protected_content_in_diagnostics()
    {
        var prepared = Prepare("""{"type":"string","enum":["allowed"]}""");
        using var value = JsonDocument.Parse("\"SENTINEL_PRIVATE_CONTENT\"");
        var problems = prepared.Validate(value.RootElement);
        Assert.Equal("specialist.value.invalid", Assert.Single(problems).Code);
        Assert.DoesNotContain("SENTINEL_PRIVATE_CONTENT", JsonSerializer.Serialize(problems));
    }

    [Theory]
    [InlineData("{\"name\":1,\"name\":2}", "specialist.json.duplicate")]
    [InlineData("1e99999999", "specialist.json.limit")]
    [InlineData("1e-99999999", "specialist.json.limit")]
    public void Ambiguous_or_unbounded_numbers_are_not_accepted_as_json_values(string json, string code)
    {
        using var value = JsonDocument.Parse(json);
        Assert.Equal(code, Assert.Single(Prepare("true").Validate(value.RootElement)).Code);
    }

    [Fact]
    public void Byte_depth_and_node_limits_apply_even_when_schema_accepts_any_value()
    {
        var prepared = Prepare("true");
        var values = new[]
        {
            "\"" + new string('é', 600_000) + "\"",
            new string('[', 40) + "0" + new string(']', 40),
            "[" + string.Join(',', Enumerable.Repeat("0", SpecialistSchemaValidator.MaximumValueNodes)) + "]"
        };
        foreach (var json in values)
        {
            using var value = JsonDocument.Parse(json);
            Assert.Equal("specialist.json.limit", Assert.Single(prepared.Validate(value.RootElement)).Code);
        }
    }

    [Fact]
    public async Task Concurrent_preparation_and_evaluation_have_independent_schema_registries()
    {
        var validator = new SpecialistSchemaValidator();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            using var schema = JsonDocument.Parse(ReviewSchema);
            var prepared = validator.Prepare(schema.RootElement);
            using var value = JsonDocument.Parse("""{"verdict":"approved","comments":"Good"}""");
            return prepared.Problems.Count == 0 && prepared.Schema!.Validate(value.RootElement).Count == 0;
        })));
        Assert.All(results, Assert.True);
    }

    private static PreparedSpecialistSchema Prepare(string json)
    {
        using var document = JsonDocument.Parse(json);
        var preparation = new SpecialistSchemaValidator().Prepare(document.RootElement);
        Assert.Empty(preparation.Problems);
        return Assert.IsType<PreparedSpecialistSchema>(preparation.Schema);
    }
}
