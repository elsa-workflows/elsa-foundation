using System.Text.Json;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core scheduler-poison store (R23).</summary>
/// <remarks>
/// <para>
/// Poison identity is the scoped pair (workflow execution ID, work-item ID). The JSON envelope is authoritative;
/// relational projections provide bounded lookup and deterministic failure-window ordering. Replacements use the
/// row revision as an optimistic compare-and-swap token and never fall back to an unconditional update.
/// </para>
/// <para>
/// It can record under the run's ownership fence (<see cref="IWorkflowSchedulerFencedPoisonStore"/>) only when the
/// ownership row is in its own database: the registered <see cref="IExecutionLivenessStateStore"/> is the
/// <see cref="EfExecutionLivenessStateStore"/> over the same context and access scope. Scheduler poison is
/// registered separately from operational state, so a host can pair this store with ownership kept elsewhere; then
/// <see cref="CanRecordFenced"/> is <see langword="false"/>.
/// </para>
/// </remarks>
public sealed class EfWorkflowSchedulerPoisonStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowSchedulerPoisonStore, IWorkflowSchedulerFencedPoisonStore
{
    private static readonly EfWriteRetry Records = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey);
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;
    private readonly Func<IExecutionLivenessStateStore?>? _ownershipStateStore;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// Creates the store with a way to find the ownership store the runtime composition registers, so it can tell
    /// whether the ownership row is in its own database. <paramref name="ownershipStateStore"/> is called when a
    /// caller asks for a fenced record, not when this store is built: reading and unfenced recording do not need the
    /// ownership store, or what it depends on, to be resolvable.
    /// </summary>
    public EfWorkflowSchedulerPoisonStore(
        RuntimeDbContext context,
        IPersistenceAccessContextAccessor accessContextAccessor,
        Func<IExecutionLivenessStateStore?> ownershipStateStore,
        TimeProvider? timeProvider = null)
        : this(context, accessContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(ownershipStateStore);
        _ownershipStateStore = ownershipStateStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool CanRecordFenced =>
        _ownershipStateStore?.Invoke() is EfExecutionLivenessStateStore ownership && ownership.UsesSameBackingStore(context, accessContextAccessor);

    /// <inheritdoc />
    public async ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(
        RuntimeSchedulerPoisonRecord record,
        RuntimeExecutionFence fence,
        CancellationToken cancellationToken = default)
    {
        ValidateRecord(record);
        ArgumentNullException.ThrowIfNull(fence);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRecordFenced)
            throw new NotSupportedException("This scheduler-poison store cannot check an execution fence: the registered execution-liveness store does not keep ownership in this store's database.");
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, record.WorkflowExecutionId, record.WorkItemId);
        var fenceRowId = EfRuntimeOperationalStoreSupport.CompositeId(
            scope, record.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(record.WorkflowExecutionId));

        // A caller that already has a transaction on this context keeps it: the fence check and the record join it
        // and commit or roll back with it. Otherwise they get a transaction of their own.
        var ownsTransaction = context.Database.CurrentTransaction is null &&
            context.Database.GetEnlistedTransaction() is null && Transaction.Current is null;

        // The owner's lease heartbeat bumps the fence row's revision without changing the lease, as it does under a
        // checkpoint commit (EfRuntimeCheckpointCommitStore.CommitFencedAsync). Run again only after a conflict on
        // that row alone, and only when this call owns the whole unit of work. Each attempt reads the row and
        // validates the fence again, so a real takeover still fails as stale.
        var retryable = ownsTransaction && !context.ChangeTracker.HasChanges();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RecordUnderFenceAsync();
            }
            catch (DbUpdateConcurrencyException exception) when (retryable &&
                attempt < EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts &&
                EfRuntimeCheckpointParticipantStaging.IsFenceRenewalConflict(exception))
            {
            }
        }

        async ValueTask<RuntimeSchedulerPoisonRecord> RecordUnderFenceAsync()
        {
            WorkflowSchedulerPoisonEntity? staged = null;
            await using IDbContextTransaction? transaction = ownsTransaction
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                // Read the fence row as it is now. A copy this context tracks from earlier work is a snapshot, and a
                // tracked query would hand that snapshot back instead of the row.
                DetachOwnershipRow(fenceRowId, unchangedOnly: true);
                await EfRuntimeCheckpointParticipantStaging.StageExecutionFenceAsync(
                    context, scope, record.WorkflowExecutionId, fence, _timeProvider, cancellationToken);

                var row = await context.WorkflowSchedulerPoisonRecords.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
                if (row is null)
                {
                    staged = ToEntity(record, scope, id, 1);
                    context.WorkflowSchedulerPoisonRecords.Add(staged);
                }
                else
                {
                    _ = ReadChecked(row, scope, record.WorkflowExecutionId, record.WorkItemId);
                    staged = ToEntity(record, scope, id, checked(row.Revision + 1));
                    AttachForUpdate(staged, row.Revision);
                }

                // One save writes the fence touch and the record, so they commit together or not at all.
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return record;
            }
            catch
            {
                if (transaction is not null)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Keep the failure that got here. Disposing the transaction still rolls it back.
                    }
                }

                throw;
            }
            finally
            {
                // Leave nothing of this unit tracked, whether it committed, was refused or failed. A later save on
                // the shared context must not write the record again, and must not reuse this fence row's revision.
                if (staged is not null)
                    Detach(staged);
                DetachOwnershipRow(fenceRowId, unchangedOnly: false);
            }
        }
    }

    public async ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(
        RuntimeSchedulerPoisonRecord record,
        CancellationToken cancellationToken = default)
    {
        ValidateRecord(record);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, record.WorkflowExecutionId, record.WorkItemId);

        // An insert retries the unique-key or revision race a concurrent recorder wins; a replacement only its revision race.
        return await Records.RunUntilSettledAsync<RuntimeSchedulerPoisonRecord>(context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await context.WorkflowSchedulerPoisonRecords.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
            {
                var inserted = ToEntity(record, scope, id, 1);
                context.WorkflowSchedulerPoisonRecords.Add(inserted);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    Detach(inserted);
                    return record;
                }
                catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) || exception is OperationCanceledException)
                {
                    // A provider failure can leave the failed Added entity tracked even though its transaction was
                    // rolled back. Detach it before the caller can stage another shared-context participant; the
                    // failed poison write must never be retried implicitly by a later SaveChanges call.
                    Detach(inserted);
                    if (Records.ShouldRetry(context, exception))
                        return EfWriteAttempt<RuntimeSchedulerPoisonRecord>.Retry(exception);
                    throw;
                }
            }

            _ = ReadChecked(row, scope, record.WorkflowExecutionId, record.WorkItemId);
            var replacement = ToEntity(record, scope, id, checked(row.Revision + 1));
            AttachForUpdate(replacement, row.Revision);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                Detach(replacement);
                return record;
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) || exception is OperationCanceledException)
            {
                // Keep the shared context usable after a generic provider failure, just as after a CAS conflict.
                Detach(replacement);
                if (exception is DbUpdateConcurrencyException)
                    return EfWriteAttempt<RuntimeSchedulerPoisonRecord>.Retry(exception);
                throw;
            }
        }, _ => throw new InvalidOperationException(
            $"Recording scheduler poison record '{record.WorkItemId}' in workflow execution '{record.WorkflowExecutionId}' did not settle after {Records.MaxAttempts} compare-and-swap attempts."), cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(
        string workflowExecutionId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        ValidateWorkItemIdentity(workItemId, nameof(workItemId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, workItemId);
        // Query by the physical scoped identity alone so a tampered scope/projection is rejected by ReadChecked
        // instead of being silently hidden as a missing record.
        var row = await context.WorkflowSchedulerPoisonRecords.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, workflowExecutionId, workItemId);
    }

    public async ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var query = context.WorkflowSchedulerPoisonRecords.AsNoTracking().Where(row =>
            row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
            row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash);
        var records = new List<RuntimeSchedulerPoisonRecord>();
        var hasCursor = false;
        long lastFirstFailedAt = 0;
        long lastLastFailedAt = 0;
        string? lastWorkItemOrderKey = null;
        string? lastWorkItemHash = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = query;
            if (hasCursor)
            {
                page = page.Where(row =>
                    row.FirstFailedAtUtcTicks > lastFirstFailedAt ||
                    row.FirstFailedAtUtcTicks == lastFirstFailedAt &&
                    (row.LastFailedAtUtcTicks > lastLastFailedAt ||
                     row.LastFailedAtUtcTicks == lastLastFailedAt &&
                     (string.Compare(row.WorkItemIdOrderKey, lastWorkItemOrderKey) > 0 ||
                      row.WorkItemIdOrderKey == lastWorkItemOrderKey &&
                      string.Compare(row.WorkItemIdHash, lastWorkItemHash) > 0)));
            }

            var rows = await page
                .OrderBy(row => row.FirstFailedAtUtcTicks)
                .ThenBy(row => row.LastFailedAtUtcTicks)
                .ThenBy(row => row.WorkItemIdOrderKey)
                .ThenBy(row => row.WorkItemIdHash)
                .Take(checked(ProviderPageSize + 1))
                .ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > ProviderPageSize;
            if (hasNext)
                rows = rows[..ProviderPageSize];

            foreach (var row in rows)
                records.Add(ReadChecked(row, scope, workflowExecutionId));

            if (!hasNext)
                return records;

            var cursor = rows[^1];
            hasCursor = true;
            lastFirstFailedAt = cursor.FirstFailedAtUtcTicks;
            lastLastFailedAt = cursor.LastFailedAtUtcTicks;
            lastWorkItemOrderKey = cursor.WorkItemIdOrderKey;
            lastWorkItemHash = cursor.WorkItemIdHash;
        }
    }

    internal static RuntimeSchedulerPoisonRecord ReadChecked(
        WorkflowSchedulerPoisonEntity row,
        string scope,
        string? expectedWorkflowExecutionId = null,
        string? expectedWorkItemId = null)
    {
        try
        {
            if (EfSchemaVersion.NotReadable(RuntimeSchedulerPoisonEfModule.Chain, row.SchemaVersion) ||
                row.Revision <= 0 ||
                row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
                row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
                throw new InvalidDataException("The scheduler-poison row scope, schema, or revision projection is corrupt.");

            var record = RuntimeArtifactJson.Deserialize<RuntimeSchedulerPoisonRecord>(RuntimeSchedulerPoisonEfModule.Chain.Upcast<WorkflowSchedulerPoisonEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
            ValidateRecord(record);
            if ((expectedWorkflowExecutionId is not null && record.WorkflowExecutionId != expectedWorkflowExecutionId) ||
                (expectedWorkItemId is not null && record.WorkItemId != expectedWorkItemId) ||
                row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, record.WorkflowExecutionId, record.WorkItemId) ||
                row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(record.WorkflowExecutionId) ||
                row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(record.WorkflowExecutionId) ||
                row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(record.WorkflowExecutionId) ||
                row.WorkItemId != EfRuntimeOperationalStoreSupport.Encode(record.WorkItemId) ||
                row.WorkItemIdHash != EfRuntimeOperationalStoreSupport.Hash(record.WorkItemId) ||
                row.WorkItemIdOrderKey != EfRuntimeOperationalStoreSupport.OrderPrefix(record.WorkItemId) ||
                row.FirstFailedAtUtcTicks != record.FirstFailedAt.UtcTicks ||
                row.LastFailedAtUtcTicks != record.LastFailedAt.UtcTicks)
                throw new InvalidDataException("The scheduler-poison row identity or projection does not match its current content.");

            return record;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted scheduler-poison record is not valid current data.", exception);
        }
    }

    private static WorkflowSchedulerPoisonEntity ToEntity(
        RuntimeSchedulerPoisonRecord record,
        string scope,
        string id,
        long revision) => new()
    {
        Id = id,
        ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
        ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(record.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(record.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(record.WorkflowExecutionId),
        WorkItemId = EfRuntimeOperationalStoreSupport.Encode(record.WorkItemId),
        WorkItemIdHash = EfRuntimeOperationalStoreSupport.Hash(record.WorkItemId),
        WorkItemIdOrderKey = EfRuntimeOperationalStoreSupport.OrderPrefix(record.WorkItemId),
        FirstFailedAtUtcTicks = record.FirstFailedAt.UtcTicks,
        LastFailedAtUtcTicks = record.LastFailedAt.UtcTicks,
        ContentJson = RuntimeArtifactJson.Serialize(record),
        SchemaVersion = RuntimeSchedulerPoisonEfModule.SchemaVersion,
        Revision = revision
    };

    private void AttachForUpdate(WorkflowSchedulerPoisonEntity row, long originalRevision)
    {
        DetachTracked(row.Id);
        context.WorkflowSchedulerPoisonRecords.Attach(row);
        context.Entry(row).Property(x => x.Revision).OriginalValue = originalRevision;
        context.Entry(row).State = EntityState.Modified;
    }

    private void DetachTracked(string id)
    {
        var tracked = context.ChangeTracker.Entries<WorkflowSchedulerPoisonEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == id);
        tracked?.State = EntityState.Detached;
    }

    private void Detach(WorkflowSchedulerPoisonEntity row) => context.Entry(row).State = EntityState.Detached;

    private void DetachOwnershipRow(string id, bool unchangedOnly)
    {
        foreach (var entry in context.ChangeTracker.Entries<ExecutionLivenessStateEntity>()
                     .Where(entry => entry.Entity.Id == id && (!unchangedOnly || entry.State == EntityState.Unchanged))
                     .ToArray())
            entry.State = EntityState.Detached;
    }

    private static void ValidateRecord(RuntimeSchedulerPoisonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateIdentity(record.WorkflowExecutionId, nameof(record.WorkflowExecutionId));
        ValidateWorkItemIdentity(record.WorkItemId, nameof(record.WorkItemId));
        if (!Enum.IsDefined(record.CommandKind))
            throw new InvalidDataException("The scheduler-poison record contains an undefined command kind.");
        if (!Enum.IsDefined(record.Disposition))
            throw new InvalidDataException("The scheduler-poison record contains an undefined disposition.");
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > RuntimeSchedulerPoisonEfModule.IdentityMaximumLength)
            throw new ArgumentException($"Runtime identity cannot exceed {RuntimeSchedulerPoisonEfModule.IdentityMaximumLength} UTF-16 code units.", parameterName);
    }

    private static void ValidateWorkItemIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > RuntimeSchedulerPoisonEfModule.WorkItemIdentityMaximumLength)
            throw new ArgumentException($"Runtime work-item identity cannot exceed {RuntimeSchedulerPoisonEfModule.WorkItemIdentityMaximumLength} UTF-16 code units.", parameterName);
    }
}
