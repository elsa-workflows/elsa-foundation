using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Groundwork.Kernel;
using Groundwork.Store;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Current-only runtime checkpoint writer for the Groundwork v2 row catalog.
/// </summary>
/// <remarks>
/// The writer deliberately owns one public Groundwork unit-of-work. It does not call the v1 document
/// bridge, open a second transaction for outbox/dispatch state, or provide a migration fallback. The
/// create-only checkpoint marker is the final staged row and is the durable replay authority.
/// </remarks>
public sealed class GroundworkV2RuntimeCheckpointWriter : IRuntimeCheckpointCommitStore
{
    private static readonly string[] CommitUnitIds =
    [
        ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
        ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind,
        ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind,
        ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
        ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
        ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
        ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind,
        ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
        ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
        ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
        ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind,
        ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind
    ];

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager;
    private readonly string? targetName;
    private readonly TimeProvider timeProvider;
    private readonly IWritePathObserver? writePathObserver;

    public GroundworkV2RuntimeCheckpointWriter(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null,
        TimeProvider? timeProvider = null,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null,
        IWritePathObserver? writePathObserver = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.rootWriteLeaseManager = rootWriteLeaseManager;
        this.targetName = targetName;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.writePathObserver = writePathObserver;
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        cancellationToken.ThrowIfCancellationRequested();

        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException("Runtime persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork runtime checkpoints require one explicit persistence scope; global and across-scope access are refused.");
        }

