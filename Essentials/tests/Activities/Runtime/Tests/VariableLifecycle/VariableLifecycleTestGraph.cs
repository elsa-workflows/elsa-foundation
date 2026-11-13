using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// The read-modify-write shape spec 1221 is about (S-10, ADR 0092): a leaf takes <c>count</c> as an input
/// (a variable-read binding) and its result is captured back into <c>count</c>, so the leaf writes <c>count + 1</c>.
/// Two of them under a <c>Parallel</c> are the reproduction drive of US1 / US4.
/// </summary>
internal static class VariableLifecycleTestGraph
{
    public const string CountKey = "var-count";
    public static readonly ValueTypeDescriptor Int32Type = new("Int32");

    public static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .Build(activityExecutionIds);

    public static RuntimeVariableDeclaration CountDeclaration(int initial = 0) =>
        new(
            CountKey,
            "count",
            Int32Type,
            ValueProtectionPolicy.InstanceInline,
            new RuntimeInputBinding(
                "count",
                Int32Type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(Int32Type, JsonSerializer.SerializeToElement(initial), ValueProtectionPolicy.InstanceInline)));

    /// <summary>A Parallel of two increment leaves of the given CLR type.</summary>
    public static WorkflowExecutable ParallelIncrements(Type incrementActivity)
    {
        var left = Increment("node-left", incrementActivity);
        var right = Increment("node-right", incrementActivity);
        var root = new ExecutableNode(
            executableNodeId: "node-parallel",
            authoredActivityId: "authored-node-parallel",
            activityType: typeof(ParallelActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(ParallelActivity.BranchSlotName("left"), [left]),
                new ExecutableChildSlot(ParallelActivity.BranchSlotName("right"), [right])
            ],
            structure: new ExecutableActivityStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    branches = new[]
                    {
                        new { name = "left", activity = left.ExecutableNodeId },
                        new { name = "right", activity = right.ExecutableNodeId }
                    }
                })));

        return WorkflowExecutionHarness.NewExecutable(root, CountDeclaration());
    }

    /// <summary>One increment leaf: input <c>Count</c> reads <c>count</c>; the result is captured into <c>count</c>.</summary>
    public static ExecutableNode Increment(string nodeId, Type incrementActivity)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(incrementActivity);
        var projection = Assert.Single(contract.Result.Projections).Key;
        var read = new RuntimeInputBinding(
            nameof(IncrementActivity.Count),
            Int32Type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(CountKey, VariableReference.WorkflowScopeId));
        var capture = new RuntimeOutputCapture(
            projection,
            $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}count",
            new RuntimeValueTypeDescriptor(Int32Type.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = CountKey,
                [RuntimeMetadataKeys.VariableName] = "count"
            });

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: incrementActivity.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [read.InputKey] = read },
            metadata: new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [projection] = capture });
    }

    public static int ReadCount(WorkflowExecutionRun run) =>
        run.WorkflowState!.RootVariableFrame!.Values[CountKey].InlineValue!.Value.GetInt32();
}

/// <summary>The incremented value, as the one captured projection.</summary>
public sealed record Incremented([property: Output] int Value);

/// <summary>An unclassified (<c>External</c>) leaf returning <c>Count + 1</c>.</summary>
public sealed class IncrementActivity : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
}

/// <summary>The same leaf, declared <c>ReplaySafe</c>.</summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReplaySafeIncrementActivity : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
}
