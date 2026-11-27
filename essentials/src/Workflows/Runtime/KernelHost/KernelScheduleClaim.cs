using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

internal static class KernelScheduleClaim
{
    internal static WorkflowTriggerBinding Create(WorkflowExecutableIdentity identity, string activationId,
        string slotId, NativeSchedule schedule, DateTimeOffset createdAt)
    {
        var key = Key(schedule);
        return new(WorkflowTriggerBinding.BuildId(activationId, identity.ArtifactId, schedule.Node, key),
            identity.ArtifactId, identity.DefinitionId, identity.ArtifactVersion, identity.ArtifactHash,
            schedule.Node, RuntimeKernelScheduleOccurrence.ReservationType, key, null, new Dictionary<string, string>(), createdAt, activationId, slotId,
            TriggerCardinality.ReservationOnly, IsActive: false);
    }

    internal static bool Matches(WorkflowTriggerBinding binding, KernelServedSlot slot, NativeSchedule schedule) =>
        binding.IsActive && binding.ActivationId == slot.ActivationId && binding.SlotId == slot.SlotId &&
        binding.ArtifactId == slot.Workflow.Id && binding.DefinitionId == slot.DefinitionId &&
        binding.ExecutableNodeId == schedule.Node && binding.StimulusType == RuntimeKernelScheduleOccurrence.ReservationType &&
        binding.StimulusHash == Key(schedule);

    private static string Key(NativeSchedule schedule) => RuntimeKernelScheduleOccurrence.ReservationKey(schedule.Key);
}
