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
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// A harness whose inline leaves run the discrete Schedule → Start → Invoke chain (fusion off), so two parallel
    /// leaves both read before either commits: the stale-base case a background step or a deactivated drain meets.
    /// </summary>
    public static WorkflowExecutionHarness NewStaleBaseHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services => services.AddSingleton(new RuntimeReplaySafeFusionOptions { Enabled = false }))
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
    public static WorkflowExecutable ParallelIncrements(Type incrementActivity, LostUpdateMode mode = LostUpdateMode.Default) =>
        WorkflowExecutionHarness.NewExecutable(
            Parallel(Increment("node-left", incrementActivity, mode), Increment("node-right", incrementActivity, mode)),
            CountDeclaration());

    /// <summary>A Parallel of the two given leaves.</summary>
    public static ExecutableNode Parallel(ExecutableNode left, ExecutableNode right)
    {
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

        return root;
    }

    /// <summary>One increment leaf: input <c>Count</c> reads <c>count</c>; the result is captured into <c>count</c>.</summary>
    public static ExecutableNode Increment(string nodeId, Type incrementActivity, LostUpdateMode mode = LostUpdateMode.Default, bool readsCount = true, string captureKey = CountKey)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(incrementActivity);
        var projection = contract.Result.Projections.Count == 1
            ? Assert.Single(contract.Result.Projections).Key
            : contract.Result.Projections.Keys.Single(key => key.EndsWith(nameof(Incremented.Value), StringComparison.Ordinal));
        var read = new RuntimeInputBinding(
            nameof(IncrementActivity.Count),
            Int32Type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(CountKey, VariableReference.WorkflowScopeId));
        var capture = new RuntimeOutputCapture(
            projection,
            $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}{(captureKey == CountKey ? "count" : "other")}",
            new RuntimeValueTypeDescriptor(Int32Type.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = captureKey,
                [RuntimeMetadataKeys.VariableName] = captureKey == CountKey ? "count" : "other"
            });

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: incrementActivity.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: readsCount
                ? new Dictionary<string, RuntimeInputBinding> { [read.InputKey] = read }
                : new Dictionary<string, RuntimeInputBinding> { [read.InputKey] = Literal(read.InputKey, 41) },
            metadata: mode == LostUpdateMode.Fault
                ? new Dictionary<string, string> { [RuntimeMetadataKeys.LostUpdateMode] = RuntimeMetadataKeys.LostUpdateModeFault }
                : new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [projection] = capture });
    }

    private static RuntimeInputBinding Literal(string inputKey, int value) =>
        new(inputKey, Int32Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(Int32Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

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
