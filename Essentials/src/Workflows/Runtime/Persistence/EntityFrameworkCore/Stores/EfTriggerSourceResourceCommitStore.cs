using System.Data;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using IsolationLevel = System.Data.IsolationLevel;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfTriggerSourceResourceCommitStore(RuntimeDbContext context, IStandaloneTriggerStore resources,
    IRuntimeIngressDeliveryStore ingress, IWorkflowActivationSwitch activationSwitch, IWorkflowTriggerBindingStore bindings,
    IPersistenceAccessContextAccessor access, TimeProvider clock, IEnumerable<IStandaloneSchedulePlanner> schedulePlanners,
    IRecurringTriggerScheduleStore? schedules = null, TriggerManagementTargets? targets = null,
    IEnumerable<IIngressReservationPolicy>? reservationPolicies = null) : ITriggerSourceResourceCommitStore
{
    private readonly IReadOnlyDictionary<string, IIngressReservationPolicy> policies = RuntimeIngressReservationRules.Policies(reservationPolicies);
    public bool SupportsActivatedResources => resources is EfStandaloneTriggerStore store && ReferenceEquals(store.Context, context) &&
        RuntimeSchemaWriteVersion.SupportsIngress(context) && ingress.SupportsAdmission &&
        activationSwitch is EfWorkflowActivationSwitch selected && RuntimeIngressReservationRules.SamePolicies(selected.ReservationPolicies, policies) && ReferenceEquals(selected.ReservationContext, context) &&
        bindings is EfWorkflowTriggerBindingStore selectedBindings && ReferenceEquals(selectedBindings.Context, context) &&
        targets?.Slots is EfWorkflowActivationAuthority authority && ReferenceEquals(authority.Context, context) &&
        targets.Executables is EfWorkflowExecutableStore material && ReferenceEquals(material.Context, context) &&
        targets.Sources is EfWorkflowExecutableSourceReferenceStore references && ReferenceEquals(references.Context, context);

    /// <exception cref="TriggerOperationException">Stored-data/provider failure; preserves scope/resource identity and the original cause.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates unchanged.</exception>
    public ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write,
        IReadOnlyList<RuntimeIngressReservationDescriptor> reservations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Trigger); ArgumentNullException.ThrowIfNull(reservations);
        cancellationToken.ThrowIfCancellationRequested();
        return EfTriggerBoundary.RunAsync(() => CommitCoreAsync(scope, write, reservations, cancellationToken), scope.Persistence.Value, write.Trigger.Id);
    }

    private async ValueTask<TriggerMutationReceipt> CommitCoreAsync(TriggerScope scope, TriggerStoreWrite write,
        IReadOnlyList<RuntimeIngressReservationDescriptor> reservations, CancellationToken cancellationToken)
    {
        access.Current.RequireScope(); access.Current.EnsureScope(scope.Persistence);
        if (resources is not EfStandaloneTriggerStore store || !ReferenceEquals(store.Context, context))
            throw new NotSupportedException("Resource and source projections require the same relational context.");
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new InvalidOperationException("A protected source change requires its own clean transaction.");
        if ((write.Trigger.HasActivated || write.Trigger.DesiredState == TriggerDesiredState.Enabled || reservations.Count != 0) && !SupportsActivatedResources)
            throw new NotSupportedException("This relational composition cannot protect activated trigger sources.");
        var claims = RuntimeIngressReservationRules.Prepare(scope, RuntimeIngressReservation.StandaloneTriggerOwner, write.Trigger.Id,
            write.Trigger.Revision, write.Trigger.ConfigurationRevision, reservations);
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var replay = await store.FindMutationAsync(scope, write.ActorId, write.RequestId, cancellationToken);
                if (replay is not null)
                {
                    if (replay.RequestFingerprint != write.RequestFingerprint || write.ExpectedRevision is not null && replay.TriggerId != write.Trigger.Id)
                        throw new TriggerOperationException("conflict", "The mutation identity already names different content.");
                    await transaction.CommitAsync(cancellationToken); return replay;
                }
                var previous = await store.FindAsync(scope, write.Trigger.Id, cancellationToken);
                if (targets is not null)
                {
                    var actualTargets = await targets.CheckAsync(scope, previous, write, cancellationToken);
                    for (var index = 0; index < actualTargets.Count; index++)
                    {
                        var observed = write.ObservedTargets![index]; var actual = actualTargets[index];
                        if (!await EfAdmissionTargetFence.LockSlotAsync(context, scope.Persistence.Value, observed.DefinitionId,
                                observed.SlotName, actual.Slot, cancellationToken) ||
                            actual.Source is { } source && !await EfAdmissionTargetFence.LockSourceAsync(
                                (EfWorkflowExecutableSourceReferenceStore)targets.Sources, scope.Persistence.Value, source, cancellationToken))
                            throw new TriggerOperationException("conflict", "A reviewed target publication changed before the resource commit.");
                    }
                }
                else if (TriggerManagementTargets.Selectors(previous, write).Count != 0)
                    throw new NotSupportedException("Protected association changes require their selected relational target stores.");
                foreach (var claim in claims)
                    if ((await RuntimeIngressReservationRules.ConflictingBindingsAsync(bindings, claim, policies, cancellationToken)).Count != 0)
                        throw new TriggerOperationException("route-conflict", "A published workflow already owns this endpoint.");
                var reservationStore = new EfIngressReservationStore(context, access);
                await reservationStore.StageOwnerAsync(scope, RuntimeIngressReservation.StandaloneTriggerOwner, write.Trigger.Id, claims, cancellationToken, policies);
                var planners = schedulePlanners.Where(planner => planner.Supports(write.Trigger.TypeKey)).ToArray();
                if (planners.Length > 1) throw new InvalidOperationException("Several modules own this schedule type.");
                if ((write.Trigger.TypeKey is "timer" or "cron") &&
                    (write.Trigger.HasActivated || previous?.Trigger.HasActivated == true || write.Trigger.DesiredState == TriggerDesiredState.Enabled))
                {
                    if (schedules is not EfRecurringTriggerScheduleStore scheduleStore || !ReferenceEquals(scheduleStore.Context, context) || planners.Length != 1)
                        throw new NotSupportedException("Resource schedules require the selected relational schedule provider and planner.");
                    await scheduleStore.StageStandaloneScheduleWriteAsync(scope, write, previous, planners[0], clock, cancellationToken);
                }
                var receipt = await store.StageWriteAsync(scope, write, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return receipt;
            }
            catch (Exception exception) when (attempt < 3 && EfRelationalExceptionClassifier.IsSaveConflict(exception,
                EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
            { await context.RollbackAndClearAsync(transaction); }
            finally { context.ChangeTracker.Clear(); }
        }
    }
}
