using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
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
            var touchedTestScopes = new Dictionary<string, WorkflowTestScope>(StringComparer.Ordinal);

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

                foreach (var activity in commit.StateChanges.ActivityExecutions)
                    await EfRuntimeCheckpointActivityExecutionParticipantStaging.StageActivityExecutionAsync(
                        context, activity, scope, commit.WorkflowExecutionId, writeCancellationToken);

                foreach (var inspection in commit.StateChanges.ActivityExecutionInspections)
                    await EfRuntimeCheckpointInspectionParticipantStaging.StageAsync(
                        context, inspection, scope, commit.WorkflowExecutionId, writeCancellationToken);

                foreach (var incident in commit.StateChanges.Incidents)
                    await EfRuntimeCheckpointIncidentParticipantStaging.StageIncidentAsync(
                        context, incident, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointRunHealthParticipantStaging.StageWorkflowRunHealthAsync(
                    context, commit.WorkflowExecutionId, commit.StateChanges.WorkflowExecution,
                    commit.StateChanges.Incidents, scope, writeCancellationToken);

                foreach (var bookmark in commit.StateChanges.Bookmarks)
                    await EfRuntimeCheckpointParticipantStaging.StageBookmarkAsync(
                        context, bookmark, scope, writeCancellationToken);

                foreach (var durableValue in commit.StateChanges.DurableValues)
                    await EfRuntimeCheckpointParticipantStaging.StageDurableValueAsync(
                        context, durableValue, scope, writeCancellationToken);

                foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
                    await EfRuntimeCheckpointActivityScopeCleanupParticipantStaging.StageAsync(
                        context, cleanup, scope, writeCancellationToken);

                foreach (var operational in commit.StateChanges.Operational)
                    await EfRuntimeCheckpointParticipantStaging.StageOperationalAsync(
                        context, operational, scope, writeCancellationToken);

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

        foreach (var change in commit.StateChanges.ActivityExecutions)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "activity execution");
            RequireId(change.StateId, change.State.Execution.ActivityExecutionId, "activity execution");
            RequireWorkflow(change.State.Execution.WorkflowExecutionId, commit.WorkflowExecutionId, "activity execution");
            change.State.EnsureValueFlowCompatible();
            change.State.EnsureSupersessionCompatible();
            RequireMatchingProvenance(change.State.ExecutionScopeId, change.State.Provenance.ExecutionScopeId,
                change.State.Attempt, change.State.Provenance.Attempt, "activity execution");
        }

        foreach (var change in commit.StateChanges.ActivityExecutionInspections)
        {
            if (change.Operation is not (RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
                throw new InvalidOperationException("The EF checkpoint writer supports activity execution inspection upsert and delete only.");
            RequireId(change.StateId, change.State.ActivityExecutionId, "activity execution inspection");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "activity execution inspection");
            RequireMatchingProvenance(change.State.ExecutionScopeId, change.State.Provenance.ExecutionScopeId,
                change.State.Attempt, change.State.Provenance.Attempt, "activity execution inspection");
        }

        var seenIncidentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in commit.StateChanges.Incidents)
        {
            if (change.Operation is not (RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert))
                throw new InvalidOperationException("The EF checkpoint writer supports incident append and upsert only.");
            RequireId(change.StateId, change.State.IncidentId, "incident");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "incident");
            if (!seenIncidentIds.Add(change.StateId))
                throw new InvalidOperationException($"Incident '{change.StateId}' occurs more than once in one checkpoint commit.");
        }

        foreach (var change in commit.StateChanges.PostCommitOutbox)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "post-commit outbox");
            RequireId(change.StateId, change.State.OutboxItemId, "post-commit outbox");
            RequireWorkflow(change.State.Intent.WorkflowExecutionId, commit.WorkflowExecutionId, "post-commit outbox");
            EfRuntimePostCommitOutboxStore.ValidatePending(change.State);
        }

        foreach (var change in commit.StateChanges.DurableValues)
        {
            if (change.Operation is not (RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
                throw new InvalidOperationException("The EF checkpoint writer supports durable-value upsert and delete only.");
            RequireId(change.StateId, change.State.DurableValueId, "durable value");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "durable value");
        }

        foreach (var change in commit.StateChanges.Bookmarks)
        {
            if (change.Operation is not (RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
                throw new InvalidOperationException("The EF checkpoint writer supports bookmark upsert and delete only.");
            RequireId(change.StateId, change.State.BookmarkId, "bookmark");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "bookmark");
        }

        foreach (var change in commit.StateChanges.Operational)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "operational state");
            RequireId(change.StateId, change.State.OperationalStateId, "operational state");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "operational state");
            if (StringComparer.Ordinal.Equals(change.StateId, $"ownership:{commit.WorkflowExecutionId}"))
                throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
        }

        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        {
            RequireWorkflow(cleanup.WorkflowExecutionId, commit.WorkflowExecutionId, "activity-scope cleanup");
            ArgumentException.ThrowIfNullOrWhiteSpace(cleanup.ExecutionScopeId);
            if (!cleanup.ActivityExecutionIds.Contains(cleanup.ExecutionScopeId, StringComparer.Ordinal))
                throw new InvalidOperationException("Activity scope cleanup must include its outer execution scope.");
            foreach (var bookmarkId in cleanup.BookmarkIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
                if (bookmarkId.Length > BookmarkStateEfModule.BookmarkIdentityMaximumLength)
                    throw new ArgumentException("Activity-scope cleanup bookmark ID exceeds the bookmark persistence contract.");
                if (commit.StateChanges.Bookmarks.Any(change => StringComparer.Ordinal.Equals(change.StateId, bookmarkId)))
                    throw new NotSupportedException("A bookmark change and cleanup deletion for the same ID require a proven staged-row transition.");
            }
            foreach (var timerId in cleanup.TimerIds)
                EfRuntimeOperationalStoreSupport.ValidateIdentity(timerId, nameof(cleanup.TimerIds));
            foreach (var workItemId in cleanup.SchedulerWorkItemIds)
            {
                EfRuntimeOperationalStoreSupport.ValidateIdentity(workItemId, nameof(cleanup.SchedulerWorkItemIds));
                if (commit.StateChanges.ConsumedSchedulerWorkItems.Any(item => StringComparer.Ordinal.Equals(item.WorkItemId, workItemId)))
                    throw new NotSupportedException("A claimed scheduler-work consume and scope cleanup deletion for the same ID require a proven staged-row transition.");
            }
        }

        var seenDispatches = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var change in commit.StateChanges.WorkflowDispatches)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "workflow dispatch");
            RequireId(change.StateId, change.State.DispatchId, "workflow dispatch");
            WorkflowDispatchLifecycle.ValidateCheckpointOwnership(commit.WorkflowExecutionId, change.State);
            if (seenDispatches.TryGetValue(change.StateId, out var duplicate) &&
                !WorkflowDispatchLifecycle.RecordsEqual(duplicate, change.State))
                throw new InvalidOperationException(
                    $"Workflow dispatch '{change.StateId}' occurs more than once with conflicting state.");
            seenDispatches[change.StateId] = change.State;
        }

        foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
            RequireWorkflow(request.ParentWorkflowExecutionId, commit.WorkflowExecutionId, "workflow dispatch cancellation");

        foreach (var consumed in commit.StateChanges.ConsumedSchedulerWorkItems)
        {
            RequireWorkflow(consumed.WorkflowExecutionId, commit.WorkflowExecutionId, "consumed scheduler work");
            ArgumentException.ThrowIfNullOrWhiteSpace(consumed.WorkItemId);
            ArgumentException.ThrowIfNullOrWhiteSpace(consumed.ClaimOwnerId);
            if (consumed.FencingToken <= 0)
                throw new InvalidOperationException("Consumed scheduler work requires a positive fencing token.");
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

    private static void RequireMatchingProvenance(
        string? stateScope, string? provenanceScope,
        ActivityExecutionAttemptLineage? stateAttempt, ActivityExecutionAttemptLineage? provenanceAttempt,
        string label)
    {
        if (stateScope is not null && provenanceScope is not null &&
            !StringComparer.Ordinal.Equals(stateScope, provenanceScope))
            throw new InvalidOperationException($"{label} execution scope must match scheduling provenance when both are present.");
        if (stateAttempt is not null && provenanceAttempt is not null && stateAttempt != provenanceAttempt)
            throw new InvalidOperationException($"{label} attempt must match scheduling provenance when both are present.");
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

        if (changes.AlterationJobTerminalChange is not null)
        {
            throw new NotSupportedException(
                "The EF checkpoint slice supports workflow-execution, scheduler, activity execution, inspection/hierarchy, incidents/run health, bookmarks, durable values, scope cleanup, operational state, dispatch, pending outbox, and claimed scheduler-work consume only; remaining participants must be staged by the complete checkpoint writer before this adapter is enabled for those runtime commits.");
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
