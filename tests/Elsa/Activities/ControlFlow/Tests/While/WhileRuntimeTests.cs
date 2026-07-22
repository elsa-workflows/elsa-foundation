using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Activities.While;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.While.Tests;

/// <summary>
/// In-process execution coverage for the <c>While</c> composite running through the real workflow agent.
/// Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares the While-specific
/// activity graph shape; provider wiring, CLR activation, execution, and assertions come from the harness.
/// <c>While</c> opts into <c>IRuntimeRematerializeInputsOnChildCompletion</c> (issue #977): its inputs are
/// re-materialized from live committed variable frames before every child-completion evaluation, so a body
/// that mutates the condition variable (via a <c>Set</c> intrinsic) terminates the loop. The pinned
/// invocation snapshot itself stays immutable (ADR 0045); the per-pass re-read is transient. A body
/// <c>Break</c> outcome remains the explicit early-exit that ends the loop before the condition re-check.
/// </summary>
public sealed class WhileRuntimeTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

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

    [Fact]
    public async Task BodySetOfContainerScopedConditionVariable_TerminatesLoopAfterOnePass()
    {
        // The #977 repro: an enclosing Sequence declares keepGoing=true, the While condition reads it, and
        // the body Sets it to false. The Set commits to the Sequence's container frame; the While's inputs
        // are re-materialized from live frames on the body-completion evaluation, so the condition observes
        // the mutation and the loop exits after exactly one pass (no DrainCycleLimitExceededException).
        await using var harness = NewHarness("actexec-root", "actexec-scope", "actexec-while", "actexec-set-0");

        var run = await harness.RunAsync(NewBodyMutatesConditionExecutable());

        Assert.Single(run.States("node-set"));
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodySetOfWorkflowScopedConditionVariable_TerminatesLoopAfterOnePass()
    {
        // Same shape at workflow scope: the root Sequence's declaration seeds the canonical root variable
        // frame, the body's Set commits back to it (#286 mid-run write-back), and the re-materialized
        // condition observes the mutation on the next evaluation.
        await using var harness = NewHarness("actexec-root", "actexec-while", "actexec-set-0");

        var root = NewSequenceNode(
            "node-root",
            [NewVariableConditionWhileNode(VariableReference.WorkflowScopeId)],
            variables: [BoolVariable("keepGoing", true)]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        Assert.Single(run.States("node-set"));
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodySetOfContainerScopedConditionVariable_TerminatesUnderCoalescing()
    {
        // The fused/coalesced drain (ADR 0032 / ADR 0047) routes parent-completion evaluation through the
        // same handler, so the per-pass re-materialization must observe the body's frame write there too.
        await using var harness = NewHarness(
            coalescing: true, "actexec-root", "actexec-scope", "actexec-while", "actexec-set-0");

        var run = await harness.RunAsync(NewBodyMutatesConditionExecutable());

        Assert.Single(run.States("node-set"));
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        NewHarness(coalescing: false, activityExecutionIds);

    private static WorkflowExecutionHarness NewHarness(bool coalescing, params string[] activityExecutionIds)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithProbeLeaf();
        if (coalescing)
            builder = builder.WithCoalescing();
        return builder.Build(activityExecutionIds);
    }

    /// <summary>
    /// Sequence(root) → Sequence(node-scope, declares keepGoing=true) → While(Condition ← keepGoing) whose
    /// body Sets keepGoing=false — the exact issue #977 container-scoped shape.
    /// </summary>
    private static WorkflowExecutable NewBodyMutatesConditionExecutable()
    {
        var scope = NewSequenceNode(
            "node-scope",
            [NewVariableConditionWhileNode(declaringScopeId: "node-scope")],
            variables: [BoolVariable("keepGoing", true)]);
        return WorkflowExecutionHarness.NewExecutable(NewSequenceNode("node-root", [scope]));
    }

    /// <summary>A While whose condition reads <c>keepGoing</c> and whose body Sets it to <c>false</c>.</summary>
    private static ExecutableNode NewVariableConditionWhileNode(string declaringScopeId)
    {
        var body = NewSetNode("node-set", "keepGoing", declaringScopeId, value: false);
        return new ExecutableNode(
            executableNodeId: "node-while",
            authoredActivityId: "authored-while",
            activityType: typeof(WhileActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(WhileDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new WhileDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new(
                    "Condition",
                    new ValueTypeDescriptor("Boolean"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference("keepGoing", declaringScopeId))
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));
    }

    private static ExecutableNode NewSetNode(string nodeId, string variableKey, string declaringScopeId, bool value)
    {
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = BoolLiteral(WorkflowIntrinsicInputKeys.Value, value)
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(variableKey, declaringScopeId));
    }

    private static ExecutableNode NewSequenceNode(
        string nodeId,
        IReadOnlyList<ExecutableNode> activities,
        IReadOnlyCollection<RuntimeVariableDeclaration>? variables = null)
    {
        var structurePayload = JsonSerializer.SerializeToNode(
            new { activities = activities.Select(activity => activity.ExecutableNodeId).ToArray() }, WebOptions)!.AsObject();
        if (variables is { Count: > 0 })
            structurePayload["variables"] = JsonSerializer.SerializeToNode(variables, WebOptions);

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(WhileDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new WhileDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, activities)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload)));
    }

    /// <summary>A Boolean variable declaration with an inline literal initial value, declared on a container's structure.</summary>
    private static RuntimeVariableDeclaration BoolVariable(string name, bool initialValue)
    {
        var type = new ValueTypeDescriptor("Boolean");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeVariableDeclaration(name, name, type, policy, new RuntimeInputBinding(
            name,
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(initialValue), policy)));
    }

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
