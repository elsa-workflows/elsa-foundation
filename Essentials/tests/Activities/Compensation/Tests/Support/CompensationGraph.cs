using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Parallel.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>Builds executable compensation-scope graphs from participant and harness leaves.</summary>
public static class CompensationGraph
{
    public const string ScopeNodeId = "node-scope";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IPayloadSerializer PayloadSerializer = SerializationServices.PayloadSerializer();

    /// <summary>One step: a forward node and an optional handler node.</summary>
    public sealed record Step(string Name, ExecutableNode Forward, ExecutableNode? Handler = null);

    public static ExecutableNode Participant(string nodeId) => ClrNode<ParticipantActivity>(nodeId);

    public static ExecutableNode Scope(params Step[] steps) => Scope(faultAfterCompensation: false, steps);

    public static ExecutableNode Scope(bool faultAfterCompensation, params Step[] steps) =>
        Scope(ScopeNodeId, faultAfterCompensation, steps);

    public static ExecutableNode Scope(string nodeId, bool faultAfterCompensation, params Step[] steps) =>
        Scope(nodeId, faultAfterCompensation, handlerMaxAttempts: 3, steps);

    public static ExecutableNode Scope(string nodeId, bool faultAfterCompensation, int handlerMaxAttempts, params Step[] steps) =>
        Scope(nodeId, faultAfterCompensation, handlerMaxAttempts, confirmOnComplete: false, steps);

