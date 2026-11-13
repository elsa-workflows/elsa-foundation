using System.Runtime.ExceptionServices;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using IsolationLevel = System.Data.IsolationLevel;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// Owns independent history transactions. Native execution/lease and deletion authority stay with
// their existing owners; these ports only append observations or retire an existing captured prefix.
internal sealed class EfRuntimeHistoryMutationStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
    EfRuntimeHistoryStore reader, TimeProvider clock, EfSchemaChain chain) : IRuntimeHistoryObservationWriter, IRuntimeHistoryRetentionStore
{
    public bool CanRecordHistory(IRuntimeHistoryStore selectedStore)
    {
        if (!ReferenceEquals(reader, selectedStore) || !reader.Uses(context, access, chain)) return false;
        try { _ = access.Current.RequireScope(); return context.Database.IsRelational(); }
        catch (InvalidOperationException) { return false; }
    }

    public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        RuntimeHistorySafety.ValidateObservation(observation);
        return Execute<RuntimeHistoryObservationResult>(observation.Event.WorkflowExecutionId, "record-observation",
            scope => EfRuntimeHistoryObservation.StageAsync(context, access, observation, scope, clock.GetUtcNow(), chain, cancellationToken),
            async _ =>
            {
                var retained = await reader.FindObservationReceiptAsync(observation, cancellationToken);
                return retained is null ? (false, new RuntimeHistoryObservationResult(false, null)) : (true, retained);
            }, cancellationToken);
    }

    public ValueTask<long> PurgeThroughAsync(string workflowExecutionId, long throughSequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        return Execute<long>(workflowExecutionId, "purge-through",
            scope => EfRuntimeHistoryRetention.StagePurgeAsync(context, access, scope, workflowExecutionId, throughSequence, chain, cancellationToken),
            async stagedThrough =>
            {
                // Reconcile only the boundary this attempt actually staged. Never retry a MaxValue
                // request against later arrivals, which would silently broaden the original purge.
                var head = await reader.FindHeadAsync(workflowExecutionId, cancellationToken);
                if (head is null) return (stagedThrough == 0, 0L);
                return (head.PurgedThroughSequence >= stagedThrough, head.PurgedThroughSequence);
            }, cancellationToken);
    }

    private async ValueTask<T> Execute<T>(string run, string operation, Func<string, ValueTask<T>> stage,
        Func<T, ValueTask<(bool Found, T Value)>> reconcile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var captured = access.Current; var scope = captured.RequireScope().Value;
        if (!reader.Uses(context, access, chain) || !context.Database.IsRelational() || context.ChangeTracker.HasChanges() ||
            context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null || context.Database.GetEnlistedTransaction() is not null)
            throw new InvalidOperationException("History mutations require the same owning reader and a clean relational context without an existing transaction.");
        void Recheck()
        {
            if (!ReferenceEquals(access.Current, captured)) throw new InvalidOperationException("Persistence access changed during the history mutation.");
            _ = captured.RequireScope(); token.ThrowIfCancellationRequested();
        }
        Recheck();
        // Invalidate only this run's clean internal history snapshots before tracking reads can
        // identity-resolve an older head. Preserve unrelated caller-owned tracked native state.
        EfRuntimeHistoryRetention.DetachHistory(context, scope, run);
        var callerEntities = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        IDbContextTransaction? transaction = null; Exception? original = null; Exception? cause = null;
        T staged = default!; var commitRequested = false;
        try
        {
            transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token); Recheck();
            staged = await stage(scope); Recheck();
            await context.SaveChangesAsync(token); Recheck();
            commitRequested = true;
            await transaction.CommitAsync(token); Recheck();
        }
        catch (Exception error) { original = cause = error; }
        finally
        {
            if (transaction is not null)
            {
                if (original is not null)
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); }
                    catch (Exception cleanup) { cause = new AggregateException(cause!, cleanup); }
                }
                try { await transaction.DisposeAsync(); }
                catch (Exception cleanup)
                {
                    original ??= cleanup;
                    cause = cause is null ? cleanup : new AggregateException(cause, cleanup);
                }
            }
            // No pending native/history work may leak into a later caller SaveChanges, including
            // after an interrupted commit whose durable outcome will be read below.
            foreach (var entry in context.ChangeTracker.Entries().Where(entry => !callerEntities.Contains(entry.Entity)).ToArray())
                entry.State = EntityState.Detached;
        }
        Recheck();
        if (original is null) return staged;
        if (commitRequested && EfRuntimeHistoryCheckpoint.IsProviderUnavailable(original, token))
        {
            try
            {
                var retained = await reconcile(staged); Recheck();
                if (retained.Found) return retained.Value;
            }
            catch (Exception lookup) when (EfRuntimeHistoryCheckpoint.IsProviderUnavailable(lookup, token) || lookup is RuntimeHistoryUnavailableException)
            { throw new RuntimeHistoryUnavailableException(run, operation, new AggregateException(cause!, lookup)); }
        }
        if (original is InvalidDataException)
            throw new RuntimeHistoryIntegrityException(run, "retained history mutation data could not be interpreted", original);
        if (original is EfRuntimeHistoryCheckpoint.HeadAllocationConflictException || EfRuntimeHistoryCheckpoint.IsProviderUnavailable(original, token))
            throw new RuntimeHistoryUnavailableException(run, operation, cause!);
        ExceptionDispatchInfo.Capture(original).Throw();
        throw new InvalidOperationException("The history mutation did not produce an outcome.");
    }
}
