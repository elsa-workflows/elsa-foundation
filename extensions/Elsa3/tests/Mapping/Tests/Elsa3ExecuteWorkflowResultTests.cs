using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1245 (FR-006 to FR-010): an awaited Elsa 3 Dispatch Workflow's Result becomes the Elsa 4 Output capture; an Execute
/// Workflow always waits, its Result converts and every read of the captured variable is rewritten to the Elsa 4 value it
/// stood for; Done routes the way Elsa 3 continued.
/// </summary>
public sealed class Elsa3ExecuteWorkflowResultTests
{
    private static readonly Elsa3Variable Credit = new() { Id = "credit", Name = "Credit", TypeName = "Object" };

    // ---- FR-006: Dispatch Workflow Result → Output ----

    [Theory]
    [InlineData("literal-true")]
    [InlineData("literal-false")]
    [InlineData("bound")]
    public async Task A_dispatch_result_capture_becomes_an_output_capture_into_the_same_variable(string wait)
    {
        var waitValue = wait switch
        {
            "literal-true" => Literal("true", "Boolean"),
            "literal-false" => Literal("false", "Boolean"),
            _ => Expression("JavaScript", "1 > 0", "Boolean")
        };
        var dispatch = Activity("dispatch", "Elsa.DispatchWorkflow", ("workflowDefinitionId", Literal("child")),
            ("waitForCompletion", waitValue), ("result", Captured("credit")));

        var result = await ConvertAsync(Sequence("root", dispatch), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
        AssertCapturesOutput(Node(result.Value!.State, "dispatch"));
    }

    [Fact]
    public async Task A_dispatch_result_read_reads_the_variable_unchanged()
    {
        var dispatch = Activity("dispatch", "Elsa.DispatchWorkflow", ("workflowDefinitionId", Literal("child")),
            ("waitForCompletion", Literal("true", "Boolean")), ("result", Captured("credit")));

        var result = await ConvertAsync(Sequence("root", dispatch, Write("getCredit().Normalized")), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("variables.Credit.Normalized", ReadExpression(result).Source);
    }

    // ---- FR-007, FR-009: Done ----

    [Theory]
    [InlineData("Elsa.DispatchWorkflow", new[] { "Dispatched", "Completed", "Faulted" })]
    [InlineData("Elsa.ExecuteWorkflow", new[] { "Completed", "Faulted" })]
    public async Task Done_routes_to_the_outcomes_elsa3_continued_on(string type, string[] outcomes)
    {
        var chart = Flowchart("root", Activity("dispatch", type, ("workflowDefinitionId", Literal("child"))),
            Activity("after", "Elsa.WriteLine", ("text", Literal("after"))));
        chart.Connections = [Connect("dispatch", "Done", "after")];

        var result = await ConvertAsync(chart);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var connections = Connections(result.Value!.State.RootActivity!);
        Assert.Equal(outcomes.Select(outcome => $"dispatch:{outcome}->after").Order(StringComparer.Ordinal),
            connections.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(connections, connection => connection.Contains(":Cancelled->", StringComparison.Ordinal));
    }

    // ---- FR-008: Execute Workflow always waits ----

    [Theory]
    [InlineData("unset", true)]
    [InlineData("literal-true", false)]
    [InlineData("literal-false", true)]
    [InlineData("bound", true)]
    [InlineData("null", true)]
    public async Task Execute_workflow_always_waits_and_warns_once(string wait, bool suspendedSentence)
    {
        var execute = Activity("execute", "Elsa.ExecuteWorkflow", ("workflowDefinitionId", Literal("child")));
        switch (wait)
        {
            case "literal-true": execute.AdditionalProperties["waitForCompletion"] = Literal("true", "Boolean"); break;
            case "literal-false": execute.AdditionalProperties["waitForCompletion"] = Literal("false", "Boolean"); break;
            case "bound": execute.AdditionalProperties["waitForCompletion"] = Expression("JavaScript", "1 > 0", "Boolean"); break;
            case "null": execute.AdditionalProperties["waitForCompletion"] = JsonSerializer.SerializeToElement<object?>(null); break;
        }

        var result = await ConvertAsync(Sequence("root", execute));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("true", Text(Input(Node(result.Value!.State, "execute"), "WaitForCompletion")));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ExecuteWorkflowDurable);
        Assert.Equal(suspendedSentence, warning.Message.Contains("Elsa 4 waits for a child that suspends", StringComparison.Ordinal));
    }

    // ---- FR-010: rows ----

    [Theory]
    [InlineData("getCredit().Output.Decision", "variables.Credit.Decision")]
    [InlineData("getCredit().Output", "variables.Credit")]
    [InlineData("variables.Credit.Output.Decision", "variables.Credit.Decision")]
    [InlineData("getVariable('Credit').Output.Decision", "variables.Credit.Decision")]
    [InlineData("getCredit()['Output'].Decision", "variables.Credit.Decision")]
    [InlineData("getCredit()?.Output?.Decision", "variables.Credit?.Decision")]
    [InlineData("getCredit().Status", "(\"Finished\")")]
    [InlineData("getCredit().CorrelationId", "(null)")]
    [InlineData("getCredit().WorkflowDefinitionVersionId", "(null)")]
    [InlineData("getCredit().WorkflowInstanceId", "args.Credit_ChildWorkflowExecutionId")]
    public async Task A_javascript_read_becomes_the_elsa4_value_it_stood_for(string read, string expected)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Write(read)), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, ReadExpression(result).Source);
        AssertCapturesOutput(Node(result.Value!.State, "execute"));
    }

    [Theory]
    [InlineData("getCredit().WorkflowInstanceId", "Credit_ChildWorkflowExecutionId", "ChildWorkflowExecutionId")]
    [InlineData("getCredit().SubStatus", "Credit_Status", "Status")]
    public async Task A_node_output_read_binds_to_the_capturing_node(string read, string parameter, string output)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Write(read)), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var binding = Assert.IsType<ActivityResultExpressionParameterBinding>(ReadExpression(result).Parameters[parameter]);
        Assert.Equal(("execute", output), (binding.ProducerNodeId, binding.ProjectionKey));
    }

    [Theory]
    [InlineData("Completed", "Finished")]
    [InlineData("Faulted", "Faulted")]
    [InlineData("DispatchFailed", "Faulted")]
    [InlineData("Cancelled", "Cancelled")]
    public async Task SubStatus_maps_the_dispatch_status(string status, string expected)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Write("getCredit().SubStatus")), Credit);

        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["Credit_Status"] = JsonSerializer.SerializeToElement(status)
        });

        Assert.Equal(expected, value.GetString());
    }

    [Theory]
    [InlineData("Completed", "{\"Decision\":\"approved\"}", "Finished")]
    [InlineData("Faulted", "{}", "Faulted")]
    public async Task The_whole_value_is_rebuilt_with_elsa3s_keys(string status, string output, string subStatus)
    {
        // US3 scenario 4: toJson(getCredit()) after a child that completed or faulted.
        var result = await ConvertAsync(Sequence("root", Execute(), Write("JSON.stringify(getCredit())")), Credit);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));

        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["Credit"] = JsonDocument.Parse(output).RootElement,
            ["Credit_ChildWorkflowExecutionId"] = JsonSerializer.SerializeToElement("child-1"),
            ["Credit_Status"] = JsonSerializer.SerializeToElement(status)
        });

        using var document = JsonDocument.Parse(value.GetString()!);
        var root = document.RootElement;
        Assert.Equal(["WorkflowDefinitionVersionId", "WorkflowInstanceId", "CorrelationId", "Status", "SubStatus", "Output"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("WorkflowDefinitionVersionId").ValueKind);
        Assert.Equal("child-1", root.GetProperty("WorkflowInstanceId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("CorrelationId").ValueKind);
        Assert.Equal("Finished", root.GetProperty("Status").GetString());
        Assert.Equal(subStatus, root.GetProperty("SubStatus").GetString());
        Assert.Equal(output, root.GetProperty("Output").GetRawText());
    }

    [Fact]
    public async Task The_whole_value_through_a_literal_getVariable_is_rebuilt()
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Write("getVariable('Credit')")), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.StartsWith("({ WorkflowDefinitionVersionId: null, WorkflowInstanceId: args.Credit_ChildWorkflowExecutionId", ReadExpression(result).Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_javascript_body_binds_the_node_outputs_through_its_arguments()
    {
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("return getCredit().WorkflowInstanceId;")));

        var result = await ConvertAsync(Sequence("root", Execute(), script), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var arguments = ExpressionOf(Input(Node(result.Value!.State, "node"), "arguments"));
        var binding = Assert.IsType<ActivityResultExpressionParameterBinding>(arguments.Parameters["Credit_ChildWorkflowExecutionId"]);
        Assert.Equal("execute", binding.ProducerNodeId);
    }

    [Fact]
    public async Task A_direct_binding_of_the_variable_becomes_an_expression_that_rebuilds_the_value()
    {
        var publish = Activity("publish", "Elsa.PublishEvent", ("eventName", Literal("e")), ("payload", Captured("credit")));

        var result = await ConvertAsync(Sequence("root", Execute(), publish), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var payload = Input(Node(result.Value!.State, "publish"), "Payload");
        Assert.Equal("JavaScript", payload.Value.ExpressionType);
        Assert.Contains("SubStatus:", ExpressionOf(payload).Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{{ Variables.Credit.Output.Decision }}", "{{ variables.Credit.Decision }}")]
    [InlineData("{{ Variables.Credit.Output }}", "{{ variables.Credit }}")]
    public async Task A_liquid_output_read_becomes_the_variable(string read, string expected)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Activity("node", "Elsa.WriteLine", ("text", Expression("Liquid", read)))), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, ReadExpression(result).Source);
    }

    [Fact]
    public async Task Two_result_captures_bind_each_read_to_its_own_node()
    {
        var address = new Elsa3Variable { Id = "address", Name = "Address", TypeName = "Object" };
        var dispatch = Activity("dispatch", "Elsa.DispatchWorkflow", ("workflowDefinitionId", Literal("child")),
            ("waitForCompletion", Literal("true", "Boolean")), ("result", Captured("address")));

        var result = await ConvertAsync(
            Sequence("root", Execute(), dispatch, Write("getCredit().WorkflowInstanceId + getAddress().Normalized")), Credit, address);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var expression = ReadExpression(result);
        Assert.Equal("args.Credit_ChildWorkflowExecutionId + variables.Address.Normalized", expression.Source);
        Assert.Equal("execute", Assert.IsType<ActivityResultExpressionParameterBinding>(expression.Parameters["Credit_ChildWorkflowExecutionId"]).ProducerNodeId);
    }

    // ---- FR-010: refusals ----

    [Theory]
    [InlineData("capture")]
    [InlineData("set-variable")]
    [InlineData("initial-value")]
    [InlineData("setter")]
    [InlineData("setVariable")]
    [InlineData("member-assignment")]
    [InlineData("computed-write")]
    public async Task Another_writer_of_the_variable_is_refused_and_named(string writer)
    {
        var variable = new Elsa3Variable { Id = Credit.Id, Name = Credit.Name, TypeName = Credit.TypeName };
        Elsa3Activity other = writer switch
        {
            "capture" => Activity("other", "Elsa.RunJavaScript", ("script", Literal("1")), ("result", Captured("credit"))),
            "set-variable" => Activity("other", "Elsa.SetVariable",
                ("variable", JsonSerializer.SerializeToElement(new { id = "credit", name = "Credit", typeName = "Object" })),
                ("value", Literal("1"))),
            "setter" => Script("setCredit({});"),
            "setVariable" => Script("setVariable('Credit', {});"),
            "member-assignment" => Script("getCredit().Output = {};"),
            "computed-write" => Script("const name = 'Cr' + 'edit'; setVariable(name, {});"),
            _ => Write("1")
        };
        if (writer == "initial-value")
            variable.Value = "{\"Output\":{}}";

        var result = await ConvertAsync(Sequence("root", Execute(), other), variable);

        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
        Assert.Equal("execute", refusal.Metadata["ActivityNodeId"]);
        Assert.Contains(writer == "initial-value" ? "initial value" : "'other'", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_absent_or_empty_initial_value_is_no_writer(string? value)
    {
        var variable = new Elsa3Variable { Id = Credit.Id, Name = Credit.Name, TypeName = Credit.TypeName, Value = value };

        var result = await ConvertAsync(Sequence("root", Execute(), Write("getCredit().Output.Decision")), variable);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("getCredit().Decision")]
    [InlineData("getCredit()[name]")]
    public async Task Another_member_is_refused(string read)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Write($"(() => {{ const name = 'x'; return {read}; }})()")), Credit);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable &&
                                                          diagnostic.Message.Contains("Execute Workflow result", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{{ Variables.Credit.SubStatus }}")]
    [InlineData("{{ Variables.Credit }}")]
    [InlineData("{{ Output.execute }}")]
    public async Task Another_liquid_read_is_refused(string read)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Activity("node", "Elsa.WriteLine", ("text", Expression("Liquid", read)))), Credit);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
    }

    [Theory]
    [InlineData("getVariable(name)")]
    [InlineData("variables[name]")]
    public async Task A_computed_read_in_scope_of_the_variable_is_refused(string read)
    {
        var result = await ConvertAsync(Sequence("root", Execute(), Write($"(() => {{ const name = 'Credit'; return {read}; }})()")), Credit);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable &&
                                                          diagnostic.Message.Contains("computed name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_node_output_read_before_the_node_is_refused()
    {
        var result = await ConvertAsync(Sequence("root", Write("getCredit().WorkflowInstanceId"), Execute()), Credit);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape);
    }

    [Fact]
    public async Task A_result_captured_into_a_memory_slot_that_is_no_variable_is_refused()
    {
        // Its reads could not be rewritten, so they would silently get the Output shape.
        var result = await ConvertAsync(Sequence("root", Execute(), Write("getCredit().Output.Decision")));

        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
        Assert.Contains("no workflow variable", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_status_read_before_the_node_is_refused()
    {
        // "Finished" holds only once the node ran; Elsa 3 read V's earlier value before it.
        var result = await ConvertAsync(Sequence("root", Write("getCredit()?.Status ?? 'not run'"), Execute()), Credit);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape);
    }

    [Fact]
    public async Task An_output_read_before_the_node_reads_only_the_variable()
    {
        // The .Output rows read only the variable, so node order does not matter to them.
        var result = await ConvertAsync(Sequence("root", Write("getCredit().Output.Decision"), Execute()), Credit);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_node_output_read_across_frames_is_refused()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1]", "Object")));
        loop.AdditionalProperties["body"] = Port(Write("getCredit().SubStatus"));

        var result = await ConvertAsync(Sequence("root", Execute(), loop), Credit);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.CrossFrameReference);
    }

    // ---- helpers ----

    private static Elsa3Activity Execute() =>
        Activity("execute", "Elsa.ExecuteWorkflow", ("workflowDefinitionId", Literal("child")), ("result", Captured("credit")));

    private static Elsa3Activity Write(string script) =>
        Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", script)));

    private static Elsa3Activity Script(string body) =>
        Activity("other", "Elsa.RunJavaScript", ("script", Literal(body)));

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));

    private static void AssertCapturesOutput(ActivityNode node)
    {
        var output = Assert.Single(node.Outputs);
        Assert.Equal("Output", output.ReferenceKey);
        Assert.Equal("Variable", output.Value.ExpressionType);
        Assert.Contains("credit", JsonSerializer.Serialize(output.Value.Value), StringComparison.Ordinal);
    }

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Elsa3ConvertedDefinition> result) =>
        ExpressionOf(Input(Node(result.Value!.State, "node"), "text"));

    private static ExpressionDefinition ExpressionOf(ArgumentState argument) =>
        (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
        .Deserialize<ExpressionDefinition>(Elsa.Serialization.Core.JsonBaseline.Options)!;
}
