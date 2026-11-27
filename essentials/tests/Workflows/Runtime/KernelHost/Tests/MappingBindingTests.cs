using Elsa.Expressions.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>Each compiled binding source onto its Native binding.</summary>
public sealed class MappingBindingTests
{
    private static Native.Workflow MapReads(params Elsa.Workflows.Runtime.Core.Models.RuntimeInputBinding[] reads)
    {
        var producers = new[]
        {
            Node("http", Types.HttpEndpoint, [Literal("Path", "orders", "String")]),
            Node("wait", Types.Event, [Literal("EventName", "paid", "String")]),
            Node("script", Types.RunJavaScript, [Literal("script", "return 1;", "String")]),
        };
        var consumers = reads.Select((read, index) => WriteLine($"w{index}", read));
        return NativeExecutableMapper.Map(Executable(Sequence("root", [.. producers, .. consumers]), Variable("v-order", "order"), Variable("v-n", "n", 1, "Int32")));
    }

    private static Native.Binding Read(Native.Workflow workflow, int index) => workflow.Nodes[$"w{index}"].Inputs["Text"];

    [Fact]
    public void Literals_variables_and_request_reads_map_with_their_member_paths()
    {
        var workflow = MapReads(
            Literal("text", "plain", "String"),
            Literal("text", null, "String"),
            VariableRead("text", "v-order"),
            VariableMember("text", "v-order", "customer", "name"),
            Request("text", "Order"),
            RequestMember("text", "Order", "lines", "count"),
            VariableRead("text", "v-n", plan: NumericWidening()));

        Assert.Equal("plain", Assert.IsType<Native.LiteralBinding>(Read(workflow, 0)).Value!.GetValue<string>());
        Assert.Null(Assert.IsType<Native.LiteralBinding>(Read(workflow, 1)).Value);
        Assert.Equal(new Native.VariableBinding("order"), Read(workflow, 2));
        Assert.Equal(new Native.VariableBinding("order", "customer.name"), Read(workflow, 3));
        Assert.Equal(new Native.VariableBinding("input", "Order"), Read(workflow, 4));
        Assert.Equal(new Native.VariableBinding("input", "Order.lines.count"), Read(workflow, 5));
        Assert.Equal(new Native.VariableBinding("n"), Read(workflow, 6));
    }

    [Fact]
    public void Result_reads_map_through_each_activity_projection()
    {
        var workflow = MapReads(
            Result("text", "wait", "Payload"),
            ResultMember("text", "wait", "Payload", "amount"),
            Result("text", "wait", "Result"),
            ResultMember("text", "http", "Request", "method"),
            Result("text", "http", "ParsedContent"),
            Result("text", "script", "result"));

        Assert.Equal(new Native.ResultBinding("wait", "Payload"), Read(workflow, 0));
        Assert.Equal(new Native.ResultBinding("wait", "Payload.amount"), Read(workflow, 1));
        Assert.Equal(new Native.ResultBinding("wait", "EventName"), Read(workflow, 2));
        Assert.Equal(new Native.ResultBinding("http", "Method"), Read(workflow, 3));
        Assert.Equal(new Native.ResultBinding("http", "Body"), Read(workflow, 4));
        Assert.Equal(new Native.ResultBinding("script", "Value"), Read(workflow, 5));
    }

    [Fact]
    public void An_expression_becomes_a_host_expression_with_its_parameters_and_the_reading_inputs_result_type()
    {
        var parameters = new Dictionary<string, ExpressionParameterBinding>
        {
            ["order"] = new VariableExpressionParameterBinding(WorkflowScope, "v-order"),
            ["request"] = new WorkflowRequestExpressionParameterBinding("Order"),
            ["paid"] = new ActivityResultExpressionParameterBinding("wait", "Payload"),
            ["greeting"] = new LiteralExpressionParameterBinding(System.Text.Json.JsonSerializer.SerializeToElement("Hello")),
            ["instance"] = new WorkflowInstanceExpressionParameterBinding(WorkflowInstanceExpressionParameterBinding.Members.InstanceId),
            ["correlation"] = new WorkflowInstanceExpressionParameterBinding(WorkflowInstanceExpressionParameterBinding.Members.CorrelationId),
        };
        var workflow = MapReads(
            Expression("text", "JavaScript", "`${greeting} ${order.name}`", "String", parameters, options: new { strict = true }),
            Expression("text", "Liquid", "{{ greeting }}", "String"));

        var script = Assert.IsType<Native.HostExpressionBinding>(Read(workflow, 0));
        Assert.Equal(("JavaScript", "`${greeting} ${order.name}`", "String"), (script.Language, script.Text, script.ResultType));
        Assert.True(script.Options!["strict"]!.GetValue<bool>());
        var mapped = script.Parameters.ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(new Native.VariableBinding("order"), mapped["order"]);
        Assert.Equal(new Native.VariableBinding("input", "Order"), mapped["request"]);
        Assert.Equal(new Native.ResultBinding("wait", "Payload"), mapped["paid"]);
        Assert.Equal("Hello", Assert.IsType<Native.LiteralBinding>(mapped["greeting"]).Value!.GetValue<string>());
        Assert.Equal(new Native.InstanceBinding(Native.InstanceField.Id), mapped["instance"]);
        Assert.Equal(new Native.InstanceBinding(Native.InstanceField.CorrelationId), mapped["correlation"]);

        var liquid = Assert.IsType<Native.HostExpressionBinding>(Read(workflow, 1));
        Assert.Equal(("Liquid", "String"), (liquid.Language, liquid.ResultType));
        Assert.Empty(liquid.Parameters);
        Assert.Null(liquid.Options);
    }

    [Fact]
    public void A_dispatch_names_the_definition_as_given()
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Request("Inputs", "Order"), Literal("WaitForCompletion", true, "Boolean")])));

        Assert.Equal(typeof(Native.DispatchWorkflow), workflow.Root.Type);
        Assert.Equal("invoicing", Assert.IsType<Native.LiteralBinding>(workflow.Root.Inputs["Workflow"]).Value!.GetValue<string>());
        Assert.Equal(new Native.VariableBinding("input", "Order"), workflow.Root.Inputs["Input"]);
        Assert.True(Assert.IsType<Native.LiteralBinding>(workflow.Root.Inputs["WaitForCompletion"]).Value!.GetValue<bool>());
    }
}