    public static ExecutableNode Scope(string nodeId, bool faultAfterCompensation, int handlerMaxAttempts, bool confirmOnComplete, params Step[] steps)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(CompensationScope));
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        Bind("faultAfterCompensation", faultAfterCompensation);
        Bind("handlerMaxAttempts", handlerMaxAttempts);
        Bind("confirmOnComplete", confirmOnComplete);

        void Bind<T>(string key, T value)
        {
            var input = contract.Inputs[key];
            bindings[input.Key] = new(
                input.Key,
                input.Type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        }

        var childSlots = new List<ExecutableChildSlot>();
        foreach (var step in steps)
        {
            childSlots.Add(new ExecutableChildSlot(CompensationScope.StepSlotName(step.Name), [step.Forward]));
            if (step.Handler is not null)
                childSlots.Add(new ExecutableChildSlot(CompensationScope.CompensationSlotName(step.Name), [step.Handler]));
        }

        var structure = new CompensationScopeExecutableStructure(
            steps.Select(step => new CompensationScopeExecutableStep(step.Name, step.Forward.ExecutableNodeId, step.Handler?.ExecutableNodeId)).ToArray());

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(CompensationScope).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: bindings,
            // As the publisher compiles it from the type: the scope receives faults from below Sequences and loops (R-3).
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.StructuralFaultHandling] = RuntimeMetadataKeys.StructuralFaultHandlingDescendant
            },
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                CompensationScope.StructureKind,
                CompensationScope.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions)),
            activityContract: contract);
    }

    /// <summary>The reservation journey: two reservations with handlers, then a confirmation without one.</summary>
    public static ExecutableNode ReservationJourney(bool faultAfterCompensation = false) =>
        Scope(
            faultAfterCompensation,
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory")),
            new Step("Reserve delivery", Participant("reserve-delivery"), Participant("cancel-delivery")),
            new Step("Confirm order", Participant("confirm-order")));

    public static WorkflowExecutable Executable(ExecutableNode root) => WorkflowExecutionHarness.NewExecutable(root);

    /// <summary>A Confirm activity; <paramref name="scope"/> names an enclosing scope by authored id.</summary>
    public static ExecutableNode ConfirmNode(string nodeId, string? scope = null) =>
        ClrNode<Confirm>(nodeId, ("scope", scope ?? string.Empty));

    /// <summary>A Compensate activity; <paramref name="scope"/> names an enclosing scope by authored id.</summary>
    public static ExecutableNode CompensateNode(string nodeId, string? scope = null, string? reason = null) =>
        ClrNode<Compensate>(nodeId, ("scope", scope ?? string.Empty), ("reason", reason ?? string.Empty));

    /// <summary>The authored activity id of a node these builders made.</summary>
    public static string AuthoredId(string nodeId) => $"authored-{nodeId}";

    public static ExecutableNode SequenceNode(string nodeId, params ExecutableNode[] activities) =>
        Composite(
            nodeId,
            typeof(SequenceActivity),
            new Dictionary<string, RuntimeInputBinding>(),
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, activities)],
            new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = activities.Select(activity => activity.ExecutableNodeId).ToArray() })));

    public static ExecutableNode ForEachNode(string nodeId, ExecutableNode body, params string[] items)
    {
        var type = new ValueTypeDescriptor("String", CollectionKind.List);
        return Composite(
            nodeId,
            typeof(ForEachActivity),
            new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(ForEachActivity.Collection)] = new(
                    nameof(ForEachActivity.Collection),
                    type,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(items), ValueProtectionPolicy.InstanceInline))
            },
            [new ExecutableChildSlot(ForEachActivity.BodySlotName, [body])],
            new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));
    }

    /// <summary>A While whose condition stays true: its body must fault to end it.</summary>
    public static ExecutableNode WhileNode(string nodeId, ExecutableNode body) =>
        Composite(
            nodeId,
            typeof(WhileActivity),
            new Dictionary<string, RuntimeInputBinding> { ["Condition"] = Literal("Condition", "Boolean", true) },
            [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    public static ExecutableNode ForNode(string nodeId, ExecutableNode body, int end) =>
        Composite(
            nodeId,
            typeof(ForActivity),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = Literal("Start", "Int32", 0),
                ["End"] = Literal("End", "Int32", end)
            },
            [new ExecutableChildSlot(ForActivity.BodySlotName, [body])],
            new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    /// <summary>A Flowchart that runs <paramref name="nodes"/> one after the other.</summary>
    public static ExecutableNode FlowchartNode(string nodeId, params ExecutableNode[] nodes)
    {
        var connections = Enumerable.Range(0, nodes.Length - 1)
            .Select(index => new FlowchartConnection(new FlowchartEndpoint(nodes[index].ExecutableNodeId), new FlowchartEndpoint(nodes[index + 1].ExecutableNodeId)))
            .ToArray();
        return Composite(
            nodeId,
            typeof(FlowchartActivity),
            new Dictionary<string, RuntimeInputBinding>(),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, nodes)],
            new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections, nodes[0].ExecutableNodeId))));
    }

    /// <summary>A Parallel with one branch per node, joining on all of them.</summary>
    public static ExecutableNode ParallelNode(string nodeId, params ExecutableNode[] branches)
    {
        var named = branches.Select((branch, index) => (Name: $"branch-{index + 1}", Node: branch)).ToArray();
        return Composite(
            nodeId,
            typeof(ParallelActivity),
            new Dictionary<string, RuntimeInputBinding>(),
            named.Select(branch => new ExecutableChildSlot(ParallelActivity.BranchSlotName(branch.Name), [branch.Node])).ToArray(),
            new ExecutableActivityStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new ParallelExecutableStructure(named.Select(branch => new ParallelExecutableBranch(branch.Name, branch.Node.ExecutableNodeId)).ToArray()))));
    }

    // A composite the harness pins as it pins any CLR activity, with the fault capability the publisher compiles.
    private static ExecutableNode Composite(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        ExecutableActivityStructure structure) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: AuthoredId(nodeId),
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(PayloadSerializer, activityType)),
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static RuntimeInputBinding Literal(string inputName, string alias, object value)
    {
        var type = new ValueTypeDescriptor(alias);
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private static ExecutableNode ClrNode<TActivity>(string nodeId, params (string Key, string Value)[] inputs)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TActivity));
        var bindings = inputs.ToDictionary(
            input => input.Key,
            input => new RuntimeInputBinding(
                input.Key,
                contract.Inputs[input.Key].Type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(contract.Inputs[input.Key].Type, JsonSerializer.SerializeToElement(input.Value), ValueProtectionPolicy.InstanceInline)),
            StringComparer.Ordinal);
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(TActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: bindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }
}
