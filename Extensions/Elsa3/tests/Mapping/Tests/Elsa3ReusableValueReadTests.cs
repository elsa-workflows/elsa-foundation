using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 246 D-8: a node in an Elsa 4 graph activity body resolves its bindings against the started workflow, never the
/// graph, so it cannot read the activity's own inputs, its variables or another body activity's result. A reusable body
/// that does is refused; a non-reusable workflow keeps converting.
/// </summary>
public sealed class Elsa3ReusableValueReadTests
{
    [Theory]
    [InlineData("getInput(\"message\")")]
    [InlineData("getVariable(\"total\")")]
    // Elsa 3's generated accessors (spec 247) resolve to the same reads.
    [InlineData("getMessage()")]
    [InlineData("getTotal()")]
    public async Task A_reusable_body_reading_an_input_or_variable_is_refused(string script)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(script, reusable: true), isReusable: true);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported, error.Code);
    }

    [Fact]
    public async Task A_reusable_body_reading_a_memory_reference_is_refused()
    {
        var definition = Definition("'x'", reusable: true);
        Node(definition, "write").AdditionalProperties["text"] = Captured("legacy:variable-total", "String");

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported);
    }

    [Theory]
    [InlineData("getInput(\"message\")")]
    [InlineData("getVariable(\"total\")")]
    public async Task A_non_reusable_workflow_keeps_reading_inputs_and_variables(string script)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(script, reusable: false), isReusable: false);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    private static Elsa3WorkflowDefinition Definition(string script, bool reusable)
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Expression("JavaScript", script)))),
            variables: [new Elsa3Variable { Id = "variable-total", Name = "total", TypeName = "String", Value = "0" }]);
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "message", Type = "String" }];
        if (reusable)
            definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        return definition;
    }

    private static Elsa3Activity Node(Elsa3WorkflowDefinition definition, string nodeId) =>
        definition.Root!.Activities!.Single(activity => activity.NodeId == nodeId);
}
