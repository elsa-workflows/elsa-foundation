using System.Text.Json;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// What a migration engineer reads when a reusable workflow is refused (spec 246 guidance review). In Elsa 3 a reusable
/// workflow runs inside the workflow that uses it, so Correlate, Set Name and Finish act on that caller, and each refusal
/// says which change to the Elsa 3 workflow keeps that behaviour, or that the workflow has to wait.
/// </summary>
public sealed class Elsa3ReusableGuidanceTests
{
    [Theory]
    [InlineData("Elsa.SetVariable", "remove the Set Variable: nothing else changes")]
    [InlineData("Elsa.SetOutput", "remove the Set Output")]
    [InlineData("Elsa.Correlate", "add a Correlate with the same value to each workflow that uses this activity, directly before the activity")]
    [InlineData("Elsa.SetName", "add a Set Name with the same value to each workflow that uses this activity")]
    [InlineData("Elsa.Finish", "connect that outcome to a Finish")]
    public async Task An_intrinsic_in_a_reusable_body_gets_advice_for_its_own_type(string type, string advice)
    {
        var result = await ConvertReusableAsync(Intrinsic(type));

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported, error.Code);
        Assert.Contains($"'n' ({type})", error.Message, StringComparison.Ordinal);
        Assert.Contains(advice, error.Guidance, StringComparison.Ordinal);
        Assert.Contains("elsa-foundation#3023", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finish_explains_that_in_Elsa_3_it_ends_the_calling_workflow()
    {
        var result = await ConvertReusableAsync(Intrinsic("Elsa.Finish"));

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Contains("ends the whole workflow that uses it", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_output_says_how_a_caller_gets_the_output_back_as_a_regular_workflow()
    {
        var result = await ConvertReusableAsync(Intrinsic("Elsa.SetOutput"));

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Contains("ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE", error.Guidance, StringComparison.Ordinal);
        Assert.Contains("Dispatch Workflow's Result", error.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("headers", "Headers")]
    [InlineData("queryStringData", "QueryStringData")]
    public async Task A_captured_http_endpoint_projection_is_reported_on_the_endpoint(string property, string output)
    {
        var endpoint = Activity("n", "Elsa.HttpEndpoint", ("path", Literal("/orders")), (property, Captured("variable-total")));

        var result = await ConvertReusableAsync(endpoint);

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported);
        Assert.Contains("'n' (Elsa.HttpEndpoint)", error.Message, StringComparison.Ordinal);
        Assert.Equal("n", error.Metadata["ActivityNodeId"]);
        Assert.Contains($"Remove the capture of '{output}' from the HTTP Endpoint", error.Guidance, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains("elsa3-http", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_captured_output_is_told_to_remove_the_capture()
    {
        var result = await ConvertReusableAsync(Activity("n", "Elsa.ReadLine", ("result", Captured("variable-total", "String"))));

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported, error.Code);
        Assert.Contains("is captured into a variable", error.Message, StringComparison.Ordinal);
        Assert.Contains("Remove the capture from this property", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bound_variable_is_reported_as_a_read()
    {
        var result = await ConvertReusableAsync(Activity("n", "Elsa.WriteLine", ("text", Captured("variable-total", "String"))));

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported, error.Code);
        Assert.Contains("reads a variable or another activity's result", error.Message, StringComparison.Ordinal);
        Assert.Contains("literal values only", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_outcome_named_done_is_told_to_update_its_callers_too()
    {
        var complete = Activity("n", "Elsa.Complete", ("outcomes", JsonSerializer.SerializeToElement(new { typeName = "Object", expression = new { type = "Literal", value = "done" } })));

        var result = await ConvertReusableAsync(complete);

        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Contains("change every workflow that routes on 'done' to the new name", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calling_a_regular_workflow_as_an_activity_says_the_import_drops_its_outputs()
    {
        var reference = ReusableActivityImportFixtures.Reference("call", targetVersionId: "regular-v1");
        var caller = ReusableActivityImportFixtures.Workflow("caller", "caller-v1", 1, false, reference);
        var regular = ReusableActivityImportFixtures.Workflow("regular", "regular-v1", 1, false, ReusableActivityImportFixtures.Leaf("root"));

        var plan = await new ReusableActivityCollectionAnalyzer().AnalyzeAsync(ReusableActivityImportFixtures.Collection(caller, regular));

        var diagnostic = Assert.Single(plan.Diagnostics, diagnostic => diagnostic.Metadata.TryGetValue("nodeId", out var nodeId) && nodeId == "call");
        Assert.Contains("Dispatch Workflow's Result", diagnostic.Guidance, StringComparison.Ordinal);
    }

    private static Elsa3Activity Intrinsic(string type) => type switch
    {
        "Elsa.SetVariable" => Activity("n", type, ("variable", JsonSerializer.SerializeToElement(new { id = "variable-total", name = "total", typeName = "String" })), ("value", Literal("1"))),
        "Elsa.SetOutput" => Activity("n", type, ("outputName", Literal("total")), ("outputValue", Literal("1"))),
        "Elsa.Correlate" => Activity("n", type, ("correlationId", Literal("order-1"))),
        "Elsa.SetName" => Activity("n", type, ("value", Literal("order 1"))),
        _ => Activity("n", type)
    };

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertReusableAsync(Elsa3Activity node)
    {
        var definition = ConversionServices.Definition(Sequence("root", node),
            variables: [new Elsa3Variable { Id = "variable-total", Name = "total", TypeName = "String", Value = "0" }],
            outputs: [new Elsa3WorkflowArgumentDefinition { Name = "total", Type = "String" }]);
        definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        return ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);
    }
}
