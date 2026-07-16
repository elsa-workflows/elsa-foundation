using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.If;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Tests;

/// <summary>
/// In-process execution coverage for the <c>If</c> composite running through the real workflow agent.
/// Built on the shared <see cref="WorkflowExecutionHarness"/>: this file only declares the If-specific
/// activity graph shape; provider wiring, CLR activation, execution, and assertions come from the harness.
/// Asserts the matching branch runs, the other does not, and the composite emits the True/False outcome.
/// </summary>
public sealed class IfRuntimeTests
{
    [Fact]
    public async Task TrueCondition_RunsThenBranch_AndEmitsTrueOutcome()
    {
        await using var harness = NewHarness("actexec-if", "actexec-then");

        var run = await harness.RunAsync(NewExecutable(condition: true));

        run.AssertOutcomes("node-if", ActivityOutcomes.True);
        run.AssertRan("node-then");
        run.AssertDidNotRun("node-else");
    }

    [Fact]
    public async Task FalseCondition_RunsElseBranch_AndEmitsFalseOutcome()
    {
        await using var harness = NewHarness("actexec-if", "actexec-else");

        var run = await harness.RunAsync(NewExecutable(condition: false));

        run.AssertOutcomes("node-if", ActivityOutcomes.False);
        run.AssertRan("node-else");
        run.AssertDidNotRun("node-then");
    }

    [Fact]
    public async Task TerminalBranch_CompletesWorkflow()
    {
        await using var harness = NewHarness("actexec-if", "actexec-then");

        var run = await harness.RunAsync(NewExecutable(condition: true));

        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task SelectedBranchIsEmpty_FinalizesWithoutSchedulingChild_AndCompletesWorkflow()
    {
        // Condition selects the Then branch, but its slot is empty: the composite must finalize via
        // CompleteCompositeActivity (True outcome) with no child scheduled, and the run must complete.
        await using var harness = NewHarness("actexec-if");

        var run = await harness.RunAsync(NewExecutable(condition: true, includeThen: false));

        run.AssertOutcomes("node-if", ActivityOutcomes.True);
        run.AssertDidNotRun("node-then", "node-else");
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(bool condition, bool includeThen = true, bool includeElse = true)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (includeThen)
            childSlots.Add(new ExecutableChildSlot(IfActivity.ThenSlotName, [WorkflowExecutionHarness.NewProbeNode("node-then")]));
        if (includeElse)
            childSlots.Add(new ExecutableChildSlot(IfActivity.ElseSlotName, [WorkflowExecutionHarness.NewProbeNode("node-else")]));

        var root = new ExecutableNode(
            executableNodeId: "node-if",
            authoredActivityId: "authored-if",
            activityType: typeof(IfActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(IfDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new IfDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new RuntimeInputBinding(
                    "Condition",
                    new ValueTypeDescriptor("Boolean"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        new ValueTypeDescriptor("Boolean"),
                        JsonSerializer.SerializeToElement(condition),
                        ValueProtectionPolicy.InstanceInline))
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                IfActivity.StructureKind,
                IfActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    then = includeThen ? "node-then" : null,
                    @else = includeElse ? "node-else" : null
                })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private sealed record IfDescriptor;
}
