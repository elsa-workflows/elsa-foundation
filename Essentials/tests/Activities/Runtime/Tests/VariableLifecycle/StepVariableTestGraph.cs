using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Builds spec 1231 (S-7) test graphs: leaves that read and write workflow variables through their step's variable
/// session, bound by <see cref="VariableReference{T}"/> inputs, next to S-10's capture leaves.
/// </summary>
internal static class StepVariableTestGraph
{
    public const string OtherKey = "var-other";
    public const string OrderKey = "var-order";
    public static readonly ValueTypeDescriptor ReferenceType = new(VariableReferenceTypes.Alias);
    public static readonly ValueTypeDescriptor AnyType = new("Any");

    public static RuntimeVariableDeclaration OtherDeclaration(int initial = 0) => Declaration(OtherKey, "other", VariableLifecycleTestGraph.Int32Type, initial);

    public static RuntimeVariableDeclaration OrderDeclaration(object initial) => Declaration(OrderKey, "order", AnyType, initial);

    public static RuntimeVariableDeclaration Declaration(string key, string name, ValueTypeDescriptor type, object initial) =>
        new(
            key,
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            new RuntimeInputBinding(
                name,
                type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(initial), ValueProtectionPolicy.InstanceInline)));

    /// <summary>The pinned binding of a variable-reference input: the variable it names, never a value.</summary>
    public static RuntimeInputBinding Reference(string inputKey, string name, string variableKey, string scopeId = VariableReference.WorkflowScopeId) =>
        new(
            inputKey,
            ReferenceType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                ReferenceType,
                JsonSerializer.SerializeToElement(new { name, scopeId, variableKey }),
                ValueProtectionPolicy.InstanceInline));

    /// <summary>An input that reads <c>count</c> through its binding (the input read set).</summary>
    public static RuntimeInputBinding ReadCount(string inputKey) =>
        new(
            inputKey,
            VariableLifecycleTestGraph.Int32Type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(VariableLifecycleTestGraph.CountKey, VariableReference.WorkflowScopeId));

    /// <summary>A leaf of the given type with the given bindings, an optional capture of its <c>Value</c> into a variable, and a lost-update mode.</summary>
    public static ExecutableNode Leaf(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings,
        string? captureInto = null,
        LostUpdateMode mode = LostUpdateMode.Default)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(activityType);
        var captures = new Dictionary<string, RuntimeOutputCapture>();
        if (captureInto is not null)
        {
            var projection = contract.Result.Projections.Keys.Single(key => key.EndsWith(nameof(Incremented.Value), StringComparison.Ordinal));
            var name = captureInto == VariableLifecycleTestGraph.CountKey ? "count" : "other";
            captures[projection] = new RuntimeOutputCapture(
                projection,
                $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}{name}",
                new RuntimeValueTypeDescriptor(VariableLifecycleTestGraph.Int32Type.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
                DurableValueLifecycle.Instance,
                DurableValueStorage.Custom,
                captureOnSuccessfulCompletion: true,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RuntimeMetadataKeys.TargetVariableReferenceKey] = captureInto,
                    [RuntimeMetadataKeys.VariableName] = name
                });
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mode == LostUpdateMode.Fault)
            metadata[RuntimeMetadataKeys.LostUpdateMode] = RuntimeMetadataKeys.LostUpdateModeFault;

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: bindings,
            metadata: metadata,
            activityContract: contract,
            outputCaptures: captures);
    }

    /// <summary>A leaf whose <c>Count</c> input is a reference bound to <c>count</c>.</summary>
    public static ExecutableNode CountLeaf(string nodeId, Type activityType, string? captureInto = null, LostUpdateMode mode = LostUpdateMode.Default) =>
        Leaf(nodeId, activityType, new Dictionary<string, RuntimeInputBinding>
        {
            ["Count"] = Reference("Count", "count", VariableLifecycleTestGraph.CountKey)
        }, captureInto, mode);

    /// <summary>A workflow running one leaf at its root, over <c>count</c>, <c>other</c> and any extra declarations.</summary>
    public static WorkflowExecutable Single(ExecutableNode leaf, params RuntimeVariableDeclaration[] extra) =>
        WorkflowExecutionHarness.NewExecutable(
            leaf,
            [VariableLifecycleTestGraph.CountDeclaration(), OtherDeclaration(), .. extra]);

    /// <summary>A Parallel of two leaves over <c>count</c> and <c>other</c>.</summary>
    public static WorkflowExecutable Parallel(ExecutableNode left, ExecutableNode right) =>
        WorkflowExecutionHarness.NewExecutable(
            VariableLifecycleTestGraph.Parallel(left, right),
            [VariableLifecycleTestGraph.CountDeclaration(), OtherDeclaration()]);

    /// <summary>A Parallel that declares a container variable <c>inner</c> (key <c>var-inner</c>) around two leaves.</summary>
    public static WorkflowExecutable ParallelWithContainerVariable(ExecutableNode left, ExecutableNode right, int initial = 0, string name = "inner")
    {
        var parallel = VariableLifecycleTestGraph.Parallel(left, right);
        var declaration = Declaration(InnerKey, name, VariableLifecycleTestGraph.Int32Type, initial);
        var structure = new ExecutableActivityStructure(
            ParallelActivity.StructureKind,
            ParallelActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new
            {
                branches = new[]
                {
                    new { name = "left", activity = left.ExecutableNodeId },
                    new { name = "right", activity = right.ExecutableNodeId }
                },
                variables = new[] { declaration }
            }));
        var root = new ExecutableNode(
            executableNodeId: parallel.ExecutableNodeId,
            authoredActivityId: parallel.AuthoredActivityId,
            activityType: parallel.ActivityType,
            activityTypeVersion: parallel.ActivityTypeVersion,
            descriptorType: parallel.DescriptorType,
            descriptorPayload: parallel.DescriptorPayload,
            inputBindings: parallel.InputBindings,
            metadata: parallel.Metadata,
            childSlots: parallel.ChildSlots,
            structure: structure);
        return WorkflowExecutionHarness.NewExecutable(root, [VariableLifecycleTestGraph.CountDeclaration(), OtherDeclaration()]);
    }

    public const string InnerKey = "var-inner";

    public static int ReadInt(WorkflowExecutionRun run, string key) =>
        run.WorkflowState!.RootVariableFrame!.Values[key].InlineValue!.Value.GetInt32();

    public static VariableVersion Version(WorkflowExecutionRun run, string key) =>
        run.WorkflowState!.RootVariableFrame!.VersionOf(key);
}
