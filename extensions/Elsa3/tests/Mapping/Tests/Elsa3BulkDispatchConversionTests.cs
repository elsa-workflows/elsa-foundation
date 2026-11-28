using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1242 US3 and US4: Elsa 3 BulkDispatchWorkflows converts one to one to the Elsa 4 structural activity, whose
/// Dispatch slot holds a generated DispatchWorkflow; what has no faithful meaning is refused with guidance.
/// </summary>
public sealed class Elsa3BulkDispatchConversionTests
{
    private const string BulkKey = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows";
    private const string Child = "acme.fulfil-line";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Targets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [Child] = ["Sku", "Quantity", "Item"] };

    // ── The Elsa 4 node ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_bulk_node_holds_a_generated_dispatch_and_the_branches_with_their_ids()
    {
        var result = await ConvertAsync(Bulk(
            ("childCompleted", Port(Write("done-branch"))),
            ("childFaulted", Port(Write("fault-branch")))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var bulk = Node(result.Value!.State, "bulk");
        var payload = bulk.Structure!.Payload;
        Assert.Equal("bulk.dispatch", payload.GetProperty("dispatch").GetProperty("nodeId").GetString());
        Assert.Equal("done-branch", payload.GetProperty("childCompleted").GetProperty("nodeId").GetString());
        Assert.Equal("fault-branch", payload.GetProperty("childFaulted").GetProperty("nodeId").GetString());
        Assert.Contains("variables.Lines", Source(Input(bulk, "Items")), StringComparison.Ordinal);
        var dispatch = Node(result.Value.State, "bulk.dispatch");
        Assert.Equal(Child, Text(Input(dispatch, "WorkflowDefinitionId")));
        Assert.Equal("true", Text(Input(dispatch, "WaitForCompletion")));
        Assert.Equal("false", Text(Input(dispatch, "CancelChildOnParentCancellation")));
    }

    [Fact]
    public async Task Every_conversion_warns_about_the_cap_window_and_item_inputs()
    {
        var result = await ConvertAsync(Bulk());

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var capped = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchCapped);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, capped.Severity);
        Assert.Contains("1,000", capped.Message, StringComparison.Ordinal);
        Assert.Contains("16", capped.Message, StringComparison.Ordinal);
        Assert.Contains("ForEach", capped.Guidance, StringComparison.Ordinal);
        var inputs = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchItemInputs);
        Assert.Contains("ParentInstanceId", inputs.Message, StringComparison.Ordinal);
        Assert.Contains("cancelled", inputs.Message, StringComparison.Ordinal);
        Assert.Contains("correlation id", inputs.Message, StringComparison.Ordinal);
    }

    // ── WaitForCompletion (FR-013) ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "true")]
    [InlineData("False", "false")]
    [InlineData("true", "true")]
    [InlineData("", "false")]
    public async Task The_wait_reaches_the_dispatch_node_with_Elsa_3_defaults(string? literal, string expected)
    {
        var result = await ConvertAsync(literal is null ? Bulk() : Bulk(("waitForCompletion", Literal(literal, "Boolean"))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(result.Value!.State, "bulk.dispatch"), "WaitForCompletion")));
    }

    [Fact]
    public async Task A_null_wait_reads_as_false()
    {
        var result = await ConvertAsync(Bulk(("waitForCompletion", JsonSerializer.SerializeToElement<object?>(null))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("false", Text(Input(Node(result.Value!.State, "bulk.dispatch"), "WaitForCompletion")));
    }

    [Fact]
    public async Task An_expression_wait_is_carried_and_lowered_on_the_dispatch_node()
    {
        var result = await ConvertAsync(Bulk(("waitForCompletion", Expression("JavaScript", "return getFlag() === true;", "Boolean"))),
            variables: [Variable("Flag")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains("variables.Flag", Source(Input(Node(result.Value!.State, "bulk.dispatch"), "WaitForCompletion")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_literal_wait_that_is_not_a_boolean_is_refused()
    {
        var result = await ConvertAsync(Bulk(("waitForCompletion", Literal("yes", "Boolean"))));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, guidance: "to true or false before converting", "is not true or false");
    }

    // ── Branches (FR-018) ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Branches_of_a_bulk_dispatch_that_never_waits_are_removed_with_a_warning()
    {
        var result = await ConvertAsync(Bulk(
            ("waitForCompletion", Literal("False", "Boolean")),
            ("childCompleted", Port(Write("done-branch"))),
            ("childFaulted", Port(Write("fault-branch")))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved));
        var payload = Node(result.Value!.State, "bulk").Structure!.Payload;
        Assert.True(payload.GetProperty("childCompleted").ValueKind == JsonValueKind.Null);
        Assert.True(payload.GetProperty("childFaulted").ValueKind == JsonValueKind.Null);
    }

    // ── Items (FR-014) ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_literal_items_value_is_refused_because_Elsa_3_dispatched_nothing()
    {
        var result = await ConvertAsync(Bulk(("items", Literal("[1,2,3]", "Object"))));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, guidance: "Bind 'items' to an expression that returns the collection", "raw JSON text", "dispatched no child");
    }

    [Fact]
    public async Task Null_items_stay_unbound()
    {
        var result = await ConvertAsync(Bulk(("items", JsonSerializer.SerializeToElement<object?>(null))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(Node(result.Value!.State, "bulk").Inputs, input => input.ReferenceKey == "Items");
    }

    // ── Refusals (FR-012, FR-016, FR-019) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_expression_target_is_refused()
    {
        var result = await ConvertAsync(Bulk(("workflowDefinitionId", Expression("JavaScript", "return 'x';"))));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, guidance: "to the literal id of the workflow", "must be a literal");
    }

    [Theory]
    [InlineData("channelName", "\"fast\"", "String")]
    [InlineData("startNewTrace", "true", "Boolean")]
    public async Task A_bound_channel_or_trace_is_refused(string member, string literal, string type)
    {
        var result = await ConvertAsync(Bulk((member, Literal(JsonSerializer.Deserialize<JsonElement>(literal).ToString(), type))));

        var error = AssertRefused(result, Elsa3ActivityDiagnosticCodes.RemovedInputBound, guidance: "before converting", member);
        Assert.Contains("clear", error.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("before converting.", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_channel_bound_to_an_expression_is_refused()
    {
        var result = await ConvertAsync(Bulk(("channelName", Expression("JavaScript", "return getChannel();"))));

        var error = AssertRefused(result, Elsa3ActivityDiagnosticCodes.RemovedInputBound, guidance: "Clear the input before converting", "channelName", "expression");
        Assert.Contains("clear", error.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("before converting.", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_channel_and_a_false_trace_convert()
    {
        var result = await ConvertAsync(Bulk(("channelName", Literal("")), ("startNewTrace", Literal("false", "Boolean"))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_target_in_neither_the_import_nor_Elsa_4_is_refused()
    {
        var result = await ConvertAsync(Bulk(), targets: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

        var error = AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchTargetUnknown, guidance: "Import the target workflow first");
        Assert.Contains(Child, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expression_item_key_is_refused()
    {
        var result = await ConvertAsync(Bulk(("defaultItemInputKey", Expression("JavaScript", "return 'Item';"))));

        var error = AssertRefused(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, guidance: "to a literal input name", "literal key");
        AssertItemKeyGuidance(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_literal_item_key_is_refused(string key)
    {
        var result = await ConvertAsync(Bulk(("defaultItemInputKey", Literal(key))));

        var error = AssertRefused(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, guidance: "to a literal input name", "blank");
        AssertItemKeyGuidance(error);
    }

    [Theory]
    [InlineData("JavaScript", "return { Sku: 'x' };")]
    [InlineData("Liquid", "{{ x }}")]
    public async Task An_expression_static_input_is_refused(string language, string expression)
    {
        var result = await ConvertAsync(Bulk(("input", Expression(language, expression, "ObjectDictionary"))));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, guidance: "Put those values in the items as members, or use a literal map", "expression");
    }

    [Fact]
    public async Task A_static_input_naming_an_undeclared_input_is_refused()
    {
        var result = await ConvertAsync(Bulk(("input", Literal("""{"Debug":true}""", "ObjectDictionary"))));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared, guidance: "It declares 'Sku'");
    }

    [Theory]
    [InlineData("getChildInstanceId()")]
    [InlineData("getVariable('ChildInstanceId')")]
    [InlineData("getInput('WorkflowOutput')")]
    [InlineData("getWorkflowStatus()")]
    [InlineData("getInput('WorkflowSubStatus')")]
    [InlineData("getInput('WorkflowInstanceId')")]
    public async Task A_branch_reading_the_childs_result_is_refused(string read)
    {
        var branch = Activity("done-branch", "Elsa.WriteLine", ("text", Expression("JavaScript", $"return {read};")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(branch))));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchChildResultRead, guidance: "the child's result is not available in the branch yet");
    }

    [Fact]
    public async Task A_branch_read_of_ChildInstanceId_is_refused_even_when_the_parent_declares_that_variable()
    {
        // Elsa 3 put ChildInstanceId into the branch's own scope, so the branch never read the parent's variable.
        var branch = Activity("done-branch", "Elsa.WriteLine", ("text", Expression("JavaScript", "return getVariable('ChildInstanceId');")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(branch))), variables: [Variable("ChildInstanceId")]);

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchChildResultRead, guidance: "the child's result is not available in the branch yet");
    }

    [Fact]
    public async Task A_branch_read_of_WorkflowStatus_is_refused_even_when_the_parent_declares_that_input()
    {
        var branch = Activity("done-branch", "Elsa.WriteLine", ("text", Expression("JavaScript", "return getInput('WorkflowStatus');")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(branch))),
            inputs: [new Elsa3WorkflowArgumentDefinition { Name = "WorkflowStatus", Type = "String" }]);

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchChildResultRead, guidance: "the child's result is not available in the branch yet");
    }

    [Fact]
    public async Task The_same_read_outside_a_branch_keeps_its_ordinary_refusal()
    {
        var result = await ConvertAsync(Sequence("root",
            Activity("outside", "Elsa.WriteLine", ("text", Expression("JavaScript", "return getChildInstanceId();"))),
            Activity("bulk", "Elsa.BulkDispatchWorkflows", BulkMembers().ToArray())));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchChildResultRead);
    }

    // ── Outcomes (FR-017) ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_corpus_routes_into_one_WaitAny_join_leave_one_connection()
    {
        var result = await ConvertFlowchartAsync("WaitAny", ["Done", "Canceled", "Completed"]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(["bulk:Done->after"], Elsa3FlowchartFixtures.Connections(Node(result.Value!.State, "chart")));
        var dead = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved);
        Assert.Contains("'Canceled'", dead.Message, StringComparison.Ordinal);
        Assert.Equal("Remove the connection from the Elsa 3 workflow.", dead.Guidance);
    }

    [Fact]
    public async Task Completed_next_to_Done_into_a_WaitAll_join_is_refused()
    {
        var result = await ConvertFlowchartAsync("WaitAll", ["Done", "Completed"]);

        var error = AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchOutcomeRoute, guidance: "Connect the follow-up to 'Done'");
        Assert.Contains("WaitAll", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canceled_into_a_WaitAll_join_is_refused()
    {
        var result = await ConvertFlowchartAsync("WaitAll", ["Done", "Canceled"]);

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchOutcomeRoute, guidance: "Connect the follow-up to 'Done'", "WaitAll");
    }

    [Fact]
    public async Task Completed_alone_is_refused_when_the_bulk_dispatch_waits()
    {
        var result = await ConvertFlowchartAsync(null, ["Completed"]);

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchOutcomeRoute, guidance: "Connect the follow-up to 'Done'");
    }

    [Fact]
    public async Task Completed_of_a_bulk_dispatch_that_never_waits_is_removed_with_a_warning()
    {
        var result = await ConvertFlowchartAsync(null, ["Done", "Completed"], ("waitForCompletion", Literal("false", "Boolean")));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(["bulk:Done->after"], Elsa3FlowchartFixtures.Connections(Node(result.Value!.State, "chart")));
        var dead = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved &&
                                                                   diagnostic.Message.Contains("'Completed'", StringComparison.Ordinal));
        Assert.Equal("Remove the connection from the Elsa 3 workflow.", dead.Guidance);
    }

    [Fact]
    public async Task Completed_of_a_bulk_dispatch_that_never_waits_into_a_WaitAll_join_is_refused()
    {
        // Elsa 3 never followed this edge; dropping it would let the WaitAll join run where Elsa 3 never ran it.
        var result = await ConvertFlowchartAsync("WaitAll", ["Done", "Completed"], ("waitForCompletion", Literal("false", "Boolean")));

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.BulkDispatchOutcomeRoute, guidance: "Connect the follow-up to 'Done'", "WaitAll");
    }

    // ── Correlation function (FR-015) ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("return 'order-17' + getItem().correlationIdSuffix;")]
    [InlineData("return 'order-17' + args.item.correlationIdSuffix;")]
    public async Task The_correlation_function_reads_the_item_as_currentItem(string script)
    {
        var result = await ConvertAsync(Bulk(("correlationIdFunction", Expression("JavaScript", script))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var expression = ReadExpression(Input(Node(result.Value!.State, "bulk.dispatch"), "CorrelationId"));
        Assert.Contains("variables.currentItem.correlationIdSuffix", expression.Source, StringComparison.Ordinal);
        var binding = Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters[ExpressionParameterSurfaces.VariablesKey("currentItem")]);
        Assert.Equal("bulk", binding.DeclaringScopeNodeId);
        Assert.Equal("currentItem", binding.VariableKey);
    }

    [Fact]
    public async Task A_custom_item_key_names_the_accessor()
    {
        var result = await ConvertAsync(Bulk(
            ("defaultItemInputKey", Literal("Line")),
            ("correlationIdFunction", Expression("JavaScript", "return getLine().id;"))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains("variables.currentItem.id", Source(Input(Node(result.Value!.State, "bulk.dispatch"), "CorrelationId")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_variable_named_like_the_accessor_wins_in_the_correlation_function()
    {
        // Elsa 3's EvaluatingJavaScript handler overwrote the item's accessor with the variable's.
        var result = await ConvertAsync(Bulk(("correlationIdFunction", Expression("JavaScript", "return getItem();"))),
            variables: [Variable("Item")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var source = Source(Input(Node(result.Value!.State, "bulk.dispatch"), "CorrelationId"));
        Assert.Contains("variables.Item", source, StringComparison.Ordinal);
        Assert.DoesNotContain("currentItem", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_target_id_is_carried_trimmed_as_it_was_looked_up()
    {
        var result = await ConvertAsync(Bulk(("workflowDefinitionId", Literal($"  {Child} "))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(Child, Text(Input(Node(result.Value!.State, "bulk.dispatch"), "WorkflowDefinitionId")));
    }

    [Fact]
    public async Task A_target_declaring_inputs_that_differ_only_in_letter_case_is_refused()
    {
        var targets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [Child] = ["Sku", "Item", "item"] };

        var result = await ConvertAsync(Bulk(), targets);

        AssertRefused(result, Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous, guidance: "Rename the inputs", "differ only in letter case");
    }

    [Fact]
    public async Task A_variable_named_like_the_item_key_does_not_change_the_item_in_Inputs()
    {
        // Elsa 3 merged the item in C#, never through a getter, so the item wins in Inputs whatever the workflow declares.
        var result = await ConvertAsync(Bulk(), variables: [Variable("Item")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var inputs = ReadExpression(Input(Node(result.Value!.State, "bulk.dispatch"), "Inputs"));
        Assert.Contains("variables.currentItem", inputs.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("variables.Item", inputs.Source, StringComparison.Ordinal);
        Assert.Equal("""{"Item":7}""", (await EvaluateInputsAsync(inputs, "7")).GetRawText());
    }

    [Fact]
    public async Task An_absent_correlation_function_leaves_the_correlation_id_unbound()
    {
        var result = await ConvertAsync(Bulk());

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(Node(result.Value!.State, "bulk.dispatch").Inputs, input => input.ReferenceKey == "CorrelationId");
    }

    [Fact]
    public async Task The_item_accessor_is_unknown_outside_the_correlation_function()
    {
        var branch = Activity("done-branch", "Elsa.WriteLine", ("text", Expression("JavaScript", "return getItem();")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(branch))));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_workflow_variable_named_currentItem_read_in_a_branch_is_refused()
    {
        var branch = Activity("done-branch", "Elsa.WriteLine", ("text", Expression("JavaScript", "return getVariable('currentItem');")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(branch))), variables: [Variable("currentItem")]);

        AssertRefused(result, Elsa3ImportDiagnosticCodes.ResolutionDiffers, guidance: "Rename one of the two variables");
    }

    // ── Per-item inputs (FR-016) ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Map_items_pass_the_declared_inputs_in_the_declared_spelling()
    {
        var inputs = await InputsAsync(Bulk());

        var passed = await EvaluateInputsAsync(inputs, """{"sku":"A-1","QUANTITY":2,"debug":true}""");

        Assert.Equal("""{"Sku":"A-1","Quantity":2}""", passed.GetRawText());
    }

    [Fact]
    public async Task Two_members_that_differ_only_in_case_fault_the_pass()
    {
        var inputs = await InputsAsync(Bulk());

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => EvaluateInputsAsync(inputs, """{"sku":"A-1","SKU":"A-2"}"""));
        Assert.Contains("differ only in letter case", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Item", "7", """{"Item":7}""")]
    [InlineData("item", "\"x\"", """{"Item":"x"}""")]
    [InlineData("Line", "7", "{}")]
    public async Task Any_other_item_passes_under_the_declared_item_key(string key, string item, string expected)
    {
        var inputs = await InputsAsync(Bulk(("defaultItemInputKey", Literal(key))));

        Assert.Equal(expected, (await EvaluateInputsAsync(inputs, item)).GetRawText());
    }

    [Fact]
    public async Task A_static_input_is_embedded_and_the_item_wins_a_clash()
    {
        var inputs = await InputsAsync(Bulk(("input", Literal("""{"quantity":1,"Sku":"default"}""", "ObjectDictionary"))));

        Assert.Equal("""{"Quantity":1,"Sku":"A-1"}""", (await EvaluateInputsAsync(inputs, """{"Sku":"A-1"}""")).GetRawText());
        Assert.Equal("""{"Quantity":1,"Sku":"default","Item":7}""", (await EvaluateInputsAsync(inputs, "7")).GetRawText());
    }

    [Fact]
    public async Task The_inputs_read_the_bulk_nodes_currentItem_and_declare_the_inputs_type()
    {
        var inputs = await InputsAsync(Bulk());

        var binding = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(inputs.Parameters).Value);
        Assert.Equal("bulk", binding.DeclaringScopeNodeId);
        Assert.Equal("currentItem", binding.VariableKey);
        Assert.DoesNotContain("item[", inputs.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ParentInstanceId", inputs.Source, StringComparison.Ordinal);
    }

    // ── Outer writes (FR-020a) ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_branch_writing_a_workflow_variable_warns_and_converts()
    {
        var branch = Activity("count", "Elsa.SetVariable",
            ("variable", JsonSerializer.SerializeToElement(new { id = "id-Total", name = "Total", typeName = "Object" })),
            ("value", Literal("1", "Object")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(branch))), variables: [Variable("Total")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'id-Total'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("child workflow", warning.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_branch_flowchart_writing_its_own_variable_does_not_warn()
    {
        var set = Activity("count", "Elsa.SetVariable",
            ("variable", JsonSerializer.SerializeToElement(new { id = "local", name = "local", typeName = "Object" })),
            ("value", Literal("1", "Object")));
        var chart = Flowchart("branch-chart", set);
        chart.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "Object" } });

        var result = await ConvertAsync(Bulk(("childCompleted", Port(chart))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite);
    }

    [Fact]
    public async Task A_branch_script_writing_a_workflow_variable_warns()
    {
        var script = Activity("tally", "Elsa.RunJavaScript", ("script", Literal("setVariable('Total', 1);")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(script))), variables: [Variable("Total")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite);
        Assert.Contains("variables from its script", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_branch_script_whose_body_is_an_expression_warns_because_its_writes_cannot_be_read()
    {
        // The body is computed, so the converter cannot see what the script writes and warns conservatively (FR-020a).
        var script = Activity("tally", "Elsa.RunJavaScript", ("script", Expression("JavaScript", "'return 1;'")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(script))), variables: [Variable("Total")]);

        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite);
        Assert.Contains("variables from its script", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_branch_script_that_writes_no_variable_does_not_warn()
    {
        var script = Activity("tally", "Elsa.RunJavaScript", ("script", Literal("return getLines().length;")));

        var result = await ConvertAsync(Bulk(("childCompleted", Port(script))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite);
    }

    [Fact]
    public async Task A_branch_output_captured_into_a_workflow_variable_warns()
    {
        var script = Activity("calc", "Elsa.RunJavaScript", ("script", Literal("return 1;")), ("result", Captured("id-Total")));

        var result = await ConvertAsync(Bulk(("childFaulted", Port(script))), variables: [Variable("Total")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite);
        Assert.Contains("into a workflow variable", warning.Message, StringComparison.Ordinal);
    }

    // ── Publish validation ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_converted_bulk_dispatch_passes_value_flow_validation()
    {
        // The Dispatch node's Inputs and CorrelationId read the bulk node's currentItem, which its structure handler
        // projects, and the branches read a variable of the workflow.
        var branch = Activity("done-branch", "Elsa.WriteLine", ("text", Expression("JavaScript", "return 'done ' + getLines();")));
        var result = await ConvertAsync(Bulk(
            ("correlationIdFunction", Expression("JavaScript", "return 'order-17' + getItem().suffix;")),
            ("childCompleted", Port(branch))));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
        new Elsa.Activities.DispatchWorkflow.Design.DispatchWorkflowDesignFeature().ConfigureServices(services);
        new Elsa.Events.EventsFeature().ConfigureServices(services);
        new Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature().ConfigureServices(services);
        services.AddSingleton<Elsa.Activities.Design.Core.Contracts.IActivityDefinitionLookup>(RealActivityCatalog.Create());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var validator = ActivatorUtilities.CreateInstance<Elsa.Workflows.Design.Validations.Validators.ValueFlowValidator>(scope.ServiceProvider);

        var errors = (await validator.Validate(new Draft(result.Value!.State), CancellationToken.None)).ToArray();

        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => $"{error.Type} at {error.Path}: {error.Message}")));
    }

    private sealed class Draft(WorkflowDefinitionState state) : Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }

    // ── Target declarations (FR-016, Q2) ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_collection_import_reads_the_targets_declared_inputs_from_the_same_import()
    {
        var child = ReusableActivityImportFixtures.Workflow(Child, $"{Child}-v1", 1, false, ReusableActivityImportFixtures.Leaf("c1"),
            new Elsa3WorkflowArgumentDefinition { Name = "Sku", Type = "Object" });
        var caller = ReusableActivityImportFixtures.Workflow("acme.order-received", "acme.order-received-v1", 1, false, CallerBulk());
        var command = new ReusableActivityImportFixtures.CapturingCommand();

        await ApplyAsync(command, child, caller);

        var stored = command.Mutation!.Workflows.Single(workflow => workflow.Version.Id == "acme.order-received-v1").Version.State;
        var inputs = ReadExpression(Input(Node(stored, "bulk.dispatch"), "Inputs"));
        Assert.Contains("[\"Sku\"]", inputs.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_collection_import_refuses_a_target_it_cannot_find()
    {
        var caller = ReusableActivityImportFixtures.Workflow("acme.order-received", "acme.order-received-v1", 1, false, CallerBulk());

        var refusal = await Assert.ThrowsAsync<ReusableActivityImportValidationException>(() =>
            ApplyAsync(new ReusableActivityImportFixtures.CapturingCommand(), caller));

        Assert.Single(refusal.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BulkDispatchTargetUnknown);
    }

    [Fact]
    public async Task Targets_outside_the_import_are_read_from_Elsa_4_and_nested_bulk_dispatches_are_found()
    {
        var nested = Activity("inner", "Elsa.BulkDispatchWorkflows", ("workflowDefinitionId", Literal("acme.published")));
        var outer = Activity("outer", "Elsa.BulkDispatchWorkflows", ("workflowDefinitionId", Literal(Child)), ("childCompleted", Port(nested)));
        var definition = ConversionServices.Definition(Sequence("root", outer, Activity("other", "Elsa.BulkDispatchWorkflows", ("workflowDefinitionId", Literal("acme.missing")))));
        var asked = new List<string>();

        var inputs = await Elsa3.Mapping.Services.Elsa3ReusableActivityImportMaterializer.BulkDispatchTargetInputsAsync(
            [definition],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [Child] = ["Sku"] },
            (target, _) =>
            {
                asked.Add(target);
                return ValueTask.FromResult<IReadOnlyList<string>?>(target == "acme.published" ? ["OrderId"] : null);
            },
            CancellationToken.None);

        Assert.Equal(["Sku"], inputs[Child]);
        Assert.Equal(["OrderId"], inputs["acme.published"]);
        Assert.False(inputs.ContainsKey("acme.missing"));
        Assert.Equal(["acme.missing", "acme.published"], asked);
    }

    [Fact]
    public void A_published_target_is_read_only_from_the_import_tenant()
    {
        static Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableSourceReference Reference(string id, string? tenant, int day) =>
            new(id, $"artifact-{id}", "workflow-definition", "acme.child", "1", "acme.child", "acme.child-v1", "1.0.0",
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(day),
                Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableReferenceScope.Published, TenantId: tenant);
        Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableSourceReference[] published =
            [Reference("tenant-a", "tenant-a", 1), Reference("tenant-b", "tenant-b", 2), Reference("global", null, 3)];

        var select = Elsa3.Mapping.Services.Elsa3ReusableActivityImportMaterializer.SelectPublishedTarget;

        Assert.Equal("tenant-a", select(published, "tenant-a", "acme.child")?.SourceReferenceId);
        Assert.Equal("global", select(published, null, "acme.child")?.SourceReferenceId);
        Assert.Null(select(published, "tenant-c", "acme.child"));
        Assert.Null(select(published, "tenant-a", "acme.other"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Elsa3Activity Bulk(params (string Name, JsonElement Value)[] overrides)
    {
        var members = BulkMembers().ToDictionary(member => member.Name, member => member.Value, StringComparer.Ordinal);
        foreach (var (name, value) in overrides)
            members[name] = value;
        return Sequence("root", Activity("bulk", "Elsa.BulkDispatchWorkflows", members.Select(member => (member.Key, member.Value)).ToArray()));
    }

    private static IEnumerable<(string Name, JsonElement Value)> BulkMembers() =>
    [
        ("workflowDefinitionId", Literal(Child)),
        ("items", Expression("JavaScript", "return getLines();", "Object")),
        ("defaultItemInputKey", Literal("Item"))
    ];

    private static Elsa3Activity CallerBulk() =>
        Sequence("root", Activity("bulk", "Elsa.BulkDispatchWorkflows",
            ("workflowDefinitionId", Literal(Child)),
            ("items", Expression("JavaScript", "return [{ sku: 'A-1' }];", "Object"))));

    private static async Task ApplyAsync(ReusableActivityImportFixtures.CapturingCommand command, params Elsa3WorkflowDefinition[] definitions)
    {
        await using var services = ReusableActivityImportFixtures.ImportServices(RealActivityCatalog.Create(), command);
        await using var scope = services.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionImporter>();
        var collection = ReusableActivityImportFixtures.Collection(definitions);
        var plan = await importer.AnalyzeAsync(collection);
        await importer.ApplyAsync(new(plan.PlanId, collection, definitions.Select(definition => definition.Id).ToArray()));
    }

    private static Elsa3Variable Variable(string name) => new() { Id = $"id-{name}", Name = name, TypeName = "Object" };

    private static async Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3Activity root,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? targets = null,
        Elsa3Variable[]? variables = null,
        Elsa3WorkflowArgumentDefinition[]? inputs = null)
    {
        var definition = ConversionServices.Definition(root, variables: [Variable("Lines"), .. variables ?? []]);
        definition.Inputs = (inputs ?? []).ToList();
        return await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, bulkDispatchTargetInputs: targets ?? Targets);
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertFlowchartAsync(
        string? joinMode, string[] ports, params (string Name, JsonElement Value)[] overrides)
    {
        var bulk = Bulk(overrides).Activities![0];
        var target = joinMode is null ? Write("after") : Activity("join", "Elsa.FlowJoin", ("mode", Literal(joinMode)));
        var children = joinMode is null ? new[] { bulk, target } : [bulk, target, Write("after")];
        var chart = Flowchart("chart", children);
        chart.Connections = ports.Select(port => Connect("bulk", port, target.NodeId)).ToList();
        if (joinMode is not null)
            chart.Connections.Add(Connect("join", "Done", "after"));
        return ConvertAsync(chart);
    }

    private static async Task<ExpressionDefinition> InputsAsync(Elsa3Activity root)
    {
        var result = await ConvertAsync(root);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return ReadExpression(Input(Node(result.Value!.State, "bulk.dispatch"), "Inputs"));
    }

    private static Task<JsonElement> EvaluateInputsAsync(ExpressionDefinition inputs, string itemJson) =>
        PortableJavaScript.EvaluateAsync(inputs, new Dictionary<string, JsonElement> { ["currentItem"] = JsonDocument.Parse(itemJson).RootElement.Clone() });

    private static ExpressionDefinition ReadExpression(ArgumentState argument) =>
        (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
        .Deserialize<ExpressionDefinition>(Elsa.Serialization.Core.JsonBaseline.Options)!;

    // FR-019: an item key the conversion cannot use points to the items or a literal map.
    private static void AssertItemKeyGuidance(Elsa3MigrationDiagnostic error)
    {
        Assert.Contains("put them in the items as members", error.Guidance, StringComparison.Ordinal);
        Assert.Contains("literal 'input' map", error.Guidance, StringComparison.Ordinal);
    }

    // SC-005: each refusal carries its code and guidance; the guidance is checked on its own, not through the message.
    private static Elsa3MigrationDiagnostic AssertRefused(
        Elsa3MigrationResult<Elsa3ConvertedDefinition> result, string code, string guidance, params string[] message)
    {
        Assert.False(result.Succeeded, "The conversion succeeded.");
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Error, error.Severity);
        Assert.Contains(guidance, error.Guidance, StringComparison.Ordinal);
        foreach (var text in message)
            Assert.Contains(text, error.Message, StringComparison.Ordinal);
        return error;
    }
}
