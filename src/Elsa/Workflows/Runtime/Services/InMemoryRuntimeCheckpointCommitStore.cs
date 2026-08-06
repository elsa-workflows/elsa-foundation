using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Application-wide state shared by scoped in-memory checkpoint-store adapters.</summary>
public sealed class InMemoryRuntimeCheckpointStoreState : IInMemoryCheckpointTransactionParticipant
{
    public InMemoryCheckpointParticipantGate TransactionGate { get; } = new();
    public bool IsAffected(InMemoryCheckpointMutationPlan plan) => true;
    internal object SyncRoot { get; } = new();
    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    internal Dictionary<string, RuntimeCheckpointCommitRecord> Commits { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, RuntimeLogicalCheckpointLedgerEntry> LogicalCheckpointLedger { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, InMemoryRuntimeCheckpointOrderState> CheckpointOrders { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, InMemoryRuntimeExecutionContextState> ExecutionContexts { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, RuntimePostCommitOutboxItem> OutboxItems { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowDispatchRecord> WorkflowDispatches { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowTestScopeRecord> WorkflowTestScopes { get; } = new(StringComparer.Ordinal);

    object IInMemoryCheckpointTransactionParticipant.CaptureCheckpointState(InMemoryCheckpointMutationPlan scope)
    {
        lock (SyncRoot)
        {
            return new CheckpointSnapshot(
                Capture(Commits, scope.CommitIds),
                Capture(LogicalCheckpointLedger, scope.CommitIds),
                Capture(CheckpointOrders, [scope.WorkflowExecutionId]),
                Capture(ExecutionContexts, [scope.WorkflowExecutionId]),
                Capture(OutboxItems, scope.OutboxItemIds),
                Capture(WorkflowDispatches, scope.WorkflowDispatchIds));
        }
    }

    void IInMemoryCheckpointTransactionParticipant.RestoreCheckpointState(object snapshot)
    {
        var checkpoint = (CheckpointSnapshot)snapshot;
        lock (SyncRoot)
        {
            Restore(Commits, checkpoint.Commits);
            Restore(LogicalCheckpointLedger, checkpoint.LogicalCheckpointLedger);
            Restore(CheckpointOrders, checkpoint.CheckpointOrders);
            Restore(ExecutionContexts, checkpoint.ExecutionContexts);
            Restore(OutboxItems, checkpoint.OutboxItems);
            Restore(WorkflowDispatches, checkpoint.WorkflowDispatches);
        }
    }

    private static ScopedSnapshot<T> Capture<T>(IReadOnlyDictionary<string, T> source, IEnumerable<string> keys)
    {
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        return new ScopedSnapshot<T>(
            selected,
            source.Where(entry => selected.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
    }

    private static void Restore<T>(IDictionary<string, T> target, ScopedSnapshot<T> snapshot)
    {
        foreach (var key in snapshot.Keys)
            target.Remove(key);
        foreach (var entry in snapshot.Values)
            target[entry.Key] = entry.Value;
    }

    private sealed record CheckpointSnapshot(
        ScopedSnapshot<RuntimeCheckpointCommitRecord> Commits,
        ScopedSnapshot<RuntimeLogicalCheckpointLedgerEntry> LogicalCheckpointLedger,
        ScopedSnapshot<InMemoryRuntimeCheckpointOrderState> CheckpointOrders,
        ScopedSnapshot<InMemoryRuntimeExecutionContextState> ExecutionContexts,
        ScopedSnapshot<RuntimePostCommitOutboxItem> OutboxItems,
        ScopedSnapshot<WorkflowDispatchRecord> WorkflowDispatches);

    private sealed record ScopedSnapshot<T>(HashSet<string> Keys, Dictionary<string, T> Values);
}

public sealed class InMemoryRuntimeCheckpointCommitStore : IRuntimeCheckpointCommitStore, IRuntimeCheckpointPreparedLedgerStore, IRuntimePostCommitOutboxStore, IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore
{
    private readonly InMemoryRuntimeCheckpointStoreState _state;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore;
    private readonly IActivityExecutionInspectionWriter? _activityExecutionInspectionWriter;
    private readonly IBookmarkStateStore? _bookmarkStateStore;
    private readonly IDurableValueStateStore? _durableValueStateStore;
    private readonly IIncidentStateStore? _incidentStateStore;
    private readonly IExecutionLivenessStateStore? _operationalStateStore;
    private readonly ISchedulerStateStore? _schedulerStateStore;
    private readonly IActivityScopeCleanupStore? _activityScopeCleanupStore;
    private readonly IActivityExecutionHierarchyWriter? _activityExecutionHierarchyWriter;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly IWorkflowSchedulerWorkQueue? _schedulerWorkQueue;
    private readonly IWorkflowExecutableRootWriteLeaseManager? _rootWriteLeaseManager;
    private readonly IWorkflowAlterationStore? _alterationStore;
    private readonly InMemoryCheckpointTransactionCoordinator _transactionCoordinator = new();
    private readonly RuntimeCheckpointPreparationPayloadCodec _preparationPayloadCodec = new();
    private readonly TimeProvider _timeProvider;
    private Exception? _nextPreparationFailure;

    /// <summary>
    /// Creates the in-memory commit store. RT-8: the seven telescoping constructors collapsed into this single primary
    /// constructor with every backing store optional. All parameters default to <c>null</c> so both the terse test
    /// constructions and the DI activation (which injects every registered backing store, unchanged) resolve to the same
    /// shape — W9's coalescing decorators keep wrapping this registration without modification.
    /// </summary>
    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IExecutionLivenessStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager,
        InMemoryRuntimeCheckpointStoreState? state,
        TimeProvider? timeProvider)
        : this(
            workflowExecutionStateStore,
            activityExecutionStateStore,
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            schedulerStateStore,
            activityExecutionInspectionWriter,
            rootWriteLeaseManager,
            state,
            timeProvider,
            workflowDispatchStore: null)
    {
    }

    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        IBookmarkStateStore? bookmarkStateStore = null,
        IDurableValueStateStore? durableValueStateStore = null,
        IIncidentStateStore? incidentStateStore = null,
        IExecutionLivenessStateStore? operationalStateStore = null,
        ISchedulerStateStore? schedulerStateStore = null,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter = null,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null,
        InMemoryRuntimeCheckpointStoreState? state = null,
        TimeProvider? timeProvider = null,
        IActivityScopeCleanupStore? activityScopeCleanupStore = null,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter = null,
        IWorkflowDispatchStore? workflowDispatchStore = null,
        IWorkflowSchedulerWorkQueue? schedulerWorkQueue = null,
        IWorkflowAlterationStore? alterationStore = null)
    {
        _state = state ?? new InMemoryRuntimeCheckpointStoreState();
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _activityExecutionInspectionWriter = activityExecutionInspectionWriter;
        _bookmarkStateStore = bookmarkStateStore;
        _durableValueStateStore = durableValueStateStore;
        _incidentStateStore = incidentStateStore;
        _operationalStateStore = operationalStateStore;
        _schedulerStateStore = schedulerStateStore;
        _activityScopeCleanupStore = activityScopeCleanupStore;
        _activityExecutionHierarchyWriter = activityExecutionHierarchyWriter;
        _rootWriteLeaseManager = rootWriteLeaseManager;
        _workflowDispatchStore = workflowDispatchStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _alterationStore = alterationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
        RuntimeCheckpointPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationIdentity);
        ArgumentNullException.ThrowIfNull(request.RequestedExecutionContext);
        cancellationToken.ThrowIfCancellationRequested();

        var payloadEnvelope = _preparationPayloadCodec.Encode(request);
        var decodedRequest = _preparationPayloadCodec.Decode(payloadEnvelope);
        var frozenRequest = decodedRequest with
        {
            Commit = decodedRequest.Commit with { ExpectedFence = request.Commit.ExpectedFence }
        };
        var commit = frozenRequest.Commit;
        var inputFingerprint = payloadEnvelope.PayloadSha256;
        var canonicalInputPayload = payloadEnvelope.CanonicalPayload;
        var plan = new InMemoryCheckpointMutationPlan(commit, commit.StateChanges.PostCommitOutbox.Select(x => x.State).ToArray());
        await using var preparationTransaction = await _transactionCoordinator.BeginAsync(plan, BuildTransactionRoots(plan), cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            RuntimeLogicalCheckpointLedgerEntry? preparedEntry = null;
            lock (_state.SyncRoot)
            {
                if (_state.LogicalCheckpointLedger.TryGetValue(commit.CommitId, out var existing))
                {
                    if (!StringComparer.Ordinal.Equals(existing.InputFingerprint, inputFingerprint))
                        return Complete(RuntimeCheckpointPreparationResult.Conflict());

                    if (existing.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed)
                        return Complete(RuntimeCheckpointPreparationResult.Replay(ToToken(existing), existing.Receipt));
                    preparedEntry = existing;
                }
                else if (_state.Commits.TryGetValue(commit.CommitId, out var legacyMarker))
                    return Complete(ReconcileLegacyMarker(frozenRequest, inputFingerprint, legacyMarker));
            }

            try
            {
                await EnsureExpectedFenceAsync(commit, cancellationToken);
            }
            catch (RuntimeStaleFencingTokenException)
            {
                return Complete(RuntimeCheckpointPreparationResult.OwnershipLost());
            }

            if (preparedEntry is not null)
            {
                lock (_state.SyncRoot)
                    return Complete(RefreshPreparedReservation(preparedEntry, commit));
            }

            var (order, expectedOrderRevision) = ReserveCheckpointOrder(commit.WorkflowExecutionId);
            ThrowInjectedPreparationFailure();
            var contextState = ReadExecutionContextState(commit.WorkflowExecutionId);
            var expectedContextRevision = contextState.Revision;
            var provenance = new RuntimeCheckpointProvenance(
                frozenRequest.RequestedExecutionContext.IsEmpty ? contextState.Snapshot : frozenRequest.RequestedExecutionContext,
                order);
            var ledgerToken = Guid.NewGuid().ToString("N");
            var canonicalInputReference = $"runtime-checkpoint:{commit.WorkflowExecutionId}:{commit.CommitId}";
            var entry = new RuntimeLogicalCheckpointLedgerEntry(
                commit.CommitId,
                ledgerToken,
                provenance,
                frozenRequest.SourceIdentity,
                frozenRequest.OperationIdentity,
                commit.Checkpoint with { Provenance = null },
                commit.StateChanges,
                commit.PostCommitIntents,
                commit.Metadata,
                frozenRequest.RequestedExecutionContext,
                expectedOrderRevision,
                expectedContextRevision,
                commit.ExpectedFence,
                inputFingerprint,
                canonicalInputPayload,
                canonicalInputReference,
                frozenRequest.InitialPersistenceMode,
                RuntimeLogicalCheckpointLedgerStatus.Prepared,
                WorkflowExecutionId: commit.WorkflowExecutionId,
                RecoveryAuthority: frozenRequest.RecoveryAuthority,
                CurrentAuthorityFence: commit.ExpectedFence,
                AuthorityRevision: 1);
            lock (_state.SyncRoot)
                _state.LogicalCheckpointLedger.Add(commit.CommitId, entry);
            return Complete(RuntimeCheckpointPreparationResult.Prepared(ToToken(entry)));
        }
        catch (Exception exception)
        {
            throw preparationTransaction.Rollback(exception);
        }
        finally
        {
            _state.WriteGate.Release();
        }

        RuntimeCheckpointPreparationResult Complete(RuntimeCheckpointPreparationResult result)
        {
            preparationTransaction.Commit();
            return result;
        }
    }

    public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
        RuntimeCheckpointPreparedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = query.DecodeCursor();
        RuntimeLogicalCheckpointLedgerEntry[] entries;
        using (_state.TransactionGate.Enter())
        {
            lock (_state.SyncRoot)
            {
                entries = _state.LogicalCheckpointLedger.Values
                    .Where(entry => entry.Status == RuntimeLogicalCheckpointLedgerStatus.Prepared)
                    .Where(entry => StringComparer.Ordinal.Equals(DecodePreparation(entry).Commit.WorkflowExecutionId, query.WorkflowExecutionId))
                    .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder)
                    .ThenBy(entry => entry.CommitId, StringComparer.Ordinal)
                    .Where(entry => cursor is null ||
                                    entry.Provenance.WorkflowCheckpointOrder > cursor.WorkflowCheckpointOrder ||
                                    entry.Provenance.WorkflowCheckpointOrder == cursor.WorkflowCheckpointOrder &&
                                    StringComparer.Ordinal.Compare(entry.CommitId, cursor.CommitId) > 0)
                    .Take(query.PageSize + 1)
                    .ToArray();
            }
        }

        var pageEntries = entries.Take(query.PageSize).ToArray();
        var reservations = pageEntries.Select(ToPreparedReservation).ToArray();
        var nextCursor = entries.Length > query.PageSize && pageEntries.Length > 0
            ? query.EncodeCursor(pageEntries[^1].Provenance.WorkflowCheckpointOrder, pageEntries[^1].CommitId)
            : null;
        return ValueTask.FromResult(new RuntimeCheckpointPreparedPage(query, reservations, nextCursor));
    }

    public async ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanCreateAdoptionPlan(request))
            return AdoptionConflict(request);

        var authorityCommit = CreateLedgerMutationCommit(request.WorkflowExecutionId, request.TargetAuthorityFence);
        var plan = new InMemoryCheckpointMutationPlan(
            authorityCommit,
            commitIds: request.Members.Select(member => member.CommitId).ToArray());
        await using var transaction = await _transactionCoordinator.BeginAsync(plan, BuildTransactionRoots(plan), cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            RuntimeCheckpointPreparedAdoptionReceipt receipt;
            lock (_state.SyncRoot)
                receipt = AdoptPreparedCore(request, apply: false);
            if (receipt.Status == RuntimeCheckpointPreparedAdoptionStatus.Adopted)
            {
                try
                {
                    await EnsureExpectedFenceAsync(authorityCommit, cancellationToken);
                }
                catch (RuntimeStaleFencingTokenException)
                {
                    transaction.Commit();
                    return receipt with { Status = RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost };
                }
                lock (_state.SyncRoot)
                    receipt = AdoptPreparedCore(request, apply: true);
            }
            transaction.Commit();
            return receipt;
        }
        catch (Exception exception)
        {
            throw transaction.Rollback(exception);
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    public async ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (!RuntimeCheckpointPreparedFoldBuilder.Matches(request))
            return PreparedFoldConflict();

        var foldedCommit = CreatePreparedFoldCommit(request);
        var plan = new InMemoryCheckpointMutationPlan(
            foldedCommit,
            request.FoldedStateChanges.PostCommitOutbox.Select(change => change.State).ToArray(),
            request.Members.Select(member => member.CommitId).ToArray());
        await using var transaction = await _transactionCoordinator.BeginAsync(plan, BuildTransactionRoots(plan), cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            RuntimeCheckpointPreparedFoldResult? result = null;
            lock (_state.SyncRoot)
            {
                var entries = ResolvePreparedFoldEntries(request);
                if (entries is null)
                {
                    transaction.Commit();
                    return PreparedFoldConflict();
                }

                var foldFingerprint = RuntimeCheckpointCommitFingerprint.ComputePreparedFold(request);
                if (entries.All(entry => entry.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed))
                {
                    if (entries.Any(entry => !StringComparer.Ordinal.Equals(entry.TerminalFoldFingerprint, foldFingerprint)))
                    {
                        transaction.Commit();
                        return PreparedFoldConflict();
                    }

                    transaction.Commit();
                    return new RuntimeCheckpointPreparedFoldResult(
                        RuntimeCheckpointCommitStoreStatus.Replay,
                        entries.ToDictionary(entry => entry.CommitId, entry => entry.Receipt!, StringComparer.Ordinal));
                }

                if (entries.Any(entry => entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared) ||
                    !IsExactPreparedPrefix(request, entries))
                {
                    transaction.Commit();
                    return PreparedFoldConflict();
                }

                var validation = ValidatePreparedFoldMembers(request, entries);
                if (!validation.Succeeded)
                {
                    transaction.Commit();
                    return PreparedFoldConflict();
                }

                result = new RuntimeCheckpointPreparedFoldResult(
                    RuntimeCheckpointCommitStoreStatus.Committed,
                    CreateFoldReceipts(request, validation.PreparedCommits!));
            }

            await CommitNewAsync(
                foldedCommit,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate),
                cancellationToken,
                validateFence: true,
                finalize: _ => FinalizePreparedFold(request, result!),
                writeCommitRecord: false);
            transaction.Commit();
            return result!;
        }
        catch (RuntimeStaleFencingTokenException)
        {
            transaction.Commit();
            return PreparedFoldConflict();
        }
        catch (Exception exception)
        {
            throw transaction.Rollback(exception);
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    private static RuntimeCheckpointPreparedReservation ToPreparedReservation(RuntimeLogicalCheckpointLedgerEntry entry) =>
        new(
            ToToken(entry),
            new RuntimeCheckpointPreparationPayloadEnvelope(
                RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
                entry.CanonicalInputPayload ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no canonical recovery input."),
                entry.InputFingerprint),
            entry.Status,
            entry.CurrentAuthorityFence,
            entry.AuthorityRevision);

    private RuntimeCheckpointPreparedAdoptionReceipt AdoptPreparedCore(
        RuntimeCheckpointPreparedAdoptionRequest request,
        bool apply)
    {
        if (!CanCreateAdoptionPlan(request))
            return AdoptionConflict(request);

        var requestedMembers = request.Members;
        if (request.Route == RuntimeCheckpointRecoveryRoute.SourceBound &&
            requestedMembers.Any(member => member.RecoveryAuthority is null))
            return AdoptionConflict(request);

        var prepared = _state.LogicalCheckpointLedger.Values
            .Where(entry => entry.Status == RuntimeLogicalCheckpointLedgerStatus.Prepared)
            .Where(entry => StringComparer.Ordinal.Equals(entry.WorkflowExecutionId, request.WorkflowExecutionId))
            .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder)
            .ThenBy(entry => entry.CommitId, StringComparer.Ordinal)
            .ToArray();
        var scope = prepared
            .Where(entry => entry.Provenance.WorkflowCheckpointOrder <= request.ThroughWorkflowCheckpointOrder)
            .ToArray();

        if (scope.Length != requestedMembers.Count ||
            scope.Length == 0 ||
            scope[^1].Provenance.WorkflowCheckpointOrder != request.ThroughWorkflowCheckpointOrder ||
            !scope.Select(ToAdoptionMember).SequenceEqual(requestedMembers))
        {
            return AdoptionConflict(request);
        }

        if (request.Route == RuntimeCheckpointRecoveryRoute.SourceFree && scope.Any(entry => entry.RecoveryAuthority is not null) ||
            request.Route == RuntimeCheckpointRecoveryRoute.SourceBound && scope.Any(entry => entry.RecoveryAuthority is null))
        {
            return AdoptionConflict(request);
        }

        var currentBinding = new RuntimeCheckpointAuthorityBinding(scope[0].CurrentAuthorityFence, scope[0].AuthorityRevision).Validate();
        if (scope.Any(entry =>
                !Equals(entry.CurrentAuthorityFence, currentBinding.CurrentAuthorityFence) ||
                entry.AuthorityRevision != currentBinding.AuthorityRevision))
        {
            return AdoptionConflict(request);
        }

        if (Equals(currentBinding.CurrentAuthorityFence, request.TargetAuthorityFence))
            return AdoptionReplay(request);
        if (!currentBinding.CanAdvanceTo(request.TargetAuthorityFence))
            return AdoptionConflict(request);

        if (!apply)
        {
            return new RuntimeCheckpointPreparedAdoptionReceipt(
                RuntimeCheckpointPreparedAdoptionStatus.Adopted,
                request.WorkflowExecutionId,
                request.Route,
                request.ThroughWorkflowCheckpointOrder,
                request.TargetAuthorityFence,
                scope.Select(entry => entry.CommitId).ToArray());
        }

        foreach (var entry in scope)
        {
            _state.LogicalCheckpointLedger[entry.CommitId] = entry with
            {
                CurrentAuthorityFence = request.TargetAuthorityFence,
                AuthorityRevision = checked(entry.AuthorityRevision + 1)
            };
        }

        return new RuntimeCheckpointPreparedAdoptionReceipt(
            RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            request.WorkflowExecutionId,
            request.Route,
            request.ThroughWorkflowCheckpointOrder,
            request.TargetAuthorityFence,
            scope.Select(entry => entry.CommitId).ToArray());
    }

    private static bool CanCreateAdoptionPlan(RuntimeCheckpointPreparedAdoptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowExecutionId) ||
            request.TargetAuthorityFence is null ||
            !Enum.IsDefined(request.Route) ||
            request.ThroughWorkflowCheckpointOrder < 1 ||
            request.Members is null ||
            request.Members.Count == 0)
        {
            return false;
        }

        var commitIds = new HashSet<string>(StringComparer.Ordinal);
        var ledgerTokens = new HashSet<string>(StringComparer.Ordinal);
        long priorOrder = 0;
        foreach (var member in request.Members)
        {
            if (member is null ||
                string.IsNullOrWhiteSpace(member.CommitId) ||
                string.IsNullOrWhiteSpace(member.LedgerToken) ||
                string.IsNullOrWhiteSpace(member.CanonicalInputFingerprint) ||
                string.IsNullOrWhiteSpace(member.CanonicalInputReference) ||
                member.WorkflowCheckpointOrder <= priorOrder ||
                member.OriginalOrderRevision < 0 ||
                member.OriginalContextRevision < 0 ||
                member.ExpectedAuthorityRevision < 1 ||
                !commitIds.Add(member.CommitId) ||
                !ledgerTokens.Add(member.LedgerToken))
            {
                return false;
            }

            priorOrder = member.WorkflowCheckpointOrder;
        }

        return priorOrder == request.ThroughWorkflowCheckpointOrder;
    }

