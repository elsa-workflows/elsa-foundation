using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core checkpoint writer for the bounded R19 execution/scheduler slice.</summary>
/// <remarks>
/// This slice owns the durable replay marker, workflow-execution and scheduler projections, and the execution fence
/// in one transaction. Workflow-execution writes also run inside the established root executable write-lease
/// boundary; replay remains resolvable before a lease is required. The remaining R20-R24 participants are still
/// rejected explicitly; they are not silently treated as committed until their EF adapters can stage changes through
/// this same context.
/// </remarks>
public sealed class EfRuntimeCheckpointCommitStore(
    BookmarkStateDbContext context,
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

        // Match the established checkpoint funnel: identity admission happens before marker reads, transaction
        // creation, or any other provider I/O. A malformed state change must not be able to reach EF by using this
        // preview adapter's narrower participant set as an excuse to bypass the shared boundary.
        ValidateCommitBoundary(commit);
        ValidateThinSlice(commit);
        if (commit.StateChanges.WorkflowExecution is { } workflowExecution)
            accessContextAccessor.Current.EnsureTenantScope(workflowExecution.State.TenantId);

        var existing = await FindMarkerAsync(scope, commit.CommitId, cancellationToken);
        if (existing is not null)
            return ResolveReplay(commit, fingerprint, existing);

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
            return result ?? throw new InvalidOperationException("The checkpoint lease callback did not produce a result.");
        }

        return await CommitNewAsync(cancellationToken);

        async ValueTask<RuntimeCheckpointCommitStoreResult> CommitNewAsync(CancellationToken writeCancellationToken)
        {
            var marker = ToEntity(commit, scope, id, fingerprint);

            await using var transaction = await context.Database.BeginTransactionAsync(writeCancellationToken);
            try
            {
                // Fence validation/touch is deliberately first. The workflow and scheduler rows then join the same
                // transaction, and the immutable marker is added only after every supported participant is staged.
                // Do not clear the tracker: callers may have staged a sibling R14-R18 mutation on this context.
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

                await EfRuntimeCheckpointOutboxParticipantStaging.StageAsync(
                    context,
                    commit,
                    scope,
                    writeCancellationToken);

                // Flush all participant rows first, then add the immutable marker as the final write in this
                // transaction. The marker is the durable commit proof, so it must never precede a participant failure.
                await context.SaveChangesAsync(writeCancellationToken);
                context.RuntimeCheckpointCommits.Add(marker);
                await context.SaveChangesAsync(writeCancellationToken);
                await transaction.CommitAsync(writeCancellationToken);
                return ResultFor(commit, marker);
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
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

    private static void ValidateCommitBoundary(RuntimeCheckpointCommit commit)
    {
        if (commit.StateChanges.WorkflowExecution is { } workflow)
        {
            RequireOperation(workflow, RuntimeStateChangeOperation.Upsert, "workflow execution");
            RequireId(workflow.StateId, workflow.State.WorkflowExecutionId, "workflow execution");
            RequireWorkflow(workflow.State.WorkflowExecutionId, commit.WorkflowExecutionId, "workflow execution");
        }

        if (commit.StateChanges.Scheduler is { } scheduler)
        {
            RequireOperation(scheduler, RuntimeStateChangeOperation.Upsert, "scheduler");
            RequireId(scheduler.StateId, scheduler.State.WorkflowExecutionId, "scheduler");
            RequireWorkflow(scheduler.State.WorkflowExecutionId, commit.WorkflowExecutionId, "scheduler");
        }

        foreach (var change in commit.StateChanges.PostCommitOutbox)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "post-commit outbox");
            RequireId(change.StateId, change.State.OutboxItemId, "post-commit outbox");
            RequireWorkflow(change.State.Intent.WorkflowExecutionId, commit.WorkflowExecutionId, "post-commit outbox");
            EfRuntimePostCommitOutboxStore.ValidatePending(change.State);
        }
    }

    private static void RequireOperation<TState>(
        RuntimeStateChange<TState> change,
        RuntimeStateChangeOperation expected,
        string label)
    {
        if (change.Operation != expected)
            throw new InvalidOperationException($"The EF checkpoint writer can only project {label} '{expected}' changes.");
    }

    private static void RequireId(string actual, string expected, string label)
    {
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidOperationException($"{label} state change StateId must match its model identity.");
    }

    private static void RequireWorkflow(string actual, string expected, string label)
    {
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidOperationException($"{label} workflow execution ID must match the checkpoint workflow execution ID.");
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
        var changes = commit.StateChanges;
        if (changes.WorkflowExecution is { Operation: not RuntimeStateChangeOperation.Upsert })
            throw new NotSupportedException("The R19 EF checkpoint slice supports workflow-execution upserts only.");
        if (changes.Scheduler is { Operation: not RuntimeStateChangeOperation.Upsert })
            throw new NotSupportedException("The R19 EF checkpoint slice supports scheduler upserts only.");

        if (changes.ActivityExecutions.Count > 0 ||
            changes.ActivityExecutionInspections.Count > 0 ||
            changes.Bookmarks.Count > 0 ||
            changes.DurableValues.Count > 0 ||
            changes.Incidents.Count > 0 ||
            changes.Operational.Count > 0 ||
            changes.ActivityScopeCleanups.Count > 0 ||
            changes.WorkflowDispatches.Count > 0 ||
            changes.WorkflowDispatchCancellations.Count > 0 ||
            changes.ConsumedSchedulerWorkItems.Count > 0 ||
            changes.AlterationJobTerminalChange is not null)
        {
            throw new NotSupportedException(
                "The R19 EF checkpoint slice supports workflow-execution, scheduler, and pending outbox changes only; remaining participants must be staged by the complete checkpoint writer before this adapter is enabled for those runtime commits.");
        }

        if (commit.PostCommitIntents.Count > 0)
        {
            var pendingIds = changes.PostCommitOutbox.Select(change => change.StateId)
                .ToHashSet(StringComparer.Ordinal);
            var intents = new Dictionary<string, RuntimePostCommitIntent>(StringComparer.Ordinal);
            foreach (var intent in commit.PostCommitIntents)
            {
                var id = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId);
                if (intents.TryGetValue(id, out var duplicate) &&
                    !EfRuntimePostCommitOutboxStore.IntentsEquivalent(duplicate, intent))
                    throw new InvalidOperationException($"Post-commit intent '{id}' occurs more than once with conflicting content.");
                intents[id] = intent;
            }
            if (!pendingIds.SetEquals(intents.Keys))
                throw new InvalidOperationException(
                    "A checkpoint with post-commit intents must include their pending outbox state changes in the same atomic unit.");

            foreach (var change in changes.PostCommitOutbox)
            {
                if (!EfRuntimePostCommitOutboxStore.IntentsEquivalent(intents[change.StateId], change.State.Intent))
                    throw new InvalidOperationException(
                        $"Post-commit outbox item '{change.StateId}' does not match its checkpoint intent.");
            }
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
                PendingOutboxIds(commit),
                Array.Empty<string>()),
            JsonOptions),
        PendingPostCommitWorkIdsJson = JsonSerializer.Serialize(PendingOutboxIds(commit), JsonOptions),
        ConsumedSchedulerWorkItemIdsJson = JsonSerializer.Serialize(Array.Empty<string>(), JsonOptions),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static string[] PendingOutboxIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.PostCommitOutbox.Select(change => change.StateId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

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
