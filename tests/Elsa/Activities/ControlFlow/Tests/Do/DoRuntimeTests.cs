using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.Do;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Tests;

/// <summary>
/// In-process execution coverage for the <c>Do</c> post-test composite running through the real workflow
/// agent. Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares the Do-specific
/// activity graph shape; provider wiring, CLR activation, execution, and assertions come from the harness.
/// The condition is an immutable invocation input. Mutable loop control is represented explicitly by a
/// body outcome (for example <c>Break</c>), never by re-materializing the parent input after each callback.
/// </summary>
public sealed class DoRuntimeTests
{
    [Fact]
    public async Task BodyRunsAtLeastOnce_EvenWhenConditionFalseFromTheStart()
    {
        // The counting evaluator returns false on its very first evaluation; the body still runs once
        // because the first pass is unconditional (post-test semantics).
        await using var harness = NewHarness("actexec-do", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(condition: false));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyBreak_EndsLoopEarly_EvenWhileTheConditionStillHolds()
    {
        // The condition would hold for three passes, but the body breaks on its first pass: exactly one
        // body pass (< 3) runs and the loop completes early, without re-checking the condition.
        await using var harness = NewHarness("actexec-do", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(condition: true, breakOnEntry: true));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
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
        // the loop recognizes it by name and ends early instead of re-checking the condition.
        var bodyOutcomes = breakOnEntry ? new[] { ActivityOutcomes.Break } : null;
        var root = new ExecutableNode(
            executableNodeId: "node-do",
            authoredActivityId: "authored-do",
            activityType: typeof(DoActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(DoDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new DoDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = BoolLiteral("Condition", condition)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(DoActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode("node-body", bodyOutcomes)])],
            structure: new ExecutableActivityStructure(
                DoActivity.StructureKind,
                DoActivity.StructureSchemaVersion,
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

    private sealed record DoDescriptor;
}
