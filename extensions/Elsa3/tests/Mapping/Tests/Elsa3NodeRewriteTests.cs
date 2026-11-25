using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Mappings;
using Microsoft.Extensions.DependencyInjection;
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

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES");
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
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES"));
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

        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES").Code);
    }

    [Theory]
    [InlineData("[\"a\",\"b\"]", "Literal")]
    [InlineData("getOutcome()", "JavaScript")]
    public async Task Complete_with_several_outcomes_or_an_expression_is_unconvertible(string outcomes, string expressionType)
    {
        var complete = Activity("complete", "Elsa.Complete", ("outcomes", Expression(expressionType, outcomes, "Object")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", complete)));

        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES").Code);
    }

    /// <summary>Spec 1208 FR-029: several outcomes convert only in a reusable workflow; Elsa 4 'finish' names one.</summary>
    [Fact]
    public async Task Complete_with_several_outcomes_in_the_root_flowchart_of_a_plain_workflow_is_unconvertible()
    {
        var root = Activity("root", "Elsa.Flowchart");
        root.Activities = [Activity("complete", "Elsa.Complete", ("outcomes", Literal("[\"a\",\"b\"]", "Object")))];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES");
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

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES");
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

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES");
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("'case1'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("$.root.activities[0].cases[1].condition", diagnostic.Path);
        Assert.Equal("cases", diagnostic.Metadata["Property"]);
    }

    /// <summary>
    /// #5789: Elsa 3 exports a case condition as a bare expression <c>{ type, value }</c>, not the typed-value shape of
    /// an activity property. Its JavaScript reaches the Elsa 4 switch in both modes instead of becoming <c>false</c>.
    /// </summary>
    [Theory]
    [InlineData("MatchFirst")]
    [InlineData("MatchAny")]
    public async Task A_bare_case_condition_carries_its_JavaScript_into_the_switch(string mode)
    {
        var flowSwitch = Activity("switch", "Elsa.FlowSwitch", ("mode", Literal(mode)));
        flowSwitch.AdditionalProperties["cases"] = JsonDocument.Parse("""
            [
              { "label": "Approve", "condition": { "type": "JavaScript", "value": "\"approve\".length === 7" } },
              { "label": "Reject", "condition": { "type": "JavaScript", "value": "\"reject\".length === 7" } },
              { "label": "Never", "condition": { "type": "Literal", "value": "" } }
            ]
            """).RootElement.Clone();

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", flowSwitch)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "switch");
        if (mode == "MatchFirst")
        {
            var value = Source(Input(node, "Value"));
            Assert.Contains("if ((\"approve\".length === 7)) { return \"Approve\"; }", value, StringComparison.Ordinal);
            Assert.Contains("if ((\"reject\".length === 7)) { return \"Reject\"; }", value, StringComparison.Ordinal);
            Assert.Contains("if (false) { return \"Never\"; }", value, StringComparison.Ordinal);
            return;
        }

        var conditions = node.Structure!.Payload.GetProperty("cases").EnumerateArray()
            .Select(item => item.GetProperty("condition").Deserialize<Elsa.Workflows.Design.Core.Models.ArgumentState>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } })!)
            .ToArray();
        Assert.Contains("\"approve\".length === 7", Source(conditions[0]), StringComparison.Ordinal);
        Assert.Contains("\"reject\".length === 7", Source(conditions[1]), StringComparison.Ordinal);
        Assert.Equal(("Literal", "false"), (conditions[2].Value.ExpressionType, Text(conditions[2])));
    }

    /// <summary>#5789: a condition that carries no source is false, as Elsa 3 evaluated it; a bare true literal is true.</summary>
    [Fact]
    public async Task Case_conditions_without_source_are_false_and_a_bare_true_literal_is_true()
    {
        var @switch = Activity("switch", "Elsa.Switch");
        @switch.AdditionalProperties["cases"] = JsonDocument.Parse("""
            [
              { "label": "absent" },
              { "label": "null", "condition": null },
              { "label": "no-value", "condition": { "type": "JavaScript" } },
              { "label": "null-value", "condition": { "type": "JavaScript", "value": null } },
              { "label": "blank", "condition": { "type": "JavaScript", "value": "  " } },
              { "label": "comment", "condition": { "type": "JavaScript", "value": "// later" } },
              { "label": "bare-true", "condition": { "type": "Literal", "value": true } }
            ]
            """).RootElement.Clone();

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var value = Source(Input(Node(result.Value!.State, "switch"), "Value"));
        foreach (var label in new[] { "absent", "null", "no-value", "null-value", "blank", "comment" })
            Assert.Contains($"if (false) {{ return \"{label}\"; }}", value, StringComparison.Ordinal);
        Assert.Contains("if (true) { return \"bare-true\"; }", value, StringComparison.Ordinal);
    }

    /// <summary>#5789: a case condition the converter cannot read is refused at its case, never read as <c>false</c>.</summary>
    [Theory]
    [InlineData("""{ "type": "Liquid", "value": "{{ true }}" }""", "'Liquid' expression")]
    [InlineData("""{ "type": "JavaScript", "value": 1 }""", "source is not text")]
    [InlineData("""{ "language": "JavaScript", "script": "true" }""", "does not recognise")]
    [InlineData("""[ "true" ]""", "does not recognise")]
    [InlineData("\"1 > 0\"", "does not recognise")]
    [InlineData("2", "does not recognise")]
    public async Task An_unreadable_case_condition_is_refused_at_its_case(string condition, string reason)
    {
        var @switch = Activity("switch", "Elsa.Switch");
        @switch.AdditionalProperties["cases"] = JsonDocument.Parse(
            $$"""[ { "label": "first", "condition": { "type": "JavaScript", "value": "true" } }, { "label": "second", "condition": {{condition}} } ]""").RootElement.Clone();

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES");
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, diagnostic.Code);
        Assert.Contains("'second'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(reason, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("$.root.activities[0].cases[1].condition", diagnostic.Path);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
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
    public async Task Match_any_preserves_populated_and_empty_cases_with_conditions_and_ids()
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", Literal("MatchAny")));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "empty", condition = new { expression = new { type = "Literal", value = "true" } } },
            new { label = "busy", condition = new { expression = new { type = "Literal", value = "true" } }, activity = Port(Activity("body", "Elsa.WriteLine", ("text", Literal("1")))) }
        });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var cases = Node(result.Value!.State, "switch").Structure!.Payload.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal("case-1", cases[1].GetProperty("caseId").GetString());
        Assert.Equal("body", cases[1].GetProperty("activity").GetProperty("nodeId").GetString());
        Assert.True(cases[1].TryGetProperty("condition", out _));
    }

    [Theory]
    [InlineData("break")]
    [InlineData("script")]
    [InlineData("scripts")]
    [InlineData("computed")]
    [InlineData("nested-label")]
    public async Task Match_any_refuses_effective_Break_at_the_case_boundary(string form)
    {
        var body = form switch
        {
            "break" => Activity("body", "Elsa.Break"),
            "script" => Activity("body", "Elsa.RunJavaScript", ("script", Literal("setOutcome('Break');"))),
            "scripts" => Activity("body", "Elsa.RunJavaScript", ("script", Literal("setOutcomes(['Break']);"))),
            "computed" => Activity("body", "Elsa.RunJavaScript", ("script", Literal("const name = 'Break'; setOutcome(name);")), ("possibleOutcomes", Literal("[\"Break\"]", "List<String>"))),
            _ => MatchAnyCase(Activity("leaf", "Elsa.WriteLine", ("text", Literal("nested"))), "Break", mode: "MatchFirst")
        };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", MatchAnyCase(body))));
        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        Assert.Contains("Break", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("cases[0].activity", diagnostic.Path, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
    }

    [Theory]
    [InlineData("Break")]
    [InlineData("Done")]
    public async Task Match_any_refuses_own_control_labels_before_they_change_Sequence_behavior(string label)
    {
        var body = Activity("body", "Elsa.WriteLine", ("text", Literal("body")));
        var source = Sequence("root", MatchAnyCase(body, label), Activity("after", "Elsa.WriteLine", ("text", Literal("after"))));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(source));
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        Assert.EndsWith("cases[0].label", diagnostic.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("if")]
    [InlineData("loop")]
    [InlineData("nested-label")]
    public async Task Match_any_keeps_proven_safe_case_bodies_convertible(string form)
    {
        var leaf = Activity("leaf", "Elsa.WriteLine", ("text", Literal("safe")));
        var body = form switch
        {
            "script" => Activity("body", "Elsa.RunJavaScript", ("script", Literal("setOutcome('Ordinary');"))),
            "if" => Activity("body", "Elsa.If", ("condition", Literal("true", "Boolean")), ("then", Port(leaf))),
            "loop" => Activity("body", "Elsa.While", ("condition", Literal("true", "Boolean")), ("body", Port(Activity("inner-break", "Elsa.Break")))),
            _ => MatchAnyCase(leaf, "Ordinary", mode: "MatchFirst")
        };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", MatchAnyCase(body))));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    private static Elsa3Activity MatchAnyCase(Elsa3Activity body, string label = "Case", string mode = "MatchAny", bool matches = true)
    {
        var node = Activity("switch-" + body.Id, "Elsa.Switch", ("mode", Literal(mode)));
        node.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label, condition = new { expression = new { type = "Literal", value = matches ? "true" : "false" } }, activity = Port(body) }
        });
        return node;
    }

    [Theory]
    [InlineData("setOutcome('Break');", false)]
    [InlineData("setOutcomes(['Done','Other']);", false)]
    [InlineData("return 42;", true)]
    [InlineData("setOutcome('Ordinary');", true)]
    public async Task Newly_enabled_owner_checks_Default_outcome_fidelity_too(string script, bool safe)
    {
        var owner = MatchAnyCase(Activity("never", "Elsa.WriteLine", ("text", Literal("never"))), matches: false);
        owner.AdditionalProperties["default"] = Port(Activity("fallback", "Elsa.RunJavaScript", ("script", Literal(script))));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", owner)));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode && item.Path.Contains(".default", StringComparison.Ordinal));
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task An_outer_loop_does_not_make_an_escaping_case_Break_faithful()
    {
        var owner = MatchAnyCase(Activity("body", "Elsa.Break"));
        var loop = Activity("outer", "Elsa.While", ("condition", Literal("true", "Boolean")), ("body", Port(owner)));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", loop)));
        Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode && item.Path.Contains("cases[0].activity", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Acme.PossibleBreak", false)]
    [InlineData("Acme.SendInvoice", true)]
    public async Task Alias_outcomes_are_checked_after_catalog_resolution(string type, bool safe)
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(PossibleBreakAliasActivity).Assembly);
        var result = await ConversionServices.ConvertAsync(catalog,
            ConversionServices.Definition(Sequence("root", MatchAnyCase(Activity("body", type)))));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("Break", false)]
    [InlineData("Ordinary", true)]
    public async Task Exact_replacement_outcomes_are_checked_after_effective_resolution(string outcome, bool safe)
    {
        await using var provider = ConversionServices.Create(RealActivityCatalog.Create());
        await using var scope = provider.CreateAsyncScope();
        var source = ConversionServices.Definition(Sequence("root", MatchAnyCase(Activity("body", "Acme.Reference"))));
        var result = await scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>().ConvertAsync(new Elsa3ConversionRequest(source,
            replacements: new Dictionary<string, Elsa3ActivityExactReplacement> { ["body"] = new("replacement-version", [],
                new HashSet<string>(), new HashSet<string> { outcome }) }));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("MatchAny", true, false)]
    [InlineData("MatchAny", false, true)]
    [InlineData("MatchFirst", true, true)]
    public async Task Nested_Done_is_refused_only_when_its_MatchAny_selection_can_be_invalid(string mode, bool secondCase, bool safe)
    {
        var nested = Activity("nested", "Elsa.Switch", ("mode", Literal(mode)));
        nested.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement((secondCase ? new[] { "Done", "Other" } : ["Done"])
            .Select(label => new { label, condition = new { expression = new { type = "Literal", value = "true" } } }).ToArray());
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", MatchAnyCase(nested))));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode && item.Message.Contains("Done", StringComparison.Ordinal));
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_proven_false_nested_case_cannot_create_a_Done_co_selection()
    {
        var nested = Activity("nested", "Elsa.Switch", ("mode", Literal("MatchAny")));
        nested.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new[]
        {
            new { label = "Done", condition = new { expression = new { type = "Literal", value = "true" } } },
            new { label = "Other", condition = new { expression = new { type = "Literal", value = "false" } } }
        });
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", MatchAnyCase(nested))));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("setOutcomes(['Done','Other']);", false)]
    [InlineData("const names=['Done','Other']; setOutcomes(names);", false)]
    [InlineData("if (Math.random() > .5) setOutcome('Done'); else setOutcome('Other');", true)]
    [InlineData("const name='Done'; setOutcome(name);", true)]
    [InlineData("setOutcomes(['Done']);", true)]
    [InlineData("setOutcomes(['Other']);", true)]
    public async Task Script_Done_co_selection_is_distinct_from_valid_alternative_singletons(string script, bool safe)
    {
        var body = Activity("body", "Elsa.RunJavaScript", ("script", Literal(script)), ("possibleOutcomes", Literal("[\"Done\",\"Other\"]", "List<String>")));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", MatchAnyCase(body))));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode && item.Message.Contains("Done", StringComparison.Ordinal));
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("const emit = typeof complete === 'function' ? complete : (value => value); return emit(null, ['Break']);", false)]
    [InlineData("const root = globalThis; const key = 'complete'; const emit = typeof root[key] === 'function' ? root[key] : (value => value); return emit(null, ['Break']);", false)]
    [InlineData("const build = new Function('return globalThis.complete'); const emit = build() || (value => value); return emit(null, ['Break']);", false)]
    [InlineData("const evaluate = eval; const emit = evaluate('globalThis.complete') || (value => value); return emit(null, ['Break']);", false)]
    [InlineData("const build = (function(){}).constructor; const emit = build('return globalThis.complete')() || (value => value); return emit(null, ['Break']);", false)]
    [InlineData("const key = 'con' + 'structor'; const build = (function(){})[key]; const emit = build('return globalThis.complete')() || (value => value); return emit(null, ['Break']);", false)]
    [InlineData("const proto = Object.getPrototypeOf(function(){}); const build = Object.values(Object.getOwnPropertyDescriptors(proto))[0].value; return build('return globalThis.complete')()(null,['Break']);", false)]
    [InlineData("const build = Reflect.get(function(){}, 'constructor'); const emit = build(\"return typeof complete === 'function' ? complete : (value => value)\")(); return emit(null, ['Break']);", false)]
    [InlineData("const { constructor: build } = Object; const emit = build(\"return typeof complete === 'function' ? complete : (value => value)\")(); return emit(null, ['Break']);", false)]
    [InlineData("const key='con'+'structor'; const { [key]: build } = Object; const emit = build(\"return typeof complete === 'function' ? complete : (value => value)\")(); return emit(null, ['Break']);", false)]
    [InlineData("const O = Object; const proto = O.getPrototypeOf(function(){}); const descriptors = O.getOwnPropertyDescriptors(proto); return Object.values(descriptors)[0].value;", false)]
    [InlineData("const { getPrototypeOf: getProto, getOwnPropertyDescriptors: describe } = Object; return describe(getProto(function(){}));", false)]
    [InlineData("const local = { complete: 42 }; return local.complete;", true)]
    [InlineData("const globalThis = { complete: 42 }; return globalThis.complete;", true)]
    [InlineData("const Reflect = { get: value => value }; return Reflect.get(42);", true)]
    [InlineData("complete: { break complete; } return 42;", true)]
    [InlineData("class Local { complete() { return 42; } } return new Local().complete();", true)]
    [InlineData("const source = { complete: () => 42 }; const { complete: read } = source; return read();", true)]
    [InlineData("const source = { value: 42 }; const { value: answer } = source; return answer;", true)]
    [InlineData("const values = [1,2]; return values[0];", true)]
    [InlineData("const local = { complete: 42 }; return local['complete'];", true)]
    [InlineData("function twice(value) { return value * 2; } return twice(2);", true)]
    [InlineData("return Math.max(1, 2);", true)]
    public async Task Script_native_completion_aliases_cannot_hide_an_unproven_case_boundary(string script, bool safe)
    {
        var body = Activity("body", "Elsa.RunJavaScript", ("script", Literal(script)), ("possibleOutcomes", Literal("[\"Break\"]", "List<String>")));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", MatchAnyCase(body))));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Parallel_consumes_named_Break_but_cannot_repair_a_skipped_Sequence_successor(bool sequence, bool safe)
    {
        var script = Activity("script", "Elsa.RunJavaScript", ("script", Literal("setOutcome('Break');")));
        var branch = sequence ? Sequence("branch", script, Activity("must-run", "Elsa.WriteLine", ("text", Literal("after")))) : script;
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.AdditionalProperties["activities"] = JsonSerializer.SerializeToElement(new[]
        {
            Port(branch), Port(Activity("other", "Elsa.WriteLine", ("text", Literal("other"))))
        });
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", MatchAnyCase(parallel))));
        Assert.Equal(safe, result.Succeeded);
        if (!safe) Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        else Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("expression", Elsa3ActivityDiagnosticCodes.UnsupportedMode)]
    [InlineData("variable", Elsa3ActivityDiagnosticCodes.UnsupportedMode)]
    [InlineData("variable-syntax", Elsa3ActivityDiagnosticCodes.UnsupportedMode)]
    [InlineData("memory", Elsa3ActivityDiagnosticCodes.ValueOutsideContract)]
    public async Task Parallel_cannot_make_an_unsupported_or_unanalyzed_script_body_safe(string form, string expectedCode)
    {
        const string source = "setOutcomes(['Done','Other']);";
        var script = Activity("body", "Elsa.RunJavaScript", ("script", form switch
        {
            "expression" => Expression("JavaScript", JsonSerializer.Serialize(source)),
            "variable" => Expression("JavaScript", "getVariable('code')"),
            "variable-syntax" => JsonSerializer.SerializeToElement(new { typeName = "String",
                expression = new { type = "Variable", value = new { id = "code-memory", name = "code", typeName = "String" } } }),
            _ => Captured("code-memory", "String")
        }));
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.AdditionalProperties["activities"] = JsonSerializer.SerializeToElement(new[] { Port(script),
            Port(Activity("sibling", "Elsa.WriteLine", ("text", Literal("sibling")))) });
        var definition = ConversionServices.Definition(Sequence("root", MatchAnyCase(parallel)), variables:
            [new Elsa3Variable { Id = "code-memory", Name = "code", TypeName = "String", Value = JsonSerializer.Serialize(source) }]);
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == expectedCode && item.Path.Contains("cases[0].activity", StringComparison.Ordinal));
        if (form == "memory")
        {
            var refusal = Assert.Single(result.Diagnostics, item => item.IsError);
            Assert.EndsWith(".script", refusal.Path, StringComparison.Ordinal);
            Assert.Contains("Variable expression", refusal.Guidance, StringComparison.Ordinal);
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        }
    }

    [Theory]
    [InlineData("return 42;")]
    [InlineData("setOutcome('Ordinary');")]
    public async Task Match_any_allows_a_script_with_an_unused_declared_Break_port(string script)
    {
        var body = Activity("body", "Elsa.RunJavaScript", ("script", Literal(script)),
            ("possibleOutcomes", Literal("[\"Break\",\"Ordinary\"]", "List<String>")));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", MatchAnyCase(body))));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n")]
    public async Task Blank_script_keeps_standalone_and_Switch_source_refusal(string script)
    {
        Elsa3Activity Body() => Activity("body", "Elsa.RunJavaScript", ("script", Literal(script)),
            ("possibleOutcomes", Literal("[\"Break\"]", "List<String>")));
        var roots = new[] { Sequence("root", Body()), Sequence("root", MatchAnyCase(Body(), mode: "MatchFirst")),
            Sequence("root", MatchAnyCase(Body())) };
        foreach (var root in roots)
        {
            var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));
            Assert.False(result.Succeeded);
            var refusal = Assert.Single(result.Diagnostics, item => item.IsError);
            Assert.Equal(Elsa3ActivityDiagnosticCodes.ValueOutsideContract, refusal.Code);
            Assert.EndsWith(".script", refusal.Path, StringComparison.Ordinal);
            Assert.Contains("Elsa 3", refusal.Guidance, StringComparison.Ordinal);
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.RequiredInputUnbound);
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        }
    }

    [Theory]
    [InlineData("Literal", "false", true)]
    [InlineData("Literal", "true", false)]
    [InlineData("JavaScript", "Math.random() > 0.5", false)]
    public async Task Nested_outcome_only_Break_is_safe_only_when_proven_unreachable(string language, string condition, bool safe)
    {
        var nested = Activity("nested", "Elsa.Switch", ("mode", Literal("MatchAny")));
        nested.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new[]
        {
            new { label = "Break", condition = new { expression = new { type = language, value = condition } } },
            new { label = "Ordinary", condition = new { expression = new { type = "Literal", value = "true" } } }
        });
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", MatchAnyCase(nested))));
        Assert.Equal(safe, result.Succeeded);
        if (safe) Assert.True(result.Succeeded, Describe(result.Diagnostics));
        else Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
    }

    [Fact]
    public async Task Match_any_control_label_diagnostic_keeps_source_property_casing()
    {
        var node = MatchAnyCase(Activity("body", "Elsa.WriteLine", ("text", Literal("body"))), "Break");
        node.AdditionalProperties["Cases"] = node.AdditionalProperties["cases"];
        node.AdditionalProperties.Remove("cases");
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node)));
        var error = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ActivityDiagnosticCodes.UnsupportedMode);
        Assert.EndsWith(".Cases[0].label", error.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"MatchSome\"")]
    [InlineData("2")]
    public async Task An_unknown_switch_mode_is_unconvertible(string mode)
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", JsonDocument.Parse(mode).RootElement));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[] { new { label = "one" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code != "ELS3-NULL-POLICIES");
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
