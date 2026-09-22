using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core scheduler-poison store (R23).</summary>
/// <remarks>
/// Poison identity is the scoped pair (workflow execution ID, work-item ID). The JSON envelope is authoritative;
/// relational projections provide bounded lookup and deterministic failure-window ordering. Replacements use the
/// row revision as an optimistic compare-and-swap token and never fall back to an unconditional update.
/// </remarks>
public sealed class EfWorkflowSchedulerPoisonStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowSchedulerPoisonStore
{
    private static readonly EfWriteRetry Records = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey);
    private const int ProviderPageSize = RuntimeStorePageRequest.MaximumLimit;

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
            if (row.Revision <= 0 ||
                EfSchemaVersion.NotReadable("RuntimeSchedulerPoison", row.SchemaVersion, RuntimeSchedulerPoisonEfModule.SchemaVersion) ||
                row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
                row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
                throw new InvalidDataException("The scheduler-poison row scope, schema, or revision projection is corrupt.");

            var record = RuntimeArtifactJson.Deserialize<RuntimeSchedulerPoisonRecord>(row.ContentJson);
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
