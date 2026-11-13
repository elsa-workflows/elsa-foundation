using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Private independent-observation staging. Source authorization and caller-owned transaction,
/// final save, acknowledgement reconciliation and provider normalization remain with the eventual owner.
/// This does not execute work, create capture coverage or enable a provider.
/// </summary>
internal static class EfRuntimeHistoryObservation
{
    internal static async ValueTask<RuntimeHistoryObservationResult> StageAsync(RuntimeDbContext context,
        IPersistenceAccessContextAccessor access, RuntimeHistoryObservation observation, string scope,
        DateTimeOffset recordedAt, EfSchemaChain chain, CancellationToken cancellationToken)
    {
        RuntimeHistorySafety.ValidateObservation(observation);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Observation staging requires its owner's active transaction.");
        var snapshot = access.Current;
        void EnsureAccess()
        {
            if (access.Current != snapshot || snapshot.RequireScope().Value != scope)
                throw new InvalidOperationException("Observation staging changed persistence access.");
        }
        EnsureAccess();
        var item = observation.Event; var run = item.WorkflowExecutionId;
        // A provider may report changed rows for this no-op lock. The checked native read, not
        // that count, establishes whether the owning execution is present.
        await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, run, cancellationToken);
        if (await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, run, cancellationToken))
        { EnsureAccess(); return new(false, null); }
        var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, run);
        var nativeRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == nativeId, cancellationToken);
        if (nativeRow is null) { EnsureAccess(); return new(false, null); }
        var native = EfWorkflowExecutionStateStore.ReadChecked(nativeRow, scope, run);
        snapshot.EnsureTenantScope(native.TenantId);
        var mapping = new EfRuntimeHistoryProjectionMapping(chain);
        var headId = EfRuntimeHistoryProjectionMapping.Id("head", scope, run);
        var headRow = await context.Set<RuntimeHistoryHeadEntity>().SingleOrDefaultAsync(row => row.Id == headId, cancellationToken);
        EnsureAccess();
        // Association and admission alone do not prove a previously committed captured checkpoint.
        if (headRow is null) return new(false, null);
        var head = mapping.Read(headRow, scope, run);
        if (!head.Policy.IsEquivalentTo(observation.Policy) || !head.Policy.IsEquivalentTo(native.HistoryCapturePolicy) ||
            native.HistoryCaptureEnabledAtAdmission == false)
            throw Invalid("observation policy disagrees with its retained native owner");
        var fingerprint = Fingerprint(item);
        var eventId = EfRuntimeHistoryProjectionMapping.Id("event", scope, run, item.HistoryEventId);
        var retainedRow = await context.Set<RuntimeHistoryEventEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);
        EnsureAccess();
        if (retainedRow is not null)
        {
            var receipt = mapping.ReadReceipt(retainedRow, scope, run, head);
            if (receipt.HistoryEventId != item.HistoryEventId || receipt.CandidateFingerprint != fingerprint)
                throw Invalid("an immutable observation has conflicting content");
            if (receipt.IsPurged) return new(true, null);
            var retained = mapping.Read(retainedRow, scope, run, head);
            if (retained.PhysicalCommitId != "owner-observation:" + item.HistoryEventId || Fingerprint(retained) != fingerprint)
                throw Invalid("a retained observation has conflicting source content or physical ownership");
            return new(true, retained);
        }
        var stored = RuntimeHistoryIdentity.Position(item, checked(head.LastSequence + 1), "owner-observation:" + item.HistoryEventId, recordedAt);
        // The last covered native physical checkpoint/time stays exact; this append owns only its observation.
        var next = head with { LastSequence = stored.Sequence,
            FirstSequence = head.FirstSequence == 0 || head.FirstSequence > head.LastSequence ? stored.Sequence : head.FirstSequence };
        EnsureAccess();
        await EfRuntimeHistoryCheckpoint.ReserveHeadAsync(context, mapping, chain, scope, run, headRow, next, cancellationToken);
        context.Set<RuntimeHistoryEventEntity>().Add(mapping.Row(scope, stored, fingerprint));
        // Even a non-occurrence advances the derived covered prefix, never its execution ordinal count.
        await EfRuntimeHistoryOccurrenceProjection.StageAsync(context, scope, run, chain, [stored], cancellationToken);
        EnsureAccess(); return new(true, stored);

        RuntimeHistoryIntegrityException Invalid(string reason) => new(run, reason);
    }

    // A bounded, purpose-separated64-character receipt fits all providers and cannot masquerade as
    // a native candidate fingerprint. The event's original time/identity remain part of its semantic hash.
    internal static string Fingerprint(RuntimeHistoryEvent item) =>
        RuntimeHistoryIdentity.CreateBinding(["runtime.history.observation.v1", RuntimeHistoryIdentity.Compute(item)]);
}
