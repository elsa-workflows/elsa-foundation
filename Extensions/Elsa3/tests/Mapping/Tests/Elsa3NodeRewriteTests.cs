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
    [InlineData("WAITALL", null)]
    [InlineData("waitany", 1)]
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

    [Theory]
    [InlineData("0", null)]
    [InlineData("1", 1)]
    public async Task A_numeric_fork_join_mode_maps_by_its_enum_value(string joinMode, int? threshold)
    {
        var result = await ConvertForkAsync(JsonDocument.Parse(joinMode).RootElement);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var payload = Node(result.Value!.State, "fork").Structure!.Payload;
        Assert.Equal(threshold, payload.GetProperty("threshold").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("threshold").GetInt32());
    }

    [Theory]
    [InlineData("2")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public async Task An_unknown_numeric_fork_join_mode_is_unconvertible(string joinMode)
    {
        var result = await ConvertForkAsync(JsonDocument.Parse(joinMode).RootElement);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains($"'{joinMode}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fork_without_a_join_mode_waits_for_all_branches_like_elsa3()
    {
        var fork = Activity("fork", "Elsa.Fork");
        fork.AdditionalProperties["branches"] = JsonSerializer.SerializeToElement(new[] { Port(Activity("a", "Elsa.WriteLine", ("text", Literal("a")))) });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", fork)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        // No threshold (absent or null) means the Parallel waits for every branch.
        var payload = Node(result.Value!.State, "fork").Structure!.Payload;
        Assert.True(!payload.TryGetProperty("threshold", out var threshold) || threshold.ValueKind == JsonValueKind.Null);
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

    /// <summary>Spec 1208 FR-029: several outcomes convert only in a reusable workflow; Elsa 4 'finish' names one.</summary>
    [Fact]
    public async Task Complete_with_several_outcomes_in_the_root_flowchart_of_a_plain_workflow_is_unconvertible()
    {
        var root = Activity("root", "Elsa.Flowchart");
        root.Activities = [Activity("complete", "Elsa.Complete", ("outcomes", Literal("[\"a\",\"b\"]", "Object")))];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("'finish'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("usable as an activity", diagnostic.Guidance, StringComparison.Ordinal);
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
        Assert.Equal("$.root.activities[0].cases[1]", diagnostic.Path);
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

    [Fact]
    public async Task Switch_conditions_with_a_terminator_or_a_trailing_comment_combine_into_one_value()
    {
        var result = await ConvertSwitchAsync("1 > 2;", "2 > 1 // adult", "true");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(3, Node(result.Value!.State, "switch").Structure!.Payload.GetProperty("cases").GetArrayLength());
    }

    [Fact]
    public async Task A_switch_condition_of_several_statements_is_unconvertible_at_its_case()
    {
        var result = await ConvertSwitchAsync("true", "var a = 1; a > 0");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("'case1'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("$.root.activities[0].cases[1].condition", diagnostic.Path);
        Assert.Equal("cases", diagnostic.Metadata["Property"]);
    }

    /// <summary>
    /// Spec 1208 FR-028: Elsa 3 MatchAny, in each form Elsa 3 serializes, becomes the Elsa 4 Switch's <c>MatchAny</c>
    /// mode with one condition per case under its case identity, and no synthesized <c>Value</c>.
    /// </summary>
    [Theory]
    [InlineData("\"MatchAny\"")]
    [InlineData("\"matchAny\"")]
    [InlineData("\"1\"")]
    [InlineData("1")]
    public async Task Switch_match_any_becomes_the_match_any_mode_with_a_condition_per_case(string mode)
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", JsonDocument.Parse(mode).RootElement));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "big", condition = new { typeName = "Boolean", expression = new { type = "JavaScript", value = "1 < 2;" } } },
            new { label = "always", condition = new { typeName = "Boolean", expression = new { type = "Literal", value = "true" } } },
            new { label = "never" }
        });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "switch");
        Assert.DoesNotContain(node.Inputs, input => input.ReferenceKey == "Value");
        var payload = node.Structure!.Payload;
        Assert.Equal("MatchAny", payload.GetProperty("mode").GetString());
        var cases = payload.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(["big", "always", "never"], cases.Select(item => item.GetProperty("match").GetString()));
        Assert.Equal(["case-0", "case-1", "case-2"], cases.Select(item => item.GetProperty("caseId").GetString()));
        Assert.All(cases, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("activity").ValueKind));
        var conditions = cases.Select(item => item.GetProperty("condition").Deserialize<Elsa.Workflows.Design.Core.Models.ArgumentState>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } })!).ToArray();
        Assert.Equal("JavaScript", conditions[0].Value.ExpressionType);
        Assert.Contains("(1 < 2)", Source(conditions[0]), StringComparison.Ordinal);
        Assert.Equal(("Literal", "true"), (conditions[1].Value.ExpressionType, Text(conditions[1])));
        Assert.Equal(("Literal", "false"), (conditions[2].Value.ExpressionType, Text(conditions[2])));
    }

    [Fact]
    public async Task A_match_any_case_that_holds_an_activity_is_unconvertible()
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", Literal("MatchAny")));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "empty", condition = new { expression = new { type = "Literal", value = "true" } } },
            new { label = "busy", condition = new { expression = new { type = "Literal", value = "true" } }, activity = Port(Activity("body", "Elsa.WriteLine", ("text", Literal("1")))) }
        });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("'busy'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("FlowSwitch", diagnostic.Guidance, StringComparison.Ordinal);
        Assert.Equal("$.root.activities[0].cases[1].activity", diagnostic.Path);
    }

    [Theory]
    [InlineData("\"MatchSome\"")]
    [InlineData("2")]
    public async Task An_unknown_switch_mode_is_unconvertible(string mode)
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", JsonDocument.Parse(mode).RootElement));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[] { new { label = "one" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("MatchAny", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>A schema-2 Switch case needs a case identity to publish; MatchFirst cases get one too.</summary>
    [Fact]
    public async Task Match_first_cases_carry_a_case_identity()
    {
        var result = await ConvertSwitchAsync("true", "false");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var payload = Node(result.Value!.State, "switch").Structure!.Payload;
        Assert.False(payload.TryGetProperty("mode", out _));
        Assert.Equal(["case-0", "case-1"], payload.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("caseId").GetString()));
        Assert.All(payload.GetProperty("cases").EnumerateArray(), item => Assert.False(item.TryGetProperty("condition", out _)));
    }

    private static Task<Elsa3MigrationResult<Elsa3.Mapping.Conversion.Elsa3ConvertedDefinition>> ConvertForkAsync(JsonElement joinMode)
    {
        var fork = Activity("fork", "Elsa.Fork", ("joinMode", joinMode));
        fork.AdditionalProperties["branches"] = JsonSerializer.SerializeToElement(new[] { Port(Activity("a", "Elsa.WriteLine", ("text", Literal("a")))) });
        return ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", fork)));
    }

    private static Task<Elsa3MigrationResult<Elsa3.Mapping.Conversion.Elsa3ConvertedDefinition>> ConvertSwitchAsync(params string[] conditions)
    {
        var @switch = Activity("switch", "Elsa.Switch");
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(conditions
            .Select((condition, index) => new { label = $"case{index}", condition = new { expression = new { type = "JavaScript", value = condition } } })
            .ToArray());
        return ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));
    }
}
