using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class InMemoryTriggerSourceResourceCommitStore(IStandaloneTriggerStore resources, IRuntimeIngressDeliveryStore ingress,
    IWorkflowActivationAuthority slots, IWorkflowActivationSwitch activationSwitch, IWorkflowTriggerBindingStore? bindings,
    InMemoryRuntimeCheckpointStoreState state, IPersistenceAccessContextAccessor access, TimeProvider clock,
    IEnumerable<IStandaloneSchedulePlanner> schedulePlanners, IRecurringTriggerScheduleStore? schedules = null,
    TriggerManagementTargets? targets = null, IEnumerable<IIngressReservationPolicy>? reservationPolicies = null) : ITriggerSourceResourceCommitStore
{
    private readonly IReadOnlyDictionary<string, IIngressReservationPolicy> policies = RuntimeIngressReservationRules.Policies(reservationPolicies);
    public bool SupportsActivatedResources => resources is InMemoryStandaloneTriggerStore store && ReferenceEquals(store.State, state) &&
        ingress.SupportsAdmission && slots is InMemoryWorkflowActivationAuthority && bindings is InMemoryWorkflowTriggerBindingStore &&
        activationSwitch is InMemoryWorkflowActivationSwitch selected && RuntimeIngressReservationRules.SamePolicies(selected.ReservationPolicies, policies) && ReferenceEquals(selected.ReservationState, state) &&
        ReferenceEquals(selected.ReservationAuthority, slots) && ReferenceEquals(selected.ReservationBindings, bindings) &&
        ReferenceEquals(selected.ReservationReferences, targets?.Sources) &&
        targets is not null && ReferenceEquals(targets.Slots, slots) && targets.Executables is InMemoryWorkflowExecutableStore material &&
        targets.Sources is InMemoryWorkflowExecutableSourceReferenceStore sources && ReferenceEquals(sources.FencedArtifactStore, material);

    public async ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write,
        IReadOnlyList<RuntimeIngressReservationDescriptor> reservations, CancellationToken cancellationToken = default)
    {
        access.Current.RequireScope(); access.Current.EnsureScope(scope.Persistence);
        if (resources is not InMemoryStandaloneTriggerStore store || !ReferenceEquals(store.State, state))
            throw new NotSupportedException("Resource and source projections require the same native state.");
        if ((write.Trigger.HasActivated || write.Trigger.DesiredState == TriggerDesiredState.Enabled || reservations.Count != 0) && !SupportsActivatedResources)
            throw new NotSupportedException("This native composition cannot protect activated trigger sources.");
        var claims = RuntimeIngressReservationRules.Prepare(scope, RuntimeIngressReservation.StandaloneTriggerOwner, write.Trigger.Id,
            write.Trigger.Revision, write.Trigger.ConfigurationRevision, reservations);
        return await state.ExecuteWriteGateAsync(_ =>
        {
            // Reconcile the immutable mutation before current publication eligibility, including after unpublication.
            lock (state.SyncRoot)
                if (state.TriggerMutations.TryGetValue((scope.Persistence.Value, scope.TenantId, write.ActorId, write.RequestId), out var accepted))
                {
                    if (accepted.RequestFingerprint != write.RequestFingerprint || write.ExpectedRevision is not null && accepted.TriggerId != write.Trigger.Id)
                        throw new TriggerOperationException("conflict", "The mutation identity already names different content.");
                    return ValueTask.FromResult(accepted);
                }
            TriggerMutationReceipt Commit()
            {
                lock (state.SyncRoot)
                {
                    if (state.TriggerMutations.TryGetValue((scope.Persistence.Value, scope.TenantId, write.ActorId, write.RequestId), out var replay))
                    {
                        if (replay.RequestFingerprint != write.RequestFingerprint || write.ExpectedRevision is not null && replay.TriggerId != write.Trigger.Id)
                            throw new TriggerOperationException("conflict", "The mutation identity already names different content.");
                        return replay;
                    }
                    RuntimeIngressReservationRules.CheckSemanticCandidates(claims, policies);
                    foreach (var claim in claims)
                    {
                        foreach (var prior in state.IngressReservations.Values)
                            RuntimeIngressReservationRules.CheckSemanticOwner(prior, claim, policies);
                        if (state.IngressReservations.TryGetValue(claim.ReservationId, out var existing)) RuntimeIngressReservationRules.CheckOwner(existing, claim);
                        if (bindings is InMemoryWorkflowTriggerBindingStore sourceBindings && RuntimeIngressReservationRules.ConflictingBindingsAsync(sourceBindings, claim, policies)
                            .AsTask().GetAwaiter().GetResult().Any(binding => !KnownOtherScope(binding)))
                            throw new TriggerOperationException("route-conflict", "A published workflow already owns this endpoint.");
                    }
                    var receipt = store.StageWrite(scope, write, cancellationToken);
                    var retained = claims.Select(claim => claim.ReservationId).ToHashSet(StringComparer.Ordinal);
                    var removed = state.IngressReservations.Values.Where(claim => claim.Scope == scope && claim.OwnerKind == RuntimeIngressReservation.StandaloneTriggerOwner &&
                        claim.OwnerId == write.Trigger.Id && !retained.Contains(claim.ReservationId)).Select(claim => claim.ReservationId).ToArray();
                    foreach (var id in removed) state.IngressReservations.Remove(id);
                    foreach (var claim in claims) state.IngressReservations[claim.ReservationId] = claim;
                    return receipt;
                }
            }
            TriggerMutationReceipt Project()
            {
                TriggerAggregate? previous;
                lock (state.SyncRoot) previous = state.StandaloneTriggers.GetValueOrDefault((scope.Persistence.Value, scope.TenantId, write.Trigger.Id));
                if (targets is not null) targets.CheckAsync(scope, previous, write, cancellationToken).GetAwaiter().GetResult();
                else if (TriggerManagementTargets.Selectors(previous, write).Count != 0)
                    throw new NotSupportedException("Protected association changes require their selected native target stores.");
                var planners = schedulePlanners.Where(planner => planner.Supports(write.Trigger.TypeKey)).ToArray();
                if (planners.Length > 1) throw new InvalidOperationException("Several modules own this schedule type.");
                if (write.Trigger.TypeKey is "timer" or "cron" &&
                    (write.Trigger.HasActivated || previous?.Trigger.HasActivated == true || write.Trigger.DesiredState == TriggerDesiredState.Enabled))
                {
                    if (schedules is not InMemoryRecurringTriggerScheduleStore scheduleStore || planners.Length != 1)
                        throw new NotSupportedException("Resource schedules require their actual native schedule provider and planner.");
                    return scheduleStore.ExecuteStandaloneScheduleWrite(scope, write, previous, planners[0], clock, Commit);
                }
                return Commit();
            }
            TriggerMutationReceipt LockedBindings()
            {
                if (bindings is not IInMemoryActivationProjection projection) return Project();
                lock (projection.SyncRoot) return Project();
            }
            TriggerMutationReceipt LockedAuthority()
            {
                if (slots is not InMemoryWorkflowActivationAuthority authority) return Project();
                lock (authority.Gate) return LockedBindings();
            }
            if (targets?.Executables is InMemoryWorkflowExecutableStore material &&
                targets.Sources is InMemoryWorkflowExecutableSourceReferenceStore references && ReferenceEquals(references.FencedArtifactStore, material))
                return ValueTask.FromResult(material.ExecuteUnderMaterialGate(_ => references.ExecuteUnderReferenceGate(_ => LockedAuthority())));
            return ValueTask.FromResult(LockedAuthority());

            bool KnownOtherScope(WorkflowTriggerBinding binding) => binding.SlotId is not null && state.IngressReservations.Values.Any(claim =>
                claim.OwnerKind == RuntimeIngressReservation.PublicationSlotOwner && claim.OwnerId == binding.SlotId &&
                claim.StimulusType == binding.StimulusType && claim.StimulusHash == binding.StimulusHash && claim.Scope.Persistence != scope.Persistence);
        }, cancellationToken);
    }
}
