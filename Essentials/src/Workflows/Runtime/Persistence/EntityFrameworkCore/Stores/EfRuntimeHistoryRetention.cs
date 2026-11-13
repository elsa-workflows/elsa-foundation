using System.Data;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Private retention/deletion participants. The owner supplies the native lock, durable transaction
/// boundary and failure reconciliation; neither helper commits or acknowledges a mutation.
/// </summary>
internal static class EfRuntimeHistoryRetention
{
    private static readonly Type[] EntityTypes = [typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity),
        typeof(RuntimeHistoryBookmarkEntity), typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryActivityEntity),
        typeof(RuntimeHistoryOccurrenceEntity), typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity)];

    internal static async ValueTask<long> StagePurgeAsync(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
        string scope, string run, long throughSequence, EfSchemaChain chain, CancellationToken token)
    {
        if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        var ensureAccess = RequireBoundary(context, access, scope, run, token);
        // A pre-existing InnoDB repeatable-read view can remain older than the current native write
        // lock. A no-op purge has no head CAS to detect that stale database snapshot. The owner must
        // supply current committed reads or serializable access, not an earlier repeatable/snapshot view.
        if (context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw new NotSupportedException("History prefix retirement requires ReadCommitted or Serializable isolation.");
        // Do not use the affected-row count as existence proof: MySQL can report changed rows.
        await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, run, token);
        if (await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, run, token))
        { ensureAccess(); return 0; }
        var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, run);
        var nativeRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == nativeId, token);
        ensureAccess();
        if (nativeRow is null) return 0;
        var native = EfWorkflowExecutionStateStore.ReadChecked(nativeRow, scope, run);
        access.Current.EnsureTenantScope(native.TenantId);
        var id = Mapping.Id("head", scope, run);
        var row = await context.Set<RuntimeHistoryHeadEntity>().SingleOrDefaultAsync(item => item.Id == id, token);
        ensureAccess();
        if (row is null) return 0;
        var mapping = new Mapping(chain); var head = mapping.Read(row, scope, run);
        if (!head.Policy.IsEquivalentTo(native.HistoryCapturePolicy) || native.HistoryCaptureEnabledAtAdmission == false)
            throw new RuntimeHistoryIntegrityException(run, "retention policy disagrees with its native owner");
        var through = Math.Max(head.PurgedThroughSequence, Math.Min(throughSequence, head.LastSequence));
        if (through == head.PurgedThroughSequence) return through;
        await EfRuntimeHistoryCheckpoint.ReserveHeadAsync(context, mapping, chain, scope, run, row,
            head with { PurgedThroughSequence = through, FirstSequence = through == long.MaxValue ? through : through + 1 }, token);
        ensureAccess();
        // Retain original identity, position and fingerprint. Replay after purge must acknowledge the
        // same semantic event rather than recreating its content or spending a new position.
        await Owned<RuntimeHistoryEventEntity>(context, scope, run).Where(item => !item.IsPurged && item.Sequence <= through)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsPurged, true).SetProperty(item => item.ContentJson, "")
                .SetProperty(item => item.Revision, item => item.Revision + 1), token);
        DetachHistory(context, scope, run); ensureAccess(); return through;
    }

    /// <summary>
    /// Full native run retirement only. The caller has already locked/admitted that exact native
    /// deletion; prefix purge must never invoke this helper or remove its replay/membership receipts.
    /// </summary>
    internal static async ValueTask StageDeleteAsync(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
        string scope, string run, EfSchemaChain chain, CancellationToken token)
    {
        var ensureAccess = RequireBoundary(context, access, scope, run, token);
        if (EntityTypes.Any(type => !chain.ContentColumns.Any(column => column.Entity == type && column.Name == "ContentJson")))
            throw new InvalidOperationException("The owner's schema chain does not cover the complete history family.");
        EfRuntimeHistoryCheckpoint.EnsureWritable(context, chain);
        // A fixed number of set-based statements, with complete physical identities. Never load
        // the run into the tracker just to retire it; long histories consume no per-event memory.
        await Delete<RuntimeHistoryOccurrenceMembershipEntity>();
        await Delete<RuntimeHistoryOccurrenceEntity>();
        await Delete<RuntimeHistoryOccurrenceCounterEntity>();
        await Delete<RuntimeHistoryBookmarkEntity>();
        await Delete<RuntimeHistoryBookmarkCountEntity>();
        await Delete<RuntimeHistoryActivityEntity>();
        await Delete<RuntimeHistoryEventEntity>();
        await Delete<RuntimeHistoryHeadEntity>();
        DetachHistory(context, scope, run); ensureAccess();

        async Task Delete<TEntity>() where TEntity : class
        { ensureAccess(); await Owned<TEntity>(context, scope, run).ExecuteDeleteAsync(token); ensureAccess(); }
    }

    private static IQueryable<TEntity> Owned<TEntity>(RuntimeDbContext context, string scope, string run) where TEntity : class
    {
        var sk = Encode(scope); var sh = Hash(scope); var rk = Encode(run); var rh = Hash(run);
        return context.Set<TEntity>().Where(row => EF.Property<string>(row, "ScopeKeyHash") == sh && EF.Property<string>(row, "ScopeKey") == sk &&
            EF.Property<string>(row, "WorkflowExecutionIdHash") == rh && EF.Property<string>(row, "WorkflowExecutionId") == rk);
    }

    private static Action RequireBoundary(RuntimeDbContext context, IPersistenceAccessContextAccessor access, string scope, string run, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope); ArgumentException.ThrowIfNullOrWhiteSpace(run); token.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("History retention requires its owner's active transaction.");
        if (context.ChangeTracker.Entries().Any(entry => EntityTypes.Contains(entry.Metadata.ClrType) &&
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("History retention cannot mix pending history changes with set-based retirement.");
        var snapshot = access.Current;
        void EnsureAccess()
        {
            if (access.Current != snapshot || snapshot.RequireScope().Value != scope)
                throw new InvalidOperationException("History retention changed persistence access.");
        }
        EnsureAccess();
        // Tracking queries identity-resolve clean entities without refreshing them. An older purge
        // request must return the current database boundary even when it performs no reservation.
        DetachHistory(context, scope, run); return EnsureAccess;
    }

    private static void DetachHistory(RuntimeDbContext context, string scope, string run)
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => EntityTypes.Contains(entry.Metadata.ClrType) &&
            entry.Property("ScopeKey").CurrentValue as string == Encode(scope) && entry.Property("WorkflowExecutionId").CurrentValue as string == Encode(run)).ToArray())
            entry.State = EntityState.Detached;
    }
}