    private static RuntimeCheckpointPreparedAdoptionMember ToAdoptionMember(RuntimeLogicalCheckpointLedgerEntry entry) =>
        new(
            entry.CommitId,
            entry.LedgerToken,
            entry.Provenance.WorkflowCheckpointOrder,
            entry.InputFingerprint,
            entry.CanonicalInputReference ?? string.Empty,
            entry.ExpectedFence,
            entry.ExpectedOrderRevision ?? -1,
            entry.ExpectedContextRevision ?? -1,
            entry.RecoveryAuthority,
            entry.CurrentAuthorityFence,
            entry.AuthorityRevision);

    private RuntimeLogicalCheckpointLedgerEntry[]? ResolvePreparedFoldEntries(RuntimeCheckpointPreparedFoldRequest request)
    {
        var entries = new RuntimeLogicalCheckpointLedgerEntry[request.Members.Count];
        for (var index = 0; index < request.Members.Count; index++)
        {
            var member = request.Members[index];
            if (!_state.LogicalCheckpointLedger.TryGetValue(member.CommitId, out var entry) ||
                !StringComparer.Ordinal.Equals(entry.WorkflowExecutionId, request.WorkflowExecutionId) ||
                entry.Provenance.WorkflowCheckpointOrder != member.WorkflowCheckpointOrder ||
                !TokenMatches(entry, member.Token))
            {
                return null;
            }

            entries[index] = entry;
        }

        return entries;
    }

