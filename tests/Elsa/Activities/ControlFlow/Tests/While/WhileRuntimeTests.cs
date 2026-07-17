using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.While;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Tests;

/// <summary>
/// In-process execution coverage for the <c>While</c> composite running through the real workflow agent.
/// Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares the While-specific
/// activity graph shape; provider wiring, CLR activation, execution, and assertions come from the harness.
/// The condition is an immutable invocation input. Mutable loop control is represented explicitly by a
/// body outcome (for example <c>Break</c>), never by re-materializing the parent input after each callback.
/// </summary>
public sealed class WhileRuntimeTests
{
    [Fact]
    public async Task ConditionFalseOnEntry_NeverRunsBody_AndCompletes()
    {
        // Zero passes: the condition is false on the entry evaluation, so the body never runs.
        await using var harness = NewHarness("actexec-while");

        var run = await harness.RunAsync(NewExecutable(condition: false));

        run.AssertDidNotRun("node-body");
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyBreak_EndsLoopEarly_EvenWhileTheConditionStillHolds()
    {
        // The condition would hold for three passes, but the body breaks on its first pass: exactly one
        // body pass (< 3) runs and the loop completes early, before the condition is re-evaluated.
        await using var harness = NewHarness("actexec-while", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(condition: true, breakOnEntry: true));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(bool condition, bool breakOnEntry = false)
    {
        // A body that completes with the Break outcome models a Break leaf placed in the loop body (#299);
        // the loop recognizes it by name and ends early instead of re-evaluating the condition.
        var bodyOutcomes = breakOnEntry ? new[] { ActivityOutcomes.Break } : null;
        var root = new ExecutableNode(
            executableNodeId: "node-while",
            authoredActivityId: "authored-while",
            activityType: typeof(WhileActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(WhileDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new WhileDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = BoolLiteral("Condition", condition)
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode("node-body", bodyOutcomes)])],
            structure: new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-body" })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static RuntimeInputBinding BoolLiteral(string key, bool value)
    {
        var type = new ValueTypeDescriptor("Boolean");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            key,
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), policy));
    }

    private sealed record WhileDescriptor;
}
