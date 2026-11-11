using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core scheduler-poison store (R23).</summary>
/// <remarks>
/// <para>
/// Poison identity is the scoped pair (workflow execution ID, work-item ID). The JSON envelope is authoritative;
/// relational projections provide bounded lookup and deterministic failure-window ordering. Replacements use the
/// row revision as an optimistic compare-and-swap token and never fall back to an unconditional update.
/// </para>
/// <para>
/// It can record under the run's ownership fence (<see cref="ISchedulerPoisonFenceCapability"/>) only when the
/// ownership row is in its own database: the registered <see cref="IExecutionLivenessStateStore"/> is the
/// <see cref="EfExecutionLivenessStateStore"/> over the same context and access scope. Scheduler poison is
/// registered separately from operational state, so a host can pair this store with ownership kept elsewhere; then
/// <see cref="CanRecordFenced"/> is <see langword="false"/>.
/// </para>
/// </remarks>
public sealed class EfWorkflowSchedulerPoisonStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowSchedulerPoisonStore, ISchedulerPoisonFenceCapability
{
    private static readonly EfWriteRetry Records = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey);
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;
    private readonly Func<IExecutionLivenessStateStore?>? _ownershipStateStore;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// Creates the store with a way to find the ownership store the runtime composition registers, so it can tell
    /// whether the ownership row is in its own database. <paramref name="ownershipStateStore"/> is called each time
    /// <see cref="CanRecordFenced"/> is read or a fenced record is asked for, and not when this store is built:
    /// reading and unfenced recording do not need the ownership store, or what it depends on, to be resolvable.
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
    /// <remarks>
    /// The fence check, the touch of the ownership row and the record are one transaction that this call opens
    /// itself. When the save conflicts on the ownership row alone, which is what the owner's own lease heartbeat
    /// causes, the transaction runs again, for <c>EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts</c>
    /// attempts in all, and each attempt checks the fence again. A conflict on that row that is not followed by
    /// another attempt is checked once more before it leaves, so a takeover never leaves as a write failure. The
    /// drainer records without the fence when this call fails for a reason other than ownership.
    /// </remarks>
    /// <exception cref="RuntimeStaleFencingTokenException">The fence is not the run's current, unexpired lease. Nothing was written.</exception>
    /// <exception cref="WorkflowSchedulerPoisonEntityFrameworkPersistenceException">
    /// A provider failure, with the provider's exception as its inner exception. Nothing was written, unless the
    /// failure came from the commit itself: a commit that reports a failure may still have been applied. The inner
    /// exception is a <see cref="DbUpdateConcurrencyException"/> when the ownership row changed under every attempt
    /// while the lease stayed this one, when it changed that way once while the context held other pending changes
    /// (the call then does not run again), and when another writer replaced the poison row between the read and
    /// the save. It is a plain <see cref="DbUpdateException"/> when another writer inserted the poison row first,
    /// which is a unique-key violation. It is the <see cref="InvalidOperationException"/> EF Core raises when the
    /// context already has a transaction, or an ambient or enlisted one.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The context tracks a pending change to this run's ownership row. Nothing was written, and the pending change
    /// is left as it was.
    /// </exception>
    /// <exception cref="NotSupportedException"><see cref="CanRecordFenced"/> is <see langword="false"/>.</exception>
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

        // A pending change to this run's ownership row is someone else's unsaved work. Staging the fence on top of
        // it would validate values that are not in the database and save that change with the record, so refuse it.
        if (context.ChangeTracker.Entries<ExecutionLivenessStateEntity>()
            .Any(entry => entry.Entity.Id == fenceRowId && entry.State != EntityState.Unchanged))
            throw new InvalidOperationException(
                $"Scheduler poison for workflow execution '{record.WorkflowExecutionId}' cannot be recorded under its execution fence while the context tracks a pending change to that execution's ownership row.");

        // The owner's lease heartbeat bumps the fence row's revision without changing the lease, as it does under a
        // checkpoint commit (EfRuntimeCheckpointCommitStore.CommitFencedAsync). Run again only after a conflict on
        // that row alone, and only when the context holds nothing else to save. Each attempt reads the row and
        // validates the fence again, so a real takeover still fails as stale.
        var retryable = !context.ChangeTracker.HasChanges();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await RecordUnderFenceAsync();
                }
                catch (DbUpdateConcurrencyException exception) when (EfRuntimeCheckpointParticipantStaging.IsFenceRenewalConflict(exception))
                {
                    if (retryable && attempt < EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts)
                        continue;

                    // The conflict is about to leave without another attempt. It does not say who changed the
                    // ownership row, and a caller that takes it for a failure that is not about ownership would
                    // record without the fence. So look once more: a lease that is no longer this one leaves as
                    // stale, and only the owner's own heartbeat leaves as the write failure.
                    await EnsureFenceIsCurrentAsync();
                    throw;
                }
            }
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(exception))
        {
            // Provider exception types do not cross this boundary. What this method throws on purpose is not a
            // provider failure and is not caught here: the stale fence, cancellation, and the refusals above.
            var identity = $"{record.WorkflowExecutionId}/{record.WorkItemId}";
            throw new WorkflowSchedulerPoisonEntityFrameworkPersistenceException(
                "recording under the execution fence",
                identity,
                $"The EF scheduler-poison store failed while recording under the execution fence scheduler poison '{identity}'.",
                exception);
        }

        async ValueTask EnsureFenceIsCurrentAsync()
        {
            var operationalStateId = RuntimeExecutionOwnershipStateId.For(record.WorkflowExecutionId);
            var row = await context.ExecutionLivenessStates.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == fenceRowId, cancellationToken);
            var ownership = row is null
                ? null
                : EfExecutionLivenessStateStore.Read(row, scope, record.WorkflowExecutionId, operationalStateId);
            RuntimeExecutionFenceValidator.EnsureCurrent(record.WorkflowExecutionId, fence, ownership, _timeProvider.GetUtcNow());
        }

        async ValueTask<RuntimeSchedulerPoisonRecord> RecordUnderFenceAsync()
        {
            WorkflowSchedulerPoisonEntity? staged = null;
            // Always a transaction of this call's own, as the checkpoint commit store opens. EF Core refuses to
            // begin one while the context has another, and that refusal leaves as this store's persistence failure.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Read the fence row as it is now. A copy this context tracks from earlier work is a snapshot, and a
                // tracked query would hand that snapshot back instead of the row.
                DetachOwnershipRow(fenceRowId);
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
                await transaction.CommitAsync(cancellationToken);
                return record;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Keep the failure that got here. Disposing the transaction still rolls it back.
                }

                throw;
            }
            finally
            {
                // Leave nothing of this unit tracked, whether it committed, was refused or failed. A later save on
                // the shared context must not write the record again, and must not reuse this fence row's revision.
                if (staged is not null)
                    Detach(staged);
                DetachOwnershipRow(fenceRowId);
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

    private void DetachOwnershipRow(string id)
    {
        foreach (var entry in context.ChangeTracker.Entries<ExecutionLivenessStateEntity>()
                     .Where(entry => entry.Entity.Id == id)
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
