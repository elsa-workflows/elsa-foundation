using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>Spec 236 data-model §6: the node-rewrite strategies and the native structures they feed.</summary>
public sealed class Elsa3NodeRewriteTests
{
    [Theory]
    [InlineData("WaitAll", null)]
    [InlineData("WaitAny", 1)]
    public async Task Fork_join_mode_becomes_the_parallel_threshold(string joinMode, int? threshold)
    {
        var fork = Activity("fork", "Elsa.Fork", ("joinMode", JsonSerializer.SerializeToElement(joinMode)));
        fork.AdditionalProperties["branches"] = JsonSerializer.SerializeToElement(new[]
        {
            Port(Activity("a", "Elsa.WriteLine", ("text", Literal("a")))),
            Port(Activity("b", "Elsa.WriteLine", ("text", Literal("b"))))
        });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", fork)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var payload = Node(result.Value!.State, "fork").Structure!.Payload;
        Assert.Equal(threshold, payload.GetProperty("threshold").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("threshold").GetInt32());
        Assert.Equal(["a", "b"], payload.GetProperty("branches").EnumerateArray().Select(branch => branch.GetProperty("activity").GetProperty("nodeId").GetString()));
    }

    [Fact]
    public async Task A_container_variable_is_scoped_to_its_container()
    {
        var set = Activity("set", "Elsa.SetVariable", ("value", Literal("x")));
        set.AdditionalProperties["variable"] = JsonSerializer.SerializeToElement(new { id = "local", name = "Local", typeName = "String" });
        var scope = Sequence("scope", set);
        scope.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "Local", typeName = "String" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", scope)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var setNode = Node(result.Value!.State, "set");
        Assert.Equal("scope", setNode.Intrinsic!.Variable!.DeclaringScopeId);
        var variables = Node(result.Value.State, "scope").Structure!.Payload.GetProperty("variables").EnumerateArray().ToArray();
        Assert.Equal("local", Assert.Single(variables).GetProperty("referenceKey").GetString());
    }

    [Fact]
    public async Task A_workflow_variable_is_scoped_to_the_workflow()
    {
        var set = Activity("set", "Elsa.SetVariable", ("value", Literal("x")));
        set.AdditionalProperties["variable"] = JsonSerializer.SerializeToElement(new { id = "global", name = "Global", typeName = "String" });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(
            Sequence("root", set), variables: [new Elsa3Variable { Id = "global", Name = "Global", TypeName = "String" }]));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(VariableReference.WorkflowScopeId, Node(result.Value!.State, "set").Intrinsic!.Variable!.DeclaringScopeId);
    }

    [Theory]
    [InlineData("Elsa.Complete")]
    [InlineData("Elsa.End")]
    public async Task Complete_and_end_outside_the_root_flowchart_of_a_plain_workflow_are_unconvertible(string type)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", Activity("end", type))));

        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("[\"a\",\"b\"]", "Literal")]
    [InlineData("getOutcome()", "JavaScript")]
    public async Task Complete_with_several_outcomes_or_an_expression_is_unconvertible(string outcomes, string expressionType)
    {
        var complete = Activity("complete", "Elsa.Complete", ("outcomes", Expression(expressionType, outcomes, "Object")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", complete)));

        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Duplicate_switch_labels_are_unconvertible()
    {
        var @switch = Activity("switch", "Elsa.Switch");
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "same", condition = new { expression = new { type = "Literal", value = "true" } } },
            new { label = "same", condition = new { expression = new { type = "Literal", value = "false" } } }
        });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("'same'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switch_cases_and_default_arrive_in_their_slots()
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", Literal("MatchFirst")));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "one", condition = new { expression = new { type = "JavaScript", value = "true" } }, activity = Port(Activity("one-body", "Elsa.WriteLine", ("text", Literal("1")))) }
        });
        @switch.AdditionalProperties["default"] = Port(Activity("fallback", "Elsa.WriteLine", ("text", Literal("d"))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var payload = Node(result.Value!.State, "switch").Structure!.Payload;
        var single = Assert.Single(payload.GetProperty("cases").EnumerateArray());
        Assert.Equal("one", single.GetProperty("match").GetString());
        Assert.Equal("one-body", single.GetProperty("activity").GetProperty("nodeId").GetString());
        Assert.Equal("fallback", payload.GetProperty("default").GetProperty("nodeId").GetString());
    }
}
