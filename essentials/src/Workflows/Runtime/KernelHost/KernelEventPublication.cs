using System.Text;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

internal interface IKernelEventPublicationRegistration
{
    void Validate(KernelServingPlan plan, WorkflowActivationCommand command, WorkflowExecutableSourceReference reference,
        IReadOnlyList<WorkflowTriggerBinding> claims);

    ValueTask<RuntimeKernelEventPublication?> ReadAsync(WorkflowActivationSlot slot, CancellationToken cancellationToken);
    ValueTask ApplyAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken);
}

internal static class KernelEventClaims
{
    internal static WorkflowTriggerBinding[] Create(KernelServingPlan plan, WorkflowExecutableIdentity identity, string activationId,
        DateTimeOffset createdAt)
    {
        if (plan.Served is not { Workflow: { } workflow }) return [];
        return NativeStartTriggers.StartEvents(workflow).Select(node =>
        {
            var name = Name(node);
            var key = RuntimeKernelEventPublication.ReservationKey(plan.SlotId, node.Id);
            return new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(activationId, identity.ArtifactId, node.Id, key),
                identity.ArtifactId, identity.DefinitionId, identity.ArtifactVersion, identity.ArtifactHash,
                node.Id, RuntimeKernelEventPublication.ReservationType, key, null,
                new Dictionary<string, string> { [RuntimeKernelEventPublication.EventNameMetadata] = name },
                createdAt, activationId, plan.SlotId, TriggerCardinality.ReservationOnly, IsActive: false);
        }).ToArray();
    }

    internal static string Name(Node node)
    {
        if (node.Inputs.GetValueOrDefault("EventName") is not LiteralBinding { Value: JsonValue value } ||
            !value.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name) || name.Contains('\0') ||
            Encoding.UTF8.GetByteCount(name) + "event:".Length > 1024 ||
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name)) != name)
            throw new ArgumentException($"Event start node '{node.Id}' requires a literal Event name supported by the installed store.");
        return name;
    }
}