        ValidateCommitBoundary(commit);
        EnsureTenantScope(context, commit);
        RequireCommitCapabilities(commit);
        var access = StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);

        if (ReadMarker(access, commit.CommitId) is { } existing)
            return ResolveReplay(commit, fingerprint, existing);

        RuntimeCheckpointCommitStoreResult? result = null;
        async ValueTask ExecuteCheckpointAsync(CancellationToken leaseCancellationToken)
        {
            result = await CommitUnitOfWorkAsync(access, commit, fingerprint, leaseCancellationToken);
        }

        if (commit.StateChanges.WorkflowExecution is { } workflowExecution)
        {
            if (rootWriteLeaseManager is null)
            {
                throw new InvalidOperationException(
                    "A workflow execution checkpoint write requires IWorkflowExecutableRootWriteLeaseManager.");
            }

            await rootWriteLeaseManager.ExecuteAsync(
                workflowExecution.State.PinnedExecutable,
                $"checkpoint:{commit.CommitId}",
                ExecuteCheckpointAsync,
                cancellationToken);
        }
        else
        {
            await ExecuteCheckpointAsync(cancellationToken);
        }

        return result ?? throw new InvalidOperationException("The checkpoint lease callback did not produce a result.");
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitUnitOfWorkAsync(
        StorageAccess access,
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = sessions.BeginUnitOfWork(access, BatchWriteOptions.Exact, CommitUnitIds, targetName);
        var stage = new StageContext(
            sessions, targetName, unitOfWork, access, commit, fingerprint, cancellationToken, timeProvider, writePathObserver);
        try
        {
            // The fence touch is intentionally the first staged mutation. Every other row, including the marker,
            // is therefore in the same decision as ownership validation.
            stage.TouchFence();
            stage.ApplyWorkflowExecution();
            stage.ValidateTestScopeAdmission();
            stage.ApplyScheduler();
            stage.ApplyActivityExecutions();
            stage.ApplyInspectionsAndHierarchy();
            stage.ApplyBookmarks();
            stage.ApplyDurableValues();
            stage.ApplyActivityScopeCleanups();
            stage.ApplyIncidents();
            stage.ApplyAlterationJob();
            stage.ApplyOperational();
            stage.ApplyDispatches();
            stage.ApplyOutbox();
            stage.ApplyConsumedSchedulerWork();
            stage.ApplyDurableTimers();
            stage.ApplyWorkflowRunHealthProjection();
            stage.StageMarker();

            // Exactly one commit call exists on the success path. If acknowledgement is ambiguous, the marker read
            // below turns a committed transaction into the same replay result without inventing a second write.
            stage.BeginCommit();
            var report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
            if (!report.IsSuccessful)
            {
                if (ContainsConflict(report.Outcomes, ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, RowWriteMode.ConditionalUpsert))
                    throw ReadCurrentFence(access, commit);
                if (ContainsConflict(report.Outcomes, ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, RowWriteMode.CompareAndDelete))
                    throw new RuntimeSchedulerWorkConsumeConflictException(
                        commit.WorkflowExecutionId,
                        FindFailedSchedulerWorkItemId(report.Outcomes, commit));
                throw new InvalidOperationException(
                    $"Groundwork rejected runtime checkpoint '{commit.CommitId}' with {report.Failed} failed row outcomes.");
            }

            return stage.Result;
        }
        catch (BatchWriteException exception) when (ContainsConflict(exception.Outcomes, ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, RowWriteMode.ConditionalUpsert) ||
                                                    ContainsConflict(exception.Outcomes, ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, RowWriteMode.CompareAndDelete))
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's attributed conflict. Providers require the caller to roll back after a batch failure.
            }

            if (ContainsConflict(exception.Outcomes, ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, RowWriteMode.ConditionalUpsert))
                throw ReadCurrentFence(access, commit);
            throw new RuntimeSchedulerWorkConsumeConflictException(
                commit.WorkflowExecutionId,
                FindFailedSchedulerWorkItemId(exception.Outcomes, commit));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the original failure. Providers may close a transaction after an ambiguous response.
            }

            // A provider can throw after applying the batch but before returning its acknowledgement. Reconcile only
            // through the create-only marker; if no marker is visible, the original failure remains authoritative.
            if (stage.CommitStarted && ReadMarker(access, commit.CommitId) is { } marker)
                return ResolveReplay(commit, fingerprint, marker);

            throw;
        }
    }

    private void RequireCommitCapabilities(RuntimeCheckpointCommit commit)
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource)
            throw new NotSupportedException(
                "Groundwork runtime checkpoint commits require provider capability evidence.");

        var capabilities = capabilitySource.Capabilities(targetName);
        if (!capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork runtime checkpoint commits require the provider's evidenced atomic-commit capability.");
        }

        if (commit.StateChanges.ConsumedSchedulerWorkItems.Count > 0 &&
            !capabilities.Any(capability => capability.Id.Equals(BatchWriteCapabilities.CompareAndDelete)))
        {
            throw new NotSupportedException(
                "Groundwork runtime scheduler-work consumption requires the provider's evidenced compare-and-delete capability.");
        }
    }

    private static void EnsureTenantScope(PersistenceAccessContext context, RuntimeCheckpointCommit commit)
    {
        if (commit.StateChanges.WorkflowExecution is { } workflowExecution)
            context.EnsureTenantScope(workflowExecution.State.TenantId);
        foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
            context.EnsureTenantScope(dispatch.State.TenantId);
    }

    private static bool ContainsConflict(IEnumerable<RowWriteOutcome> outcomes, string unitId, RowWriteMode mode) =>
        outcomes.Any(outcome =>
            outcome.Write.Unit.Id.Value == unitId &&
            outcome.Write.Mode == mode &&
            outcome.Outcome.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound or WriteOutcomeStatus.ComparisonMismatch);

    private static string FindFailedWriteId(
        IEnumerable<RowWriteOutcome> outcomes,
        string unitId,
        RowWriteMode mode) =>
        outcomes.Where(outcome =>
                outcome.Write.Unit.Id.Value == unitId &&
                outcome.Write.Mode == mode &&
                outcome.Outcome.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound or WriteOutcomeStatus.ComparisonMismatch)
            .Select(outcome => outcome.Write.Key?.Values.TryGetValue(ElsaRuntimeV2StorageManifest.IdField, out var raw) == true
                ? raw as string
                : outcome.Write.Values?.Values.TryGetValue(ElsaRuntimeV2StorageManifest.IdField, out var value) == true
                    ? value as string
                    : null)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? string.Empty;

    private static string FindFailedSchedulerWorkItemId(
        IEnumerable<RowWriteOutcome> outcomes,
        RuntimeCheckpointCommit commit)
    {
        var physicalId = FindFailedWriteId(
            outcomes,
            ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
            RowWriteMode.CompareAndDelete);
        return commit.StateChanges.ConsumedSchedulerWorkItems
                   .FirstOrDefault(item => StringComparer.Ordinal.Equals(
                       GroundworkV2SchedulerWorkStorageConventions.PhysicalId(
                           item.WorkflowExecutionId,
                           item.WorkItemId),
                       physicalId))
                   ?.WorkItemId
               ?? physicalId;
    }

    private RuntimeStaleFencingTokenException ReadCurrentFence(StorageAccess access, RuntimeCheckpointCommit commit)
    {
        var operationalStateId = $"ownership:{commit.WorkflowExecutionId}";
        var identity = GroundworkV2RuntimeLivenessCodec.Identity(commit.WorkflowExecutionId, operationalStateId);
        var entry = ReadRowIsolated(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, access, identity);
        if (entry is null)
            return new(
                commit.WorkflowExecutionId,
                commit.ExpectedFence?.FencingToken ?? 0,
                0,
                RuntimeFencingRejectionReason.NoActiveLease);

        var state = GroundworkV2RuntimeLivenessCodec.Deserialize(entry.Values.Values);
        var currentToken = state.ExecutionLease?.FencingToken ?? ReadHighestIssuedToken(state);
        var reason = state.ExecutionLease is null
            ? RuntimeFencingRejectionReason.NoActiveLease
            : state.ExecutionLease.IsExpired(timeProvider.GetUtcNow())
                ? RuntimeFencingRejectionReason.ExpiredLease
                : RuntimeFencingRejectionReason.StaleToken;
        return new(
            commit.WorkflowExecutionId,
            commit.ExpectedFence?.FencingToken ?? 0,
            currentToken,
            reason);
    }

    private static long ReadHighestIssuedToken(ExecutionLivenessState state)
    {
        if (state.Metadata.TryGetValue(Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
            return token;
        return state.ExecutionLease?.FencingToken ?? 0;
    }

    // Keep the v2 adapter's admission boundary identical to the established checkpoint funnel. These checks are
    // intentionally before capability discovery, marker reads, UOW creation, or any provider I/O.
    private static void ValidateCommitBoundary(RuntimeCheckpointCommit commit)
    {
        var stateChanges = commit.StateChanges;
        if (stateChanges.WorkflowExecution is { } workflow)
        {
            RequireOperation(workflow, RuntimeStateChangeOperation.Upsert, "workflow execution");
            RequireId(workflow.StateId, workflow.State.WorkflowExecutionId, "workflow execution");
            RequireWorkflow(workflow.State.WorkflowExecutionId, commit.WorkflowExecutionId, "workflow execution");
        }

        if (stateChanges.Scheduler is { } scheduler)
        {
            RequireOperation(scheduler, RuntimeStateChangeOperation.Upsert, "scheduler");
            RequireId(scheduler.StateId, scheduler.State.WorkflowExecutionId, "scheduler");
            RequireWorkflow(scheduler.State.WorkflowExecutionId, commit.WorkflowExecutionId, "scheduler");
        }

        foreach (var change in stateChanges.ActivityExecutions)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "activity execution");
            RequireId(change.StateId, change.State.Execution.ActivityExecutionId, "activity execution");
            RequireWorkflow(change.State.Execution.WorkflowExecutionId, commit.WorkflowExecutionId, "activity execution");
            change.State.EnsureValueFlowCompatible();
            change.State.EnsureSupersessionCompatible();
            RequireMatchingScope(change.State.ExecutionScopeId, change.State.Provenance.ExecutionScopeId, "activity execution");
            RequireMatchingAttempt(change.State.Attempt, change.State.Provenance.Attempt, "activity execution");
        }

        foreach (var change in stateChanges.ActivityExecutionInspections)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, RuntimeStateChangeOperation.Delete, "activity execution inspection");
            RequireId(change.StateId, change.State.ActivityExecutionId, "activity execution inspection");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "activity execution inspection");
            RequireMatchingScope(change.State.ExecutionScopeId, change.State.Provenance.ExecutionScopeId, "activity execution inspection");
            RequireMatchingAttempt(change.State.Attempt, change.State.Provenance.Attempt, "activity execution inspection");
        }

        foreach (var change in stateChanges.Bookmarks)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, RuntimeStateChangeOperation.Delete, "bookmark");
            RequireId(change.StateId, change.State.BookmarkId, "bookmark");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "bookmark");
        }

        foreach (var change in stateChanges.DurableValues)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, RuntimeStateChangeOperation.Delete, "durable value");
            RequireId(change.StateId, change.State.DurableValueId, "durable value");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "durable value");
        }

        var seenIncidents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in stateChanges.Incidents)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Append, RuntimeStateChangeOperation.Upsert, "incident");
            RequireId(change.StateId, change.State.IncidentId, "incident");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "incident");
            if (!seenIncidents.Add(change.StateId))
                throw new InvalidOperationException(
                    $"Incident '{change.StateId}' occurs more than once in one checkpoint commit.");
        }

        var ownershipStateId = $"ownership:{commit.WorkflowExecutionId}";
        foreach (var change in stateChanges.Operational)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "operational state");
            RequireId(change.StateId, change.State.OperationalStateId, "operational state");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "operational state");
            if (StringComparer.Ordinal.Equals(change.State.OperationalStateId, ownershipStateId))
                throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
        }

        var seenDispatches = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var change in stateChanges.WorkflowDispatches)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "workflow dispatch");
            RequireId(change.StateId, change.State.DispatchId, "workflow dispatch");
            WorkflowDispatchLifecycle.ValidateCheckpointOwnership(commit.WorkflowExecutionId, change.State);
            if (seenDispatches.TryGetValue(change.StateId, out var duplicate) &&
                !WorkflowDispatchLifecycle.RecordsEqual(duplicate, change.State))
                throw new InvalidOperationException($"Workflow dispatch '{change.StateId}' occurs more than once with conflicting state.");
            seenDispatches[change.StateId] = change.State;
        }

        foreach (var request in stateChanges.WorkflowDispatchCancellations)
            RequireWorkflow(request.ParentWorkflowExecutionId, commit.WorkflowExecutionId, "workflow dispatch cancellation");

        foreach (var consumed in stateChanges.ConsumedSchedulerWorkItems)
        {
            RequireWorkflow(consumed.WorkflowExecutionId, commit.WorkflowExecutionId, "consumed scheduler work item");
            if (string.IsNullOrWhiteSpace(consumed.ClaimOwnerId) || consumed.FencingToken < 0)
                throw new InvalidOperationException("Consumed scheduler work items require a non-empty claim owner and non-negative fencing token.");
        }

        foreach (var change in stateChanges.PostCommitOutbox)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "post-commit outbox");
            RequireId(change.StateId, change.State.OutboxItemId, "post-commit outbox");
            RequireWorkflow(change.State.Intent.WorkflowExecutionId, commit.WorkflowExecutionId, "post-commit outbox");
            if (change.State.Status != RuntimePostCommitOutboxStatus.Pending)
                throw new InvalidOperationException("Checkpoint outbox writes must remain Pending until post-commit delivery claims them.");
        }

        if (stateChanges.AlterationJobTerminalChange is { } alteration &&
            !StringComparer.Ordinal.Equals(alteration.CheckpointCommitId, commit.CommitId))
            throw new InvalidOperationException("Workflow alteration terminal evidence must reference its checkpoint commit ID.");

        if (stateChanges.ActivityScopeCleanups.Any(cleanup =>
                !StringComparer.Ordinal.Equals(cleanup.WorkflowExecutionId, commit.WorkflowExecutionId) ||
                !cleanup.ActivityExecutionIds.Contains(cleanup.ExecutionScopeId, StringComparer.Ordinal)))
            throw new InvalidOperationException(
                "Activity scope cleanup must belong to the checkpoint workflow and include its outer execution scope.");
    }

    private static void RequireOperation<TState>(
        RuntimeStateChange<TState> change,
        RuntimeStateChangeOperation expected,
        string label)
    {
        if (change.Operation != expected)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project {label} '{expected}' changes.");
    }

    private static void RequireOperation<TState>(
        RuntimeStateChange<TState> change,
        RuntimeStateChangeOperation first,
        RuntimeStateChangeOperation second,
        string label)
    {
        var operation = change.Operation;
        if (operation != first && operation != second)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project {label} '{first}' or '{second}' changes.");
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

    private static void RequireMatchingScope(string? stateScope, string? provenanceScope, string label)
    {
        if (stateScope is not null && provenanceScope is not null &&
            !StringComparer.Ordinal.Equals(stateScope, provenanceScope))
            throw new InvalidOperationException($"{label} ExecutionScopeId must match its scheduling provenance when both are present.");
    }

    private static void RequireMatchingAttempt(
        ActivityExecutionAttemptLineage? stateAttempt,
        ActivityExecutionAttemptLineage? provenanceAttempt,
        string label)
    {
        if (stateAttempt is not null && provenanceAttempt is not null && stateAttempt != provenanceAttempt)
            throw new InvalidOperationException($"{label} Attempt must match its scheduling provenance when both are present.");
    }

    private CheckpointMarker? ReadMarker(StorageAccess access, string commitId)
    {
        var entry = ReadRowIsolated(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, access, commitId);
        return entry is null ? null : DeserializeMarker(entry.Values.Values);
    }

    /// <summary>
    /// Reads one row through a unit of work of its own rather than through the session source's cached
    /// session. The cached session wraps one provider connection, and the source hands the same instance
    /// to every caller whose access matches — so two concurrent commits for the same scope share a
    /// connection, and PostgreSQL and SQL Server refuse concurrent commands on one connection outright
    /// ("a command is already in progress"). SQLite serializes and the MongoDB driver is thread-safe,
    /// which is why the defect surfaces on exactly two of the four providers. The commit itself already
    /// runs in its own unit of work per call; this gives its reads the same isolation. The unit of work
    /// is disposed without committing, which rolls back a transaction that staged nothing.
    /// </summary>
    private StoredEntry? ReadRowIsolated(string unitId, StorageAccess access, string id)
    {
        var unit = sessions.Unit(unitId, targetName);
        using var unitOfWork = sessions.BeginUnitOfWork(access, BatchWriteOptions.Default, [unitId], targetName);
        return unitOfWork.OpenSession(unit).Read(GroundworkRuntimeRowStore.Key(id));
    }

    private static CheckpointMarker DeserializeMarker(IReadOnlyDictionary<string, object?> values)
    {
        var content = ReadContent(values);
        var marker = GroundworkV2RuntimeJson.Deserialize<CheckpointMarker>(content) ??
                     throw new InvalidDataException("Groundwork runtime checkpoint marker content was empty.");
        var rowId = ReadRequiredString(values, ElsaRuntimeV2StorageManifest.IdField);
        if (!StringComparer.Ordinal.Equals(marker.CommitId, rowId))
            throw new InvalidDataException("Groundwork runtime checkpoint marker identity does not match its row key.");
        return marker;
    }

    private static RuntimeCheckpointCommitStoreResult ResolveReplay(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CheckpointMarker marker)
    {
        if (!StringComparer.Ordinal.Equals(marker.Fingerprint, fingerprint))
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
        return new RuntimeCheckpointCommitStoreResult(marker.PendingPostCommitWorkIds)
        {
            ConsumedSchedulerWorkItemIds = marker.ConsumedSchedulerWorkItemIds
        };
    }

    private static string ReadContent(IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var raw))
            throw new InvalidDataException("Groundwork runtime row did not contain JSON content.");
        return raw switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new InvalidDataException("Groundwork runtime row content was not JSON.")
        };
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var raw))
        {
            if (raw is string text && !string.IsNullOrWhiteSpace(text))
                return text;
            if (raw is JsonElement { ValueKind: JsonValueKind.String } element &&
                !string.IsNullOrWhiteSpace(element.GetString()))
                return element.GetString()!;
        }

        throw new InvalidDataException($"Groundwork runtime row is missing required string field '{field}'.");
    }

    private sealed class StageContext
    {
        private readonly IGroundworkStorageSessionSource sessions;
        private readonly string? targetName;
        private readonly IUnitOfWork unitOfWork;
        private readonly StorageAccess access;
        private readonly RuntimeCheckpointCommit commit;
        private readonly string fingerprint;
        private readonly CancellationToken cancellationToken;
        private readonly TimeProvider timeProvider;
        private readonly IWritePathObserver? writePathObserver;
        private readonly HashSet<string> touchedTestScopes = new(StringComparer.Ordinal);
        private readonly HashSet<string> newIncidentIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IStorageSession> unitSessions = new(StringComparer.Ordinal);
        private bool newWorkflowExecution;
        private bool? workflowExistedBeforeCheckpoint;

        public StageContext(
            IGroundworkStorageSessionSource sessions,
            string? targetName,
            IUnitOfWork unitOfWork,
            StorageAccess access,
            RuntimeCheckpointCommit commit,
            string fingerprint,
            CancellationToken cancellationToken,
            TimeProvider timeProvider,
            IWritePathObserver? writePathObserver)
        {
            this.sessions = sessions;
            this.targetName = targetName;
            this.unitOfWork = unitOfWork;
            this.access = access;
            this.commit = commit;
            this.fingerprint = fingerprint;
            this.cancellationToken = cancellationToken;
            this.timeProvider = timeProvider;
            this.writePathObserver = writePathObserver;
        }

        public bool CommitStarted { get; private set; }

        public void BeginCommit() => CommitStarted = true;

        public RuntimeCheckpointCommitStoreResult Result => new(
            commit.StateChanges.PostCommitOutbox
                .Select(change => change.State.OutboxItemId)
                .Order(StringComparer.Ordinal)
                .ToArray())
        {
            ConsumedSchedulerWorkItemIds = commit.StateChanges.ConsumedSchedulerWorkItems
                .Select(item => item.WorkItemId)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };

        public void TouchFence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (commit.ExpectedFence is not { } expected)
                return;

            var operationalStateId = $"ownership:{commit.WorkflowExecutionId}";
            var row = Open(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind);
            var entry = row.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2RuntimeLivenessCodec.Identity(commit.WorkflowExecutionId, operationalStateId)));
            if (entry is null)
                throw NewStaleFence(RuntimeFencingRejectionReason.NoActiveLease, 0, expected.FencingToken);

            var state = GroundworkV2RuntimeLivenessCodec.Deserialize(entry.Values.Values);
            var currentToken = state.ExecutionLease?.FencingToken ?? ReadHighestIssuedToken(state);
            if (state.ExecutionLease is null)
                throw NewStaleFence(RuntimeFencingRejectionReason.NoActiveLease, currentToken, expected.FencingToken);
            if (state.ExecutionLease.IsExpired(timeProvider.GetUtcNow()))
                throw NewStaleFence(RuntimeFencingRejectionReason.ExpiredLease, currentToken, expected.FencingToken);
            if (!StringComparer.Ordinal.Equals(state.ExecutionLease.LeaseId, expected.LeaseId) ||
                !StringComparer.Ordinal.Equals(state.ExecutionLease.OwnerId, expected.OwnerId) ||
                state.ExecutionLease.FencingToken != expected.FencingToken)
            {
                throw NewStaleFence(RuntimeFencingRejectionReason.StaleToken, currentToken, expected.FencingToken);
            }

            unitOfWork.Stage(RowWrite.ConditionalUpsert(
                Unit(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind),
                GroundworkV2RuntimeLivenessCodec.Values(state),
                Observed(WriteOptions.IfVersion(entry.Version ?? 0))));
        }

        public void ApplyWorkflowExecution()
        {
            if (commit.StateChanges.WorkflowExecution is not { } change)
                return;

            workflowExistedBeforeCheckpoint = Open(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind)
                .Read(GroundworkRuntimeRowStore.Key(change.StateId)) is not null;
            newWorkflowExecution = !workflowExistedBeforeCheckpoint.Value;
            Apply(
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
                change,
                ProjectWorkflowExecution);
        }

        public void ValidateTestScopeAdmission()
        {
            if (newWorkflowExecution && commit.StateChanges.WorkflowExecution?.State.TestScope is { } executionScope)
                AssertOpenTestScope(executionScope, commit.WorkflowExecutionId);
            foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
            {
                var isNew = Open(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(
                        GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatch.State.DispatchId))) is null;
                if (isNew && dispatch.State.TestScope is { } scope)
                    AssertOpenTestScope(scope, dispatch.State.ChildWorkflowExecutionId);
            }
        }

        public void ApplyScheduler() => Apply(
            ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind,
            commit.StateChanges.Scheduler,
            ProjectScheduler);

        public void ApplyActivityExecutions()
        {
            foreach (var change in commit.StateChanges.ActivityExecutions)
            {
                // Activity execution identity is composite. The direct store and the checkpoint funnel must stage
                // the same injective physical key and envelope, not merely the activity component of that key.
                unitOfWork.Stage(RowWrite.Upsert(
                    Unit(ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind),
                    GroundworkV2ActivityExecutionStorageConventions.Values(change.State),
                    Observed(WriteOptions.Unconditional)));
            }
        }

        public void ApplyInspectionsAndHierarchy()
        {
            foreach (var change in commit.StateChanges.ActivityExecutionInspections)
            {
                var inspectionPhysicalId = GroundworkV2ActivityExecutionInspectionStorageConventions.PhysicalId(
                    change.State.WorkflowExecutionId,
                    change.State.ActivityExecutionId);
                if (change.Operation == RuntimeStateChangeOperation.Delete)
                {
                    StageDelete(
                        ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
                        inspectionPhysicalId);
                    StageHierarchyDeleteIfPresent(
                        change.State.WorkflowExecutionId,
                        change.State.ActivityExecutionId);
                    continue;
                }

                StageValues(
                    ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
                    GroundworkV2ActivityExecutionInspectionStorageConventions.Values(change.State),
                    change.Operation);

                var effectiveScope = EffectiveExecutionScope(change.State.ExecutionScopeId, change.State.Provenance.ExecutionScopeId);
                if (string.IsNullOrWhiteSpace(effectiveScope))
                {
                    StageHierarchyDeleteIfPresent(
                        change.State.WorkflowExecutionId,
                        change.State.ActivityExecutionId);
                    continue;
                }
                var hierarchy = ActivityExecutionHierarchyProjector.FromInspection(
                    string.IsNullOrWhiteSpace(change.State.ExecutionScopeId)
                        ? change.State with { ExecutionScopeId = effectiveScope }
                        : change.State);
                StageValues(
                    ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
                    GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(hierarchy),
                    change.Operation);
            }
        }

        /// <summary>
        /// Retracts an activity's hierarchy row, which may never have been written.
        /// </summary>
        /// <remarks>
        /// Hierarchy rows exist only for activities that carry an execution scope, so both call sites here
        /// are speculative: they retract a row for an activity that may never have had one. A staged delete
        /// of an absent row comes back <c>NotFound</c> and fails the whole unit of work, so the row has to be
        /// resolved first. The document substrate this replaced treated the same delete as a no-op, which is
        /// why the difference only appears now.
        /// </remarks>
        private void StageHierarchyDeleteIfPresent(string workflowExecutionId, string activityExecutionId)
        {
            var physicalId = GroundworkV2ActivityExecutionHierarchyStorageConventions.PhysicalId(
                workflowExecutionId,
                activityExecutionId);
            if (Open(ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(physicalId)) is null)
            {
                return;
            }

            StageDelete(ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind, physicalId);
        }

        public void ApplyBookmarks()
        {
            foreach (var change in commit.StateChanges.Bookmarks)
            {
                var physicalId = GroundworkV2BookmarkStorageConventions.PhysicalId(
                    change.State.WorkflowExecutionId,
                    change.State.BookmarkId);
                if (change.Operation == RuntimeStateChangeOperation.Delete)
                {
                    StageDelete(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind, physicalId);
                    continue;
                }

                StageValues(
                    ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
                    GroundworkV2BookmarkStorageConventions.Values(change.State),
                    change.Operation);
            }
        }

        public void ApplyDurableValues()
        {
            foreach (var change in commit.StateChanges.DurableValues)
            {
                var physicalId = GroundworkV2DurableValueStorageConventions.PhysicalId(
                    change.State.WorkflowExecutionId,
                    change.State.DurableValueId);
                if (change.Operation == RuntimeStateChangeOperation.Delete)
                {
                    StageDelete(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind, physicalId);
                    continue;
                }

                StageValues(
                    ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind,
                    GroundworkV2DurableValueStorageConventions.Values(change.State),
                    change.Operation);
            }
        }

        public void ApplyActivityScopeCleanups()
        {
            foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
            {
                foreach (var bookmarkId in cleanup.BookmarkIds)
                    StageCleanupDelete(
                        ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
                        GroundworkV2BookmarkStorageConventions.PhysicalId(cleanup.WorkflowExecutionId, bookmarkId),
                        cleanup.WorkflowExecutionId);
                foreach (var timerId in cleanup.TimerIds)
                    StageCleanupDelete(
                        ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
                        GroundworkV2DurableTimerStorageConventions.PhysicalId(cleanup.WorkflowExecutionId, timerId),
                        cleanup.WorkflowExecutionId);
                foreach (var workItemId in cleanup.SchedulerWorkItemIds)
                    StageCleanupDelete(
                        ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
                        GroundworkV2SchedulerWorkStorageConventions.PhysicalId(cleanup.WorkflowExecutionId, workItemId),
                        cleanup.WorkflowExecutionId);
            }
        }

        public void ApplyIncidents()
        {
            foreach (var change in commit.StateChanges.Incidents)
            {
                var physicalId = GroundworkV2IncidentStateStorageConventions.PhysicalId(
                    change.State.WorkflowExecutionId,
                    change.State.IncidentId);
                var entry = Open(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(physicalId));
                if (entry is null &&
                    change.Operation is RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert)
                    newIncidentIds.Add(change.StateId);
                if (change.Operation == RuntimeStateChangeOperation.Append)
                {
                    StageValues(
                        ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
                        GroundworkV2IncidentStateStorageConventions.Values(change.State),
                        RuntimeStateChangeOperation.Append);
                    continue;
                }

                var existing = entry is null
                    ? null
                    : GroundworkV2IncidentStateStorageConventions.Deserialize(entry.Values.Values);
                IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, change.State);
                StageValues(
                    ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
                    GroundworkV2IncidentStateStorageConventions.Values(change.State),
                    RuntimeStateChangeOperation.Upsert,
                    entry is null
                        ? WriteOptions.CreateOnly
                        : WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                            $"Incident '{change.State.IncidentId}' did not expose a provider revision.")));
            }
        }

        /// <summary>
        /// Folds the workflow state and all incident changes for this execution into one optimistic
        /// run-health projection write. The projection is intentionally absent from incident storage;
        /// an incident-only write therefore fails closed when its execution projection is missing.
        /// </summary>
        public void ApplyWorkflowRunHealthProjection()
        {
            if (commit.StateChanges.WorkflowExecution is null && commit.StateChanges.Incidents.Count == 0)
                return;

            var healthSession = Open(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind);
            var healthKey = GroundworkRuntimeRowStore.Key(commit.WorkflowExecutionId);
            var existingHealthEntry = healthSession.Read(healthKey);
            var existingHealth = existingHealthEntry is null
                ? null
                : GroundworkV2WorkflowRunHealthStorageConventions.Deserialize(existingHealthEntry.Values.Values);
            var workflowExists = workflowExistedBeforeCheckpoint ??
                                 Open(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind)
                                     .Read(GroundworkRuntimeRowStore.Key(commit.WorkflowExecutionId)) is not null;

            if (commit.StateChanges.WorkflowExecution is null && (!workflowExists || existingHealth is null))
            {
                throw new InvalidOperationException(
                    $"An incident-only checkpoint for workflow execution '{commit.WorkflowExecutionId}' requires both the workflow and its run-health projection.");
            }

            if (commit.StateChanges.WorkflowExecution is { } workflowStateChange)
            {
                if (newWorkflowExecution && existingHealth is not null)
                {
                    throw new InvalidOperationException(
                        $"Workflow execution '{workflowStateChange.State.WorkflowExecutionId}' is new but already has a run-health projection.");
                }

                if (!newWorkflowExecution && existingHealth is null)
                {
                    throw new InvalidOperationException(
                        $"Workflow execution '{workflowStateChange.State.WorkflowExecutionId}' already exists but has no run-health projection.");
                }
            }

            var incidentDelta = checked((long)newIncidentIds.Count);

            var next = commit.StateChanges.WorkflowExecution is { } workflowChange
                ? GroundworkV2WorkflowRunHealthStorageConventions.Values(
                    workflowChange.State.WorkflowExecutionId,
                    workflowChange.State.PinnedExecutable.DefinitionId,
                    workflowChange.State.RunKind,
                    existingHealth?.StartedAt ?? workflowChange.State.StartedAt,
                    workflowChange.State.Status,
                    checked((existingHealth?.IncidentCount ?? 0) + incidentDelta),
                    checked((existingHealth?.IncidentBearingCount ?? 0) + (incidentDelta > 0 && (existingHealth?.IncidentCount ?? 0) == 0 ? 1 : 0)))
                : GroundworkV2WorkflowRunHealthStorageConventions.Values(
                    existingHealth! with
                    {
                        IncidentCount = checked(existingHealth.IncidentCount + incidentDelta),
                        IncidentBearingCount = checked(existingHealth.IncidentBearingCount +
                            (incidentDelta > 0 && existingHealth.IncidentCount == 0 ? 1 : 0))
                    });

            var options = existingHealthEntry is null
                ? WriteOptions.CreateOnly
                : WriteOptions.IfVersion(existingHealthEntry.Version ?? throw new InvalidDataException(
                    $"Groundwork workflow run-health projection '{commit.WorkflowExecutionId}' did not expose an optimistic revision."));
            StageValues(
                ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind,
                next,
                RuntimeStateChangeOperation.Upsert,
                options);
        }

        public void ApplyAlterationJob()
        {
            var change = commit.StateChanges.AlterationJobTerminalChange;
            if (change is null)
                return;

            var entry = Open(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind)
                .Read(GroundworkRuntimeRowStore.Key(change.JobId));
            if (entry is null)
                throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");

            var job = Deserialize<WorkflowAlterationJobState>(entry.Values.Values);
            if (!StringComparer.Ordinal.Equals(job.WorkflowExecutionId, commit.WorkflowExecutionId))
                throw new InvalidOperationException(
                    $"Alteration job '{change.JobId}' belongs to workflow '{job.WorkflowExecutionId}', not '{commit.WorkflowExecutionId}'.");

            if (job.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
            {
                if (StringComparer.Ordinal.Equals(job.CheckpointCommitId, change.CheckpointCommitId) &&
                    job.Status == change.Status &&
                    job.CompletedAt == change.CompletedAt &&
                    Equals(job.SafeFailure, change.SafeFailure) &&
                    job.Outcomes.SequenceEqual(change.Outcomes))
                    return;
                throw new InvalidOperationException(
                    "A terminal alteration job cannot be terminalized with conflicting checkpoint evidence.");
            }

            if (job.Status != WorkflowAlterationJobStatus.Running ||
                job.Claim is null ||
                !StringComparer.Ordinal.Equals(job.Claim.Token, change.ClaimToken))
                throw new InvalidOperationException($"The alteration job claim for '{change.JobId}' is no longer current.");

            var content = new WorkflowAlterationJobState(
                job.JobId,
                job.PlanId,
                job.WorkflowExecutionId,
                job.TenantPartition,
                job.CaptureOrdinal,
                change.Status,
                job.Claim,
                job.AttemptCount,
                change.Outcomes.ToArray(),
                change.CheckpointCommitId,
                change.SafeFailure,
                job.CreatedAt,
                job.StartedAt,
                change.CompletedAt,
                job.Revision + 1,
                job.CapturedConcurrency);
            StageUpsert(
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
                change.JobId,
                content,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField] = change.JobId,
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobPlanIdField] = job.PlanId,
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCaptureOrdinalField] = job.CaptureOrdinal,
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField] = change.Status.ToString(),
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField] = change.CheckpointCommitId,
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField] = null
                },
                WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                    $"Alteration job '{change.JobId}' did not expose a provider revision.")));
        }

        public void ApplyOperational()
        {
            foreach (var change in commit.StateChanges.Operational)
            {
                if (change.Operation == RuntimeStateChangeOperation.Delete)
                {
                    StageDelete(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, change.StateId);
                    continue;
                }

                var values = GroundworkV2RuntimeLivenessCodec.Values(change.State);
                StageValues(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, values, change.Operation);
            }
        }

        public void ApplyDispatches()
        {
            var staged = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in commit.StateChanges.WorkflowDispatches)
            {
                if (!staged.Add(change.StateId))
                    continue;

                var entry = Open(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(
                        GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(change.State.DispatchId)));
                if (entry is null)
                {
                    WorkflowDispatchLifecycle.ValidateNew(change.State);
                    StageDispatch(change.State, WriteOptions.CreateOnly);
                    continue;
                }

                var existing = GroundworkV2WorkflowDispatchStorageConventions.Deserialize(entry.Values.Values);
                WorkflowDispatchLifecycle.ValidateTransition(existing, change.State);
                if (WorkflowDispatchLifecycle.RecordsEqual(existing, change.State))
                    continue;
                StageDispatch(
                    change.State,
                    WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                        $"Workflow dispatch '{change.StateId}' did not expose a provider revision.")));
            }

            foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
            {
                var entry = Open(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(
                        GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(request.DispatchId)));
                if (entry is null)
                    throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");

                var existing = GroundworkV2WorkflowDispatchStorageConventions.Deserialize(entry.Values.Values);
                if (!StringComparer.Ordinal.Equals(existing.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
                    !StringComparer.Ordinal.Equals(existing.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId))
                {
                    throw new InvalidOperationException(
                        $"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with the persisted dispatch identity.");
                }
                if (!WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(existing))
                    throw new InvalidOperationException(
                        $"Workflow dispatch '{request.DispatchId}' does not permit parent cancellation propagation.");

                if (existing.Status == WorkflowDispatchStatus.Pending &&
                    !WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(existing))
                {
                    StageDispatch(
                        WorkflowDispatchLifecycle.CancelBeforeAdmission(existing, request.RequestedAt),
                        WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                            $"Workflow dispatch '{request.DispatchId}' did not expose a provider revision.")));
                }
                else if (existing.Status == WorkflowDispatchStatus.Started &&
                         !WorkflowDispatchLifecycle.IsCancellationRequested(existing))
                {
                    StageDispatch(
                        WorkflowDispatchLifecycle.MarkCancellationRequested(existing, request.RequestedAt),
                        WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                            $"Workflow dispatch '{request.DispatchId}' did not expose a provider revision.")));
                }
            }
        }

        public void ApplyOutbox()
        {
            var staged = new Dictionary<string, RuntimePostCommitOutboxItem>(StringComparer.Ordinal);
            foreach (var change in commit.StateChanges.PostCommitOutbox)
            {
                var candidate = change.State;
                if (staged.TryGetValue(change.StateId, out var duplicate))
                {
                    if (!GroundworkV2PostCommitOutboxStorageConventions.PendingItemsEquivalent(duplicate, candidate))
                        throw new InvalidOperationException(
                            $"Post-commit outbox item '{change.StateId}' occurs more than once with conflicting intent.");
                    continue;
                }

                staged.Add(change.StateId, candidate);
                var physicalId = GroundworkV2PostCommitOutboxStorageConventions.PhysicalId(candidate.OutboxItemId);
                var entry = Open(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(physicalId));
                if (entry is null)
                {
                    unitOfWork.Stage(RowWrite.Upsert(
                        Unit(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind),
                        GroundworkV2PostCommitOutboxStorageConventions.Values(candidate),
                        Observed(WriteOptions.CreateOnly)));
                    continue;
                }

                var existing = GroundworkV2PostCommitOutboxStorageConventions.Deserialize(entry.Values.Values);
                if (!StringComparer.Ordinal.Equals(existing.OutboxItemId, candidate.OutboxItemId))
                {
                    throw new InvalidOperationException(
                        $"Groundwork physical outbox identity collision detected for '{candidate.OutboxItemId}'.");
                }
                if (GroundworkV2PostCommitOutboxStorageConventions.PendingItemsEquivalent(existing, candidate))
                    continue;
                throw new InvalidOperationException(
                    $"Post-commit outbox item '{change.StateId}' already exists with conflicting intent or delivery state.");
            }
        }

        public void ApplyConsumedSchedulerWork()
        {
            foreach (var consumed in commit.StateChanges.ConsumedSchedulerWorkItems)
            {
                var entry = Open(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(
                        GroundworkV2SchedulerWorkStorageConventions.PhysicalId(
                            consumed.WorkflowExecutionId,
                            consumed.WorkItemId)));
                if (entry is null)
                    throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
                var envelope = GroundworkV2SchedulerWorkStorageConventions.Deserialize(entry.Values.Values);
                GroundworkV2SchedulerWorkStorageConventions.EnsureLogicalIdentity(
                    envelope,
                    consumed.WorkflowExecutionId,
                    consumed.WorkItemId);
                GroundworkV2SchedulerWorkStorageConventions.EnsurePhysicalIdentity(
                    entry.Values.Values,
                    envelope);
                var values = entry.Values.Values;
                var workflowExecutionId = ReadOptionalString(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
                if (workflowExecutionId is null || !StringComparer.Ordinal.Equals(workflowExecutionId, consumed.WorkflowExecutionId))
                    throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
                ValidateSchedulerClaim(values, consumed);
                unitOfWork.Stage(RowWrite.CompareAndDelete(
                    Unit(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind),
                    GroundworkRuntimeRowStore.Key(
                        GroundworkV2SchedulerWorkStorageConventions.PhysicalId(
                            consumed.WorkflowExecutionId,
                            consumed.WorkItemId)),
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = consumed.WorkflowExecutionId,
                        [ElsaRuntimeV2StorageManifest.SchedulerWorkClaimOwnerIdField] = consumed.ClaimOwnerId,
                        [ElsaRuntimeV2StorageManifest.SchedulerWorkFencingTokenField] = consumed.FencingToken
                    },
                    Observed(WriteOptions.Unconditional)));
            }
        }

        public void ApplyDurableTimers()
        {
            // The current checkpoint contract has no durable-timer state changes. Including this declared unit in the
            // exact UOW is intentional: future timer changes cannot silently escape the dependency-closed commit.
        }

        public void StageMarker()
        {
            var marker = new CheckpointMarker(
                commit.CommitId,
                commit.WorkflowExecutionId,
                commit.Checkpoint.OccurredAt,
                fingerprint,
                commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).Order(StringComparer.Ordinal).ToArray(),
                commit.StateChanges.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId).Order(StringComparer.Ordinal).ToArray());
            Stage(
                ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
                commit.CommitId,
                marker,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind
                },
                RuntimeStateChangeOperation.Append);
        }

        private void AssertOpenTestScope(WorkflowTestScope expected, string ownerId)
        {
            var entry = Open(ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind)
                .Read(GroundworkRuntimeRowStore.Key(expected.ScopeId));
            if (entry is null)
                throw new InvalidOperationException($"Workflow test scope '{expected.ScopeId}' is not admitted for '{ownerId}'.");
            var record = Deserialize<WorkflowTestScopeRecord>(entry.Values.Values);
            if (!WorkflowTestScope.ContextEquals(record.Scope, expected) ||
                record.State != WorkflowTestScopeState.Open ||
                record.Scope.IsExpired(commit.Checkpoint.OccurredAt))
            {
                throw new InvalidOperationException($"Workflow test scope '{expected.ScopeId}' is not open at checkpoint '{commit.CommitId}'.");
            }

            if (!touchedTestScopes.Add(expected.ScopeId))
                return;

            // Admission is a same-value CAS, not merely a pre-read. A concurrent close/expire operation therefore
            // conflicts with this checkpoint and rolls back every staged row in the exact UOW.
            unitOfWork.Stage(RowWrite.ConditionalUpsert(
                Unit(ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind),
                entry.Values,
                Observed(WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                    $"Workflow test scope '{expected.ScopeId}' did not expose a provider revision.")))));
        }

        private void Apply<TState>(
            string unitId,
            RuntimeStateChange<TState>? change,
            Func<TState, IReadOnlyDictionary<string, object?>> projection)
        {
            if (change is null)
                return;
            if (change.Operation == RuntimeStateChangeOperation.Delete)
            {
                StageDelete(unitId, change.StateId);
                return;
            }

            Stage(
                unitId,
                change.StateId,
                change.State!,
                projection(change.State!),
                change.Operation);
        }

        private void StageCleanupDelete(string unitId, string id, string workflowExecutionId)
        {
            var entry = Open(unitId).Read(GroundworkRuntimeRowStore.Key(id));
            if (entry is null)
                return;

            var projectedWorkflow = ReadOptionalString(entry.Values.Values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
            if (projectedWorkflow is not null && !StringComparer.Ordinal.Equals(projectedWorkflow, workflowExecutionId))
            {
                throw new InvalidOperationException(
                    $"Activity scope cleanup row '{id}' belongs to workflow '{projectedWorkflow}', not '{workflowExecutionId}'.");
            }

            StageDelete(
                unitId,
                id,
                WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                    $"Activity scope cleanup row '{id}' did not expose a provider revision.")));
        }

        private void Stage(
            string unitId,
            string id,
            object content,
            IReadOnlyDictionary<string, object?> projections,
            RuntimeStateChangeOperation operation)
        {
            var values = GroundworkRuntimeRowStore.Values(
                id,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                Serialize(content!),
                projections);
            StageValues(unitId, values, operation);
        }

        private void StageUpsert<TState>(
            string unitId,
            string id,
            TState content,
            Func<TState, IReadOnlyDictionary<string, object?>> projection,
            WriteOptions? options = null)
        {
            var values = GroundworkRuntimeRowStore.Values(
                id,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                Serialize(content!),
                projection(content));
            StageValues(unitId, values, RuntimeStateChangeOperation.Upsert, options);
        }

        private void StageUpsert(
            string unitId,
            string id,
            object content,
            IReadOnlyDictionary<string, object?> projections,
            WriteOptions? options = null) =>
            StageValues(
                unitId,
                GroundworkRuntimeRowStore.Values(
                    id,
                    ElsaRuntimeV2StorageManifest.SchemaVersion,
                    Serialize(content),
                    projections),
                RuntimeStateChangeOperation.Upsert,
                options);

        private void StageDispatch(WorkflowDispatchRecord state, WriteOptions? options = null) =>
            StageValues(
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                GroundworkV2WorkflowDispatchStorageConventions.Values(state),
                RuntimeStateChangeOperation.Upsert,
                options);

        private void StageValues(
            string unitId,
            StorageValues values,
            RuntimeStateChangeOperation operation,
            WriteOptions? options = null)
        {
            var unit = Unit(unitId);
            var undeclared = values.Values.Keys
                .Where(field => !unit.Columns.Any(column => StringComparer.Ordinal.Equals(column.Name, field)))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (undeclared.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Runtime projection for unit '{unit.Id.Value}' contains undeclared field(s): {string.Join(", ", undeclared)}.");
            }

            var write = operation switch
            {
                RuntimeStateChangeOperation.Append => RowWrite.Insert(unit, values, Observed(options ?? WriteOptions.CreateOnly)),
                RuntimeStateChangeOperation.Upsert when options is not null => RowWrite.ConditionalUpsert(unit, values, Observed(options)),
                _ => RowWrite.Upsert(unit, values, Observed(options ?? WriteOptions.Unconditional))
            };
            unitOfWork.Stage(write);
        }

        private void StageDelete(string unitId, string id, WriteOptions? options = null) =>
            unitOfWork.Stage(RowWrite.Delete(
                Unit(unitId), GroundworkRuntimeRowStore.Key(id), Observed(options ?? WriteOptions.Unconditional)));

        // Attaching the observer is what makes the production commit path measurable: the static WriteOptions
        // singletons carry no observer, and a caller outside src/ cannot reach the staging calls to supply one.
        //
        // Every write staged in this type must pass through here. Note that Stage and StageDelete are NOT the
        // only staging sites — five phases call unitOfWork.Stage(RowWrite...) directly (the liveness CAS, the
        // activity upsert, the outbox insert, the scheduler-work compare-and-delete, and the test-scope CAS),
        // and each wraps its own options. Missing one does not fail: it undercounts, silently, while the
        // adapter still reports its observer as exact. If you add a staging site, wrap it.
        private WriteOptions Observed(WriteOptions options) =>
            writePathObserver is null ? options : options with { Observer = writePathObserver };

        private IStorageSession Open(string unitId) =>
            unitSessions.TryGetValue(unitId, out var session)
                ? session
                : unitSessions[unitId] = unitOfWork.OpenSession(Unit(unitId));

        private StorageUnit Unit(string unitId) =>
            sessions.Unit(unitId, targetName);

        private static string Serialize(object value) => GroundworkV2RuntimeJson.Serialize(value);

        private RuntimeStaleFencingTokenException NewStaleFence(
            RuntimeFencingRejectionReason reason,
            long currentToken,
            long presentedToken) =>
            new(commit.WorkflowExecutionId, presentedToken, currentToken, reason);

        private static IReadOnlyDictionary<string, object?> ProjectWorkflowExecution(WorkflowExecutionState state) =>
            GroundworkV2WorkflowExecutionStorageConventions.Projections(state);

        private static IReadOnlyDictionary<string, object?> ProjectScheduler(SchedulerState state) =>
            GroundworkV2SchedulerStateStorageConventions.Projections(state);

        private static string? EffectiveExecutionScope(string? stateScope, string? provenanceScope) =>
            string.IsNullOrWhiteSpace(stateScope) ? provenanceScope : stateScope;

        private static void ValidateSchedulerClaim(
            IReadOnlyDictionary<string, object?> values,
            ConsumedSchedulerWorkItem consumed)
        {
            var projectedOwner = ReadOptionalString(values, ElsaRuntimeV2StorageManifest.SchedulerWorkClaimOwnerIdField);
            var projectedToken = ReadOptionalInt64(values, ElsaRuntimeV2StorageManifest.SchedulerWorkFencingTokenField);
            if (projectedOwner is null || projectedToken is null ||
                !StringComparer.Ordinal.Equals(projectedOwner, consumed.ClaimOwnerId) ||
                projectedToken != consumed.FencingToken)
                throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
        }

        private static long ReadHighestIssuedToken(ExecutionLivenessState state)
        {
            if (state.Metadata.TryGetValue(Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
                long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
                return token;
            return state.ExecutionLease?.FencingToken ?? 0;
        }

        private static string? ReadOptionalString(IReadOnlyDictionary<string, object?> values, string field) =>
            values.TryGetValue(field, out var raw)
                ? raw switch
                {
                    string text when !string.IsNullOrWhiteSpace(text) => text,
                    JsonElement { ValueKind: JsonValueKind.String } element when !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString(),
                    _ => null
                }
                : null;

        private static long? ReadOptionalInt64(IReadOnlyDictionary<string, object?> values, string field) =>
            values.TryGetValue(field, out var raw) switch
            {
                true when raw is long value => value,
                true when raw is int value => value,
                true when raw is JsonElement element && element.TryGetInt64(out var value) => value,
                _ => null
            };

        private static T Deserialize<T>(IReadOnlyDictionary<string, object?> values)
        {
            var result = GroundworkV2RuntimeJson.Deserialize<T>(ReadContent(values));
            return result ?? throw new InvalidDataException($"Groundwork runtime row could not deserialize as {typeof(T).Name}.");
        }

    }

    private sealed record CheckpointMarker(
        string CommitId,
        string WorkflowExecutionId,
        DateTimeOffset OccurredAt,
        string Fingerprint,
        IReadOnlyCollection<string> PendingPostCommitWorkIds,
        IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds);

}
