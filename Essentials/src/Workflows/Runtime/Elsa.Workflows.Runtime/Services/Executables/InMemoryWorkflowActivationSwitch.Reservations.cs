using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Services.Executables;

public sealed partial class InMemoryWorkflowActivationSwitch
{
    private readonly IReadOnlyDictionary<string, IIngressReservationPolicy> _reservationPolicies;
    internal IReadOnlyDictionary<string, IIngressReservationPolicy> ReservationPolicies => _reservationPolicies;
    private readonly InMemoryRuntimeCheckpointStoreState? _reservationState;
    private readonly IPersistenceAccessContextAccessor? _reservationAccess;
    private readonly InMemoryWorkflowTriggerBindingStore? _reservationBindings;
    internal InMemoryRuntimeCheckpointStoreState? ReservationState => _reservationState;
    internal InMemoryWorkflowActivationAuthority ReservationAuthority => _authority;
    internal InMemoryWorkflowTriggerBindingStore? ReservationBindings => _reservationBindings;
    internal IWorkflowExecutableSourceReferenceStore ReservationReferences => _references;

    private async ValueTask<WorkflowExecutableSourceReference?> ReadReservationSourceAsync(string activationId, CancellationToken ct) =>
        _reservationState is null ? null : await _references.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), ct)
            ?? throw new InvalidDataException("An activation reservation requires its actual source reference.");

    private IReadOnlyList<RuntimeIngressReservation> PrepareReservations(WorkflowActivationSlot slot, WorkflowExecutableSourceReference? source)
    {
        if (_reservationState is null) return [];
        var persistence = _reservationAccess?.Current.RequireScope() ?? throw new NotSupportedException("Publication reservations require a scoped physical Runtime boundary.");
        if (slot.ActiveActivationId is null) return [];
        if (source?.ActivationId != slot.ActiveActivationId || source.SlotId != slot.SlotId || _reservationBindings is null)
            throw new InvalidDataException("The publication reservation does not name the exact prepared slot source.");
        var scope = new TriggerScope(persistence, source.TenantId);
        var bindings = _reservationBindings.ListAllByActivationAsync(slot.ActiveActivationId).AsTask().GetAwaiter().GetResult();
        var descriptors = bindings.Where(binding => binding.Cardinality == TriggerCardinality.Exclusive)
            .GroupBy(binding => (binding.StimulusType, binding.StimulusHash)).Select(group =>
                new RuntimeIngressReservationDescriptor(group.Key.StimulusType, group.Key.StimulusHash, group.First().Metadata)).ToArray();
        var claims = RuntimeIngressReservationRules.Prepare(scope, RuntimeIngressReservation.PublicationSlotOwner, slot.SlotId, slot.Revision, slot.Revision, descriptors);
        RuntimeIngressReservationRules.CheckSemanticCandidates(claims, _reservationPolicies);
        foreach (var claim in claims)
        {
            foreach (var prior in _reservationState.IngressReservations.Values)
                RuntimeIngressReservationRules.CheckSemanticOwner(prior, claim, _reservationPolicies);
            if (_reservationState.IngressReservations.TryGetValue(claim.ReservationId, out var existing)) RuntimeIngressReservationRules.CheckOwner(existing, claim);
            var active = RuntimeIngressReservationRules.ConflictingBindingsAsync(_reservationBindings, claim, _reservationPolicies).AsTask().GetAwaiter().GetResult();
            if (active.Any(binding => binding.SlotId != slot.SlotId && !KnownOtherScope(binding, persistence)))
                throw new TriggerOperationException("route-conflict", "Another published workflow owns this exclusive endpoint.");
        }
        return claims;
    }

    private bool KnownOtherScope(WorkflowTriggerBinding binding, PersistenceScope current) => binding.SlotId is not null &&
        _reservationState!.IngressReservations.Values.Any(claim => claim.OwnerKind == RuntimeIngressReservation.PublicationSlotOwner &&
            claim.OwnerId == binding.SlotId && claim.StimulusType == binding.StimulusType && claim.StimulusHash == binding.StimulusHash && claim.Scope.Persistence != current);

    private void ApplyReservations(WorkflowActivationSlot slot, IReadOnlyList<RuntimeIngressReservation> claims)
    {
        if (_reservationState is null) return;
        var persistence = _reservationAccess!.Current.RequireScope();
        var retained = claims.Select(claim => claim.ReservationId).ToHashSet(StringComparer.Ordinal);
        var removed = _reservationState.IngressReservations.Values.Where(claim => claim.Scope.Persistence == persistence &&
            claim.OwnerKind == RuntimeIngressReservation.PublicationSlotOwner && claim.OwnerId == slot.SlotId && !retained.Contains(claim.ReservationId)).Select(claim => claim.ReservationId).ToArray();
        foreach (var id in removed) _reservationState.IngressReservations.Remove(id);
        foreach (var claim in claims) _reservationState.IngressReservations[claim.ReservationId] = claim;
    }
}
