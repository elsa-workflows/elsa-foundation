using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 plan D-1 to D-8 and FR-004e: in a reusable workflow, Elsa 3 Complete/End become the graph Complete activity,
/// the contract declares the outcomes, SetOutput writes a mapped graph variable, and the graph provider accepts the
/// result (R-10). Bodies use Sequence, loops and Parallel; Flowchart roots wait for #2705.
/// </summary>
public sealed class Elsa3ReusableTerminalTests
{
    private const string CompleteKey = Elsa3TerminalPlacementRewrite.CompleteActivityTypeKey;

    [Fact]
    public async Task Complete_deep_in_a_loop_inside_parallel_ends_the_reusable_activity()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1]", "Object")));
        loop.AdditionalProperties["body"] = Port(Activity("fail", "Elsa.Complete", ("outcomes", Literal("fail"))));
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.Activities = [loop];
        var definition = Reusable(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("x"))), parallel), outcomes: ["fail"]);
        var catalog = RealActivityCatalog.Create();

        var result = await ConversionServices.ConvertAsync(catalog, definition, isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var complete = Node(result.Value!.State, "fail");
        Assert.Equal(catalog.VersionIdOf(CompleteKey), complete.ActivityVersionId);
        Assert.Null(complete.Intrinsic);
        Assert.Equal("fail", Text(Input(complete, "outcome")));
        Assert.Equal([("done", "Done"), ("fail", "fail")], Outcomes(result.Value));
        Assert.Equal(("Done", "done"), Assert.Single(result.Value.OutcomeMappings) is var mapping ? (mapping.SourceOutcomeReferenceKey, mapping.BoundaryOutcomeReferenceKey) : default);
        Assert.Empty(Errors(await GraphProviderProof.ValidateAsync(catalog, result.Value)));
        GraphProviderProof.AssertCompiled(await GraphProviderProof.CompileAsync(catalog, result.Value));
    }

    [Fact]
    public async Task End_completes_with_done_and_a_natural_end_needs_no_node()
    {
        var catalog = RealActivityCatalog.Create();
        var root = Activity("root", "Elsa.Flowchart");
        root.Activities = [Activity("end", "Elsa.End")];

        var result = await ConversionServices.ConvertAsync(catalog, Reusable(root), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("done", Text(Input(Node(result.Value!.State, "end"), "outcome")));
        Assert.Equal([("done", "Done")], Outcomes(result.Value));
        Assert.Empty(Errors(await GraphProviderProof.ValidateAsync(catalog, result.Value)));
        GraphProviderProof.AssertCompiled(await GraphProviderProof.CompileAsync(catalog, result.Value));
    }

    [Fact]
    public async Task End_outside_the_root_flowchart_is_unconvertible()
    {
        // Elsa 3 End ends only its enclosing flowchart, and does nothing in a Sequence (spec 239 FR-011, amends D-3).
        var result = await ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(), Reusable(Sequence("root", Activity("end", "Elsa.End"))), isReusable: true);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, error.Code);
        Assert.Equal("end", error.Metadata["ActivityNodeId"]);
    }

    [Theory]
    [InlineData("[\"a\",\"b\"]", "Literal")]
    [InlineData("pick()", "JavaScript")]
    [InlineData("done", "Literal")]
    public async Task Several_outcomes_an_expression_or_a_done_collision_is_unconvertible(string outcomes, string expressionType)
    {
        var complete = Activity("complete", "Elsa.Complete", ("outcomes", Expression(expressionType, outcomes, "Object")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Reusable(Sequence("root", complete)), isReusable: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
    }

    [Fact]
    public async Task An_undeclared_outcome_is_declared_with_a_warning()
    {
        var result = await ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            Reusable(Sequence("root", Activity("complete", "Elsa.Complete", ("outcomes", Literal("Not Found"))))),
            isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutcomeUndeclared);
        // Names stay verbatim, so caller ports keep their Elsa 3 names.
        Assert.Equal([("Not Found", "Not Found"), ("done", "Done")], Outcomes(result.Value!));
    }

    [Fact]
    public async Task A_declared_outcome_nothing_completes_with_is_dropped_with_a_warning()
    {
        var result = await ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            Reusable(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("x")))), outcomes: ["Rejected", "Done"]),
            isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.OutcomeUnreached, warning.Code);
        Assert.Contains("'Rejected'", warning.Message, StringComparison.Ordinal);
        Assert.Equal([("done", "Done")], Outcomes(result.Value!));
    }

    [Fact]
    public async Task Break_still_converts_to_break_in_a_reusable_workflow()
    {
        var loop = Activity("loop", "Elsa.While", ("condition", Literal("true", "Boolean")));
        loop.AdditionalProperties["body"] = Port(Activity("break", "Elsa.Break"));
        var catalog = RealActivityCatalog.Create();

        var result = await ConversionServices.ConvertAsync(catalog, Reusable(Sequence("root", loop)), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(catalog.VersionIdOf("Elsa.Activities.Primitives.Activities.Break"), Node(result.Value!.State, "break").ActivityVersionId);
    }

    [Fact]
    public async Task Complete_and_end_in_a_plain_workflow_are_unchanged()
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", Activity("end", "Elsa.End"))));

        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("Elsa.SetOutput")]
    [InlineData("Elsa.SetName")]
    [InlineData("Elsa.Correlate")]
    public async Task An_activity_that_becomes_an_engine_intrinsic_refuses_the_reusable(string type)
    {
        // Spec 246 D-7: a graph activity resolves every node to a published version and intrinsics have none, so the
        // conversion names the node instead of letting publish fail on an unresolved dependency.
        var set = type switch
        {
            "Elsa.SetOutput" => Activity("set", type, ("outputName", Literal("CustomOutput")), ("outputValue", Literal("42"))),
            "Elsa.SetName" => Activity("set", type, ("value", Literal("name"))),
            _ => Activity("set", type, ("correlationId", Literal("id")))
        };
        var definition = Reusable(
            Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("x"))), set),
            outputs: [new Elsa3WorkflowArgumentDefinition { Name = "CustomOutput", Type = "String" }]);

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported, error.Code);
        Assert.Equal("set", error.Metadata["ActivityNodeId"]);
        Assert.Equal(type, error.Metadata["ActivityType"]);
    }

    [Fact]
    public async Task The_same_activity_in_a_non_reusable_workflow_still_converts()
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("set", "Elsa.SetOutput", ("outputName", Literal("CustomOutput")), ("outputValue", Literal("42")))),
            outputs: [new Elsa3WorkflowArgumentDefinition { Name = "CustomOutput", Type = "String" }]);

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: false);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task SetOutput_in_a_plain_workflow_stays_the_set_output_intrinsic()
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(
            Sequence("root", Activity("set", "Elsa.SetOutput", ("outputName", Literal("CustomOutput")), ("outputValue", Literal("42"))))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(Elsa3IntrinsicContracts.SetOutput, Node(result.Value!.State, "set").ActivityVersionId);
        Assert.Empty(result.Value.OutputMappings);
    }

    private static Elsa3WorkflowDefinition Reusable(
        Elsa3Activity root,
        IEnumerable<string>? outcomes = null,
        IEnumerable<Elsa3WorkflowArgumentDefinition>? outputs = null)
    {
        var definition = ConversionServices.Definition(root, outputs: outputs);
        definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        definition.Outcomes = (outcomes ?? []).ToList();
        return definition;
    }

    private static IEnumerable<(string ReferenceKey, string Name)> Outcomes(Elsa3ConvertedDefinition converted) =>
        converted.BoundaryOutcomes.Select(outcome => (outcome.ReferenceKey, outcome.Name));

    private static IEnumerable<ActivityDiagnostic> Errors(IEnumerable<ActivityDiagnostic> diagnostics) =>
        diagnostics.Where(diagnostic => diagnostic.Severity == ActivityDiagnosticSeverity.Error);
}
