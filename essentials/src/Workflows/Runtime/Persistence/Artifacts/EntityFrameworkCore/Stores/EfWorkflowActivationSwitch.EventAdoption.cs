using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowActivationSwitch
{
    async ValueTask<bool> IWorkflowEventAdoption.TryAdoptEventsAsync(WorkflowActivationSlot slot,
        WorkflowExecutableSourceReference source, IReadOnlyList<WorkflowTriggerBinding> claims, CancellationToken token)
    {
        const string stimulusType = RuntimeKernelEventPublication.ReservationType;
        if (slot.ActiveActivationId is not { } activation || source.Scope != WorkflowExecutableReferenceScope.Published ||
            source.ActivationId != activation || source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(activation) ||
            source.SlotId != slot.SlotId || claims.Count == 0 || claims.Any(claim => claim.StimulusType != stimulusType))
            throw new ArgumentException("Event adoption requires the active slot's exact publication source and claims.", nameof(source));

        return await RunAsync($"Event adoption for activation '{activation}'", async scope =>
        {
            if (!await EfAdmissionTargetFence.LockSlotAsync(_context, scope, slot.WorkflowDefinitionId, slot.SlotName, slot, token) ||
                !await EfAdmissionTargetFence.LockSourceAsync(_references, scope, source, token) || !source.IsLive(_timeProvider.GetUtcNow()))
                return Step.Discard(false);

            _accessContextAccessor.Current.EnsureTenantScope(source.TenantId);
            var persistence = new PersistenceScope(scope);
            var owner = new TriggerScope(persistence, source.TenantId);
            var executable = await new EfWorkflowExecutableStore(_context, _accessContextAccessor).FindAsync(source.ArtifactId, token);
            if (executable is null || claims.Any(claim => claim.ArtifactHash != executable.Identity.ArtifactHash ||
                    !executable.NodesById.ContainsKey(claim.ExecutableNodeId)))
                throw new InvalidDataException("The Event claims differ from the selected immutable executable.");

            await _bindings.StageBindingAdoptionAsync(scope, activation, stimulusType, claims, token);
            var descriptors = claims.Select(claim => new RuntimeIngressReservationDescriptor(stimulusType, claim.StimulusHash, claim.Metadata)).ToArray();
            var reservations = RuntimeIngressReservationRules.Prepare(owner, RuntimeIngressReservation.PublicationSlotOwner,
                slot.SlotId, slot.Revision, slot.Revision, descriptors);
            await new EfIngressReservationStore(_context, _accessContextAccessor).StageOwnerAsync(owner,
                RuntimeIngressReservation.PublicationSlotOwner, slot.SlotId, reservations, token, _reservationPolicies, stimulusType);
            await new EfRuntimeKernelEventPublicationStore(_context, _accessContextAccessor)
                .StageAsync(owner, slot, source, claims, inheritTenant: false, token);
            return source.IsLive(_timeProvider.GetUtcNow()) ? Step.Commit(true) : Step.Discard(false);
        }, token, retryStageConflicts: true);
    }
}
