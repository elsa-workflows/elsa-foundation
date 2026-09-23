using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core checkpoint writer for bounded Runtime participant slices.</summary>
/// <remarks>
/// The durable replay marker, supported participant projections, and execution fence share one transaction.
/// Workflow-execution writes also run inside the established root executable write-lease boundary; replay remains
/// resolvable before a lease is required. Unsupported participants are rejected explicitly until their EF adapters
/// can stage changes through this same context.
/// </remarks>
public sealed class EfRuntimeCheckpointCommitStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider? timeProvider = null,
    IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null) : IRuntimeCheckpointCommitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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

        // The commit's structural rules were applied by RuntimeCheckpointCommitValidator in the application layer before
        // the commit reached this store. What is checked here is only reserved-key integrity and this provider's storage
        // limits and capability, still before marker reads, transaction creation, or any other provider I/O.
        RuntimeExecutionOwnershipStateId.EnsureNotWritten(commit.StateChanges.Operational);
        ValidateStorageLimits(commit);
        ValidateThinSlice(commit);
        if (commit.StateChanges.WorkflowExecution is { } workflowExecution)
        {
            accessContextAccessor.Current.EnsureTenantScope(workflowExecution.State.TenantId);
            if (workflowExecution.State.TestScope is { } testScope)
                accessContextAccessor.Current.EnsureTenantScope(testScope.TenantId);
        }
        foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
        {
            accessContextAccessor.Current.EnsureTenantScope(dispatch.State.TenantId);
            if (dispatch.State.TestScope is { } testScope)
                accessContextAccessor.Current.EnsureTenantScope(testScope.TenantId);
        }
        EfRuntimeAlterationCheckpointParticipationGate.Validate(context, commit, scope);

        var existing = await FindMarkerAsync(scope, commit.CommitId, cancellationToken);
        if (existing is not null)
        {
            var replay = ResolveReplay(commit, fingerprint, existing);
            EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
            return replay;
        }

        if (commit.StateChanges.WorkflowExecution is { } executionChange)
        {
            if (rootWriteLeaseManager is null)
                throw new InvalidOperationException(
                    "A workflow execution checkpoint write requires IWorkflowExecutableRootWriteLeaseManager.");

            RuntimeCheckpointCommitStoreResult? result = null;
            async ValueTask ExecuteCheckpointAsync(CancellationToken leaseCancellationToken) =>
                result = await CommitNewAsync(leaseCancellationToken);

            await rootWriteLeaseManager.ExecuteAsync(
                executionChange.State.PinnedExecutable,
                $"checkpoint:{commit.CommitId}",
                ExecuteCheckpointAsync,
                cancellationToken);
            var completed = result ?? throw new InvalidOperationException("The checkpoint lease callback did not produce a result.");
            EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
            return completed;
        }

        var immediate = await CommitNewAsync(cancellationToken);
        EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
        return immediate;

        async ValueTask<RuntimeCheckpointCommitStoreResult> CommitNewAsync(CancellationToken writeCancellationToken)
        {
            var marker = ToEntity(commit, scope, id, fingerprint);
            var touchedTestScopes = new Dictionary<string, WorkflowTestScopeRecord>(StringComparer.Ordinal);

            await using var transaction = await context.Database.BeginTransactionAsync(writeCancellationToken);
            try
            {
                // Stage against the rows as they are now. An unchanged tracked entity is only a snapshot from earlier
                // work on this context, and one execution commits from more than one scope (a nested drain commits in
                // its own scope between two commits of the outer one); a stale snapshot would carry a superseded
                // revision into the concurrency check and refuse this commit. Pending sibling R14-R18 mutations that a
                // caller staged on this context are kept, so this is not a tracker clear.
                foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Unchanged).ToArray())
                    entry.State = EntityState.Detached;

                // Fence validation/touch is deliberately first. The workflow and scheduler rows then join the same
                // transaction, and the immutable marker is added only after every supported participant is staged.
                if (commit.ExpectedFence is { } expectedFence)
                {
                    await EfRuntimeCheckpointParticipantStaging.StageExecutionFenceAsync(
                        context,
                        scope,
                        commit.WorkflowExecutionId,
                        expectedFence,
                        _timeProvider,
                        writeCancellationToken);
                }

                if (commit.StateChanges.WorkflowExecution is { } workflowChange)
                {
                    await EfRuntimeCheckpointParticipantStaging.StageWorkflowExecutionAsync(
                        context,
                        workflowChange,
                        scope,
                        commit.Checkpoint.OccurredAt,
                        touchedTestScopes,
                        writeCancellationToken);
                }

                if (commit.StateChanges.Scheduler is { } schedulerChange)
                {
                    await EfRuntimeCheckpointParticipantStaging.StageSchedulerAsync(
                        context,
                        schedulerChange,
                        scope,
                        writeCancellationToken);
                }

                // Each participant type below stages its whole collection, because the insert/update/delete decision
                // needs only the row carrying each participant's immutable physical identity. Reading those one at a
                // time made a commit's round-trip count equal to its participant count, and no validator caps any of
                // these collections; the seams now take one chunked tracked load per type instead.
                await EfRuntimeCheckpointActivityExecutionParticipantStaging.StageActivityExecutionsAsync(
                    context, commit.StateChanges.ActivityExecutions, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointInspectionParticipantStaging.StageAsync(
                    context, commit.StateChanges.ActivityExecutionInspections, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointIncidentParticipantStaging.StageIncidentsAsync(
                    context, commit.StateChanges.Incidents, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointRunHealthParticipantStaging.StageWorkflowRunHealthAsync(
                    context, commit.WorkflowExecutionId, commit.StateChanges.WorkflowExecution,
                    commit.StateChanges.Incidents, scope, writeCancellationToken);

                await EfRuntimeCheckpointParticipantStaging.StageBookmarksAsync(
                    context, commit.StateChanges.Bookmarks, scope, writeCancellationToken);

                await EfRuntimeCheckpointParticipantStaging.StageDurableValuesAsync(
                    context, commit.StateChanges.DurableValues, scope, writeCancellationToken);

                await EfRuntimeCheckpointActivityScopeCleanupParticipantStaging.StageAsync(
                    context, commit.StateChanges.ActivityScopeCleanups, scope, writeCancellationToken);

                await EfRuntimeCheckpointParticipantStaging.StageOperationalAsync(
                    context, commit.StateChanges.Operational, scope, writeCancellationToken);

                await EfRuntimeCheckpointDispatchParticipantStaging.StageAsync(
                    context,
                    commit,
                    scope,
                    touchedTestScopes,
                    writeCancellationToken);

                await EfRuntimeCheckpointOutboxParticipantStaging.StageAsync(
                    context,
                    commit,
                    scope,
                    writeCancellationToken);

                foreach (var consumed in commit.StateChanges.ConsumedSchedulerWorkItems)
                    await EfRuntimeCheckpointParticipantStaging.StageConsumedSchedulerWorkAsync(
                        context, consumed, scope, writeCancellationToken);

                if (commit.StateChanges.AlterationJobTerminalChange is { } terminalJob)
                    await EfRuntimeCheckpointAlterationJobParticipantStaging.StageAsync(
                        context, terminalJob, scope, commit.WorkflowExecutionId, writeCancellationToken);

                // Flush all participant rows first, then add the immutable marker as the final write in this
                // transaction. The marker is the durable commit proof, so it must never precede a participant failure.
                await context.SaveChangesAsync(writeCancellationToken);
                context.RuntimeCheckpointCommits.Add(marker);
                await context.SaveChangesAsync(writeCancellationToken);
                await transaction.CommitAsync(writeCancellationToken);
                return ResultFor(commit, marker);
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            {
                await RollbackAndRestoreAsync(transaction);
                var winner = await FindMarkerAsync(scope, commit.CommitId, writeCancellationToken);
                if (winner is not null)
                    return ResolveReplay(commit, fingerprint, winner);
                throw;
            }
            catch (OperationCanceledException)
            {
                await RollbackAndRestoreAsync(transaction);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RollbackAndRestoreAsync(transaction);

                // A provider can acknowledge the commit ambiguously. The immutable marker is the only authoritative
                // reconciliation signal: if it is visible, return the original result; otherwise preserve the failure.
                var reconciled = await FindMarkerAsync(scope, commit.CommitId, writeCancellationToken);
                if (reconciled is not null)
                    return ResolveReplay(commit, fingerprint, reconciled);
                throw;
            }
        }
    }

    /// <summary>
    /// Identity lengths this provider's columns can hold for activity-scope cleanup targets. These describe EF's storage
    /// model, not the checkpoint contract, and are checked before any provider I/O.
    /// </summary>
    private static void ValidateStorageLimits(RuntimeCheckpointCommit commit)
    {
        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        {
            foreach (var bookmarkId in cleanup.BookmarkIds)
            {
                if (bookmarkId.Length > BookmarkStateEfModule.BookmarkIdentityMaximumLength)
                    throw new ArgumentException("Activity-scope cleanup bookmark ID exceeds the bookmark persistence contract.");
            }
            foreach (var timerId in cleanup.TimerIds)
                EfRuntimeOperationalStoreSupport.ValidateIdentity(timerId, nameof(cleanup.TimerIds));
            foreach (var workItemId in cleanup.SchedulerWorkItemIds)
                EfRuntimeOperationalStoreSupport.ValidateIdentity(workItemId, nameof(cleanup.SchedulerWorkItemIds));
        }
    }

    private async ValueTask<RuntimeCheckpointCommitEntity?> FindMarkerAsync(
        string scope,
        string commitId,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, commitId);
        var row = await context.RuntimeCheckpointCommits.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, commitId);
    }

    private static void ValidateThinSlice(RuntimeCheckpointCommit commit)
    {
        var changes = commit.StateChanges;
        if (changes.WorkflowExecution is { Operation: not RuntimeStateChangeOperation.Upsert })
            throw new NotSupportedException("The R19 EF checkpoint slice supports workflow-execution upserts only.");
        if (changes.Scheduler is { Operation: not RuntimeStateChangeOperation.Upsert })
            throw new NotSupportedException("The R19 EF checkpoint slice supports scheduler upserts only.");
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
                PendingOutboxIds(commit),
                ConsumedSchedulerWorkIds(commit)),
            JsonOptions),
        PendingPostCommitWorkIdsJson = JsonSerializer.Serialize(PendingOutboxIds(commit), JsonOptions),
        ConsumedSchedulerWorkItemIdsJson = JsonSerializer.Serialize(ConsumedSchedulerWorkIds(commit), JsonOptions),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static string[] PendingOutboxIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.PostCommitOutbox.Select(change => change.StateId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string[] ConsumedSchedulerWorkIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static RuntimeCheckpointCommitEntity ReadChecked(
        RuntimeCheckpointCommitEntity row,
        string scope,
        string expectedCommitId)
    {
        if (EfSchemaVersion.NotReadable("RuntimeOperationalState", row.SchemaVersion, RuntimeOperationalStateEfModule.SchemaVersion) ||
            row.Revision != 1 ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope) ||
            row.CommitId != EfRuntimeOperationalStoreSupport.Encode(expectedCommitId) ||
            row.CommitIdHash != EfRuntimeOperationalStoreSupport.Hash(expectedCommitId) ||
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

    private async ValueTask RollbackAndRestoreAsync(IDbContextTransaction transaction)
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

        // A failed unit of work must not leave Added/Modified participant rows queued for an accidental later
        // SaveChanges call. The caller can reload and retry from durable state; preserving stale tracked values here
        // would turn a rollback into a hidden second attempt.
        context.ChangeTracker.Clear();
    }
}