    private bool IsExactPreparedPrefix(
        RuntimeCheckpointPreparedFoldRequest request,
        IReadOnlyList<RuntimeLogicalCheckpointLedgerEntry> entries)
    {
        var allPrepared = _state.LogicalCheckpointLedger.Values
            .Where(entry => entry.Status == RuntimeLogicalCheckpointLedgerStatus.Prepared)
            .Where(entry => StringComparer.Ordinal.Equals(entry.WorkflowExecutionId, request.WorkflowExecutionId))
            .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder)
            .ThenBy(entry => entry.CommitId, StringComparer.Ordinal)
            .ToArray();
        var expected = allPrepared
            .TakeWhile(entry => entry.Provenance.WorkflowCheckpointOrder <= request.MaxWorkflowCheckpointOrder)
            .ToArray();
        return expected.Length == entries.Count && expected.SequenceEqual(entries);
    }

    private static PreparedFoldValidation ValidatePreparedFoldMembers(
        RuntimeCheckpointPreparedFoldRequest request,
        IReadOnlyList<RuntimeLogicalCheckpointLedgerEntry> entries)
    {
        var preparedCommits = new RuntimeCheckpointPreparedCommit?[request.Members.Count];
        for (var index = 0; index < request.Members.Count; index++)
        {
            var member = request.Members[index];
            var entry = entries[index];
            try
            {
                member.Validate();
            }
            catch (ArgumentException)
            {
                return PreparedFoldValidation.Invalid;
            }

            if (member.Disposition == RuntimeCheckpointPreparedDisposition.Failed)
            {
                if (!CurrentBindingMatches(entry, member))
                    return PreparedFoldValidation.Invalid;
                continue;
            }

            var prepared = member.PreparedCommit!;
            if (!CurrentBindingMatches(entry, member) ||
                !Equals(prepared.CurrentAuthorityFence, member.ExpectedCurrentAuthorityFence) ||
                prepared.AuthorityRevision != member.ExpectedAuthorityRevision ||
                !StringComparer.Ordinal.Equals(prepared.Commit.CommitId, entry.CommitId) ||
                !StringComparer.Ordinal.Equals(prepared.Commit.WorkflowExecutionId, request.WorkflowExecutionId) ||
                !Equals(prepared.Commit.Checkpoint.Provenance, entry.Provenance) ||
                !StringComparer.Ordinal.Equals(prepared.VerifiedCommitFingerprint, RuntimeCheckpointCommitFingerprint.Compute(prepared.Commit)))
            {
                return PreparedFoldValidation.Invalid;
            }

            preparedCommits[index] = prepared;
        }

        if (request.RecoveryRoute == RuntimeCheckpointRecoveryRoute.SourceFree &&
            entries.Any(entry => entry.RecoveryAuthority is not null))
            return PreparedFoldValidation.Invalid;

        return new PreparedFoldValidation(preparedCommits);
    }

    private static bool CurrentBindingMatches(
        RuntimeLogicalCheckpointLedgerEntry entry,
        RuntimeCheckpointPreparedFoldMember member) =>
        Equals(entry.CurrentAuthorityFence, member.ExpectedCurrentAuthorityFence) &&
        entry.AuthorityRevision == member.ExpectedAuthorityRevision;

    private static IReadOnlyDictionary<string, RuntimeCheckpointCommitStoreResult> CreateFoldReceipts(
        RuntimeCheckpointPreparedFoldRequest request,
        IReadOnlyList<RuntimeCheckpointPreparedCommit?> preparedCommits)
    {
        var receipts = new Dictionary<string, RuntimeCheckpointCommitStoreResult>(StringComparer.Ordinal);
        for (var index = 0; index < request.Members.Count; index++)
        {
            var member = request.Members[index];
            var prepared = preparedCommits[index];
            receipts.Add(member.CommitId, member.Disposition switch
            {
                RuntimeCheckpointPreparedDisposition.Committed => new RuntimeCheckpointCommitStoreResult(
                    prepared!.Commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray())
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Committed,
                    CommitFingerprint = prepared.VerifiedCommitFingerprint,
                    ConsumedSchedulerWorkItemIds = prepared.Commit.StateChanges.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId).ToArray()
                },
                RuntimeCheckpointPreparedDisposition.Skipped => new RuntimeCheckpointCommitStoreResult([])
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Skipped,
                    CommitFingerprint = prepared!.VerifiedCommitFingerprint
                },
                RuntimeCheckpointPreparedDisposition.Failed => new RuntimeCheckpointCommitStoreResult([])
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Failed,
                    FailureCode = member.FailureCode,
                    FailureMessage = member.FailureMessage
                },
                _ => throw new InvalidOperationException("The prepared-checkpoint disposition is invalid.")
            });
        }

        return receipts;
    }

    private void FinalizePreparedFold(
        RuntimeCheckpointPreparedFoldRequest request,
        RuntimeCheckpointPreparedFoldResult result)
    {
        var foldFingerprint = RuntimeCheckpointCommitFingerprint.ComputePreparedFold(request);
        var entries = ResolvePreparedFoldEntries(request) ??
                      throw new InvalidOperationException("Prepared checkpoint fold membership changed before finalization.");
        if (entries.Where((entry, index) => !CurrentBindingMatches(entry, request.Members[index])).Any())
            throw new InvalidOperationException("Prepared checkpoint fold authority changed before finalization.");
        var order = ReadCheckpointOrderState(request.WorkflowExecutionId);
        if (order.Reserved < request.MaxWorkflowCheckpointOrder || order.Committed >= request.MaxWorkflowCheckpointOrder)
            throw new InvalidOperationException("Prepared checkpoint fold order authority changed before finalization.");

        var context = _state.ExecutionContexts.GetValueOrDefault(request.WorkflowExecutionId) ?? InMemoryRuntimeExecutionContextState.Empty;
        var lastCommittedIndex = -1;
        for (var index = request.Members.Count - 1; index >= 0; index--)
        {
            if (request.Members[index].Disposition != RuntimeCheckpointPreparedDisposition.Committed)
                continue;
            lastCommittedIndex = index;
            break;
        }

        if (lastCommittedIndex >= 0)
        {
            var terminalContext = entries[lastCommittedIndex].Provenance.ExecutionContext ??
                                  throw new InvalidOperationException("Prepared checkpoint fold has no committed execution context.");
            if (!context.Snapshot.Equals(terminalContext))
                _state.ExecutionContexts[request.WorkflowExecutionId] = new(terminalContext, checked(context.Revision + 1));
        }
        _state.CheckpointOrders[request.WorkflowExecutionId] = new(
            order.Reserved,
            Math.Max(order.Committed, request.MaxWorkflowCheckpointOrder),
            checked(order.Revision + 1));

        for (var index = 0; index < request.Members.Count; index++)
        {
            var member = request.Members[index];
            var entry = entries[index];
            var receipt = result.Receipts[member.CommitId];
            var status = member.Disposition switch
            {
                RuntimeCheckpointPreparedDisposition.Committed => RuntimeLogicalCheckpointLedgerStatus.Committed,
                RuntimeCheckpointPreparedDisposition.Skipped => RuntimeLogicalCheckpointLedgerStatus.Skipped,
                RuntimeCheckpointPreparedDisposition.Failed => RuntimeLogicalCheckpointLedgerStatus.Failed,
                _ => throw new InvalidOperationException("The prepared-checkpoint disposition is invalid.")
            };
            var fingerprint = member.PreparedCommit?.VerifiedCommitFingerprint;
            var terminalAuthority = request.RecoveryRoute == RuntimeCheckpointRecoveryRoute.SourceFree
                ? entry with
                {
                    CurrentAuthorityFence = request.TargetAuthorityFence,
                    AuthorityRevision = checked(entry.AuthorityRevision + 1)
                }
                : entry;
            _state.LogicalCheckpointLedger[entry.CommitId] = terminalAuthority.CompactTerminal(
                status,
                receipt,
                fingerprint,
                foldFingerprint,
                request.WorkflowExecutionId);

            if (status != RuntimeLogicalCheckpointLedgerStatus.Committed)
                continue;

            var prepared = member.PreparedCommit!;
            _state.Commits.Add(entry.CommitId, new RuntimeCheckpointCommitRecord(
                prepared.Commit,
                prepared.Decision,
                receipt.PendingPostCommitWorkIds,
                receipt.ConsumedSchedulerWorkItemIds));
        }
    }

    private static RuntimeCheckpointCommit CreatePreparedFoldCommit(RuntimeCheckpointPreparedFoldRequest request) =>
        new(
            $"prepared-fold:{request.WorkflowExecutionId}:{request.MaxWorkflowCheckpointOrder}",
            new RuntimeCheckpoint(
                $"prepared-fold:{request.WorkflowExecutionId}:{request.MaxWorkflowCheckpointOrder}",
                "PreparedFold",
                request.WorkflowExecutionId,
                DateTimeOffset.UnixEpoch,
                [],
                new Dictionary<string, string>()),
            request.FoldedStateChanges,
            [],
            new Dictionary<string, string>())
        {
            ExpectedFence = request.TargetAuthorityFence
        };

    private sealed record PreparedFoldValidation(IReadOnlyList<RuntimeCheckpointPreparedCommit?>? PreparedCommits)
    {
        public static PreparedFoldValidation Invalid { get; } = new((IReadOnlyList<RuntimeCheckpointPreparedCommit?>?)null);
        public bool Succeeded => PreparedCommits is not null;
    }

    private static RuntimeCheckpointPreparedFoldResult PreparedFoldConflict() =>
        new(RuntimeCheckpointCommitStoreStatus.Conflict, new Dictionary<string, RuntimeCheckpointCommitStoreResult>(StringComparer.Ordinal));

    private static RuntimeCheckpointPreparedAdoptionReceipt AdoptionConflict(RuntimeCheckpointPreparedAdoptionRequest request) =>
        new(
            RuntimeCheckpointPreparedAdoptionStatus.Conflict,
            request.WorkflowExecutionId,
            request.Route,
            request.ThroughWorkflowCheckpointOrder,
            request.TargetAuthorityFence,
            request.Members?.Select(member => member?.CommitId ?? string.Empty).ToArray() ?? []);

    private static RuntimeCheckpointPreparedAdoptionReceipt AdoptionReplay(RuntimeCheckpointPreparedAdoptionRequest request) =>
        new(
            RuntimeCheckpointPreparedAdoptionStatus.Replay,
            request.WorkflowExecutionId,
            request.Route,
            request.ThroughWorkflowCheckpointOrder,
            request.TargetAuthorityFence,
            request.Members.Select(member => member.CommitId).ToArray());

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
        RuntimeCheckpointPreparationToken token,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        token.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var plan = new InMemoryCheckpointMutationPlan(commit, commit.StateChanges.PostCommitOutbox.Select(x => x.State).ToArray());
        await using var transaction = await _transactionCoordinator.BeginAsync(plan, BuildTransactionRoots(plan), cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            bool terminalReplay;
            lock (_state.SyncRoot)
                terminalReplay = _state.LogicalCheckpointLedger.TryGetValue(token.CommitId, out var existing) &&
                                 existing.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed;
            if (terminalReplay)
                return await CommitPreparedCoreAsync(token, commit, decision, cancellationToken);

            var result = await CommitPreparedCoreAsync(token, commit, decision, cancellationToken);
            transaction.Commit();
            return result;
        }
        catch (RuntimeStaleFencingTokenException)
        {
            transaction.Commit();
            return OwnershipLost();
        }
        catch (Exception exception)
        {
            throw transaction.Rollback(exception);
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedCoreAsync(
        RuntimeCheckpointPreparationToken token,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken)
    {
        RuntimeLogicalCheckpointLedgerEntry entry;
        lock (_state.SyncRoot)
        {
            if (!_state.LogicalCheckpointLedger.TryGetValue(token.CommitId, out entry!) || !TokenMatches(entry, token))
                return Conflict();
            var preparedInput = DecodePreparation(entry);
            if (!StringComparer.Ordinal.Equals(commit.CommitId, entry.CommitId) ||
                !StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, preparedInput.Commit.WorkflowExecutionId) ||
                !token.Provenance.Equals(commit.Checkpoint.Provenance))
                return Conflict();

            var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
            if (entry.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed)
            {
                if (entry.ReconciledLegacyMarker == false && !StringComparer.Ordinal.Equals(entry.CommitFingerprint, fingerprint))
                    return Conflict();
                if (entry.ReconciledLegacyMarker == true && !RawCommitMatchesEntry(entry, commit))
                    return Conflict();
                return (entry.Receipt ?? new RuntimeCheckpointCommitStoreResult([])) with
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Replay,
                    CommitFingerprint = entry.CommitFingerprint
                };
            }

            if (entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared || !ProviderRevisionsMatch(entry, token))
                return Conflict();
            if (!Equals(entry.ExpectedFence, commit.ExpectedFence))
                return Conflict();
            if (_state.Commits.TryGetValue(commit.CommitId, out var existing))
            {
                if (!StringComparer.Ordinal.Equals(RuntimeCheckpointCommitFingerprint.Compute(existing.Commit), fingerprint))
                    return Conflict();
                var replay = Replay(existing, fingerprint);
                FinalizeLedger(entry, replay with { Status = RuntimeCheckpointCommitStoreStatus.Committed }, RuntimeLogicalCheckpointLedgerStatus.Committed, fingerprint, commitContext: true);
                return replay;
            }
        }

        await EnsureExpectedFenceAsync(commit, cancellationToken);
        var commitFingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
        if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
        {
            lock (_state.SyncRoot)
            {
                var skipped = new RuntimeCheckpointCommitStoreResult([])
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Skipped,
                    CommitFingerprint = commitFingerprint
                };
                FinalizeLedger(entry, skipped, RuntimeLogicalCheckpointLedgerStatus.Skipped, commitFingerprint, commitContext: false);
                return skipped;
            }
        }

        RuntimeCheckpointCommitStoreResult? committed = null;
        await CommitNewAsync(
            commit,
            decision,
            cancellationToken,
            validateFence: false,
            finalize: result =>
            {
                committed = result with
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Committed,
                    CommitFingerprint = commitFingerprint
                };
                FinalizeLedger(entry, committed, RuntimeLogicalCheckpointLedgerStatus.Committed, commitFingerprint, commitContext: true);
            });
        return committed ?? throw new InvalidOperationException($"Prepared runtime checkpoint '{commit.CommitId}' completed without a receipt.");
    }

    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IExecutionLivenessStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager,
        InMemoryRuntimeCheckpointStoreState? state,
        TimeProvider? timeProvider,
        IActivityScopeCleanupStore? activityScopeCleanupStore,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter)
        : this(
            workflowExecutionStateStore,
            activityExecutionStateStore,
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            schedulerStateStore,
            activityExecutionInspectionWriter,
            rootWriteLeaseManager,
            state,
            timeProvider,
            activityScopeCleanupStore,
            activityExecutionHierarchyWriter,
            workflowDispatchStore: null)
    {
    }

    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IExecutionLivenessStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter,
        IActivityScopeCleanupStore? activityScopeCleanupStore,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter)
        : this(
            workflowExecutionStateStore,
            activityExecutionStateStore,
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            schedulerStateStore,
            activityExecutionInspectionWriter,
            rootWriteLeaseManager: null,
            state: null,
            timeProvider: null,
            activityScopeCleanupStore: activityScopeCleanupStore,
            activityExecutionHierarchyWriter: activityExecutionHierarchyWriter)
    {
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = new InMemoryCheckpointMutationPlan(commit, commit.StateChanges.PostCommitOutbox.Select(x => x.State).ToArray());
        await using var transaction = await _transactionCoordinator.BeginAsync(plan, BuildTransactionRoots(plan), cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
            lock (_state.SyncRoot)
            {
                if (_state.Commits.TryGetValue(commit.CommitId, out var existing))
                {
                    if (!StringComparer.Ordinal.Equals(
                            RuntimeCheckpointCommitFingerprint.Compute(existing.Commit),
                            fingerprint))
                    {
                        throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
                    }
                    return new RuntimeCheckpointCommitStoreResult(existing.PendingPostCommitWorkIds)
                    {
                        ConsumedSchedulerWorkItemIds = existing.ConsumedSchedulerWorkItemIds,
                        Status = RuntimeCheckpointCommitStoreStatus.Replay,
                        CommitFingerprint = fingerprint
                    };
                }
            }

            var result = await CommitNewAsync(commit, decision, cancellationToken);
            transaction.Commit();
            return result;
        }
        catch (Exception exception)
        {
            throw transaction.Rollback(exception);
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitNewAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken,
        bool validateFence = true,
        Action<RuntimeCheckpointCommitStoreResult>? finalize = null,
        bool writeCommitRecord = true)
    {
        if (validateFence)
            await EnsureExpectedFenceAsync(commit, cancellationToken);

        var pendingOutboxItems = commit.StateChanges.PostCommitOutbox.Select(change => change.State).ToArray();
        var consumedSchedulerWorkItemIds = commit.StateChanges.ConsumedSchedulerWorkItems
            .Select(item => item.WorkItemId)
            .ToArray();
        var result = new RuntimeCheckpointCommitStoreResult(pendingOutboxItems.Select(item => item.OutboxItemId).ToArray())
        {
            ConsumedSchedulerWorkItemIds = consumedSchedulerWorkItemIds
        };
        ValidatePendingOutboxItems(pendingOutboxItems);
        ValidateConsumedSchedulerWorkItems(commit);
        ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
        await ValidateWorkflowTestScopesAsync(commit, cancellationToken);
        ValidateSchedulerStateChange(commit);
        ValidateActivityExecutionStateChanges(commit);
        ValidateActivityExecutionInspectionChanges(commit);
        ValidateBookmarkStateChanges(commit);
        ValidateDurableValueStateChanges(commit);
        await ValidateIncidentStateChangesAsync(commit, cancellationToken);
        ValidateOperationalStateChanges(commit);
        await ValidateWorkflowDispatchChangesAsync(commit, cancellationToken);
        await ValidateWorkflowDispatchCancellationsAsync(commit, cancellationToken);
        await ValidateAlterationJobTerminalChangeAsync(commit, cancellationToken);
        await ExecuteWithWorkflowExecutionRootWriteLeaseAsync(commit, async ct =>
        {
            await ApplyConsumedSchedulerWorkItemsAsync(commit.StateChanges.ConsumedSchedulerWorkItems, ct);
            await ApplyWorkflowExecutionStateChangeAsync(commit.StateChanges.WorkflowExecution, ct);
            await ApplySchedulerStateChangeAsync(commit.StateChanges.Scheduler, ct);
            await ApplyActivityExecutionStateChangesAsync(commit.StateChanges.ActivityExecutions, ct);
            await ApplyActivityExecutionInspectionChangesAsync(commit.StateChanges.ActivityExecutionInspections, ct);
            await ApplyActivityExecutionHierarchyChangesAsync(commit.StateChanges.ActivityExecutionInspections, ct);
            await ApplyBookmarkStateChangesAsync(commit.StateChanges.Bookmarks, ct);
            await ApplyDurableValueStateChangesAsync(commit.StateChanges.DurableValues, ct);
            await ApplyIncidentStateChangesAsync(commit.StateChanges.Incidents, ct);
            await ApplyOperationalStateChangesAsync(commit.StateChanges.Operational, ct);
            await ApplyActivityScopeCleanupsAsync(commit.StateChanges.ActivityScopeCleanups, ct);
            await ApplyWorkflowDispatchChangesAsync(commit.StateChanges.WorkflowDispatches, ct);
            await ApplyWorkflowDispatchCancellationsAsync(commit.StateChanges.WorkflowDispatchCancellations, ct);
            await ApplyAlterationJobTerminalChangeAsync(commit, ct);

            try
            {
                lock (_state.SyncRoot)
                {
                    foreach (var item in pendingOutboxItems)
                        SavePendingOutboxItem(item);

                    if (writeCommitRecord)
                    {
                        _state.Commits.Add(commit.CommitId, new RuntimeCheckpointCommitRecord(
                            commit,
                            decision,
                            pendingOutboxItems.Select(item => item.OutboxItemId).ToArray(),
                            consumedSchedulerWorkItemIds));
                    }
                    finalize?.Invoke(result);
                }
            }
            catch (Exception exception) when (writeCommitRecord && exception is not RuntimeSchedulerWorkConsumeConflictException)
            {
                throw new RuntimeCheckpointInconsistentDurabilityException(commit.CommitId, exception);
            }
        }, cancellationToken);

        return result;
    }

    private IReadOnlyCollection<InMemoryCheckpointTransactionRoot> BuildTransactionRoots(InMemoryCheckpointMutationPlan plan)
    {
        var roots = new List<InMemoryCheckpointTransactionRoot>
        {
            new("checkpoint-state", _state)
        };
        Add(plan.MutatesWorkflowExecution, "workflow-execution-state", _workflowExecutionStateStore);
        Add(plan.MutatesScheduler, "scheduler-state", _schedulerStateStore);
        Add(plan.ActivityExecutionStateIds.Count > 0, "activity-execution-state", _activityExecutionStateStore);
        Add(plan.ActivityInspectionIds.Count > 0, "activity-execution-inspection", _activityExecutionInspectionWriter);
        Add(plan.ActivityHierarchyIds.Count > 0, "activity-execution-hierarchy", _activityExecutionHierarchyWriter);
        Add(plan.BookmarkIds.Count > 0, "bookmark-state", _bookmarkStateStore);
        Add(plan.DurableValueIds.Count > 0, "durable-value-state", _durableValueStateStore);
        Add(plan.IncidentIds.Count > 0, "incident-state", _incidentStateStore);
        Add(plan.OperationalStateIds.Count > 0 || plan.HasExpectedFence, "execution-liveness-state", _operationalStateStore);
        Add(plan.HasActivityScopeCleanup, "activity-scope-cleanup", _activityScopeCleanupStore);
        Add(plan.SchedulerWorkItemIds.Count > 0, "scheduler-work-queue", _schedulerWorkQueue);
        Add(plan.WorkflowDispatchIds.Count > 0, "workflow-dispatch", _workflowDispatchStore);
        Add(plan.AlterationJobId is not null, "workflow-alteration", _alterationStore);
        return roots;

        void Add(bool affected, string name, object? component)
        {
            if (affected)
                roots.Add(new(name, component));
        }
    }

    private static InMemoryCheckpointMutationPlan CreateLedgerMutationPlan(
        string workflowExecutionId,
        IEnumerable<string> commitIds,
        RuntimeExecutionFence? expectedFence = null)
    {
        var commit = CreateLedgerMutationCommit(workflowExecutionId, expectedFence);
        return new InMemoryCheckpointMutationPlan(commit, commitIds: commitIds.ToArray());
    }

    private static RuntimeCheckpointCommit CreateLedgerMutationCommit(
        string workflowExecutionId,
        RuntimeExecutionFence? expectedFence)
    {
        var commit = new RuntimeCheckpointCommit(
            $"prepared-ledger:{workflowExecutionId}",
            new RuntimeCheckpoint(
                $"prepared-ledger:{workflowExecutionId}",
                "PreparedLedger",
                workflowExecutionId,
                DateTimeOffset.UnixEpoch,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>())
        {
            ExpectedFence = expectedFence
        };
        return commit;
    }

    /// <summary>Test seam that throws once after reserving an order but before adding the prepared ledger entry.</summary>
    public void InjectPreparationFailureForTesting(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.CompareExchange(ref _nextPreparationFailure, exception, null) is not null)
            throw new InvalidOperationException("A preparation failure is already pending.");
    }

    private void ThrowInjectedPreparationFailure()
    {
        var exception = Interlocked.Exchange(ref _nextPreparationFailure, null);
        if (exception is not null)
            throw exception;
    }

    private async ValueTask ApplyConsumedSchedulerWorkItemsAsync(
        IReadOnlyCollection<ConsumedSchedulerWorkItem> consumed,
        CancellationToken cancellationToken)
    {
        if (consumed.Count == 0)
            return;
        if (_schedulerWorkQueue is null)
            throw new InvalidOperationException("The checkpoint contains consumed scheduler work items but no scheduler work queue is configured.");

        foreach (var item in consumed)
        {
            var result = await _schedulerWorkQueue.ConsumeClaimedAsync(item, cancellationToken);
            if (!result.Succeeded)
                throw new RuntimeSchedulerWorkConsumeConflictException(item.WorkflowExecutionId, item.WorkItemId);
        }
    }

    private async ValueTask ValidateAlterationJobTerminalChangeAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (commit.StateChanges.AlterationJobTerminalChange is not { } change)
            return;
        if (_alterationStore is null)
            throw new InvalidOperationException("A checkpoint carrying alteration-job terminal evidence requires an alteration store.");
        await _alterationStore.ValidateTerminalJobChangeAsync(change, cancellationToken);
    }

    private async ValueTask ApplyAlterationJobTerminalChangeAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (commit.StateChanges.AlterationJobTerminalChange is not { } change)
            return;
        await _alterationStore!.ApplyTerminalJobChangeAsync(change, cancellationToken);
    }

    private void ValidateConsumedSchedulerWorkItems(RuntimeCheckpointCommit commit)
    {
        foreach (var item in commit.StateChanges.ConsumedSchedulerWorkItems)
        {
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, item.WorkflowExecutionId))
                throw new InvalidOperationException("Consumed scheduler work item WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private async ValueTask EnsureExpectedFenceAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ExpectedFence is not { } expectedFence)
            return;
        if (_operationalStateStore is null)
        {
            throw new InvalidOperationException(
                "A checkpoint carrying an execution fence requires an execution liveness-state store.");
        }

        var state = await _operationalStateStore.FindAsync(
            commit.WorkflowExecutionId,
            InMemoryExecutionLivenessStateStore.GetOwnershipStateId(commit.WorkflowExecutionId),
            cancellationToken);
        var currentToken = ReadHighestIssuedToken(state);
        var current = state?.ExecutionLease;
        var reason = current is null
            ? RuntimeFencingRejectionReason.NoActiveLease
            : current.IsExpired(_timeProvider.GetUtcNow())
                ? RuntimeFencingRejectionReason.ExpiredLease
                : RuntimeFencingRejectionReason.StaleToken;
        if (current is null ||
            current.IsExpired(_timeProvider.GetUtcNow()) ||
            !StringComparer.Ordinal.Equals(current.LeaseId, expectedFence.LeaseId) ||
            !StringComparer.Ordinal.Equals(current.OwnerId, expectedFence.OwnerId) ||
            current.FencingToken != expectedFence.FencingToken)
        {
            throw new RuntimeStaleFencingTokenException(
                commit.WorkflowExecutionId,
                expectedFence.FencingToken,
                currentToken,
                reason);
        }
    }

    private static long ReadHighestIssuedToken(ExecutionLivenessState? state)
    {
        if (state is null)
            return 0;
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
        {
            return token;
        }

        return state.ExecutionLease?.FencingToken ?? 0;
    }

    private async ValueTask ExecuteWithWorkflowExecutionRootWriteLeaseAsync(
        RuntimeCheckpointCommit commit,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutionStateStore is null || commit.StateChanges.WorkflowExecution is not { } workflowExecutionChange)
        {
            await write(cancellationToken);
            return;
        }

        if (_rootWriteLeaseManager is null)
            throw new InvalidOperationException("A workflow executable root-write lease manager is required before workflow execution state can be persisted.");

        await _rootWriteLeaseManager.ExecuteAsync(
            workflowExecutionChange.State.PinnedExecutable,
            $"checkpoint:{commit.CommitId}",
            write,
            cancellationToken);
    }

    public IReadOnlyCollection<RuntimeCheckpointCommitRecord> ListCommits()
    {
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            return _state.Commits.Values.ToArray();
        }
    }

    public IReadOnlyCollection<RuntimeLogicalCheckpointLedgerEntry> ListLogicalCheckpointLedgerEntries()
    {
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
            return _state.LogicalCheckpointLedger.Values.ToArray();
    }

    public (long Reserved, long Committed, long Revision) GetCheckpointOrderHighWatermarks(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var current = ReadCheckpointOrderState(workflowExecutionId);
            return (current.Reserved, current.Committed, current.Revision);
        }
    }

    /// <summary>Test seam exposing the provider-owned generic context snapshot and revision.</summary>
    public (RuntimeExecutionContextSnapshot Snapshot, long Revision) GetExecutionContextForTesting(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var current = _state.ExecutionContexts.GetValueOrDefault(workflowExecutionId) ?? InMemoryRuntimeExecutionContextState.Empty;
            return (current.Snapshot, current.Revision);
        }
    }

    /// <summary>Test observation of successful provider-atomic fold transactions; unlike commit markers, this counts physical folds.</summary>

    private (long Order, long ExpectedRevision) ReserveCheckpointOrder(string workflowExecutionId)
    {
        lock (_state.SyncRoot)
        {
            var current = ReadCheckpointOrderState(workflowExecutionId);
            var order = checked(current.Reserved + 1);
            var next = new InMemoryRuntimeCheckpointOrderState(order, current.Committed, checked(current.Revision + 1));
            _state.CheckpointOrders[workflowExecutionId] = next;
            return (order, next.Revision);
        }
    }

    private InMemoryRuntimeCheckpointOrderState ReadCheckpointOrderState(string workflowExecutionId) =>
        _state.CheckpointOrders.GetValueOrDefault(workflowExecutionId) ?? InMemoryRuntimeCheckpointOrderState.Empty;

    private InMemoryRuntimeExecutionContextState ReadExecutionContextState(string workflowExecutionId)
    {
        lock (_state.SyncRoot)
            return _state.ExecutionContexts.GetValueOrDefault(workflowExecutionId) ?? InMemoryRuntimeExecutionContextState.Empty;
    }

    private RuntimeCheckpointPreparationResult RefreshPreparedReservation(
        RuntimeLogicalCheckpointLedgerEntry entry,
        RuntimeCheckpointCommit commit)
    {
        var preparedInput = DecodePreparation(entry);
        var workflowExecutionId = preparedInput.Commit.WorkflowExecutionId;
        var order = ReadCheckpointOrderState(workflowExecutionId);
        if (order.Reserved < entry.Provenance.WorkflowCheckpointOrder ||
            order.Committed >= entry.Provenance.WorkflowCheckpointOrder)
            return RuntimeCheckpointPreparationResult.Conflict();

        var context = _state.ExecutionContexts.GetValueOrDefault(workflowExecutionId) ?? InMemoryRuntimeExecutionContextState.Empty;
        if (context.Revision != entry.ExpectedContextRevision &&
            !context.Snapshot.Equals(entry.Provenance.ExecutionContext))
            return RuntimeCheckpointPreparationResult.Conflict();

        var refreshed = entry with
        {
            LedgerToken = Guid.NewGuid().ToString("N"),
            ExpectedOrderRevision = order.Revision,
            ExpectedContextRevision = context.Revision,
            ExpectedFence = commit.ExpectedFence
        };
        _state.LogicalCheckpointLedger[entry.CommitId] = refreshed;
        return RuntimeCheckpointPreparationResult.Replay(ToToken(refreshed), refreshed.Receipt);
    }

    private RuntimeCheckpointPreparationResult ReconcileLegacyMarker(
        RuntimeCheckpointPrepareRequest request,
        string inputFingerprint,
        RuntimeCheckpointCommitRecord marker)
    {
        if (!StringComparer.Ordinal.Equals(RuntimeCheckpointCommitFingerprint.ComputeInput(marker.Commit), RuntimeCheckpointCommitFingerprint.ComputeInput(request.Commit)) ||
            !StringComparer.Ordinal.Equals(request.SourceIdentity, marker.Commit.Checkpoint.Name) ||
            !StringComparer.Ordinal.Equals(request.OperationIdentity, marker.Commit.Checkpoint.CheckpointId) ||
            !request.RequestedExecutionContext.IsEmpty)
            return RuntimeCheckpointPreparationResult.Conflict();

        var (order, expectedOrderRevision) = ReserveCheckpointOrder(request.Commit.WorkflowExecutionId);
        var provenance = new RuntimeCheckpointProvenance(request.RequestedExecutionContext, order);
        var entry = new RuntimeLogicalCheckpointLedgerEntry(
            request.Commit.CommitId,
            Guid.NewGuid().ToString("N"),
            provenance,
            request.SourceIdentity,
            request.OperationIdentity,
            request.Commit.Checkpoint with { Provenance = null },
            request.Commit.StateChanges,
            request.Commit.PostCommitIntents,
            request.Commit.Metadata,
            request.RequestedExecutionContext,
            expectedOrderRevision,
            ReadExecutionContextState(request.Commit.WorkflowExecutionId).Revision,
            request.Commit.ExpectedFence,
            inputFingerprint,
            _preparationPayloadCodec.Encode(request).CanonicalPayload,
            $"runtime-checkpoint:{request.Commit.WorkflowExecutionId}:{request.Commit.CommitId}",
            marker.Decision.Mode,
            RuntimeLogicalCheckpointLedgerStatus.Prepared,
            ReconciledLegacyMarker: true,
            WorkflowExecutionId: request.Commit.WorkflowExecutionId);
        _state.LogicalCheckpointLedger.Add(entry.CommitId, entry);
        var legacyFingerprint = RuntimeCheckpointCommitFingerprint.Compute(marker.Commit);
        var receipt = new RuntimeCheckpointCommitStoreResult(marker.PendingPostCommitWorkIds)
        {
            ConsumedSchedulerWorkItemIds = marker.ConsumedSchedulerWorkItemIds,
            Status = RuntimeCheckpointCommitStoreStatus.Committed,
            CommitFingerprint = legacyFingerprint
        };
        FinalizeLedger(entry, receipt, RuntimeLogicalCheckpointLedgerStatus.Committed, legacyFingerprint, commitContext: false);
        return RuntimeCheckpointPreparationResult.Replay(ToToken(_state.LogicalCheckpointLedger[entry.CommitId]), receipt);
    }

    private bool ProviderRevisionsMatch(RuntimeLogicalCheckpointLedgerEntry entry, RuntimeCheckpointPreparationToken token)
    {
        var preparedInput = DecodePreparation(entry);
        var workflowExecutionId = preparedInput.Commit.WorkflowExecutionId;
        var order = ReadCheckpointOrderState(workflowExecutionId);
        var context = _state.ExecutionContexts.GetValueOrDefault(workflowExecutionId) ?? InMemoryRuntimeExecutionContextState.Empty;
        return order.Revision == token.ExpectedOrderRevision &&
               order.Reserved >= token.Provenance.WorkflowCheckpointOrder &&
               order.Committed < token.Provenance.WorkflowCheckpointOrder &&
               context.Revision == token.ExpectedContextRevision;
    }

    private static bool TokenMatches(RuntimeLogicalCheckpointLedgerEntry entry, RuntimeCheckpointPreparationToken token)
    {
        var identity = entry.TerminalPreparationToken;
        return identity is not null
            ? identity.Equals(token)
            : StringComparer.Ordinal.Equals(entry.CommitId, token.CommitId) &&
              StringComparer.Ordinal.Equals(entry.LedgerToken, token.LedgerToken) &&
              entry.Provenance.Equals(token.Provenance) &&
              entry.ExpectedOrderRevision == token.ExpectedOrderRevision &&
              entry.ExpectedContextRevision == token.ExpectedContextRevision &&
              Equals(entry.ExpectedFence, token.ExpectedFence) &&
              StringComparer.Ordinal.Equals(entry.InputFingerprint, token.CanonicalInputFingerprint) &&
              StringComparer.Ordinal.Equals(entry.CanonicalInputReference, token.CanonicalInputReference) &&
              entry.InitialPersistenceMode == token.InitialPersistenceMode &&
              Equals(entry.RecoveryAuthority, token.RecoveryAuthority);
    }

    private bool RawCommitMatchesEntry(RuntimeLogicalCheckpointLedgerEntry entry, RuntimeCheckpointCommit commit)
    {
        var raw = DecodePreparation(entry).Commit;
        return StringComparer.Ordinal.Equals(
            RuntimeCheckpointCommitFingerprint.ComputeInput(raw),
            RuntimeCheckpointCommitFingerprint.ComputeInput(commit));
    }

    private RuntimeCheckpointPrepareRequest DecodePreparation(RuntimeLogicalCheckpointLedgerEntry entry) =>
        _preparationPayloadCodec.Decode(new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            entry.CanonicalInputPayload ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no canonical recovery input."),
            entry.InputFingerprint));

    private void FinalizeLedger(
        RuntimeLogicalCheckpointLedgerEntry entry,
        RuntimeCheckpointCommitStoreResult receipt,
        RuntimeLogicalCheckpointLedgerStatus status,
        string fingerprint,
        bool commitContext)
    {
        var preparedInput = DecodePreparation(entry);
        var workflowExecutionId = preparedInput.Commit.WorkflowExecutionId;
        var order = ReadCheckpointOrderState(workflowExecutionId);
        if (order.Revision != entry.ExpectedOrderRevision ||
            order.Reserved < entry.Provenance.WorkflowCheckpointOrder ||
            order.Committed >= entry.Provenance.WorkflowCheckpointOrder)
            throw new InvalidOperationException($"Runtime checkpoint order revision changed while finalizing '{entry.CommitId}'.");
        var context = _state.ExecutionContexts.GetValueOrDefault(workflowExecutionId) ?? InMemoryRuntimeExecutionContextState.Empty;
        if (commitContext && context.Revision != entry.ExpectedContextRevision)
            throw new InvalidOperationException($"Runtime execution-context revision changed while finalizing '{entry.CommitId}'.");

        _state.CheckpointOrders[workflowExecutionId] = new(
            order.Reserved,
            status == RuntimeLogicalCheckpointLedgerStatus.Committed
                ? Math.Max(order.Committed, entry.Provenance.WorkflowCheckpointOrder)
                : order.Committed,
            checked(order.Revision + 1));
        if (commitContext)
        {
            if (!preparedInput.RequestedExecutionContext.IsEmpty && !context.Snapshot.Equals(entry.Provenance.ExecutionContext))
                _state.ExecutionContexts[workflowExecutionId] = new(entry.Provenance.ExecutionContext!, checked(context.Revision + 1));
        }

        _state.LogicalCheckpointLedger[entry.CommitId] = entry with
        {
            Status = status,
            CommitFingerprint = fingerprint,
            Receipt = receipt
        };
    }

    private static RuntimeCheckpointCommitStoreResult Conflict() =>
        new([]) { Status = RuntimeCheckpointCommitStoreStatus.Conflict };

    private static RuntimeCheckpointCommitStoreResult OwnershipLost() =>
        new([]) { Status = RuntimeCheckpointCommitStoreStatus.OwnershipLost };

    private static RuntimeCheckpointCommitStoreResult Replay(RuntimeCheckpointCommitRecord existing, string fingerprint) =>
        new(existing.PendingPostCommitWorkIds)
        {
            ConsumedSchedulerWorkItemIds = existing.ConsumedSchedulerWorkItemIds,
            Status = RuntimeCheckpointCommitStoreStatus.Replay,
            CommitFingerprint = fingerprint
        };

    private static RuntimeCheckpointPreparationToken ToToken(RuntimeLogicalCheckpointLedgerEntry entry) =>
        entry.TerminalPreparationToken ?? new RuntimeCheckpointPreparationToken(
            entry.CommitId,
            entry.LedgerToken,
            entry.Provenance,
            entry.ExpectedOrderRevision ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no order authority."),
            entry.ExpectedContextRevision ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no context authority."),
            entry.ExpectedFence,
            entry.InputFingerprint,
            entry.CanonicalInputReference ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no canonical input reference."),
            entry.InitialPersistenceMode ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no candidate persistence mode."),
            entry.RecoveryAuthority).Validate();

    /// <summary>Test seam: seeds a pending outbox item directly, bypassing the commit path (§2.23.3).</summary>
    public ValueTask AddPendingForTestingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            SavePendingOutboxItem(item);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.OwnerId is not null)
            throw new NotSupportedException("The in-memory post-commit outbox store does not implement delivery ownership filtering.");

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var items = _state.OutboxItems.Values
                .Where(item => IsDeliverable(item, query))
                .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(items);
        }
    }

    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            return new ValueTask<RuntimePostCommitOutboxItem?>(
                _state.OutboxItems.TryGetValue(outboxItemId, out var item) ? item : null);
        }
    }

    public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(result.OutboxItemId, out var existing))
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' was not found.");

            if (existing.IsTerminal)
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");
            if (existing.Status == RuntimePostCommitOutboxStatus.Delivering)
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is claimed; its owner and fencing token are required.");

            var deliveryAttemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(existing.DeliveryAttemptCount);
            var status = NormalizeDeliveryStatus(existing, result.Status, deliveryAttemptCount);
            DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
                ? NextRetryAvailableAt(existing, result.RecordedAt)
                : null;

            _state.OutboxItems[result.OutboxItemId] = new RuntimePostCommitOutboxItem(
                outboxItemId: existing.OutboxItemId,
                intent: existing.Intent,
                status: status,
                recordedAt: existing.RecordedAt,
                availableAt: availableAt,
                retryPolicy: existing.RetryPolicy,
                deliveryAttemptCount: deliveryAttemptCount,
                deliveringOwnerId: null,
                deliveryStartedAt: null,
                deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
                lastFailureMessage: result.FailureMessage,
                metadata: existing.Metadata,
                deliveryFencingToken: existing.DeliveryFencingToken);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var claimable = _state.OutboxItems.Values
                .Where(item => RuntimePostCommitOutboxClaimTransitions.CanClaim(item, request))
                .OrderBy(RuntimePostCommitOutboxClaimTransitions.ClaimableAt)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                .Take(request.Limit)
                .ToArray();
            var claims = new RuntimePostCommitOutboxClaim[claimable.Length];
            for (var index = 0; index < claimable.Length; index++)
            {
                var claim = RuntimePostCommitOutboxClaimTransitions.Claim(claimable[index], request);
                _state.OutboxItems[claim.OutboxItemId] = claim.Item;
                claims[index] = claim;
            }

            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>(claims);
        }
    }

    public ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(claim.OutboxItemId, out var existing))
                throw new InvalidOperationException($"Post-commit outbox item '{claim.OutboxItemId}' was not found.");

            _state.OutboxItems[claim.OutboxItemId] = RuntimePostCommitOutboxClaimTransitions.Complete(existing, claim, result);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        var childExecution = completion.WorkflowDispatch is { } projectedDispatch &&
                             _workflowExecutionStateStore is not null
            ? await _workflowExecutionStateStore.FindAsync(
                projectedDispatch.ChildWorkflowExecutionId,
                cancellationToken)
            : null;

        await using var checkpointGate = await _state.TransactionGate.EnterAsync(cancellationToken);
        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            lock (_state.SyncRoot)
            {
                if (!_state.OutboxItems.TryGetValue(completion.Claim.OutboxItemId, out var existingOutbox))
                    throw new InvalidOperationException($"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");

                // Validate the claim/fence before considering lifecycle precedence. A stale claimant cannot turn a
                // newer generation into an acknowledgement merely because the deterministic child is now visible.
                var completedOutbox = RuntimePostCommitOutboxClaimTransitions.Complete(
                    existingOutbox,
                    completion.Claim,
                    completion.DeliveryResult);
                WorkflowDispatchRecord? winningDispatch = null;
                var admissionWins = false;
                if (completion.WorkflowDispatch is { } dispatch)
                {
                    if (!_state.WorkflowDispatches.TryGetValue(dispatch.DispatchId, out var existingDispatch))
                        throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found in the atomic checkpoint store.");

                    winningDispatch = WorkflowDispatchLifecycle.ResolveSuccessfulChildDelivery(
                        existingDispatch,
                        childExecution,
                        completion.DeliveryResult.RecordedAt);
                    admissionWins = winningDispatch is not null;
                    if (admissionWins)
                    {
                        completedOutbox = RuntimePostCommitOutboxClaimTransitions.Complete(
                            existingOutbox,
                            completion.Claim,
                            new RuntimePostCommitOutboxDeliveryResult(
                                completion.Claim.OutboxItemId,
                                RuntimePostCommitOutboxStatus.Delivered,
                                completion.DeliveryResult.RecordedAt));
                    }
                    else
                    {
                        WorkflowDispatchLifecycle.ValidateTransition(existingDispatch, dispatch);
                        winningDispatch = dispatch;
                    }
                }

                if (!admissionWins && completion.FollowUpOutboxItem is { } followUp)
                {
                    if (StringComparer.Ordinal.Equals(followUp.OutboxItemId, completion.Claim.OutboxItemId))
                        throw new InvalidOperationException("A post-commit follow-up cannot replace the claimed outbox item.");
                    if (_state.OutboxItems.TryGetValue(followUp.OutboxItemId, out var existingFollowUp) &&
                        !PendingOutboxItemsEquivalent(existingFollowUp, followUp))
                    {
                        throw new InvalidOperationException($"Post-commit follow-up item '{followUp.OutboxItemId}' already exists with conflicting state.");
                    }
                }

                _state.OutboxItems[completion.Claim.OutboxItemId] = completedOutbox;
                if (winningDispatch is not null)
                    _state.WorkflowDispatches[winningDispatch.DispatchId] = winningDispatch;
                if (!admissionWins && completion.FollowUpOutboxItem is { } followUpOutboxItem)
                    _state.OutboxItems.TryAdd(followUpOutboxItem.OutboxItemId, followUpOutboxItem);
            }
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    internal ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default)
        => RedriveAsync(request, accessContext: null, cancellationToken);

    internal ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        PersistenceAccessContext? accessContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointGate = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            _state.WorkflowDispatches.TryGetValue(request.DispatchId, out var dispatch);
            if (dispatch is not null)
                accessContext?.EnsureTenantScope(dispatch.TenantId);
            RuntimePostCommitOutboxItem? deadLetter = null;
            var deadLetterId = dispatch is null ? null : WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch);
            if (deadLetterId is not null)
                _state.OutboxItems.TryGetValue(deadLetterId, out deadLetter);

            var transition = WorkflowDispatchRedriveTransitions.Evaluate(request, dispatch, deadLetter);
            if (transition.HasMutation)
            {
                _state.WorkflowDispatches[request.DispatchId] = transition.WorkflowDispatch!;
                _state.OutboxItems[transition.OutboxItem!.OutboxItemId] = transition.OutboxItem;
            }
            return ValueTask.FromResult(transition.Result);
        }
    }

    private async ValueTask ApplyWorkflowExecutionStateChangeAsync(
        RuntimeStateChange<WorkflowExecutionState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        await _workflowExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    /// <summary>
    /// Validates every pending outbox item in the commit — status, conflicts against the store, and
    /// conflicts <b>within the commit itself</b> — before any state is projected or mutated (#386).
    /// This runs outside the inconsistent-durability guard so a data-conflict validation failure
    /// surfaces as a plain <see cref="InvalidOperationException"/> instead of being misclassified as
    /// a <see cref="RuntimeCheckpointInconsistentDurabilityException"/>: with all validation front-
    /// loaded here (and commits serialized by the write gate), nothing validation-shaped can throw
    /// inside the guarded mutation block.
    /// </summary>
    private void ValidatePendingOutboxItems(IReadOnlyCollection<RuntimePostCommitOutboxItem> items)
    {
        lock (_state.SyncRoot)
        {
            var seen = new Dictionary<string, RuntimePostCommitOutboxItem>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                if (item.Status != RuntimePostCommitOutboxStatus.Pending)
                    throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

                if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing) && !IsSamePendingIntent(existing, item))
                    throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");

                if (seen.TryGetValue(item.OutboxItemId, out var duplicate) && !IsSamePendingIntent(duplicate, item))
                    throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");

                seen[item.OutboxItemId] = item;
            }
        }
    }

    private void SavePendingOutboxItem(RuntimePostCommitOutboxItem item)
    {
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing))
        {
            if (IsSamePendingIntent(existing, item))
                return;

            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        _state.OutboxItems.Add(item.OutboxItemId, item);
    }

    private async ValueTask ApplySchedulerStateChangeAsync(
        RuntimeStateChange<SchedulerState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (_schedulerStateStore is null || stateChange is null)
            return;

        await _schedulerStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyActivityExecutionStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
            await _activityExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyActivityExecutionInspectionChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionInspectionWriter is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _activityExecutionInspectionWriter.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected activity execution inspection state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyActivityExecutionHierarchyChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionHierarchyWriter is null)
            return;
        foreach (var stateChange in stateChanges)
        {
            var projection = stateChange.State;
            if (!string.IsNullOrWhiteSpace(projection.ExecutionScopeId ?? projection.Provenance.ExecutionScopeId))
                await _activityExecutionHierarchyWriter.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection), cancellationToken);
        }
    }

    private async ValueTask ApplyBookmarkStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_bookmarkStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _bookmarkStateStore.DeleteAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.BookmarkId,
                    cancellationToken);
                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _bookmarkStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected bookmark state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyDurableValueStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_durableValueStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _durableValueStateStore.DeleteAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.DurableValueId,
                    cancellationToken);
                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _durableValueStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected durable value state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyIncidentStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
            {
                var added = await _incidentStateStore.TryAddAsync(stateChange.State, cancellationToken);
                if (!added)
                    throw new InvalidOperationException($"Incident state '{stateChange.State.IncidentId}' already exists for workflow execution '{stateChange.State.WorkflowExecutionId}'.");

                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _incidentStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected incident state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyOperationalStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_operationalStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _operationalStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected operational state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyActivityScopeCleanupsAsync(
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups,
        CancellationToken cancellationToken)
    {
        if (_activityScopeCleanupStore is null)
        {
            if (cleanups.Count != 0)
                throw new InvalidOperationException("The checkpoint contains activity-scope cleanup but no cleanup store is configured.");
            return;
        }

        foreach (var cleanup in cleanups)
            await _activityScopeCleanupStore.ApplyAsync(cleanup, cancellationToken);
    }

    private async ValueTask ApplyWorkflowDispatchChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (stateChanges.Count == 0)
            return;
        if (_workflowDispatchStore is null)
            throw new InvalidOperationException("Workflow dispatch checkpoint changes require an IWorkflowDispatchStore.");

        foreach (var stateChange in stateChanges)
            await _workflowDispatchStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyWorkflowDispatchCancellationsAsync(
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return;
        if (_workflowDispatchStore is not IWorkflowDispatchCancellationStore cancellationStore)
        {
            throw new InvalidOperationException(
                "Workflow dispatch cancellation requests require an IWorkflowDispatchCancellationStore.");
        }

        foreach (var request in requests)
            await cancellationStore.ApplyCancellationAsync(request, cancellationToken);
    }

    private void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint commit store can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }

    private async ValueTask ValidateWorkflowTestScopesAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        var execution = commit.StateChanges.WorkflowExecution?.State;
        var existingExecution = execution is { TestScope: not null, ParentWorkflowExecutionId: null } &&
                                _workflowExecutionStateStore is not null
            ? await _workflowExecutionStateStore.FindAsync(execution.WorkflowExecutionId, cancellationToken)
            : null;
        lock (_state.SyncRoot)
        {
            if (execution is { TestScope: { } rootScope, ParentWorkflowExecutionId: null } &&
                existingExecution is null)
            {
                AssertOpenScope(rootScope, commit.Checkpoint.OccurredAt);
            }

            foreach (var change in commit.StateChanges.WorkflowDispatches)
            {
                var dispatch = change.State;
                if (dispatch.TestScope is { } scope && !_state.WorkflowDispatches.ContainsKey(dispatch.DispatchId))
                    AssertOpenScope(scope, commit.Checkpoint.OccurredAt);
            }
        }
    }

    private void AssertOpenScope(WorkflowTestScope scope, DateTimeOffset observedAt)
    {
        if (!_state.WorkflowTestScopes.TryGetValue(scope.ScopeId, out var record) ||
            record.State != WorkflowTestScopeState.Open ||
            record.Scope.IsExpired(observedAt) ||
            !WorkflowTestScope.ContextEquals(record.Scope, scope))
        {
            throw new InvalidOperationException("The workflow test scope is not open in the current persistence context.");
        }
    }

    private void ValidateSchedulerStateChange(RuntimeCheckpointCommit commit)
    {
        if (_schedulerStateStore is null || commit.StateChanges.Scheduler is null)
            return;

        var stateChange = commit.StateChanges.Scheduler;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint commit store can only project scheduler state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change StateId must match SchedulerState.WorkflowExecutionId.");

        if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
    }

    private void ValidateActivityExecutionStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_activityExecutionStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.ActivityExecutions)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project activity execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.Execution.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution state change StateId must match ActivityExecution.ActivityExecutionId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.Execution.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateActivityExecutionInspectionChanges(RuntimeCheckpointCommit commit)
    {
        if (_activityExecutionInspectionWriter is null)
            return;

        foreach (var stateChange in commit.StateChanges.ActivityExecutionInspections)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project activity execution inspection '{RuntimeStateChangeOperation.Upsert}' changes.");

            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution inspection state change StateId must match ActivityExecutionInspectionProjection.ActivityExecutionId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution inspection WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateBookmarkStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_bookmarkStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Bookmarks)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project bookmark state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.BookmarkId))
                throw new InvalidOperationException("Bookmark state change StateId must match BookmarkState.BookmarkId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Bookmark state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateDurableValueStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_durableValueStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.DurableValues)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project durable value state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.DurableValueId))
                throw new InvalidOperationException("Durable value state change StateId must match DurableValueState.DurableValueId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Durable value state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private async ValueTask ValidateIncidentStateChangesAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Append and not RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project incident state '{RuntimeStateChangeOperation.Append}' or '{RuntimeStateChangeOperation.Upsert}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.IncidentId))
                throw new InvalidOperationException("Incident state change StateId must match IncidentState.IncidentId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Incident state change WorkflowExecutionId must match the checkpoint workflow execution ID.");

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                var existing = await _incidentStateStore.FindAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.IncidentId,
                    cancellationToken);
                IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, stateChange.State);
            }
        }
    }

    private void ValidateOperationalStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_operationalStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match ExecutionLivenessState.OperationalStateId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (StringComparer.Ordinal.Equals(
                    stateChange.State.OperationalStateId,
                    InMemoryExecutionLivenessStateStore.GetOwnershipStateId(commit.WorkflowExecutionId)))
            {
                throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
            }
        }
    }

    private async ValueTask ValidateWorkflowDispatchChangesAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowDispatches.Count == 0)
            return;
        if (_workflowDispatchStore is null)
            throw new InvalidOperationException("Workflow dispatch checkpoint changes require an IWorkflowDispatchStore.");

        var seen = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var stateChange in commit.StateChanges.WorkflowDispatches)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project workflow dispatch '{RuntimeStateChangeOperation.Upsert}' changes.");
            WorkflowDispatchLifecycle.ValidateCheckpointOwnership(commit.WorkflowExecutionId, stateChange.State);

            if (seen.TryGetValue(stateChange.StateId, out var duplicate) && !WorkflowDispatchLifecycle.RecordsEqual(duplicate, stateChange.State))
                throw new InvalidOperationException($"Workflow dispatch '{stateChange.StateId}' occurs more than once with conflicting state in the same checkpoint.");
            seen[stateChange.StateId] = stateChange.State;

            var existing = await _workflowDispatchStore.FindAsync(stateChange.StateId, cancellationToken);
            if (existing is not null)
                WorkflowDispatchLifecycle.ValidateTransition(existing, stateChange.State);
            else
                WorkflowDispatchLifecycle.ValidateNew(stateChange.State);
        }
    }

    private async ValueTask ValidateWorkflowDispatchCancellationsAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowDispatchCancellations.Count == 0)
            return;
        if (_workflowDispatchStore is not IWorkflowDispatchCancellationStore)
        {
            throw new InvalidOperationException(
                "Workflow dispatch cancellation requests require an IWorkflowDispatchCancellationStore.");
        }

        foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
        {
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, request.ParentWorkflowExecutionId))
            {
                throw new InvalidOperationException(
                    $"Workflow dispatch cancellation request '{request.DispatchId}' must be committed by its parent workflow execution.");
            }

            var existing = await _workflowDispatchStore.FindAsync(request.DispatchId, cancellationToken);
            if (existing is null)
                throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");
            if (!StringComparer.Ordinal.Equals(existing.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
                !StringComparer.Ordinal.Equals(existing.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId))
            {
                throw new InvalidOperationException(
                    $"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with the persisted dispatch identity.");
            }
            if (!WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(existing))
            {
                throw new InvalidOperationException(
                    $"Workflow dispatch '{request.DispatchId}' does not permit parent cancellation propagation.");
            }
        }
    }

    private static bool IsSamePendingIntent(RuntimePostCommitOutboxItem existing, RuntimePostCommitOutboxItem item) =>
        existing.Status == RuntimePostCommitOutboxStatus.Pending
        && StringComparer.Ordinal.Equals(existing.Intent.IntentId, item.Intent.IntentId)
        && StringComparer.Ordinal.Equals(existing.Intent.WorkflowExecutionId, item.Intent.WorkflowExecutionId)
        && StringComparer.Ordinal.Equals(existing.Intent.Kind, item.Intent.Kind)
        && StringComparer.Ordinal.Equals(existing.Intent.ActivityExecutionId, item.Intent.ActivityExecutionId)
        && StringComparer.Ordinal.Equals(existing.Intent.IdempotencyKey, item.Intent.IdempotencyKey)
        && StringComparer.Ordinal.Equals(existing.Intent.DependsOnWaitRegistrationId, item.Intent.DependsOnWaitRegistrationId)
        && existing.Intent.WaitFailurePolicy == item.Intent.WaitFailurePolicy
        && PayloadEquals(existing.Intent.Payload, item.Intent.Payload)
        && MetadataEquals(existing.Intent.Metadata, item.Intent.Metadata);

    private static bool PendingOutboxItemsEquivalent(RuntimePostCommitOutboxItem existing, RuntimePostCommitOutboxItem item) =>
        IsSamePendingIntent(existing, item) &&
        existing.RecordedAt == item.RecordedAt &&
        existing.AvailableAt == item.AvailableAt &&
        existing.DeliveryAttemptCount == item.DeliveryAttemptCount &&
        existing.DeliveryFencingToken == item.DeliveryFencingToken &&
        existing.RetryPolicy.IsEquivalentTo(item.RetryPolicy) &&
        MetadataEquals(existing.Metadata, item.Metadata);

    private static bool PayloadEquals(System.Text.Json.JsonElement? left, System.Text.Json.JsonElement? right)
    {
        if (left.HasValue != right.HasValue)
            return false;

        return !left.HasValue || StringComparer.Ordinal.Equals(left.Value.GetRawText(), right!.Value.GetRawText());
    }

    private static bool MetadataEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.All(entry => right.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));
    }

    private static bool IsDeliverable(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxQuery query)
    {
        if (query.WorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, query.WorkflowExecutionId))
            return false;

        if (query.IntentKind is not null && !StringComparer.Ordinal.Equals(item.Intent.Kind, query.IntentKind))
            return false;

        if (item.AvailableAt is { } availableAt && availableAt > query.Now)
            return false;

        if (item.Status == RuntimePostCommitOutboxStatus.Pending)
            return true;

        if (item.Status == RuntimePostCommitOutboxStatus.FailedRetryable)
            return !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount);

        return false;
    }

    private static RuntimePostCommitOutboxStatus NormalizeDeliveryStatus(
        RuntimePostCommitOutboxItem existing,
        RuntimePostCommitOutboxStatus status,
        int deliveryAttemptCount)
    {
        if (status != RuntimePostCommitOutboxStatus.FailedRetryable)
            return status;

        return existing.RetryPolicy.IsExhaustedAfterAttempt(deliveryAttemptCount)
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : RuntimePostCommitOutboxStatus.FailedRetryable;
    }

    private static DateTimeOffset NextRetryAvailableAt(RuntimePostCommitOutboxItem existing, DateTimeOffset recordedAt) =>
        existing.RetryPolicy.Delay is { } delay ? recordedAt.Add(delay) : recordedAt;
}

public sealed record RuntimeCheckpointCommitRecord(
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision,
    IReadOnlyCollection<string> PendingPostCommitWorkIds,
    IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds);

internal sealed record InMemoryRuntimeCheckpointOrderState(long Reserved, long Committed, long Revision)
{
    public static InMemoryRuntimeCheckpointOrderState Empty { get; } = new(0, 0, 0);
}

internal sealed record InMemoryRuntimeExecutionContextState(RuntimeExecutionContextSnapshot Snapshot, long Revision)
{
    public static InMemoryRuntimeExecutionContextState Empty { get; } = new(RuntimeExecutionContextSnapshot.Empty, 0);
}
