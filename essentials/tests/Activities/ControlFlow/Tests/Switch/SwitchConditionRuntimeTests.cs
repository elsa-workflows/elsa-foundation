using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

public sealed class SwitchConditionRuntimeTests
{
    [Theory]
    [InlineData(true, true, "approved")]
    [InlineData(false, true, "rejected")]
    [InlineData(false, false, "fallback")]
    public async Task Pinned_conditions_select_the_first_true_branch_or_default(bool first, bool second, string selected)
    {
        await using var harness = NewHarness();
        var run = await harness.RunAsync(Executable(first, second));

        run.AssertRan(selected);
        run.AssertDidNotRun(new[] { "approved", "rejected", "fallback" }.Where(id => id != selected).ToArray());
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task A_true_empty_case_completes_without_falling_through()
    {
        await using var harness = NewHarness();
        var run = await harness.RunAsync(Executable(true, true, emptyFirst: true));

        run.AssertOutcomes("switch", "Approved");
        run.AssertDidNotRun("approved", "rejected", "fallback");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task JavaScript_condition_uses_the_normal_evaluator_and_skips_a_later_throw()
    {
        await using var harness = NewHarness();
        var executable = Executable(false, false, firstBinding: Expression("a", "1 + 1 === 2"), secondBinding: Expression("b", "throw new Error('must not evaluate')"));
        var run = await harness.RunAsync(executable);
        run.AssertRan("approved");
        run.AssertDidNotRun("rejected", "fallback");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task An_evaluated_expression_error_records_the_normal_input_failure_instead_of_taking_default()
    {
        await using var harness = NewHarness();
        var run = await harness.RunAsync(Executable(false, false, secondBinding: Expression("b", "throw new Error('evaluated')")), allowPendingWorkOnTerminalCompletion: false, allowAcceptedButFaulted: true);
        run.AssertDidNotRun("approved", "rejected", "fallback");
        var activity = run.State("switch");
        Assert.Equal(ActivityExecutionStatus.Scheduled, activity.Status);
        Assert.Null(activity.InputSnapshot);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId));
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal("ExpressionEvaluationFailed", incident.Metadata[RuntimeMetadataKeys.InputFailureCode]);
        Assert.Equal(SwitchActivity.ConditionInputKey("b"), incident.Metadata[RuntimeMetadataKeys.InputKey]);
    }

    [Theory]
    [InlineData(true, true, false, "Approved")]
    [InlineData(false, true, false, "Rejected")]
    [InlineData(false, false, false, "Default")]
    [InlineData(true, true, true, "Approved")]
    public async Task Flowchart_follows_the_selected_case_outcome_after_its_optional_branch(bool first, bool second, bool emptyFirst, string outcome)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services =>
            {
                new ActivitiesControlFlowFeature().ConfigureServices(services);
                new ActivitiesFlowchartFeature().ConfigureServices(services);
            })
            .WithProbeLeaf().Build("graph-invocation", "switch-invocation", "branch-or-target", "target-invocation");
        var switchNode = Executable(first, second, emptyFirst).RootActivity;
        var outcomes = new[] { "Approved", "Rejected", "Default" };
        var children = new[] { switchNode }.Concat(outcomes.Select(name => WorkflowExecutionHarness.NewProbeNode("after-" + name))).ToArray();
        var connections = outcomes.Select(name => new FlowchartConnection(new FlowchartEndpoint("switch", name), new FlowchartEndpoint("after-" + name))).ToArray();
        var root = new ExecutableNode("graph", "graph", typeof(Elsa.Activities.Flowchart.Activities.Flowchart).FullName!, "1.0.0",
            descriptorType: typeof(FlowchartDescriptor).FullName!, descriptorPayload: JsonSerializer.SerializeToElement(new FlowchartDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(), metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(Elsa.Activities.Flowchart.Activities.Flowchart.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure("elsa.flowchart.structure", "1.0.0", JsonSerializer.SerializeToElement(new FlowchartStructure(connections, "switch"))));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        var target = run.AssertCompleted("after-" + outcome);
        var selected = run.AssertCompleted("switch");
        Assert.Equal(selected.InvocationId, target.SchedulingActivityExecutionId);
        run.AssertOutcomes("switch", outcome);
        run.AssertDidNotRun(outcomes.Where(name => name != outcome).Select(name => "after-" + name).ToArray());
        if (emptyFirst) run.AssertDidNotRun("approved", "rejected", "fallback");
        else run.AssertCompleted(outcome == "Default" ? "fallback" : outcome.ToLowerInvariant());
        run.AssertWorkflowCompleted();
    }

    private sealed record FlowchartDescriptor;

    private static RuntimeInputBinding Expression(string id, string source) => new(
        SwitchActivity.ConditionInputKey(id), new ValueTypeDescriptor("Boolean"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Expression, expression: new RuntimeExpressionBinding("JavaScript", source, new("alias", "Boolean", null)));

    private static WorkflowExecutionHarness NewHarness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => { new ActivitiesControlFlowFeature().ConfigureServices(services); new ExpressionsFeature().ConfigureServices(services);
            new JavaScriptFeature().ConfigureServices(services); new JintFeature().ConfigureServices(services); })
        .WithProbeLeaf().Build("switch-invocation", "branch-invocation");

    private static WorkflowExecutable Executable(bool first, bool second, bool emptyFirst = false, RuntimeInputBinding? firstBinding = null, RuntimeInputBinding? secondBinding = null)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            cases = new[]
            {
                new { match = "Approved", activity = emptyFirst ? null : "approved", conditionInputKey = SwitchActivity.ConditionInputKey("a") },
                new { match = "Rejected", activity = (string?)"rejected", conditionInputKey = SwitchActivity.ConditionInputKey("b") }
            },
            @default = "fallback"
        });
        var bindings = new[] { firstBinding ?? Binding("a", first), secondBinding ?? Binding("b", second) }.ToDictionary(binding => binding.InputKey);
        var prototype = ClrActivityContractTestBuilder.BuildContract(typeof(SwitchActivity));
        var contract = new ActivityContract(prototype.ActivityTypeKey, "4.1.0", prototype.DescriptorKind, prototype.DescriptorPayload,
            bindings.Values.Select(binding => new ActivityInputContract(binding.InputKey, binding.InputKey,
                new ValueTypeDescriptor("Boolean"), false, false, false, null, ActivityValuePolicy.Default) { IsStructureInput = true }),
            prototype.Result,
            ["Approved", "Rejected", "Default", "Break"],
            prototype.Activation, prototype.SideEffectProfile);
        var slots = new List<ExecutableChildSlot>
        {
            new(SwitchActivity.CaseSlotName("Rejected"), [WorkflowExecutionHarness.NewProbeNode("rejected")]),
            new(SwitchActivity.DefaultSlotName, [WorkflowExecutionHarness.NewProbeNode("fallback")])
        };
        if (!emptyFirst) slots.Add(new(SwitchActivity.CaseSlotName("Approved"), [WorkflowExecutionHarness.NewProbeNode("approved")]));
        var node = new ExecutableNode("switch", "switch", typeof(SwitchActivity).FullName!, "4.1.0",
            descriptorType: prototype.DescriptorKind, descriptorPayload: prototype.DescriptorPayload,
            inputBindings: bindings, metadata: new Dictionary<string, string>(), childSlots: slots,
            structure: new ExecutableActivityStructure(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, payload),
            activityContract: contract);
        return WorkflowExecutionHarness.NewExecutable(node);
    }

    private static RuntimeInputBinding Binding(string id, bool value) => new(
        SwitchActivity.ConditionInputKey(id), new ValueTypeDescriptor("Boolean"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Boolean"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

}
