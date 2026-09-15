using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Opt-in EF Core create-only checkpoint marker store (R19 thin slice).
/// </summary>
/// <remarks>
/// This first slice owns the durable replay marker and the transaction boundary around it. It deliberately accepts
/// only an empty change set: R20-R24 state participants are not silently treated as committed until their EF adapters
/// can stage their changes through this same context. A caller may stage already-tracked EF rows before invoking this
/// store; those rows and the marker are flushed by one transaction, which is the seam used by the R19 atomicity tests.
/// </remarks>
public sealed class EfRuntimeCheckpointCommitStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IRuntimeCheckpointCommitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, commit.CommitId);

        var existing = await FindMarkerAsync(scope, commit.CommitId, cancellationToken);
        if (existing is not null)
            return ResolveReplay(commit, fingerprint, existing);

        ValidateThinSlice(commit);

        var marker = ToEntity(commit, scope, id, fingerprint);
        var trackedStates = context.ChangeTracker.Entries()
            .ToDictionary(entry => entry.Entity, entry => entry.State);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Do not clear the tracker. The checkpoint writer's participants share this context, and clearing here
            // would silently discard a sibling mutation staged before the marker. Flush those already-tracked
            // participant rows first, then add the immutable marker as the final write in this transaction. The
            // marker is the durable commit proof, so it must never precede a participant failure.
            await context.SaveChangesAsync(cancellationToken);
            context.RuntimeCheckpointCommits.Add(marker);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFor(commit, marker);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            await RollbackAndRestoreAsync(transaction, marker, trackedStates);
            var winner = await FindMarkerAsync(scope, commit.CommitId, cancellationToken);
            if (winner is not null)
                return ResolveReplay(commit, fingerprint, winner);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAndRestoreAsync(transaction, marker, trackedStates);

            // A provider can acknowledge the commit ambiguously. The immutable marker is the only authoritative
            // reconciliation signal: if it is visible, return the original result; otherwise preserve the failure.
            var reconciled = await FindMarkerAsync(scope, commit.CommitId, cancellationToken);
            if (reconciled is not null)
                return ResolveReplay(commit, fingerprint, reconciled);
            throw;
        }
    }

    private async ValueTask<RuntimeCheckpointCommitEntity?> FindMarkerAsync(
        string scope,
        string commitId,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, commitId);
        var row = await context.RuntimeCheckpointCommits.AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == id &&
                candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
                candidate.CommitId == EfRuntimeOperationalStoreSupport.Encode(commitId) &&
                candidate.CommitIdHash == EfRuntimeOperationalStoreSupport.Hash(commitId),
                cancellationToken);
        return row is null ? null : ReadChecked(row, scope, commitId);
    }

    private static void ValidateThinSlice(RuntimeCheckpointCommit commit)
    {
        if (commit.ExpectedFence is not null)
            throw new NotSupportedException("The R19 EF marker slice does not yet own execution-fence validation; use the complete checkpoint writer once R20-R24 participants are available.");

        var changes = commit.StateChanges;
        if (changes.WorkflowExecution is not null ||
            changes.Scheduler is not null ||
            changes.ActivityExecutions.Count > 0 ||
            changes.ActivityExecutionInspections.Count > 0 ||
            changes.Bookmarks.Count > 0 ||
            changes.DurableValues.Count > 0 ||
            changes.Incidents.Count > 0 ||
            changes.Operational.Count > 0 ||
            changes.ActivityScopeCleanups.Count > 0 ||
            changes.WorkflowDispatches.Count > 0 ||
            changes.WorkflowDispatchCancellations.Count > 0 ||
            changes.ConsumedSchedulerWorkItems.Count > 0 ||
            changes.AlterationJobTerminalChange is not null ||
            changes.PostCommitOutbox.Count > 0 ||
            commit.PostCommitIntents.Count > 0)
        {
            throw new NotSupportedException(
                "The R19 EF checkpoint marker slice accepts only an empty state change set; R20-R24 participants must be staged by the complete checkpoint writer before this adapter is enabled for runtime commits.");
        }
    }

    private static RuntimeCheckpointCommitEntity ToEntity(
        RuntimeCheckpointCommit commit,
        string scope,
        string id,
        string fingerprint) => new()
    {
        Id = id,
        ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
        ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        CommitId = EfRuntimeOperationalStoreSupport.Encode(commit.CommitId),
        CommitIdHash = EfRuntimeOperationalStoreSupport.Hash(commit.CommitId),
        CommitIdOrderKey = EfRuntimeOperationalStoreSupport.Order(commit.CommitId),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(commit.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(commit.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(commit.WorkflowExecutionId),
        OccurredAtUtcTicks = commit.Checkpoint.OccurredAt.UtcTicks,
        Fingerprint = fingerprint,
        ContentJson = JsonSerializer.Serialize(
            new MarkerDocument(
                commit.CommitId,
                commit.WorkflowExecutionId,
                commit.Checkpoint.OccurredAt,
                fingerprint,
                Array.Empty<string>(),
                Array.Empty<string>()),
            JsonOptions),
        PendingPostCommitWorkIdsJson = JsonSerializer.Serialize(Array.Empty<string>(), JsonOptions),
        ConsumedSchedulerWorkItemIdsJson = JsonSerializer.Serialize(Array.Empty<string>(), JsonOptions),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static RuntimeCheckpointCommitEntity ReadChecked(
        RuntimeCheckpointCommitEntity row,
        string scope,
        string expectedCommitId)
    {
        if (row.Revision != 1 ||
            row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope) ||
            row.CommitId != EfRuntimeOperationalStoreSupport.Encode(expectedCommitId) ||
            row.CommitIdHash != EfRuntimeOperationalStoreSupport.Hash(expectedCommitId) ||
            row.CommitIdOrderKey != EfRuntimeOperationalStoreSupport.Order(expectedCommitId) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, expectedCommitId))
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker identity or scope projection is corrupt.");
        }

        if (string.IsNullOrWhiteSpace(row.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId)) ||
            row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId)) ||
            string.IsNullOrWhiteSpace(row.Fingerprint) ||
            row.Fingerprint.Length != 64)
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker projection is corrupt.");
        }

        var workflowExecutionId = EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId);
        MarkerDocument content;
        try
        {
            content = JsonSerializer.Deserialize<MarkerDocument>(row.ContentJson, JsonOptions)
                      ?? throw new InvalidDataException();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker content is invalid.", exception);
        }

        if (!StringComparer.Ordinal.Equals(content.CommitId, expectedCommitId) ||
            !StringComparer.Ordinal.Equals(content.WorkflowExecutionId, workflowExecutionId) ||
            content.OccurredAt.UtcTicks != row.OccurredAtUtcTicks ||
            !StringComparer.Ordinal.Equals(content.Fingerprint, row.Fingerprint) ||
            !IdsEqual(content.PendingPostCommitWorkIds, row.PendingPostCommitWorkIdsJson, "pending post-commit work") ||
            !IdsEqual(content.ConsumedSchedulerWorkItemIds, row.ConsumedSchedulerWorkItemIdsJson, "consumed scheduler work"))
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker content does not match its projections.");
        }

        return row;
    }

    private static bool IdsEqual(IReadOnlyCollection<string> content, string projectionJson, string label) =>
        content.SequenceEqual(DeserializeIds(projectionJson, label), StringComparer.Ordinal);

    private static RuntimeCheckpointCommitStoreResult ResolveReplay(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        RuntimeCheckpointCommitEntity marker)
    {
        if (!StringComparer.Ordinal.Equals(marker.Fingerprint, fingerprint))
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);

        var workflowExecutionId = EfRuntimeOperationalStoreSupport.Decode(marker.WorkflowExecutionId);
        if (!StringComparer.Ordinal.Equals(workflowExecutionId, commit.WorkflowExecutionId) ||
            marker.OccurredAtUtcTicks != commit.Checkpoint.OccurredAt.UtcTicks)
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker does not match its checkpoint identity.");
        }

        return new RuntimeCheckpointCommitStoreResult(
            DeserializeIds(marker.PendingPostCommitWorkIdsJson, "pending post-commit work"))
        {
            ConsumedSchedulerWorkItemIds = DeserializeIds(marker.ConsumedSchedulerWorkItemIdsJson, "consumed scheduler work")
        };
    }

    private static RuntimeCheckpointCommitStoreResult ResultFor(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointCommitEntity marker) =>
        ResolveReplay(commit, marker.Fingerprint, marker);

    private static IReadOnlyCollection<string> DeserializeIds(string json, string label)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json, JsonOptions);
            if (values is null || values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException();
            return values;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException($"The persisted runtime checkpoint marker contains invalid {label} identifiers.", exception);
        }
    }

    private sealed record MarkerDocument(
        string CommitId,
        string WorkflowExecutionId,
        DateTimeOffset OccurredAt,
        string Fingerprint,
        IReadOnlyCollection<string> PendingPostCommitWorkIds,
        IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds);

    private async ValueTask RollbackAndRestoreAsync(
        IDbContextTransaction transaction,
        RuntimeCheckpointCommitEntity marker,
        IReadOnlyDictionary<object, EntityState> trackedStates)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // Preserve the original provider failure. The reconciliation read below remains authoritative when it is
            // available; a provider that cannot roll back is still handled by its transaction disposal.
        }

        await transaction.DisposeAsync();

        var markerEntry = context.Entry(marker);
        if (markerEntry.State != EntityState.Detached)
            markerEntry.State = EntityState.Detached;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity == marker)
                continue;
            if (trackedStates.TryGetValue(entry.Entity, out var state))
                entry.State = state;
        }
    }
}
