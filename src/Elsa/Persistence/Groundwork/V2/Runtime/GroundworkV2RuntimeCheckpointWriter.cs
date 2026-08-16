using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static GroundworkV2RuntimeCheckpointWriter()
    {
        Json.Converters.Add(new JsonStringEnumConverter());
    }

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
    private readonly string? targetName;
    private readonly TimeProvider timeProvider;

    public GroundworkV2RuntimeCheckpointWriter(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
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
        if (commit.StateChanges.ActivityScopeCleanups.Count > 0)
        {
            throw new NotSupportedException(
                "Groundwork runtime v2 cannot apply activity-scope cleanup requests until their current row dependency is declared.");
        }
        RequireAtomicCommitCapability();
        var access = StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);

        if (ReadMarker(access, commit.CommitId) is { } existing)
            return ResolveReplay(commit, fingerprint, existing);

        using var unitOfWork = sessions.BeginUnitOfWork(access, BatchWriteOptions.Exact, CommitUnitIds, targetName);
        var stage = new StageContext(unitOfWork, access, commit, fingerprint, cancellationToken, timeProvider);
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
                if (ContainsConflict(report.Outcomes, ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, RowWriteMode.Delete))
                    throw new RuntimeSchedulerWorkConsumeConflictException(
                        commit.WorkflowExecutionId,
                        commit.StateChanges.ConsumedSchedulerWorkItems
                            .Select(item => item.WorkItemId)
                            .FirstOrDefault() ?? string.Empty);
                throw new InvalidOperationException(
                    $"Groundwork rejected runtime checkpoint '{commit.CommitId}' with {report.Failed} failed row outcomes.");
            }

            return stage.Result;
        }
        catch (BatchWriteException exception) when (ContainsConflict(exception.Outcomes, ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, RowWriteMode.ConditionalUpsert) ||
                                                    ContainsConflict(exception.Outcomes, ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, RowWriteMode.Delete))
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
                commit.StateChanges.ConsumedSchedulerWorkItems
                    .Select(item => item.WorkItemId)
                    .FirstOrDefault() ?? string.Empty);
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

    private void RequireAtomicCommitCapability()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork runtime checkpoint commits require the provider's evidenced atomic-commit capability.");
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
            outcome.Outcome.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound);

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
        }

        if (stateChanges.AlterationJobTerminalChange is { } alteration &&
            !StringComparer.Ordinal.Equals(alteration.CheckpointCommitId, commit.CommitId))
            throw new InvalidOperationException("Workflow alteration terminal evidence must reference its checkpoint commit ID.");

        if (stateChanges.ActivityScopeCleanups.Any(cleanup =>
                !StringComparer.Ordinal.Equals(cleanup.WorkflowExecutionId, commit.WorkflowExecutionId)))
            throw new InvalidOperationException("Activity scope cleanup WorkflowExecutionId must match the checkpoint workflow execution ID.");
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
        var marker = JsonSerializer.Deserialize<CheckpointMarker>(content, Json) ??
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
        private readonly IUnitOfWork unitOfWork;
        private readonly StorageAccess access;
        private readonly RuntimeCheckpointCommit commit;
        private readonly string fingerprint;
        private readonly CancellationToken cancellationToken;
        private readonly TimeProvider timeProvider;
        private readonly HashSet<string> touchedTestScopes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IStorageSession> unitSessions = new(StringComparer.Ordinal);

        public StageContext(
            IUnitOfWork unitOfWork,
            StorageAccess access,
            RuntimeCheckpointCommit commit,
            string fingerprint,
            CancellationToken cancellationToken,
            TimeProvider timeProvider)
        {
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

        public void ApplyWorkflowExecution() => Apply(
            ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
            commit.StateChanges.WorkflowExecution,
            ProjectWorkflowExecution);

        public void ValidateTestScopeAdmission()
        {
            if (commit.StateChanges.WorkflowExecution?.State.TestScope is { } executionScope)
                AssertOpenTestScope(executionScope, commit.WorkflowExecutionId);
            foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
            {
                if (dispatch.State.TestScope is { } scope)
                    AssertOpenTestScope(scope, dispatch.State.ChildWorkflowExecutionId);
            }
            foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
            {
                var row = Open(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(request.DispatchId));
                if (row is not null && Deserialize<WorkflowDispatchRecord>(row.Values.Values).TestScope is { } scope)
                    AssertOpenTestScope(scope, request.ChildWorkflowExecutionId);
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

                var hierarchy = ActivityExecutionHierarchyProjector.FromInspection(change.State);
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

        public void ApplyBookmarks() => ApplyMany(
            ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind,
            commit.StateChanges.Bookmarks,
            ProjectBookmark);

        public void ApplyDurableValues() => ApplyMany(
            ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind,
            commit.StateChanges.DurableValues,
            ProjectDurableValue);

        public void ApplyIncidents() => ApplyMany(
            ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
            commit.StateChanges.Incidents,
            ProjectIncident);

        public void ApplyAlterationJob()
        {
            var change = commit.StateChanges.AlterationJobTerminalChange;
            if (change is null)
                return;

            var content = new AlterationJobTerminalProjection(
                change.JobId,
                change.Status,
                change.CheckpointCommitId,
                change.CompletedAt,
                change.Outcomes,
                change.SafeFailure);
            StageUpsert(
                ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
                change.JobId,
                content,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField] = change.JobId,
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField] = change.Status.ToString(),
                    [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField] = change.CheckpointCommitId
                });
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
            ApplyMany(
                ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                commit.StateChanges.WorkflowDispatches,
                ProjectDispatch);

            foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
            {
                var entry = Open(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind)
                    .Read(GroundworkRuntimeRowStore.Key(request.DispatchId));
                if (entry is null)
                    continue;

                var existing = Deserialize<WorkflowDispatchRecord>(entry.Values.Values);
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

        public void ApplyOutbox() => ApplyMany(
            ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
            commit.StateChanges.PostCommitOutbox,
            ProjectOutbox);

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
                if (workflowExecutionId is not null && !StringComparer.Ordinal.Equals(workflowExecutionId, consumed.WorkflowExecutionId))
                    throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
                ValidateSchedulerClaim(values, consumed);
                // The public exact-UOW precondition is a provider revision CAS. Groundwork v2 does not expose a
                // composite owner+token precondition, so this revision guard is deliberately fail-closed: a claim
                // renewal that advances the revision can be reported claim-lost even though owner+token are stable.
                StageDelete(
                    ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
                    consumed.WorkItemId,
                    WriteOptions.IfVersion(entry.Version ?? throw new RuntimeSchedulerWorkConsumeConflictException(
                        consumed.WorkflowExecutionId,
                        consumed.WorkItemId)));
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
            IReadOnlyDictionary<string, object?> projections) =>
            Stage(unitId, id, content, projections, RuntimeStateChangeOperation.Upsert);

        private void StageValues(
            string unitId,
            StorageValues values,
            RuntimeStateChangeOperation operation,
            WriteOptions? options = null)
        {
            var unit = Unit(unitId);
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
            ElsaRuntimeV2StorageManifest.Require(unitId);

        private static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Json);

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
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.Execution.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ParentActivityExecutionIdField] = state.ParentActivityExecutionId,
                [ElsaRuntimeV2StorageManifest.ExecutionScopeIdField] = state.ExecutionScopeId ?? state.Provenance.ExecutionScopeId,
                [ElsaRuntimeV2StorageManifest.StatusField] = state.Status.ToString()
            };

        private static IReadOnlyDictionary<string, object?> ProjectInspection(ActivityExecutionInspectionProjection state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField] = state.ExecutionSequence,
                [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryScheduledAtField] = state.ScheduledAt,
                [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField] = state.ActivityExecutionId
            };

        private static IReadOnlyDictionary<string, object?> ProjectBookmark(BookmarkState state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StimulusHashField] = state.StimulusHash,
                [ElsaRuntimeV2StorageManifest.StimulusTypeField] = state.StimulusType,
                [ElsaRuntimeV2StorageManifest.StimulusLookupKeyField] = StimulusLookupKey.FromPair(state.StimulusType, state.StimulusHash),
                [ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField] = StimulusLookupKey.FromType(state.StimulusType),
                [ElsaRuntimeV2StorageManifest.BookmarkIdField] = state.BookmarkId
            };

        private static IReadOnlyDictionary<string, object?> ProjectDurableValue(DurableValueState state) =>
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.DurableValueIdField] = state.DurableValueId
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
            var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var raw)
                ? raw switch
                {
                    string text => text,
                    JsonElement element => element.GetRawText(),
                    _ => null
                }
                : null;
            if (content is null)
                throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);

            using var document = ParseSchedulerContent(content, consumed);
            var root = document.RootElement;
            var owner = FindString(root, "claimOwnerId") ?? FindString(root, "ownerId");
            var token = FindInt64(root, "fencingToken");
            if (owner is null || token is null ||
                !StringComparer.Ordinal.Equals(owner, consumed.ClaimOwnerId) || token != consumed.FencingToken)
                throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
        }

        private static JsonDocument ParseSchedulerContent(string content, ConsumedSchedulerWorkItem consumed)
        {
            try
            {
                return JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
            }
        }

        private static string? FindString(JsonElement root, string propertyName)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (root.TryGetProperty(propertyName, out var direct) && direct.ValueKind == JsonValueKind.String)
                return direct.GetString();
            if (root.TryGetProperty("claim", out var claim))
                return FindString(claim, propertyName) ?? FindString(claim, "ownerId");
            return null;
        }

        private static long? FindInt64(JsonElement root, string propertyName)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (root.TryGetProperty(propertyName, out var direct) && direct.TryGetInt64(out var value))
                return value;
            if (root.TryGetProperty("claim", out var claim))
                return FindInt64(claim, propertyName);
            return null;
        }

        private static long ReadHighestIssuedToken(ExecutionLivenessState state)
        {
            if (state.Metadata.TryGetValue(Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
                long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
                return token;
            return state.ExecutionLease?.FencingToken ?? 0;
        }

        private static string? ReadOptionalString(IReadOnlyDictionary<string, object?> values, string field) =>
            values.TryGetValue(field, out var raw) && raw is string text && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;

        private static T Deserialize<T>(IReadOnlyDictionary<string, object?> values)
        {
            var result = JsonSerializer.Deserialize<T>(ReadContent(values), Json);
            return result ?? throw new InvalidDataException($"Groundwork runtime row could not deserialize as {typeof(T).Name}.");
        }

        private sealed record AlterationJobTerminalProjection(
            string JobId,
            WorkflowAlterationJobStatus Status,
            string CheckpointCommitId,
            DateTimeOffset CompletedAt,
            IReadOnlyCollection<WorkflowAlterationOutcome> Outcomes,
            WorkflowAlterationSafeFailure? SafeFailure);
    }

    private sealed record CheckpointMarker(
        string CommitId,
        string WorkflowExecutionId,
        DateTimeOffset OccurredAt,
        string Fingerprint,
        IReadOnlyCollection<string> PendingPostCommitWorkIds,
        IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds);

    private static class StimulusLookupKey
    {
        public static string FromPair(string stimulusType, string stimulusHash) =>
            Hash($"{stimulusType.Length}:{stimulusType}{stimulusHash}");

        public static string FromType(string stimulusType) => Hash(stimulusType);

        private static string Hash(string value) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
