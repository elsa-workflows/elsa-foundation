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

    [Fact]
    public async Task BodySetOfTheConditionVariable_IsObservedOnThePostTestCheck_AndTheLoopTerminates()
    {
        // #972 defect 2 (Do mirror): the unconditional first pass Sets the condition variable to false; the
        // post-test check re-materializes Condition from the live root frame and observes it — exactly one
        // body pass, then Done (a pinned first-activation snapshot would keep the condition true forever).
        await using var harness = NewHarness("actexec-do", "actexec-set-0");

        var run = await harness.RunAsync(NewVariableConditionExecutable());

        Assert.Single(run.States("node-set"));
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
        var rootFrame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        Assert.False(rootFrame.Values["var-keep-going"].InlineValue!.Value.GetBoolean());
    }

    private static WorkflowExecutable NewVariableConditionExecutable()
    {
        var booleanType = new ValueTypeDescriptor("Boolean");
        var setNode = new ExecutableNode(
            executableNodeId: "node-set",
            authoredActivityId: "authored-set",
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = BoolLiteral(WorkflowIntrinsicInputKeys.Value, false)
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference("var-keep-going", Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId));
        var root = new ExecutableNode(
            executableNodeId: "node-do",
            authoredActivityId: "authored-do",
            activityType: typeof(DoActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(DoDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new DoDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new(
                    inputKey: "Condition",
                    targetType: booleanType,
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference("var-keep-going", Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId))
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(DoActivity.BodySlotName, [setNode])],
            structure: new ExecutableActivityStructure(
                DoActivity.StructureKind,
                DoActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-set" })));

        return WorkflowExecutionHarness.NewExecutable(
            root,
            new RuntimeVariableDeclaration(
                "var-keep-going",
                "KeepGoing",
                booleanType,
                ValueProtectionPolicy.InstanceInline,
                new RuntimeInputBinding(
                    "var-keep-going",
                    booleanType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    // Starts TRUE: a pinned first-activation snapshot would re-check true forever; only the
                    // re-materialized post-test check observes the body's false.
                    literal: ValueEnvelope.Inline(booleanType, JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline))));
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services))
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
