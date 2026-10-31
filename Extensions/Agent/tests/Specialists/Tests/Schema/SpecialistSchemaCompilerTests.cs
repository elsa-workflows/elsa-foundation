using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Design.Services;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Schema;

public sealed class SpecialistSchemaCompilerTests
{
    [Fact]
    public void Public_names_are_json_fields_while_opaque_keys_bind_constraints()
    {
        var contract = Contract(new("String"));
        var manifest = Manifest(new Dictionary<string, JsonElement>
        {
            ["opaque.output.key"] = Json("""{"enum":["approved","changes_requested"]}""")
        });
        var compiled = Compile(contract, manifest);
        Assert.Empty(compiled.Problems);
        Assert.True(compiled.Inputs!.Schema.GetProperty("properties").TryGetProperty("Brief", out _));
        Assert.False(compiled.Inputs.Schema.GetProperty("properties").TryGetProperty("opaque.input.key", out _));
        Assert.Empty(compiled.Result!.Validate(Json("""{"Verdict":"approved"}""")));
        Assert.NotEmpty(compiled.Result.Validate(Json("""{"Verdict":"maybe"}""")));
        var renamedDisplay = contract with { Outputs = [contract.Outputs[0] with { DisplayName = "A friendlier label" }] };
        Assert.True(JsonElement.DeepEquals(compiled.Result.Schema, Compile(renamedDisplay, manifest).Result!.Schema));
    }

    [Fact]
    public void Required_presence_and_allowed_null_are_separate()
    {
        var contract = Contract(new("String")) with
        {
            Inputs = [new("input", "Brief", new("String"), true, true, null, "elsa.json")],
            Outputs = [new("output", "Verdict", new("String"), true, true, "elsa.json")]
        };
        var compiled = Compile(contract, Manifest());
        Assert.Empty(compiled.Problems);
        Assert.Empty(compiled.Inputs!.Validate(Json("""{"Brief":null}""")));
        Assert.NotEmpty(compiled.Inputs.Validate(Json("{}")));
        Assert.Empty(compiled.Result!.Validate(Json("""{"Verdict":null}""")));
        Assert.NotEmpty(compiled.Result.Validate(Json("{}")));
    }

    [Theory]
    [InlineData("String", "{\"type\":\"integer\"}")]
    [InlineData("String", "{\"enum\":[\"allowed\",1]}")]
    [InlineData("Int32", "{\"maximum\":2147483648}")]
    [InlineData("Int64", "{\"maximum\":9223372036854775808}")]
    [InlineData("Int64", "{\"minimum\":-9223372036854775809}")]
    [InlineData("String", "{\"minLength\":10,\"maxLength\":2}")]
    [InlineData("Int32", "{\"exclusiveMinimum\":5,\"maximum\":5}")]
    [InlineData("Int32", "{\"minimum\":5,\"exclusiveMaximum\":5}")]
    [InlineData("Int32", "{\"exclusiveMinimum\":5,\"exclusiveMaximum\":4}")]
    [InlineData("String", "{\"minimum\":5}")]
    [InlineData("Int32", "{\"minLength\":1}")]
    [InlineData("String", "{\"items\":{\"type\":\"string\"}}")]
    public void Constraints_cannot_widen_or_contradict_the_public_type(string alias, string constraint)
    {
        var compiled = Compile(Contract(new(alias)), Manifest(new Dictionary<string, JsonElement> { ["opaque.output.key"] = Json(constraint) }));
        Assert.Null(compiled.Result);
        Assert.Contains(compiled.Problems, x => x.Code == "specialist.schema.refinement");
    }

    [Fact]
    public void Narrowed_integer_bounds_keep_full_Int64_precision()
    {
        var compiled = Compile(Contract(new("Int64")), Manifest(new Dictionary<string, JsonElement>
        {
            ["opaque.output.key"] = Json("""{"maximum":9223372036854775806}""")
        }));
        Assert.Empty(compiled.Problems);
        Assert.Empty(compiled.Result!.Validate(Json("""{"Verdict":9223372036854775806}""")));
        Assert.NotEmpty(compiled.Result.Validate(Json("""{"Verdict":9223372036854775807}""")));
    }

