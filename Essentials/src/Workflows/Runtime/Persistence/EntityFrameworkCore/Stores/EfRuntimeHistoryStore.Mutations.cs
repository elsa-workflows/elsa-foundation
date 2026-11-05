using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistorySupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeHistoryStore
{
    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution. An interrupted commit may require replay.</exception>
    public async ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var e = observation.Event;
        RuntimeHistorySafety.ValidateMetadata(e);
        if (e.Authority != RuntimeHistoryAuthority.DeliveryObservation || e.Sequence != 0 || e.PhysicalCommitId is not null || e.RecordedAt is not null ||
            e.CheckpointOrdinal is not null || e.SchemaVersion != 1 || !observation.Policy.SupportedKinds.Contains(e.Name, StringComparer.Ordinal))
            throw new ArgumentException("An ingress observation must declare its own unpositioned, admitted delivery authority.", nameof(observation));
        EnsureCleanMutation();
        var scope = Scope; var run = e.WorkflowExecutionId; var fingerprint = RuntimeHistoryIdentity.Compute(e);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (!await GuardExistingRunAsync(scope, run, observation.Policy, cancellationToken)) return new(false, null);
                    var headId = Id("head", scope, run);
                    var headRow = await context.RuntimeHistoryHeads.SingleOrDefaultAsync(x => x.Id == headId, cancellationToken);
                    // Association alone cannot create capture coverage for a legacy or not-yet-committed run.
                    if (headRow is null) return new(false, null);
                    var head = Read(headRow, scope, run);
                    if (!head.Policy.IsEquivalentTo(observation.Policy)) throw new InvalidOperationException("Observation policy differs from the admitted history policy.");
                    var eventId = Id("event", scope, run, e.HistoryEventId);
                    var old = await context.RuntimeHistoryEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
                    if (old is not null)
                    {
                        if (ReadReceipt(old, scope, run, head) != e.HistoryEventId || old.CandidateFingerprint != fingerprint)
                            throw new InvalidOperationException("The retained observation identity has conflicting content.");
                        var existing = old.IsPurged ? null : Read(old, scope, run, head);
                        await transaction.CommitAsync(cancellationToken);
                        return new(true, existing);
                    }
                    var stored = RuntimeHistoryIdentity.Position(e, checked(head.LastSequence + 1), "observation:" + Hash(e.HistoryEventId), _timeProvider.GetUtcNow());
                    context.RuntimeHistoryEvents.Add(EventRow(scope, stored, fingerprint));
                    // Preserve the last covered runtime checkpoint and its recorded instant. Delivery observations own
                    // their own durable boundary and never claim that another runtime transition was captured.
                    Update(headRow, head with { FirstSequence = head.FirstSequence == 0 ? stored.Sequence : head.FirstSequence, LastSequence = stored.Sequence });
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new(true, stored);
                }
                catch (Exception exception) when (attempt < 2 && EfRuntimeHistoryCheckpoint.IsHeadConflict(exception))
                { await transaction.RollbackAsync(cancellationToken); }
                finally { context.ChangeTracker.Clear(); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new RuntimeHistoryEntityFrameworkPersistenceException("recording a delivery observation", scope, run, exception); }
    }

    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution. An interrupted commit may require replay.</exception>
    public async ValueTask<long> PurgeThroughAsync(string workflowExecutionId, long throughSequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        EnsureCleanMutation();
        var scope = Scope;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (!await GuardExistingRunAsync(scope, workflowExecutionId, null, cancellationToken)) return 0;
                var id = Id("head", scope, workflowExecutionId);
                var row = await context.RuntimeHistoryHeads.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (row is null) return 0;
                var head = Read(row, scope, workflowExecutionId);
                var through = Math.Max(head.PurgedThroughSequence, Math.Min(throughSequence, head.LastSequence));
                if (through == head.PurgedThroughSequence) return through;
                EnsureWritable(context);
                // Retain compact semantic receipts through run deletion. Removing the identity/fingerprint would let
                // a delayed logical event under another physical commit resurrect content at a new sequence.
                var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(workflowExecutionId); var rk = Encode(workflowExecutionId);
                await context.RuntimeHistoryEvents.Where(x => x.ScopeKeyHash == sh && x.ScopeKey == sk && x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk &&
                    !x.IsPurged && x.Sequence <= through).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsPurged, true)
                        .SetProperty(x => x.ContentJson, "").SetProperty(x => x.Revision, x => x.Revision + 1), cancellationToken);
                Update(row, head with { PurgedThroughSequence = through, FirstSequence = through == long.MaxValue ? through : through + 1 });
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return through;
            }
            finally { context.ChangeTracker.Clear(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new RuntimeHistoryEntityFrameworkPersistenceException("purging retained history", scope, workflowExecutionId, exception); }
    }

    private async Task<bool> GuardExistingRunAsync(string scope, string run, RuntimeHistoryCapturePolicy? policy, CancellationToken cancellationToken)
    {
        if (await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, run, cancellationToken) == 0 ||
            await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, run, cancellationToken)) return false;
        var id = EfWorkflowExecutionStateStore.CreateId(scope, run);
        var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return false;
        var state = EfWorkflowExecutionStateStore.ReadChecked(row, scope, run);
        access.Current.EnsureTenantScope(state.TenantId);
        if (policy is not null && !policy.IsEquivalentTo(state.HistoryCapturePolicy))
            throw new InvalidOperationException("Delivery observations require the execution's admitted capture policy.");
        return true;
    }
    private void EnsureCleanMutation()
    {
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new InvalidOperationException("An independent history mutation requires a clean context and its own durable boundary.");
        foreach (var entry in context.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
    }
}
