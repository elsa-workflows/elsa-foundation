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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
                throw new InvalidOperationException(
                    $"Groundwork rejected runtime checkpoint '{commit.CommitId}' with {report.Failed} failed row outcomes.");

            return stage.Result;
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

                var hierarchy = new HierarchyProjection(
                    change.State.ActivityExecutionId,
                    change.State.WorkflowExecutionId,
                    change.State.ExecutionScopeId ?? string.Empty,
                    change.State.ExecutionSequence,
                    change.State.ActivityExecutionId,
                    string.IsNullOrEmpty(change.State.ExecutionScopeId));
                StageUpsert(
                    ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
                    change.StateId,
                    hierarchy,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = hierarchy.WorkflowExecutionId,
                        [ElsaRuntimeV2StorageManifest.ExecutionScopeIdField] = hierarchy.ExecutionScopeId,
                        [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyIsScopeRootField] = hierarchy.IsScopeRoot,
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
                var ownershipStateId = $"ownership:{commit.WorkflowExecutionId}";
                if (commit.ExpectedFence is not null && StringComparer.Ordinal.Equals(change.StateId, ownershipStateId))
                {
                    var existing = Open(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind)
                        .Read(GroundworkRuntimeRowStore.Key(GroundworkV2RuntimeLivenessCodec.Identity(commit.WorkflowExecutionId, ownershipStateId)));
                    if (existing is null)
                        throw NewStaleFence(RuntimeFencingRejectionReason.NoActiveLease, 0, commit.ExpectedFence.FencingToken);
                    unitOfWork.Stage(RowWrite.ConditionalUpsert(
                        Unit(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind),
                        values,
                        WriteOptions.IfVersion(existing.Version ?? 0)));
                }
                else
                {
                    StageValues(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, values, change.Operation);
                }
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
                        ProjectDispatch);
                }
                else if (existing.Status == WorkflowDispatchStatus.Started &&
                         !WorkflowDispatchLifecycle.IsCancellationRequested(existing))
                {
                    StageUpsert(
                        ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                        request.DispatchId,
                        WorkflowDispatchLifecycle.MarkCancellationRequested(existing, request.RequestedAt),
                        ProjectDispatch);
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
                StageDelete(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, consumed.WorkItemId);
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
            Func<TState, IReadOnlyDictionary<string, object?>> projection)
        {
            var values = GroundworkRuntimeRowStore.Values(
                id,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                Serialize(content!),
                projection(content));
            StageValues(unitId, values, RuntimeStateChangeOperation.Upsert);
        }

        private void StageUpsert(
            string unitId,
            string id,
            object content,
            IReadOnlyDictionary<string, object?> projections) =>
            Stage(unitId, id, content, projections, RuntimeStateChangeOperation.Upsert);

        private void StageValues(string unitId, StorageValues values, RuntimeStateChangeOperation operation)
        {
            var unit = Unit(unitId);
            var write = operation switch
            {
                RuntimeStateChangeOperation.Append => RowWrite.Insert(unit, values, WriteOptions.CreateOnly),
                _ => RowWrite.Upsert(unit, values, WriteOptions.Unconditional)
            };
            unitOfWork.Stage(write);
        }

        private void StageDelete(string unitId, string id) =>
            unitOfWork.Stage(RowWrite.Delete(Unit(unitId), GroundworkRuntimeRowStore.Key(id), WriteOptions.Unconditional));

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
                [ElsaRuntimeV2StorageManifest.StimulusLookupKeyField] = state.StimulusHash,
                [ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField] = state.StimulusType,
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
                return;

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var owner = FindString(root, "claimOwnerId") ?? FindString(root, "ownerId");
            var token = FindInt64(root, "fencingToken");
            if (owner is null && token is null)
                return;
            if (!StringComparer.Ordinal.Equals(owner, consumed.ClaimOwnerId) || token != consumed.FencingToken)
                throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
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

        private sealed record HierarchyProjection(
            string Id,
            string WorkflowExecutionId,
            string ExecutionScopeId,
            long ExecutionSequence,
            string ActivityExecutionId,
            bool IsScopeRoot);

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
}