    [Theory]
    [InlineData(CollectionKind.Array)]
    [InlineData(CollectionKind.List)]
    public void Collection_items_retain_the_declared_element_type(CollectionKind collection)
    {
        var compiled = Compile(Contract(new("String", collection)), Manifest());
        Assert.Empty(compiled.Problems);
        Assert.Empty(compiled.Result!.Validate(Json("""{"Verdict":["one","two"]}""")));
        Assert.NotEmpty(compiled.Result.Validate(Json("""{"Verdict":["one",2]}""")));
    }

    [Fact]
    public void Dictionary_property_constraints_do_not_bypass_its_value_type()
    {
        var contract = Contract(new("String", CollectionKind.Dictionary));
        var widening = Compile(contract, Manifest(new Dictionary<string, JsonElement>
        {
            ["opaque.output.key"] = Json("""{"properties":{"count":{"type":"integer"}}}""")
        }));
        Assert.Contains(widening.Problems, x => x.Code == "specialist.schema.refinement");
        var narrower = Compile(contract, Manifest(new Dictionary<string, JsonElement>
        {
            ["opaque.output.key"] = Json("""{"properties":{"code":{"maxLength":2}}}""")
        }));
        Assert.Empty(narrower.Problems);
        Assert.Empty(narrower.Result!.Validate(Json("""{"Verdict":{"code":"AB","other":"text"}}""")));
        Assert.NotEmpty(narrower.Result.Validate(Json("""{"Verdict":{"code":"ABC"}}""")));
        Assert.NotEmpty(narrower.Result.Validate(Json("""{"Verdict":{"other":12}}""")));
    }

    [Fact]
    public void Unknown_constraint_keys_and_duplicate_json_names_are_actionable_refusals()
    {
        var contract = Contract(new("String"));
        var unknown = Compile(contract, Manifest(new Dictionary<string, JsonElement> { ["not-a-port"] = Json("{}") }));
        Assert.Contains(unknown.Problems, x => x.Code == "specialist.schema.unknown-member" && x.Path.EndsWith("not-a-port", StringComparison.Ordinal));
        var duplicate = contract with { Outputs = [contract.Outputs[0], contract.Outputs[0] with { ReferenceKey = "other-key" }] };
        Assert.Contains(Compile(duplicate, Manifest()).Problems, x => x.Code == "specialist.schema.member");
    }

    [Fact]
    public void Local_constraint_definitions_are_resolved_before_intersection()
    {
        var compiled = Compile(Contract(new("String")), Manifest(new Dictionary<string, JsonElement>
        {
            ["opaque.output.key"] = Json("""{"$defs":{"choice":{"enum":["approved"]}},"$ref":"#/$defs/choice"}""")
        }));
        Assert.Empty(compiled.Problems);
        Assert.Empty(compiled.Result!.Validate(Json("""{"Verdict":"approved"}""")));
        Assert.NotEmpty(compiled.Result.Validate(Json("""{"Verdict":"other"}""")));
    }

    private static ActivityContract Contract(TypeReference resultType) => new("1",
        [new("opaque.input.key", "Brief", new("String"), true, false, null, "elsa.json")],
        [new("opaque.output.key", "Verdict", resultType, true, false, "elsa.json")], [new("Done", "Done", true)]);

    private static SpecialistManifest Manifest(IReadOnlyDictionary<string, JsonElement>? constraints = null) =>
        new("Review the brief.", new("test", "editorial", "fixture"), Json("{}"), new Dictionary<string, JsonElement>(),
            constraints ?? new Dictionary<string, JsonElement>(), new());

    private static SpecialistSchemaCompilation Compile(ActivityContract contract, SpecialistManifest manifest) =>
        new SpecialistSchemaCompiler(new()).Compile(contract, manifest);

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
