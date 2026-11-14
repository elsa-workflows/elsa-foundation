using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowActivationSwitch
{
    private readonly IReadOnlyDictionary<string, IIngressReservationPolicy> _reservationPolicies;
    internal IReadOnlyDictionary<string, IIngressReservationPolicy> ReservationPolicies => _reservationPolicies;
    internal RuntimeDbContext ReservationContext => _context;

    private async ValueTask StageReservationsAsync(WorkflowActivationSlot slot, CancellationToken ct)
    {
        var persistence = _accessContextAccessor.Current.RequireScope();
        var store = new EfIngressReservationStore(_context, _accessContextAccessor);
        TriggerScope scope;
        IReadOnlyList<RuntimeIngressReservation> claims;
        if (slot.ActiveActivationId is { } activation)
        {
            var source = await _references.FindAsync(WorkflowActivationReferenceIdentity.Create(activation), ct)
                ?? throw new InvalidDataException("The publication reservation requires its exact source reference.");
            if (source.ActivationId != activation || source.SlotId != slot.SlotId)
                throw new InvalidDataException("The publication reservation does not name the selected logical slot.");
            scope = new(persistence, source.TenantId);
            var bindings = await _bindings.ListAllByActivationAsync(activation, ct);
            var descriptors = bindings.Where(binding => binding.Cardinality == TriggerCardinality.Exclusive)
                .GroupBy(binding => (binding.StimulusType, binding.StimulusHash)).Select(group =>
                    new RuntimeIngressReservationDescriptor(group.Key.StimulusType, group.Key.StimulusHash, group.First().Metadata)).ToArray();
            claims = RuntimeIngressReservationRules.Prepare(scope, RuntimeIngressReservation.PublicationSlotOwner, slot.SlotId, slot.Revision, slot.Revision, descriptors);
            if (claims.Any(claim => _reservationPolicies.ContainsKey(claim.StimulusType))) RuntimeSchemaWriteVersion.RequireReservationSemantics(_context);
            foreach (var claim in claims)
                if ((await RuntimeIngressReservationRules.ConflictingBindingsAsync(_bindings, claim, _reservationPolicies, ct, binding => binding.SlotId == slot.SlotId)).Any(binding => binding.SlotId != slot.SlotId))
                    throw new TriggerOperationException("route-conflict", "Another published workflow owns this exclusive endpoint.");
        }
        else
        {
            var scopeHash = EfRuntimeOperationalStoreSupport.Hash(persistence.Value); var ownerHash = EfRuntimeOperationalStoreSupport.Hash(slot.SlotId);
            var existing = await _context.IngressReservations.AsNoTracking().Where(row => row.ScopeHash == scopeHash &&
                row.OwnerKind == RuntimeIngressReservation.PublicationSlotOwner && row.OwnerHash == ownerHash).ToArrayAsync(ct);
            var tenants = existing.Select(row => EfIngressReservationStore.Read(row, persistence).Scope.TenantId).Distinct(StringComparer.Ordinal).ToArray();
            if (tenants.Length > 1) throw new InvalidDataException("A logical slot has conflicting reservation tenants.");
            scope = new(persistence, tenants.SingleOrDefault()); claims = [];
        }
        await store.StageOwnerAsync(scope, RuntimeIngressReservation.PublicationSlotOwner, slot.SlotId, claims, ct, _reservationPolicies);
    }
}
