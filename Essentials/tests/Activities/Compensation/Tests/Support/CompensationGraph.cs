using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>Builds executable compensation-scope graphs from participant and harness leaves.</summary>
public static class CompensationGraph
{
    public const string ScopeNodeId = "node-scope";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>One step: a forward node and an optional handler node.</summary>
    public sealed record Step(string Name, ExecutableNode Forward, ExecutableNode? Handler = null);

    public static ExecutableNode Participant(string nodeId) => ClrNode<ParticipantActivity>(nodeId);

    public static ExecutableNode Scope(params Step[] steps) => Scope(faultAfterCompensation: false, steps);

    public static ExecutableNode Scope(bool faultAfterCompensation, params Step[] steps) =>
        Scope(ScopeNodeId, faultAfterCompensation, steps);

    public static ExecutableNode Scope(string nodeId, bool faultAfterCompensation, params Step[] steps)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(CompensationScope));
        var input = contract.Inputs["faultAfterCompensation"];
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [input.Key] = new(
                input.Key,
                input.Type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(faultAfterCompensation), ValueProtectionPolicy.InstanceInline))
        };

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
            metadata: new Dictionary<string, string>(),
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

    private static ExecutableNode ClrNode<TActivity>(string nodeId)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TActivity));
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(TActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }
}
