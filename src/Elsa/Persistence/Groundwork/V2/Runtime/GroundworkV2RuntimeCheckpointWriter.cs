using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Groundwork.Kernel;
using Groundwork.Store;

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
        ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind
    ];

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager;
    private readonly string? targetName;
    private readonly TimeProvider timeProvider;

    public GroundworkV2RuntimeCheckpointWriter(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null,
        TimeProvider? timeProvider = null,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.rootWriteLeaseManager = rootWriteLeaseManager;
        this.targetName = targetName;
        this.timeProvider = timeProvider ?? TimeProvider.System;
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
        var stage = new StageContext(sessions, targetName, unitOfWork, access, commit, fingerprint, cancellationToken, timeProvider);
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
                        FindFailedWriteId(report.Outcomes, ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, RowWriteMode.CompareAndDelete));
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
                FindFailedWriteId(exception.Outcomes, ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, RowWriteMode.CompareAndDelete));
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

    private RuntimeStaleFencingTokenException ReadCurrentFence(StorageAccess access, RuntimeCheckpointCommit commit)
    {
        var operationalStateId = $"ownership:{commit.WorkflowExecutionId}";
        var identity = GroundworkV2RuntimeLivenessCodec.Identity(commit.WorkflowExecutionId, operationalStateId);
        var unit = sessions.Unit(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, targetName);
        var entry = sessions.Open(unit.Id.Value, access, targetName).Read(GroundworkRuntimeRowStore.Key(identity));
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
            RequireOperation(change, RuntimeStateChangeOperation.Upsert, "activity execution inspection");
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

        foreach (var change in stateChanges.Incidents)
        {
            RequireOperation(change, RuntimeStateChangeOperation.Append, RuntimeStateChangeOperation.Upsert, "incident");
            RequireId(change.StateId, change.State.IncidentId, "incident");
            RequireWorkflow(change.State.WorkflowExecutionId, commit.WorkflowExecutionId, "incident");
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
        var unit = sessions.Unit(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, targetName);
        var entry = sessions.Open(unit.Id.Value, access, targetName).Read(GroundworkRuntimeRowStore.Key(commitId));
        return entry is null ? null : DeserializeMarker(entry.Values.Values);
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
        private readonly HashSet<string> touchedTestScopes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IStorageSession> unitSessions = new(StringComparer.Ordinal);
        private bool newWorkflowExecution;

        public StageContext(
            IGroundworkStorageSessionSource sessions,
            string? targetName,
            IUnitOfWork unitOfWork,
            StorageAccess access,
            RuntimeCheckpointCommit commit,
            string fingerprint,
            CancellationToken cancellationToken,
            TimeProvider timeProvider)
        {
            this.sessions = sessions;
            this.targetName = targetName;
            this.unitOfWork = unitOfWork;
            this.access = access;
            this.commit = commit;
            this.fingerprint = fingerprint;
            this.cancellationToken = cancellationToken;
            this.timeProvider = timeProvider;
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
                WriteOptions.IfVersion(entry.Version ?? 0)));
        }

        public void ApplyWorkflowExecution()
        {
            if (commit.StateChanges.WorkflowExecution is not { } change)
                return;

            newWorkflowExecution = Open(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind)
                .Read(GroundworkRuntimeRowStore.Key(change.StateId)) is null;
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
                    .Read(GroundworkRuntimeRowStore.Key(dispatch.StateId)) is null;
                if (isNew && dispatch.State.TestScope is { } scope)
                    AssertOpenTestScope(scope, dispatch.State.ChildWorkflowExecutionId);
            }
        }

        public void ApplyScheduler() => Apply(
            ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind,
            commit.StateChanges.Scheduler,
            ProjectScheduler);

        public void ApplyActivityExecutions() => ApplyMany(
            ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind,
            commit.StateChanges.ActivityExecutions,
            ProjectActivityExecution);

        public void ApplyInspectionsAndHierarchy()
        {
            ApplyMany(
                ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
                commit.StateChanges.ActivityExecutionInspections,
                ProjectInspection);
            foreach (var change in commit.StateChanges.ActivityExecutionInspections)
            {
                if (change.Operation == RuntimeStateChangeOperation.Delete)
                {
                    StageDelete(ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind, change.StateId);
                    continue;
                }

                var effectiveScope = EffectiveExecutionScope(change.State.ExecutionScopeId, change.State.Provenance.ExecutionScopeId);
                if (string.IsNullOrWhiteSpace(effectiveScope))
                    continue;
                var hierarchy = ActivityExecutionHierarchyProjector.FromInspection(
                    string.IsNullOrWhiteSpace(change.State.ExecutionScopeId)
                        ? change.State with { ExecutionScopeId = effectiveScope }
                        : change.State);
                StageUpsert(
                    ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
                    change.StateId,
                    hierarchy,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = hierarchy.WorkflowExecutionId,
                        [ElsaRuntimeV2StorageManifest.ExecutionScopeIdField] = hierarchy.ExecutionScopeId,
                        [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyIsScopeRootField] = StringComparer.Ordinal.Equals(hierarchy.ExecutionScopeId, hierarchy.ActivityExecutionId),
                        [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyExecutionSequenceField] = hierarchy.ExecutionSequence,
                        [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyActivityExecutionIdField] = hierarchy.ActivityExecutionId
                    });
            }
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
                    StageCleanupDelete(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, workItemId, cleanup.WorkflowExecutionId);
            }
        }

        public void ApplyIncidents()
        {
            foreach (var change in commit.StateChanges.Incidents)
            {
                var entry = Open(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(change.StateId));
                if (change.Operation == RuntimeStateChangeOperation.Append)
                {
                    Stage(
                        ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
                        change.StateId,
                        change.State,
                        ProjectIncident(change.State),
                        RuntimeStateChangeOperation.Append);
                    continue;
                }

                var existing = entry is null
                    ? null
                    : Deserialize<IncidentState>(entry.Values.Values);
                IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, change.State);
                StageUpsert(
                    ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
                    change.StateId,
                    change.State,
                    ProjectIncident,
                    entry is null
                        ? WriteOptions.CreateOnly
                        : WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                            $"Incident '{change.StateId}' did not expose a provider revision.")));
            }
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
                    .Read(GroundworkRuntimeRowStore.Key(change.StateId));
                if (entry is null)
                {
                    WorkflowDispatchLifecycle.ValidateNew(change.State);
                    StageUpsert(
                        ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                        change.StateId,
                        change.State,
                        ProjectDispatch,
                        WriteOptions.CreateOnly);
                    continue;
                }

                var existing = Deserialize<WorkflowDispatchRecord>(entry.Values.Values);
                WorkflowDispatchLifecycle.ValidateTransition(existing, change.State);
                if (WorkflowDispatchLifecycle.RecordsEqual(existing, change.State))
                    continue;
                StageUpsert(
                    ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                    change.StateId,
                    change.State,
                    ProjectDispatch,
                    WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                        $"Workflow dispatch '{change.StateId}' did not expose a provider revision.")));
            }

            foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
            {
                var entry = Open(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(request.DispatchId));
                if (entry is null)
                    throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");

                var existing = Deserialize<WorkflowDispatchRecord>(entry.Values.Values);
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
                    StageUpsert(
                        ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                        request.DispatchId,
                        WorkflowDispatchLifecycle.CancelBeforeAdmission(existing, request.RequestedAt),
                        ProjectDispatch,
                        WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                            $"Workflow dispatch '{request.DispatchId}' did not expose a provider revision.")));
                }
                else if (existing.Status == WorkflowDispatchStatus.Started &&
                         !WorkflowDispatchLifecycle.IsCancellationRequested(existing))
                {
                    StageUpsert(
                        ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                        request.DispatchId,
                        WorkflowDispatchLifecycle.MarkCancellationRequested(existing, request.RequestedAt),
                        ProjectDispatch,
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
                    if (!PendingOutboxItemsEquivalent(duplicate, candidate))
                        throw new InvalidOperationException(
                            $"Post-commit outbox item '{change.StateId}' occurs more than once with conflicting intent.");
                    continue;
                }

                staged.Add(change.StateId, candidate);
                var entry = Open(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(change.StateId));
                if (entry is null)
                {
                    StageUpsert(
                        ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                        change.StateId,
                        candidate,
                        ProjectOutbox,
                        WriteOptions.CreateOnly);
                    continue;
                }

                var existing = Deserialize<RuntimePostCommitOutboxItem>(entry.Values.Values);
                if (PendingOutboxItemsEquivalent(existing, candidate))
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
                    .Read(GroundworkRuntimeRowStore.Key(consumed.WorkItemId));
                if (entry is null)
                    throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
                var values = entry.Values.Values;
                var workflowExecutionId = ReadOptionalString(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
                if (workflowExecutionId is null || !StringComparer.Ordinal.Equals(workflowExecutionId, consumed.WorkflowExecutionId))
                    throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
                ValidateSchedulerClaim(values, consumed);
                unitOfWork.Stage(RowWrite.CompareAndDelete(
                    Unit(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind),
                    GroundworkRuntimeRowStore.Key(consumed.WorkItemId),
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = consumed.WorkflowExecutionId,
                        [ElsaRuntimeV2StorageManifest.SchedulerWorkClaimOwnerIdField] = consumed.ClaimOwnerId,
                        [ElsaRuntimeV2StorageManifest.SchedulerWorkFencingTokenField] = consumed.FencingToken
                    }));
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
                WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException(
                    $"Workflow test scope '{expected.ScopeId}' did not expose a provider revision."))));
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

        private void ApplyMany<TState>(
            string unitId,
            IReadOnlyCollection<RuntimeStateChange<TState>> changes,
            Func<TState, IReadOnlyDictionary<string, object?>> projection)
        {
            foreach (var change in changes)
                Apply(unitId, change, projection);
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
                RuntimeStateChangeOperation.Append => RowWrite.Insert(unit, values, options ?? WriteOptions.CreateOnly),
                RuntimeStateChangeOperation.Upsert when options is not null => RowWrite.ConditionalUpsert(unit, values, options),
                _ => RowWrite.Upsert(unit, values, options ?? WriteOptions.Unconditional)
            };
            unitOfWork.Stage(write);
        }

        private void StageDelete(string unitId, string id, WriteOptions? options = null) =>
            unitOfWork.Stage(RowWrite.Delete(Unit(unitId), GroundworkRuntimeRowStore.Key(id), options ?? WriteOptions.Unconditional));

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
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistorySortTicksField] = (state.UpdatedAt ?? state.CreatedAt).UtcTicks,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryTenantIdField] = state.TenantId,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryAuthorityPartitionField] = state.Authority is { } authority
                    ? WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity, authority.RootInitiator, authority.Metadata)
                    : null,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryDefinitionIdField] = state.PinnedExecutable.DefinitionId,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryStatusField] = (int)state.Status,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryRunKindField] = (int)state.RunKind,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryCorrelationIdField] = state.CorrelationId,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField] = state.PinnedExecutable.ArtifactId
            };

        private static IReadOnlyDictionary<string, object?> ProjectScheduler(SchedulerState state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind
            };

        private static IReadOnlyDictionary<string, object?> ProjectActivityExecution(ActivityExecutionState state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.ActivityExecutionIdField] = state.Execution.ActivityExecutionId,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.Execution.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ParentActivityExecutionIdField] = state.ParentActivityExecutionId,
                [ElsaRuntimeV2StorageManifest.ExecutionScopeIdField] = EffectiveExecutionScope(state.ExecutionScopeId, state.Provenance.ExecutionScopeId),
                [ElsaRuntimeV2StorageManifest.StatusField] = state.Status.ToString()
            };

        private static string? EffectiveExecutionScope(string? stateScope, string? provenanceScope) =>
            string.IsNullOrWhiteSpace(stateScope) ? provenanceScope : stateScope;

        private static IReadOnlyDictionary<string, object?> ProjectInspection(ActivityExecutionInspectionProjection state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField] = state.ExecutionSequence,
                [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryScheduledAtField] = state.ScheduledAt,
                [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField] = state.ActivityExecutionId
            };

        private static IReadOnlyDictionary<string, object?> ProjectIncident(IncidentState state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StatusField] = state.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.CreatedAtField] = state.CreatedAt,
                [ElsaRuntimeV2StorageManifest.IncidentIdField] = state.IncidentId
            };

        private static IReadOnlyDictionary<string, object?> ProjectDispatch(WorkflowDispatchRecord state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                [ElsaRuntimeV2StorageManifest.ParentWorkflowExecutionIdField] = state.ParentWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ChildWorkflowExecutionIdField] = state.ChildWorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StatusField] = state.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.TestScopeIdField] = state.TestScope?.ScopeId,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchCreatedAtField] = state.CreatedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowDispatchIdField] = state.DispatchId
            };

        private static IReadOnlyDictionary<string, object?> ProjectOutbox(RuntimePostCommitOutboxItem state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.Intent.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxStatusField] = (int)state.Status,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField] = state.AvailableAt,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField] = state.DeliveryVisibleAfter,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField] = state.RecordedAt,
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField] = RuntimePostCommitOutboxIdentity.CreateProjectionValue(state.OutboxItemId),
                [ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField] = state.Intent.Kind
            };

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

        private static bool PendingOutboxItemsEquivalent(
            RuntimePostCommitOutboxItem left,
            RuntimePostCommitOutboxItem right) =>
            left.Status == RuntimePostCommitOutboxStatus.Pending &&
            right.Status == RuntimePostCommitOutboxStatus.Pending &&
            StringComparer.Ordinal.Equals(left.Intent.IntentId, right.Intent.IntentId) &&
            StringComparer.Ordinal.Equals(left.Intent.WorkflowExecutionId, right.Intent.WorkflowExecutionId) &&
            StringComparer.Ordinal.Equals(left.Intent.Kind, right.Intent.Kind) &&
            StringComparer.Ordinal.Equals(left.Intent.ActivityExecutionId, right.Intent.ActivityExecutionId) &&
            StringComparer.Ordinal.Equals(left.Intent.IdempotencyKey, right.Intent.IdempotencyKey) &&
            StringComparer.Ordinal.Equals(left.Intent.DependsOnWaitRegistrationId, right.Intent.DependsOnWaitRegistrationId) &&
            left.Intent.WaitFailurePolicy == right.Intent.WaitFailurePolicy &&
            PayloadEquals(left.Intent.Payload, right.Intent.Payload) &&
            MetadataEquals(left.Intent.Metadata, right.Intent.Metadata) &&
            left.RecordedAt == right.RecordedAt &&
            left.AvailableAt == right.AvailableAt &&
            left.DeliveryAttemptCount == right.DeliveryAttemptCount &&
            left.DeliveryFencingToken == right.DeliveryFencingToken &&
            left.DeliveryVisibleAfter == right.DeliveryVisibleAfter &&
            left.RetryPolicy.IsEquivalentTo(right.RetryPolicy) &&
            MetadataEquals(left.Metadata, right.Metadata);

        private static bool PayloadEquals(JsonElement? left, JsonElement? right) =>
            left.HasValue == right.HasValue &&
            (!left.HasValue || StringComparer.Ordinal.Equals(left.Value.GetRawText(), right!.Value.GetRawText()));

        private static bool MetadataEquals(
            IReadOnlyDictionary<string, string> left,
            IReadOnlyDictionary<string, string> right) =>
            left.Count == right.Count &&
            left.All(entry => right.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));

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
