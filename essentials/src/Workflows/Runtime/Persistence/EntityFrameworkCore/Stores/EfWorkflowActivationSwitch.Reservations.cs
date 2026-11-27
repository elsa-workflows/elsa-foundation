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

    private async ValueTask<Func<CancellationToken, Task>?> StageReservationsAsync(WorkflowActivationSlot slot, CancellationToken ct,
        IReadOnlyList<WorkflowTriggerBinding>? adoptedSchedules = null, bool repair = false, bool slotFenced = false)
    {
        var persistence = _accessContextAccessor.Current.RequireScope();
        var store = new EfIngressReservationStore(_context, _accessContextAccessor);
        var onlySchedules = adoptedSchedules is not null && !repair;
        TriggerScope scope;
        IReadOnlyList<RuntimeIngressReservation> claims;
        RuntimeKernelScheduleControl[] desired = [];
        WorkflowExecutableSourceReference? source = null;
        WorkflowTriggerBinding[] eventEntries = [];
        var inheritEventTenant = false;
        if (slot.ActiveActivationId is { } activation)
        {
            source = await _references.FindAsync(WorkflowActivationReferenceIdentity.Create(activation), ct)
                ?? throw new InvalidDataException("The publication reservation requires its exact source reference.");
            if (source.ActivationId != activation || source.SlotId != slot.SlotId)
                throw new InvalidDataException("The publication reservation does not name the selected logical slot.");
            scope = new(persistence, source.TenantId);
            var bindings = onlySchedules ? adoptedSchedules! : await _bindings.ListAllByActivationAsync(activation, ct);
            if (repair && adoptedSchedules is not null)
                bindings = [.. bindings.Where(binding => binding.StimulusType != RuntimeKernelScheduleOccurrence.ReservationType), .. adoptedSchedules];
            var descriptors = bindings.Where(binding => binding.Cardinality is TriggerCardinality.Exclusive or TriggerCardinality.ReservationOnly ||
                _reservationPolicies.ContainsKey(binding.StimulusType))
                .GroupBy(binding => (binding.StimulusType, binding.StimulusHash)).Select(group =>
                    new RuntimeIngressReservationDescriptor(group.Key.StimulusType, group.Key.StimulusHash, group.First().Metadata)).ToArray();
            claims = RuntimeIngressReservationRules.Prepare(scope, RuntimeIngressReservation.PublicationSlotOwner, slot.SlotId, slot.Revision, slot.Revision, descriptors);
            desired = EfRuntimeKernelScheduleControlStore.Describe(scope, slot, source, bindings);
            eventEntries = bindings.Where(binding => binding.StimulusType == RuntimeKernelEventPublication.ReservationType).ToArray();
            // Upgraded publication writers always compare semantics, including before standalone activation is finalized.
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
            inheritEventTenant = tenants.Length == 0;
        }
        var previous = await store.StageOwnerAsync(scope, RuntimeIngressReservation.PublicationSlotOwner, slot.SlotId,
            claims, ct, _reservationPolicies, adoptSchedules: onlySchedules);
        if (!onlySchedules)
            await new EfRuntimeKernelEventPublicationStore(_context, _accessContextAccessor)
                .StageAsync(scope, slot, source, eventEntries, inheritEventTenant, ct);
        if (desired.Length == 0 && !previous.Any(claim => claim.StimulusType == RuntimeKernelScheduleOccurrence.ReservationType))
            return null;

        var controls = new EfRuntimeKernelScheduleControlStore(_context, _accessContextAccessor);
        if (!desired.Any(control => control.Owner?.Continuity is not null))
        {
            await controls.StageAsync(persistence, slot.SlotId, desired, ct, adoptSchedules: adoptedSchedules is not null);
            return null;
        }

        // The first SaveChanges claims or moves the physical slot. Sample the Foundation clock with that claim held,
        // inside the transaction that will publish the sampled instant with its controls.
        return async token =>
        {
            await controls.StageAsync(persistence, slot.SlotId, desired, token, adoptSchedules: adoptedSchedules is not null,
                readCutover: ReadCutoverAsync);
            if (!_context.ChangeTracker.HasChanges()) return;
            var assignOrdinals = EfRuntimeKernelScheduleControlStore.AssignOrdinalsAfterSave(_context, persistence.Value);
            await _context.SaveChangesAsync(token);
            if (assignOrdinals is not null) await assignOrdinals(token);
        };

        async ValueTask<DateTimeOffset> ReadCutoverAsync(CancellationToken token)
        {
            var clock = _scheduleCutoverClock ?? throw new InvalidOperationException("Managed schedule publication requires the bound Actor Foundation clock.");
            if (!slotFenced && !await EfAdmissionTargetFence.LockSlotAsync(_context, persistence.Value, slot.WorkflowDefinitionId,
                    slot.SlotName, slot, token))
                throw new DbUpdateConcurrencyException("The schedule publication changed before its cutover clock could be sampled.");
            return await clock.ReadAsync(persistence, token);
        }
    }
}
