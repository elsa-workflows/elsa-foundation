using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRuntimeCheckpointCommitStore"/> for the Groundwork bridge.
/// </summary>
/// <remarks>
/// <para>
/// Runtime checkpoints are applied through one Groundwork document unit-of-work so lifecycle state,
/// inspection projections, side-effect state, and the commit marker succeed or roll back together:
/// </para>
/// <list type="bullet">
/// <item>On entry, an existing marker document keyed by <c>CommitId</c> short-circuits redelivery.</item>
/// <item>The marker is committed in the same document unit-of-work as the runtime state changes.</item>
/// </list>
/// </remarks>
public sealed class GroundworkRuntimeCheckpointWriter : IRuntimeCheckpointCommitStore, IRuntimeCheckpointPreparedLedgerStore
{
    private static readonly TimeSpan CommitAcknowledgementReconciliationTimeout = TimeSpan.FromSeconds(10);
    private readonly IDocumentStore _commitLedger;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly IWorkflowExecutableRootWriteLeaseManager _rootWriteLeaseManager;
    private readonly TimeProvider _timeProvider;
    private readonly RuntimeGroupCommitCoordinator? _groupCommitCoordinator;
    private readonly RuntimeCheckpointPreparationPayloadCodec _preparationPayloadCodec = new();

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IExecutionLivenessStateStore operationalStateStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
        TimeProvider? timeProvider = null,
        RuntimeGroupCommitCoordinator? groupCommitCoordinator = null)
        : this(
            commitLedger,
            serializer,
            accessContextAccessor,
            workflowExecutionStateStore,
            schedulerStateStore,
            activityExecutionStateStore,
            new GroundworkActivityExecutionInspectionStore(commitLedger, serializer),
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            rootWriteLeaseManager,
            timeProvider,
            groupCommitCoordinator)
    {
    }

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IActivityExecutionInspectionWriter activityExecutionInspectionWriter,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IExecutionLivenessStateStore operationalStateStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
        TimeProvider? timeProvider = null,
        RuntimeGroupCommitCoordinator? groupCommitCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(commitLedger);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionInspectionWriter);
        ArgumentNullException.ThrowIfNull(bookmarkStateStore);
        ArgumentNullException.ThrowIfNull(durableValueStateStore);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        ArgumentNullException.ThrowIfNull(rootWriteLeaseManager);
        _commitLedger = commitLedger;
        _serializer = serializer;
        _accessContextAccessor = accessContextAccessor;
        _rootWriteLeaseManager = rootWriteLeaseManager;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _groupCommitCoordinator = groupCommitCoordinator;
    }

    /// <summary>Durably reserves generic checkpoint provenance and canonical recovery input.</summary>
    /// <exception cref="ArgumentNullException">The request or one of its required values is null.</exception>
    /// <exception cref="ArgumentException">A required identity is blank.</exception>
    /// <exception cref="InvalidOperationException">The request is outside the active tenant scope.</exception>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    /// <exception cref="GroundworkRuntimeCheckpointWriterException">Groundwork cannot encode or atomically persist the preparation.</exception>
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
        EnsureTenantScope(request.Commit);
        EnsureAtomicBoundary(request.Commit);

        RuntimeCheckpointPreparationPayloadEnvelope envelope;
        RuntimeCheckpointPrepareRequest frozenRequest;
        try
        {
            envelope = _preparationPayloadCodec.Encode(request);
            var decodedRequest = _preparationPayloadCodec.Decode(envelope);
            frozenRequest = decodedRequest with
            {
                Commit = decodedRequest.Commit with { ExpectedFence = request.Commit.ExpectedFence }
            };
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new GroundworkRuntimeCheckpointWriterException(
                $"Failed to encode runtime checkpoint preparation '{request.Commit.CommitId}' for workflow execution '{request.Commit.WorkflowExecutionId}'.",
                e);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
                var store = new GroundworkDocumentUnitOfWorkStore(_commitLedger, unitOfWork);
                var existingLedger = await LoadLedgerAsync(store, request.Commit.CommitId, cancellationToken);
                if (existingLedger is not null)
                {
                    var result = await ResolvePreparedReplayAsync(store, existingLedger, frozenRequest, envelope, cancellationToken);
                    await unitOfWork.CommitAsync(cancellationToken);
                    return result;
                }

                var legacyMarker = await LoadMarkerAsync(store, request.Commit, cancellationToken);
                var legacyFingerprint = RuntimeCheckpointCommitFingerprint.Compute(request.Commit);
                if (legacyMarker is not null && !StringComparer.Ordinal.Equals(legacyMarker.Fingerprint, legacyFingerprint))
                    return RuntimeCheckpointPreparationResult.Conflict();
                var receipt = legacyMarker is null ? null : ResolveReplay(request.Commit, legacyFingerprint, legacyMarker);
                if (legacyMarker is not null &&
                    (!request.RequestedExecutionContext.IsEmpty ||
                     !StringComparer.Ordinal.Equals(request.SourceIdentity, request.Commit.Checkpoint.Name) ||
                     !StringComparer.Ordinal.Equals(request.OperationIdentity, request.Commit.Checkpoint.CheckpointId)))
                {
                    return RuntimeCheckpointPreparationResult.Conflict();
                }
                if (legacyMarker is null)
                    await ValidateAndTouchExpectedFenceAsync(store, request.Commit, cancellationToken);
                var coordination = await LoadCoordinationAsync(store, request.Commit.WorkflowExecutionId, cancellationToken);
                var current = coordination?.Document ?? RuntimeCheckpointCoordinationDocument.Empty(request.Commit.WorkflowExecutionId);
                var order = checked(current.ReservedOrder + 1);
                var nextRevision = checked(current.OrderRevision + 1);
                var provenance = new RuntimeCheckpointProvenance(
                    request.RequestedExecutionContext.IsEmpty ? current.ExecutionContext : request.RequestedExecutionContext,
                    order);
                var status = legacyMarker is null
                    ? RuntimeLogicalCheckpointLedgerStatus.Prepared
                    : RuntimeLogicalCheckpointLedgerStatus.Committed;
                var ledgerEntry = new RuntimeLogicalCheckpointLedgerEntry(
                    request.Commit.CommitId,
                    ComputeLedgerToken(
                        request.Commit.CommitId,
                        envelope.PayloadSha256,
                        order,
                        nextRevision,
                        current.ContextRevision,
                        request.Commit.ExpectedFence),
                    provenance,
                    request.SourceIdentity,
                    request.OperationIdentity,
                    frozenRequest.Commit.Checkpoint with { Provenance = null },
                    frozenRequest.Commit.StateChanges,
                    frozenRequest.Commit.PostCommitIntents,
                    frozenRequest.Commit.Metadata,
                    frozenRequest.RequestedExecutionContext,
                    nextRevision,
                    current.ContextRevision,
                    request.Commit.ExpectedFence,
                    envelope.PayloadSha256,
                    envelope.CanonicalPayload,
                    $"groundwork-runtime-checkpoint:{request.Commit.WorkflowExecutionId}:{request.Commit.CommitId}",
                    frozenRequest.InitialPersistenceMode,
                    status,
                    legacyMarker?.Fingerprint,
                    receipt,
                    ReconciledLegacyMarker: legacyMarker is not null,
                    WorkflowExecutionId: request.Commit.WorkflowExecutionId,
                    RecoveryAuthority: frozenRequest.RecoveryAuthority,
                    CurrentAuthorityFence: request.Commit.ExpectedFence,
                    AuthorityRevision: 1);
                var nextCoordination = current with
                {
                    ReservedOrder = order,
                    CommittedOrder = legacyMarker is null ? current.CommittedOrder : Math.Max(current.CommittedOrder, order),
                    OrderRevision = nextRevision
                };
                await SaveCoordinationAsync(store, nextCoordination, coordination?.Version ?? 0, cancellationToken);
                await SaveLedgerAsync(store, new RuntimeCheckpointLedgerDocument(ledgerEntry), expectedVersion: 0, cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
                var token = ToToken(ledgerEntry);
                return legacyMarker is null
                    ? RuntimeCheckpointPreparationResult.Prepared(token)
                    : RuntimeCheckpointPreparationResult.Replay(token, receipt);
            }
            catch (PreparedDocumentConcurrencyException)
            {
                continue;
            }
            catch (RuntimeStaleFencingTokenException)
            {
                return RuntimeCheckpointPreparationResult.OwnershipLost();
            }
            catch (Exception e) when (e is not OperationCanceledException and
                                      not RuntimeCheckpointReplayConflictException and
                                      not GroundworkRuntimeCheckpointWriterException)
            {
                throw new GroundworkRuntimeCheckpointWriterException(
                    $"Failed to prepare runtime checkpoint '{request.Commit.CommitId}' for workflow execution '{request.Commit.WorkflowExecutionId}'.",
                    e);
            }
        }
    }

    /// <summary>
    /// Reads only durable <see cref="RuntimeLogicalCheckpointLedgerStatus.Prepared"/> reservations through the
    /// manifest-declared workflow/status/order/commit route. The public cursor is intentionally a protocol cursor,
    /// rather than a provider continuation, so it remains bound to the recovery protocol version and filter.
    /// </summary>
    public async ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
        RuntimeCheckpointPreparedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (_commitLedger is not IBoundedDocumentStore boundedStore)
        {
            throw new GroundworkRuntimeCheckpointWriterException(
                "The Groundwork document store does not expose the declared bounded prepared-checkpoint ledger route.",
                new NotSupportedException(nameof(IBoundedDocumentStore)));
        }

        var cursor = query.DecodeCursor();
        var clauses = new List<DocumentQueryClause>
        {
            DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowExecutionIdField,
                query.WorkflowExecutionId)),
            DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerStatusField,
                ((int)RuntimeLogicalCheckpointLedgerStatus.Prepared).ToString(CultureInfo.InvariantCulture)))
        };
        if (cursor is not null)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.GreaterThanOrEqual(
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowCheckpointOrderField,
                cursor.WorkflowCheckpointOrder.ToString(CultureInfo.InvariantCulture))));
        }

        var result = await boundedStore.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
                ElsaRuntimeStorageManifest.PagePreparedRuntimeCheckpointsQuery,
                clauses,
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowCheckpointOrderField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerCommitIdField)
                ],
                take: Math.Min(ElsaGroundworkQueryRoutes.MaximumResultCount, checked(query.PageSize + 1))),
            cancellationToken);

        var reservations = result.Documents
            .Select(_serializer.Deserialize<RuntimeCheckpointLedgerDocument>)
            .Select(document => document.Entry)
            .Where(entry => entry.Status == RuntimeLogicalCheckpointLedgerStatus.Prepared)
            .Where(entry => StringComparer.Ordinal.Equals(entry.WorkflowExecutionId ?? entry.RawCheckpoint?.WorkflowExecutionId, query.WorkflowExecutionId))
            .Where(entry => cursor is null || IsAfter(entry, cursor))
            .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder)
            .ThenBy(entry => entry.CommitId, StringComparer.Ordinal)
            .Take(query.PageSize + 1)
            .ToArray();

        var hasNext = reservations.Length > query.PageSize;
        var pageEntries = hasNext ? reservations[..query.PageSize] : reservations;
        var pageReservations = pageEntries
            .Select(ToPreparedReservation)
            .ToArray();
        var nextCursor = hasNext
            ? query.EncodeCursor(
                pageEntries[^1].Provenance.WorkflowCheckpointOrder,
                pageEntries[^1].CommitId)
            : null;
        return new RuntimeCheckpointPreparedPage(query, pageReservations, nextCursor);
    }

    /// <summary>
    /// Atomically adopts one complete, ordered durable Prepared scope.  This deliberately changes only the mutable
    /// current-authority binding; preparation identity and recovery input remain byte-for-byte provider-owned.
    /// </summary>
    public async ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAdoptionRequestWellFormed(request))
            return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Conflict, request);

        // Groundwork queries are store-level rather than UoW-bound. Observe both the coordination version and exact
        // Prepared membership before opening the transaction, then prove that observation is still current inside it.
        // Every Prepare/finalization updates the coordination row, so the version check closes the scan/begin window.
        var observedCoordination = await LoadCoordinationAsync(_commitLedger, request.WorkflowExecutionId, cancellationToken);
        if (observedCoordination is null)
            return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Conflict, request);
        var entries = await LoadPreparedEntriesThroughAsync(
            _commitLedger,
            request.WorkflowExecutionId,
            request.ThroughWorkflowCheckpointOrder,
            cancellationToken);

        try
        {
            await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
            var store = new GroundworkDocumentUnitOfWorkStore(_commitLedger, unitOfWork);
            var coordination = await LoadCoordinationAsync(store, request.WorkflowExecutionId, cancellationToken)
                ?? throw new PreparedDocumentConcurrencyException();
            if (coordination.Version != observedCoordination.Version ||
                !Equals(coordination.Document, observedCoordination.Document))
                throw new PreparedDocumentConcurrencyException();

            if (!AdoptionScopeMatches(entries, request))
                return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Conflict, request);

            var currentBindingsMatch = entries.All(entry =>
                Equals(entry.CurrentAuthorityFence, request.Members.Single(member => StringComparer.Ordinal.Equals(member.CommitId, entry.CommitId)).ExpectedCurrentAuthorityFence) &&
                entry.AuthorityRevision == request.Members.Single(member => StringComparer.Ordinal.Equals(member.CommitId, entry.CommitId)).ExpectedAuthorityRevision);
            if (!currentBindingsMatch)
                return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Conflict, request);

            if (entries.All(entry => Equals(entry.CurrentAuthorityFence, request.TargetAuthorityFence)))
            {
                await unitOfWork.CommitAsync(cancellationToken);
                return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Replay, request);
            }

            if (!CanAdvanceAuthorityFence(entries, request.TargetAuthorityFence))
                return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost, request);

            try
            {
                await ValidateAndTouchExpectedFenceAsync(
                    store,
                    CreatePreparedLedgerAuthorityCommit(request.WorkflowExecutionId, request.TargetAuthorityFence),
                    cancellationToken);
            }
            catch (RuntimeStaleFencingTokenException)
            {
                return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost, request);
            }

            foreach (var entry in entries)
            {
                var loaded = await LoadLedgerAsync(store, entry.CommitId, cancellationToken)
                    ?? throw new PreparedDocumentConcurrencyException();
                if (loaded.Document.Entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared ||
                    !StringComparer.Ordinal.Equals(loaded.Document.Entry.LedgerToken, entry.LedgerToken) ||
                    loaded.Document.Entry.Provenance.WorkflowCheckpointOrder != entry.Provenance.WorkflowCheckpointOrder ||
                    !StringComparer.Ordinal.Equals(loaded.Document.Entry.InputFingerprint, entry.InputFingerprint) ||
                    !StringComparer.Ordinal.Equals(loaded.Document.Entry.CanonicalInputReference, entry.CanonicalInputReference) ||
                    !Equals(loaded.Document.Entry.CurrentAuthorityFence, entry.CurrentAuthorityFence) ||
                    loaded.Document.Entry.AuthorityRevision != entry.AuthorityRevision)
                    throw new PreparedDocumentConcurrencyException();

                var adopted = entry with
                {
                    CurrentAuthorityFence = request.TargetAuthorityFence,
                    AuthorityRevision = checked(entry.AuthorityRevision + 1)
                };
                await SaveLedgerAsync(store, new RuntimeCheckpointLedgerDocument(adopted), loaded.Version, cancellationToken);
            }

            // The bounded membership scan is supplied by the owner store because Groundwork's document UoW exposes
            // only read-by-id. CAS-touching the authoritative coordination row in this same transaction closes both
            // sides of that scan: every hidden insertion or terminal removal also changes this row and conflicts.
            await SaveCoordinationAsync(store, coordination.Document, coordination.Version, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Adopted, request);
        }
        catch (PreparedDocumentConcurrencyException)
        {
            return AdoptionReceipt(RuntimeCheckpointPreparedAdoptionStatus.Conflict, request);
        }
    }

    /// <summary>Atomically assigns explicit terminal dispositions and applies the folded durable effects once.</summary>
    public async ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (!RuntimeCheckpointPreparedFoldBuilder.Matches(request))
            return FoldConflict();

        var foldFingerprint = RuntimeCheckpointCommitFingerprint.ComputePreparedFold(request);
        var observedCoordination = await LoadCoordinationAsync(_commitLedger, request.WorkflowExecutionId, cancellationToken);
        if (observedCoordination is null)
            return FoldConflict();
        var observedPreparedScope = await LoadPreparedEntriesThroughAsync(
            _commitLedger,
            request.WorkflowExecutionId,
            request.MaxWorkflowCheckpointOrder,
            cancellationToken);
        try
        {
            await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
            var store = new GroundworkDocumentUnitOfWorkStore(_commitLedger, unitOfWork);
            var coordination = await LoadCoordinationAsync(store, request.WorkflowExecutionId, cancellationToken)
                ?? throw new PreparedDocumentConcurrencyException();
            if (coordination.Version != observedCoordination.Version ||
                !Equals(coordination.Document, observedCoordination.Document))
                throw new PreparedDocumentConcurrencyException();
            var loaded = new List<VersionedLedger>(request.Members.Count);
            foreach (var member in request.Members)
            {
                var ledger = await LoadLedgerAsync(store, member.CommitId, cancellationToken);
                if (ledger is null || !TokenMatches(ledger.Document.Entry, member.Token))
                    return FoldConflict();
                loaded.Add(ledger);
            }

            if (loaded.Any(item => !StringComparer.Ordinal.Equals(item.Document.Entry.WorkflowExecutionId, request.WorkflowExecutionId)) ||
                loaded.Any(item => item.Document.Entry.Provenance.WorkflowCheckpointOrder > request.MaxWorkflowCheckpointOrder))
                return FoldConflict();

            if (loaded.All(item => item.Document.Entry.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed))
            {
                if (loaded.Any(item => !StringComparer.Ordinal.Equals(item.Document.Entry.TerminalFoldFingerprint, foldFingerprint)))
                    return FoldConflict();
                var replays = loaded.ToDictionary(
                    item => item.Document.Entry.CommitId,
                    item => item.Document.Entry.Receipt ?? new RuntimeCheckpointCommitStoreResult([]),
                    StringComparer.Ordinal);
                await unitOfWork.CommitAsync(cancellationToken);
                return new RuntimeCheckpointPreparedFoldResult(RuntimeCheckpointCommitStoreStatus.Replay, replays);
            }

            if (loaded.Any(item => item.Document.Entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared))
                return FoldConflict();
            if (!FoldMembersMatch(loaded, request))
                return FoldConflict();
            if (!IsExactFoldScope(observedPreparedScope, request))
                return FoldConflict();

            var committedMembers = request.Members
                .Where(member => member.Disposition == RuntimeCheckpointPreparedDisposition.Committed)
                .ToArray();
            if (committedMembers.Length == 0)
            {
                try
                {
                    await ValidateAndTouchExpectedFenceAsync(
                        store,
                        CreatePreparedLedgerAuthorityCommit(request.WorkflowExecutionId, request.TargetAuthorityFence),
                        cancellationToken);
                }
                catch (RuntimeStaleFencingTokenException)
                {
                    return FoldConflict();
                }
            }
            if (committedMembers.Length > 0)
            {
                var effectCarrier = committedMembers[^1].PreparedCommit!;
                // The replayed member retains its immutable original preparation fence. Durable application is owned
                // by the provider-validated current/target fold authority, so the synthetic effect carrier must touch
                // the active successor lease without mutating any member token or canonical preparation identity.
                var foldedCommit = effectCarrier.Commit with
                {
                    ExpectedFence = request.TargetAuthorityFence,
                    StateChanges = request.FoldedStateChanges
                };
                EnsureTenantScope(foldedCommit);
                EnsureAtomicBoundary(foldedCommit);
                ValidateCheckpointStateChanges(foldedCommit);
                await ApplyStagedAsync(
                    store,
                    foldedCommit,
                    effectCarrier.VerifiedCommitFingerprint,
                    cancellationToken,
                    writeCommitMarker: false);

                foreach (var member in committedMembers)
                {
                    var prepared = member.PreparedCommit!;
                    await MarkCommittedAsync(store, prepared.Commit, prepared.VerifiedCommitFingerprint, cancellationToken);
                }
            }

            if (coordination.Document.ReservedOrder < request.MaxWorkflowCheckpointOrder ||
                coordination.Document.CommittedOrder >= request.MaxWorkflowCheckpointOrder)
                return FoldConflict();
            var receipts = new Dictionary<string, RuntimeCheckpointCommitStoreResult>(StringComparer.Ordinal);
            for (var index = 0; index < request.Members.Count; index++)
            {
                var member = request.Members[index];
                var entry = loaded[index].Document.Entry;
                var status = member.Disposition switch
                {
                    RuntimeCheckpointPreparedDisposition.Committed => RuntimeLogicalCheckpointLedgerStatus.Committed,
                    RuntimeCheckpointPreparedDisposition.Skipped => RuntimeLogicalCheckpointLedgerStatus.Skipped,
                    RuntimeCheckpointPreparedDisposition.Failed => RuntimeLogicalCheckpointLedgerStatus.Failed,
                    _ => throw new ArgumentOutOfRangeException()
                };
                var receipt = member.Disposition switch
                {
                    RuntimeCheckpointPreparedDisposition.Committed => new RuntimeCheckpointCommitStoreResult(
                        member.PreparedCommit!.Commit.StateChanges.PostCommitOutbox
                            .Select(change => change.State.OutboxItemId)
                            .Order(StringComparer.Ordinal)
                            .ToArray())
                    {
                        Status = RuntimeCheckpointCommitStoreStatus.Committed,
                        CommitFingerprint = member.PreparedCommit.VerifiedCommitFingerprint,
                        ConsumedSchedulerWorkItemIds = member.PreparedCommit.Commit.StateChanges.ConsumedSchedulerWorkItems
                            .Select(item => item.WorkItemId)
                            .Order(StringComparer.Ordinal)
                            .ToArray()
                    },
                    RuntimeCheckpointPreparedDisposition.Skipped => new RuntimeCheckpointCommitStoreResult([])
                    {
                        Status = RuntimeCheckpointCommitStoreStatus.Skipped,
                        CommitFingerprint = member.PreparedCommit!.VerifiedCommitFingerprint
                    },
                    RuntimeCheckpointPreparedDisposition.Failed => new RuntimeCheckpointCommitStoreResult([]) { Status = RuntimeCheckpointCommitStoreStatus.Failed, FailureCode = member.FailureCode, FailureMessage = member.FailureMessage },
                    _ => throw new ArgumentOutOfRangeException()
                };
                var fingerprint = member.PreparedCommit?.VerifiedCommitFingerprint;
                var terminalAuthority = request.RecoveryRoute == RuntimeCheckpointRecoveryRoute.SourceFree
                    ? entry with
                    {
                        CurrentAuthorityFence = request.TargetAuthorityFence,
                        AuthorityRevision = checked(entry.AuthorityRevision + 1)
                    }
                    : entry;
                var terminal = terminalAuthority.CompactTerminal(status, receipt, fingerprint, foldFingerprint, request.WorkflowExecutionId);
                await SaveLedgerAsync(store, new RuntimeCheckpointLedgerDocument(terminal), loaded[index].Version, cancellationToken);
                receipts.Add(member.CommitId, receipt);
            }

            var terminalContext = committedMembers.Length > 0
                ? committedMembers[^1].PreparedCommit!.Token.Provenance.ExecutionContext ??
                  throw new InvalidOperationException("A committed prepared fold member has no execution context.")
                : coordination.Document.ExecutionContext;
            var contextChanged = !coordination.Document.ExecutionContext.Equals(terminalContext);
            var nextCoordination = coordination.Document with
            {
                CommittedOrder = Math.Max(coordination.Document.CommittedOrder, request.MaxWorkflowCheckpointOrder),
                OrderRevision = checked(coordination.Document.OrderRevision + 1),
                ExecutionContext = terminalContext,
                ContextRevision = contextChanged
                    ? checked(coordination.Document.ContextRevision + 1)
                    : coordination.Document.ContextRevision
            };
            await SaveCoordinationAsync(store, nextCoordination, coordination.Version, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new RuntimeCheckpointPreparedFoldResult(RuntimeCheckpointCommitStoreStatus.Committed, receipts);
        }
        catch (PreparedDocumentConcurrencyException)
        {
            return FoldConflict();
        }

        RuntimeCheckpointPreparedFoldResult FoldConflict() =>
            new(RuntimeCheckpointCommitStoreStatus.Conflict, new Dictionary<string, RuntimeCheckpointCommitStoreResult>(StringComparer.Ordinal));
    }

    /// <summary>Atomically finalizes a durable checkpoint preparation with its state, outbox, context, and marker.</summary>
    /// <exception cref="ArgumentNullException">The token, commit, or persistence decision is null.</exception>
    /// <exception cref="ArgumentException">The token is invalid.</exception>
    /// <exception cref="InvalidOperationException">The commit is outside the active tenant scope.</exception>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    /// <exception cref="RuntimeSchedulerWorkConsumeConflictException">A scheduler work claim is no longer owned by this checkpoint.</exception>
    /// <exception cref="GroundworkRuntimeCheckpointWriterException">Groundwork cannot read or atomically finalize the preparation.</exception>
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
        EnsureTenantScope(commit);
        EnsureAtomicBoundary(commit);

        try
        {
            await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
            var store = new GroundworkDocumentUnitOfWorkStore(_commitLedger, unitOfWork);
            var loadedLedger = await LoadLedgerAsync(store, token.CommitId, cancellationToken);
            if (loadedLedger is null || !TokenMatches(loadedLedger.Document.Entry, token))
                return Conflict();
            var entry = loadedLedger.Document.Entry;
            var preparedRequest = DecodePreparation(entry);
            if (!StringComparer.Ordinal.Equals(commit.CommitId, entry.CommitId) ||
                !StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, preparedRequest.Commit.WorkflowExecutionId) ||
                !token.Provenance.Equals(commit.Checkpoint.Provenance))
                return Conflict();
            var finalFingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
            if (entry.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed)
            {
                if (entry.ReconciledLegacyMarker == false && !StringComparer.Ordinal.Equals(entry.CommitFingerprint, finalFingerprint))
                    return Conflict();
                if (entry.ReconciledLegacyMarker == true && !RawCommitMatches(preparedRequest.Commit, commit))
                    return Conflict();
                return (entry.Receipt ?? new RuntimeCheckpointCommitStoreResult([])) with
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Replay,
                    CommitFingerprint = entry.CommitFingerprint
                };
            }
            if (entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared)
                return Conflict();
            if (!Equals(entry.ExpectedFence, commit.ExpectedFence))
                return Conflict();

            var coordination = await LoadCoordinationAsync(store, commit.WorkflowExecutionId, cancellationToken);
            if (coordination is null ||
                coordination.Document.OrderRevision != token.ExpectedOrderRevision ||
                coordination.Document.ContextRevision != token.ExpectedContextRevision ||
                coordination.Document.ReservedOrder < token.Provenance.WorkflowCheckpointOrder ||
                coordination.Document.CommittedOrder >= token.Provenance.WorkflowCheckpointOrder)
                return Conflict();
            var existingMarker = await LoadMarkerAsync(store, commit, cancellationToken);
            RuntimeCheckpointCommitStoreResult receipt;
            RuntimeLogicalCheckpointLedgerStatus finalStatus;
            var replayedExistingMarker = existingMarker is not null;
            if (existingMarker is not null)
            {
                if (!StringComparer.Ordinal.Equals(existingMarker.Fingerprint, finalFingerprint))
                    return Conflict();
                receipt = ResolveReplay(commit, finalFingerprint, existingMarker) with
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Committed,
                    CommitFingerprint = finalFingerprint
                };
                finalStatus = RuntimeLogicalCheckpointLedgerStatus.Committed;
            }
            else if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
            {
                await ValidateAndTouchExpectedFenceAsync(store, commit, cancellationToken);
                receipt = new RuntimeCheckpointCommitStoreResult([])
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Skipped,
                    CommitFingerprint = finalFingerprint
                };
                finalStatus = RuntimeLogicalCheckpointLedgerStatus.Skipped;
            }
            else
            {
                receipt = await ApplyStagedAsync(store, commit, finalFingerprint, cancellationToken, writeCommitMarker: false);
                receipt = receipt with
                {
                    Status = RuntimeCheckpointCommitStoreStatus.Committed,
                    CommitFingerprint = finalFingerprint
                };
                finalStatus = RuntimeLogicalCheckpointLedgerStatus.Committed;
                await MarkCommittedAsync(store, commit, finalFingerprint, cancellationToken);
            }

            var nextCoordination = coordination.Document with
            {
                CommittedOrder = finalStatus == RuntimeLogicalCheckpointLedgerStatus.Committed
                    ? Math.Max(coordination.Document.CommittedOrder, token.Provenance.WorkflowCheckpointOrder)
                    : coordination.Document.CommittedOrder,
                OrderRevision = checked(coordination.Document.OrderRevision + 1),
                ExecutionContext = finalStatus == RuntimeLogicalCheckpointLedgerStatus.Committed &&
                                   !preparedRequest.RequestedExecutionContext.IsEmpty
                    ? token.Provenance.ExecutionContext ??
                      throw new InvalidOperationException("A committed prepared checkpoint has no execution context.")
                    : coordination.Document.ExecutionContext,
                ContextRevision = finalStatus == RuntimeLogicalCheckpointLedgerStatus.Committed &&
                                  !preparedRequest.RequestedExecutionContext.IsEmpty &&
                                  !coordination.Document.ExecutionContext.Equals(token.Provenance.ExecutionContext)
                    ? checked(coordination.Document.ContextRevision + 1)
                    : coordination.Document.ContextRevision
            };
            var finalEntry = entry with
            {
                Status = finalStatus,
                CommitFingerprint = finalFingerprint,
                Receipt = receipt
            };
            await SaveCoordinationAsync(store, nextCoordination, coordination.Version, cancellationToken);
            await SaveLedgerAsync(store, new RuntimeCheckpointLedgerDocument(finalEntry), loadedLedger.Version, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return replayedExistingMarker
                ? receipt with { Status = RuntimeCheckpointCommitStoreStatus.Replay }
                : receipt;
        }
        catch (RuntimeStaleFencingTokenException)
        {
            return new RuntimeCheckpointCommitStoreResult([]) { Status = RuntimeCheckpointCommitStoreStatus.OwnershipLost };
        }
        catch (PreparedDocumentConcurrencyException)
        {
            return Conflict();
        }
        catch (Exception e) when (e is not OperationCanceledException and
                                  not RuntimeSchedulerWorkConsumeConflictException and
                                  not RuntimeCheckpointReplayConflictException and
                                  not GroundworkRuntimeCheckpointWriterException)
        {
            throw new GroundworkRuntimeCheckpointWriterException(
                $"Failed to atomically commit prepared runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}'.",
                e);
        }
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        cancellationToken.ThrowIfCancellationRequested();
        if (commit.StateChanges.WorkflowExecution is { } workflowExecutionChange)
            _accessContextAccessor.Current.EnsureTenantScope(workflowExecutionChange.State.TenantId);
        foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
            _accessContextAccessor.Current.EnsureTenantScope(dispatch.State.TenantId);

        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
        if (await LoadMarkerAsync(commit, cancellationToken) is { } existing)
            return ResolveReplay(commit, fingerprint, existing);

        ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
        ValidateSchedulerStateChange(commit);
        ValidateActivityExecutionStateChanges(commit);
        ValidateActivityExecutionInspectionChanges(commit);
        ValidateBookmarkStateChanges(commit);
        ValidateDurableValueStateChanges(commit);
        ValidateIncidentStateChanges(commit);
        ValidateOperationalStateChanges(commit);
        ValidateActivityScopeCleanups(commit);
        ValidateConsumedSchedulerWorkItems(commit);
        ValidateWorkflowDispatchChanges(commit);
        ValidateWorkflowDispatchCancellations(commit);

        return await ExecuteWithWorkflowExecutionRootWriteLeaseAsync(
            commit,
            (candidate, token) => WriteAsync(candidate, fingerprint, token),
            cancellationToken);
    }

    // The durable write, taken under the execution root-write lease. When a group-commit coordinator is registered
    // (spec 115), concurrent commits waiting on the single durable writer are folded into one shared unit-of-work / one
    // fsync; a lone committer degrades to the same single-commit path used when group commit is off. Either way this
    // one commit is applied atomically and acknowledged only after its bytes are durable.
    private ValueTask<RuntimeCheckpointCommitStoreResult> WriteAsync(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (_groupCommitCoordinator is null)
            return ApplyAtomicallyAsync(commit, fingerprint, cancellationToken);

        return _groupCommitCoordinator.SubmitAsync(
            BatchTenantKey(commit),
            RuntimeCheckpointCommitScope(),
            (transactionalStore, token) => ApplyStagedAsync(transactionalStore, commit, fingerprint, token),
            token => ApplyAtomicallyAsync(commit, fingerprint, token),
            cancellationToken);
    }

    // Batches may only fold same-tenant commits: the Groundwork unit-of-work scope resolver rejects a mixed-tenant
    // transaction. The checkpoint's tenant is carried on its workflow-execution state (validated in CommitAsync);
    // commits without a workflow-execution change key on the empty tenant.
    private static string BatchTenantKey(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.WorkflowExecution?.State.TenantId ?? string.Empty;

    private async ValueTask<RuntimeCheckpointCommitStoreResult> ExecuteWithWorkflowExecutionRootWriteLeaseAsync(
        RuntimeCheckpointCommit commit,
        Func<RuntimeCheckpointCommit, CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> write,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowExecution is not { } workflowExecutionChange)
            return await write(commit, cancellationToken);

        RuntimeCheckpointCommitStoreResult? result = null;
        await _rootWriteLeaseManager.ExecuteAsync(
            workflowExecutionChange.State.PinnedExecutable,
            $"checkpoint:{commit.CommitId}",
            async ct => result = await write(commit, ct),
            cancellationToken);
        return result ?? throw new InvalidOperationException("The checkpoint root-write lease completed without a store result.");
    }

    private static IReadOnlyCollection<string> OutboxIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.OutboxItemId)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyCollection<string> ConsumedIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.ConsumedSchedulerWorkItems
            .Select(item => item.WorkItemId)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private async ValueTask<CheckpointCommitMarker?> LoadMarkerAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
        => await LoadMarkerAsync(_commitLedger, commit, cancellationToken);

    private async ValueTask<CheckpointCommitMarker?> LoadMarkerAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await store.LoadAsync(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(commit.CommitId),
                cancellationToken);
            if (envelope is null)
                return null;

            var marker = _serializer.Deserialize<CheckpointCommitMarker>(envelope);
            if (!StringComparer.Ordinal.Equals(marker.CommitId, commit.CommitId))
                throw new InvalidOperationException($"Groundwork physical document identity collision detected for runtime checkpoint commit '{commit.CommitId}'.");
            return marker;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkRuntimeCheckpointWriterException($"Failed to load the runtime checkpoint commit marker for commit '{commit.CommitId}' and workflow execution '{commit.WorkflowExecutionId}'.", e);
        }
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> ApplyAtomicallyAsync(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (_commitLedger.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw new GroundworkRuntimeCheckpointWriterException($"The Groundwork document store cannot atomically commit runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}' because it does not support cross-unit atomic transactions.", new NotSupportedException($"Unsupported transaction boundary '{_commitLedger.TransactionBoundary}'."));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
                var transactionalStore = new GroundworkDocumentUnitOfWorkStore(_commitLedger, unitOfWork);
                var result = await ApplyStagedAsync(transactionalStore, commit, fingerprint, cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
                return result;
            }
            catch (FenceConcurrencyException)
            {
                if (await LoadMarkerAsync(commit, cancellationToken) is { } marker)
                    return ResolveReplay(commit, fingerprint, marker);
                if (await ExpectedFenceRemainsCurrentAsync(commit, cancellationToken))
                    continue;
                throw await NewStaleFenceExceptionAsync(commit, cancellationToken);
            }
            catch (CheckpointMarkerConcurrencyException)
            {
                var marker = await LoadMarkerAsync(commit, cancellationToken)
                    ?? throw new GroundworkRuntimeCheckpointWriterException(
                        $"Runtime checkpoint marker '{commit.CommitId}' conflicted but no committed marker could be reloaded.",
                        new InvalidOperationException("The create-only marker conflict could not be reconciled."));
                return ResolveReplay(commit, fingerprint, marker);
            }
            catch (DocumentCommitAcknowledgementUncertainException e)
            {
                using var reconciliation = new CancellationTokenSource(
                    CommitAcknowledgementReconciliationTimeout,
                    _timeProvider);
                try
                {
                    if (await LoadMarkerAsync(commit, reconciliation.Token) is { } marker)
                        return ResolveReplay(commit, fingerprint, marker);
                }
                catch (OperationCanceledException) when (reconciliation.IsCancellationRequested)
                {
                    // Preserve the uncertain-acknowledgement contract when the independently bounded
                    // reconciliation read itself times out. The provider may still have committed.
                }
                throw new GroundworkRuntimeCheckpointWriterException(
                    $"Runtime checkpoint '{commit.CommitId}' may have committed, but its durable acknowledgement could not be reconciled.",
                    e);
            }
            catch (Exception e) when (e is not OperationCanceledException and
                                      not RuntimeStaleFencingTokenException and
                                      not RuntimeSchedulerWorkConsumeConflictException and
                                      not RuntimeCheckpointReplayConflictException and
                                      not GroundworkRuntimeCheckpointWriterException)
            {
                throw new GroundworkRuntimeCheckpointWriterException($"Failed to atomically commit runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}'.", e);
            }
        }
    }

    // Stages every state change and the create-only commit marker for one checkpoint into an already-open unit-of-work
    // WITHOUT committing it. Shared by the single-commit path (which then commits this one checkpoint) and by the
    // group-commit coordinator (which folds many checkpoints into one unit-of-work before a single commit). Throws on
    // fence / create-only-marker concurrency (which poisons the unit-of-work) so the caller decides how to recover:
    // the single path retries or reconciles the marker; the group path rolls the shared transaction back and re-drives
    // each member individually. Persists byte-identical documents regardless of which path drives it.
    private async ValueTask<RuntimeCheckpointCommitStoreResult> ApplyStagedAsync(
        IDocumentStore transactionalStore,
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken,
        bool writeCommitMarker = true)
    {
        await ValidateAndTouchExpectedFenceAsync(transactionalStore, commit, cancellationToken);
        var stores = GroundworkApplyStores.Create(transactionalStore, _serializer, _accessContextAccessor);
        await ValidateAlterationJobTerminalChangeAsync(stores.AlterationStore, commit.StateChanges.AlterationJobTerminalChange, cancellationToken);
        await ValidateAndTouchTestScopesAsync(stores, commit, cancellationToken);
        await ApplyWorkflowExecutionStateChangeAsync(stores.WorkflowExecutionStateStore, commit.StateChanges.WorkflowExecution, cancellationToken);
        await ApplySchedulerStateChangeAsync(stores.SchedulerStateStore, commit.StateChanges.Scheduler, cancellationToken);
        await ApplyActivityExecutionStateChangesAsync(stores.ActivityExecutionStateStore, commit.StateChanges.ActivityExecutions, cancellationToken);
        await ApplyActivityExecutionInspectionChangesAsync(stores.ActivityExecutionInspectionWriter, commit.StateChanges.ActivityExecutionInspections, cancellationToken);
        await ApplyActivityExecutionHierarchyChangesAsync(stores.ActivityExecutionHierarchyWriter, commit.StateChanges.ActivityExecutionInspections, cancellationToken);
        await ApplyBookmarkStateChangesAsync(stores.BookmarkStateStore, commit.StateChanges.Bookmarks, cancellationToken);
        await ApplyDurableValueStateChangesAsync(stores.DurableValueStateStore, commit.StateChanges.DurableValues, cancellationToken);
        await ApplyIncidentStateChangesAsync(stores.IncidentStateStore, commit.StateChanges.Incidents, cancellationToken);
        await ApplyOperationalStateChangesAsync(stores.ExecutionLivenessStateStore, commit.StateChanges.Operational, cancellationToken);
        await ApplyActivityScopeCleanupsAsync(stores, commit.StateChanges.ActivityScopeCleanups, cancellationToken);
        await ApplyConsumedSchedulerWorkItemsAsync(stores.SchedulerWorkQueue, commit.StateChanges.ConsumedSchedulerWorkItems, cancellationToken);
        await ApplyWorkflowDispatchChangesAsync(stores.WorkflowDispatchStore, commit.StateChanges.WorkflowDispatches, cancellationToken);
        await ApplyWorkflowDispatchCancellationsAsync(stores.WorkflowDispatchStore, commit.StateChanges.WorkflowDispatchCancellations, cancellationToken);
        await ApplyAlterationJobTerminalChangeAsync(stores.AlterationStore, commit.StateChanges.AlterationJobTerminalChange, cancellationToken);
        await ApplyPostCommitOutboxAsync(stores.PostCommitOutboxStore, commit.StateChanges.PostCommitOutbox, cancellationToken);
        if (writeCommitMarker)
            await MarkCommittedAsync(transactionalStore, commit, fingerprint, cancellationToken);
        return new RuntimeCheckpointCommitStoreResult(OutboxIds(commit))
        {
            ConsumedSchedulerWorkItemIds = ConsumedIds(commit)
        };
    }

    private static DocumentCommitScope RuntimeCheckpointCommitScope() =>
        DocumentCommitScope.Of(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind,
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
            ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
            ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
            ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
            ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind,
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind);

    private static async ValueTask ValidateAndTouchTestScopesAsync(
        GroundworkApplyStores stores,
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        var execution = commit.StateChanges.WorkflowExecution?.State;
        if (execution is { TestScope: { } rootScope, ParentWorkflowExecutionId: null })
        {
            var existing = await stores.WorkflowExecutionStateStore.FindAsync(
                execution.WorkflowExecutionId,
                cancellationToken);
            if (existing is null)
                await stores.WorkflowTestScopeStore.AssertOpenAsync(rootScope, commit.Checkpoint.OccurredAt, cancellationToken);
        }

        foreach (var change in commit.StateChanges.WorkflowDispatches)
        {
            if (change.State.TestScope is not { } scope)
                continue;
            var existing = await stores.WorkflowDispatchStore.FindAsync(change.State.DispatchId, cancellationToken);
            if (existing is null)
                await stores.WorkflowTestScopeStore.AssertOpenAsync(scope, commit.Checkpoint.OccurredAt, cancellationToken);
        }
    }

    private async ValueTask ValidateAndTouchExpectedFenceAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ExpectedFence is not { } expectedFence)
            return;

        var operationalStateId = GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId);
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            GroundworkExecutionLivenessStateStore.DocumentIdentity(commit.WorkflowExecutionId, operationalStateId),
            cancellationToken);
        if (envelope is null)
            throw NewStaleFenceException(commit, currentToken: 0, RuntimeFencingRejectionReason.NoActiveLease);

        var document = _serializer.Deserialize<GroundworkExecutionLivenessStateStore.ExecutionLivenessStateDocument>(envelope);
        var state = document.State;
        var currentToken = ReadHighestIssuedToken(state);
        var current = state.ExecutionLease;
        if (current is null)
            throw NewStaleFenceException(commit, currentToken, RuntimeFencingRejectionReason.NoActiveLease);
        if (current.IsExpired(_timeProvider.GetUtcNow()))
            throw NewStaleFenceException(commit, currentToken, RuntimeFencingRejectionReason.ExpiredLease);
        if (!Matches(current, expectedFence))
            throw NewStaleFenceException(commit, currentToken, RuntimeFencingRejectionReason.StaleToken);

        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                envelope.DocumentKind,
                envelope.Id,
                envelope.SchemaVersion,
                envelope.ContentJson,
                envelope.Version),
            cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
            throw new FenceConcurrencyException();
        throw new InvalidOperationException($"Groundwork rejected execution-fence touch with status '{result.Status}'.");
    }

    private async ValueTask<bool> ExpectedFenceRemainsCurrentAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ExpectedFence is not { } expectedFence)
            return false;

        var store = new GroundworkExecutionLivenessStateStore(_commitLedger, _serializer);
        var loaded = await store.FindVersionedAsync(
            commit.WorkflowExecutionId,
            GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId),
            cancellationToken);
        return loaded?.State.ExecutionLease is { } current &&
               !current.IsExpired(_timeProvider.GetUtcNow()) &&
               Matches(current, expectedFence);
    }

    private async ValueTask<RuntimeStaleFencingTokenException> NewStaleFenceExceptionAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        var store = new GroundworkExecutionLivenessStateStore(_commitLedger, _serializer);
        var state = await store.FindAsync(
            commit.WorkflowExecutionId,
            GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId),
            cancellationToken);
        var currentToken = ReadHighestIssuedToken(state);
        var reason = state?.ExecutionLease is null
            ? RuntimeFencingRejectionReason.NoActiveLease
            : state.ExecutionLease.IsExpired(_timeProvider.GetUtcNow())
                ? RuntimeFencingRejectionReason.ExpiredLease
                : RuntimeFencingRejectionReason.StaleToken;
        return NewStaleFenceException(commit, currentToken, reason);
    }

    private static RuntimeStaleFencingTokenException NewStaleFenceException(
        RuntimeCheckpointCommit commit,
        long currentToken,
        RuntimeFencingRejectionReason reason) =>
        new(
            commit.WorkflowExecutionId,
            commit.ExpectedFence?.FencingToken ?? 0,
            currentToken,
            reason);

    private static bool Matches(RuntimeExecutionLease lease, RuntimeExecutionFence fence) =>
        StringComparer.Ordinal.Equals(lease.LeaseId, fence.LeaseId) &&
        StringComparer.Ordinal.Equals(lease.OwnerId, fence.OwnerId) &&
        lease.FencingToken == fence.FencingToken;

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

    private static RuntimeCheckpointCommitStoreResult ResolveReplay(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CheckpointCommitMarker marker)
    {
        if (!StringComparer.Ordinal.Equals(marker.Fingerprint, fingerprint))
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
        return new RuntimeCheckpointCommitStoreResult(marker.PendingPostCommitWorkIds)
        {
            ConsumedSchedulerWorkItemIds = marker.ConsumedSchedulerWorkItemIds ?? []
        };
    }

    private async ValueTask MarkCommittedAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var marker = new CheckpointCommitMarker(
            commit.CommitId,
            commit.WorkflowExecutionId,
            commit.Checkpoint.OccurredAt,
            ElsaRuntimeStorageManifest.CheckpointCommitCollection,
            fingerprint,
            OutboxIds(commit),
            ConsumedIds(commit));
        var (schemaVersion, content) = _serializer.Serialize(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, marker);
        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(commit.CommitId),
                schemaVersion,
                content,
                ExpectedVersion: 0),
            cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new CheckpointMarkerConcurrencyException();
        throw new InvalidOperationException($"Groundwork rejected runtime checkpoint commit marker '{commit.CommitId}' with status '{result.Status}'.");
    }

    private static async ValueTask ApplyWorkflowExecutionStateChangeAsync(
        IWorkflowExecutionStateStore store,
        RuntimeStateChange<WorkflowExecutionState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (stateChange is null)
            return;
        await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyPostCommitOutboxAsync(
        GroundworkRuntimePostCommitOutboxStore store,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> stateChanges,
        CancellationToken cancellationToken)
    {
        // Persisted in the same unit-of-work as the rest of the checkpoint: the continuation work is durable
        // atomically with the state that produced it, so it can never be silently lost on this path.
        foreach (var stateChange in stateChanges)
            await store.SavePendingAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ValidateAlterationJobTerminalChangeAsync(
        IWorkflowAlterationStore store,
        WorkflowAlterationJobTerminalChange? change,
        CancellationToken cancellationToken)
    {
        if (change is not null)
            await store.ValidateTerminalJobChangeAsync(change, cancellationToken);
    }

    private static async ValueTask ApplyAlterationJobTerminalChangeAsync(
        IWorkflowAlterationStore store,
        WorkflowAlterationJobTerminalChange? change,
        CancellationToken cancellationToken)
    {
        if (change is not null)
            await store.ApplyTerminalJobChangeAsync(change, cancellationToken);
    }

    private static async ValueTask ApplyWorkflowDispatchChangesAsync(
        IWorkflowDispatchStore store,
        IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyWorkflowDispatchCancellationsAsync(
        IWorkflowDispatchCancellationStore store,
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
            await store.ApplyCancellationAsync(request, cancellationToken);
    }

    private static async ValueTask ApplySchedulerStateChangeAsync(
        ISchedulerStateStore store,
        RuntimeStateChange<SchedulerState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (stateChange is null)
            return;
        await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityExecutionStateChangesAsync(
        IActivityExecutionStateStore store,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityExecutionInspectionChangesAsync(
        IActivityExecutionInspectionWriter writer,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await writer.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityExecutionHierarchyChangesAsync(
        IActivityExecutionHierarchyWriter writer,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            var projection = stateChange.State;
            if (!string.IsNullOrWhiteSpace(projection.ExecutionScopeId ?? projection.Provenance.ExecutionScopeId))
                await writer.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection), cancellationToken);
        }
    }

    private static async ValueTask ApplyBookmarkStateChangesAsync(
        IBookmarkStateStore store,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await store.DeleteAsync(stateChange.State.WorkflowExecutionId, stateChange.State.BookmarkId, cancellationToken);
                continue;
            }
            await store.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private static async ValueTask ApplyDurableValueStateChangesAsync(
        IDurableValueStateStore store,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await store.DeleteAsync(stateChange.State.WorkflowExecutionId, stateChange.State.DurableValueId, cancellationToken);
                continue;
            }
            await store.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private static async ValueTask ApplyIncidentStateChangesAsync(
        IIncidentStateStore store,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            // Append and Upsert both resolve to a durable write. Unlike the in-memory writer, a redelivered
            // Append is not an error: a crash may have applied the incident without writing the commit
            // marker, so re-applying the same commit must be idempotent. TryAdd-then-Save guarantees the
            // incident exists without throwing on a second pass.
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
            {
                var added = await store.TryAddAsync(stateChange.State, cancellationToken);
                if (!added)
                    await store.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }
            await store.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private static async ValueTask ApplyOperationalStateChangesAsync(
        IExecutionLivenessStateStore store,
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityScopeCleanupsAsync(
        GroundworkApplyStores stores,
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups,
        CancellationToken cancellationToken)
    {
        foreach (var cleanup in cleanups)
        {
            foreach (var bookmarkId in cleanup.BookmarkIds)
                await stores.BookmarkStateStore.DeleteAsync(cleanup.WorkflowExecutionId, bookmarkId, cancellationToken);
            foreach (var timerId in cleanup.TimerIds)
                await stores.DurableTimerStore.DeleteAsync(cleanup.WorkflowExecutionId, timerId, cancellationToken);
            foreach (var workItemId in cleanup.SchedulerWorkItemIds)
                await stores.SchedulerWorkQueue.DeleteAsync(cleanup.WorkflowExecutionId, workItemId, cancellationToken);
        }
    }

    private static async ValueTask ApplyConsumedSchedulerWorkItemsAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IReadOnlyCollection<ConsumedSchedulerWorkItem> consumed,
        CancellationToken cancellationToken)
    {
        // Fold the claimed work item's acknowledgement into this same unit-of-work (WU-1 / spec 105). The delete is
        // owner+token fence-checked, so a stale claimant's commit fails claim-lost here and the whole checkpoint rolls
        // back rather than deleting successor-owned work.
        foreach (var item in consumed)
        {
            var result = await schedulerWorkQueue.ConsumeClaimedAsync(item, cancellationToken);
            if (!result.Succeeded)
                throw new RuntimeSchedulerWorkConsumeConflictException(item.WorkflowExecutionId, item.WorkItemId);
        }
    }

    private static void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (stateChange is null)
            return;
        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");
        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }

    private static void ValidateSchedulerStateChange(RuntimeCheckpointCommit commit)
    {
        if (commit.StateChanges.Scheduler is null)
            return;
        var stateChange = commit.StateChanges.Scheduler;
        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project scheduler state '{RuntimeStateChangeOperation.Upsert}' changes.");
        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change StateId must match SchedulerState.WorkflowExecutionId.");
        if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
    }

    private static void ValidateActivityExecutionStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.ActivityExecutions)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project activity execution state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.Execution.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution state change StateId must match ActivityExecution.ActivityExecutionId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.Execution.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
            stateChange.State.EnsureValueFlowCompatible();
            stateChange.State.EnsureSupersessionCompatible();
            if (stateChange.State.ExecutionScopeId is not null &&
                stateChange.State.Provenance.ExecutionScopeId is not null &&
                !StringComparer.Ordinal.Equals(stateChange.State.ExecutionScopeId, stateChange.State.Provenance.ExecutionScopeId))
                throw new InvalidOperationException("Activity execution state ExecutionScopeId must match ActivitySchedulingProvenance.ExecutionScopeId when both are present.");
            if (stateChange.State.Attempt is not null &&
                stateChange.State.Provenance.Attempt is not null &&
                stateChange.State.Attempt != stateChange.State.Provenance.Attempt)
                throw new InvalidOperationException("Activity execution state Attempt must match ActivitySchedulingProvenance.Attempt when both are present.");
        }
    }

    private static void ValidateActivityExecutionInspectionChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.ActivityExecutionInspections)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project activity execution inspection '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution inspection state change StateId must match ActivityExecutionInspectionProjection.ActivityExecutionId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution inspection WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (stateChange.State.ExecutionScopeId is not null &&
                stateChange.State.Provenance.ExecutionScopeId is not null &&
                !StringComparer.Ordinal.Equals(stateChange.State.ExecutionScopeId, stateChange.State.Provenance.ExecutionScopeId))
                throw new InvalidOperationException("Activity execution inspection ExecutionScopeId must match ActivitySchedulingProvenance.ExecutionScopeId when both are present.");
            if (stateChange.State.Attempt is not null &&
                stateChange.State.Provenance.Attempt is not null &&
                stateChange.State.Attempt != stateChange.State.Provenance.Attempt)
                throw new InvalidOperationException("Activity execution inspection Attempt must match ActivitySchedulingProvenance.Attempt when both are present.");
        }
    }

    private static void ValidateBookmarkStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Bookmarks)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project bookmark state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.BookmarkId))
                throw new InvalidOperationException("Bookmark state change StateId must match BookmarkState.BookmarkId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Bookmark state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateDurableValueStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.DurableValues)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project durable value state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.DurableValueId))
                throw new InvalidOperationException("Durable value state change StateId must match DurableValueState.DurableValueId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Durable value state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateIncidentStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Append and not RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project incident state '{RuntimeStateChangeOperation.Append}' or '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.IncidentId))
                throw new InvalidOperationException("Incident state change StateId must match IncidentState.IncidentId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Incident state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateOperationalStateChanges(RuntimeCheckpointCommit commit)
    {
        var ownershipStateId = GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId);
        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match ExecutionLivenessState.OperationalStateId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (StringComparer.Ordinal.Equals(stateChange.State.OperationalStateId, ownershipStateId))
                throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
        }
    }

    private static void ValidateActivityScopeCleanups(RuntimeCheckpointCommit commit)
    {
        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        {
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, cleanup.WorkflowExecutionId))
                throw new InvalidOperationException("Activity scope cleanup WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (!cleanup.ActivityExecutionIds.Contains(cleanup.ExecutionScopeId, StringComparer.Ordinal))
                throw new InvalidOperationException("Activity scope cleanup must include its outer execution scope ID.");
        }
    }

    private static void ValidateConsumedSchedulerWorkItems(RuntimeCheckpointCommit commit)
    {
        foreach (var item in commit.StateChanges.ConsumedSchedulerWorkItems)
        {
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, item.WorkflowExecutionId))
                throw new InvalidOperationException("Consumed scheduler work item WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateWorkflowDispatchChanges(RuntimeCheckpointCommit commit)
    {
        var seen = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var stateChange in commit.StateChanges.WorkflowDispatches)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            {
                throw new InvalidOperationException(
                    $"The Groundwork checkpoint writer can only project workflow dispatch '{RuntimeStateChangeOperation.Upsert}' changes.");
            }

            WorkflowDispatchLifecycle.ValidateCheckpointOwnership(commit.WorkflowExecutionId, stateChange.State);
            if (seen.TryGetValue(stateChange.StateId, out var duplicate) &&
                !WorkflowDispatchLifecycle.RecordsEqual(duplicate, stateChange.State))
            {
                throw new InvalidOperationException(
                    $"Workflow dispatch '{stateChange.StateId}' occurs more than once with conflicting state in the same checkpoint.");
            }

            seen[stateChange.StateId] = stateChange.State;
        }
    }

    private static void ValidateWorkflowDispatchCancellations(RuntimeCheckpointCommit commit)
    {
        foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
        {
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, request.ParentWorkflowExecutionId))
            {
                throw new InvalidOperationException(
                    $"Workflow dispatch cancellation '{request.DispatchId}' must be committed by its parent workflow execution.");
            }
        }
    }

    private void EnsureAtomicBoundary(RuntimeCheckpointCommit commit)
    {
        if (_commitLedger.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
        {
            throw new GroundworkRuntimeCheckpointWriterException(
                $"The Groundwork document store cannot atomically prepare runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}' because it does not support cross-unit atomic transactions.",
                new NotSupportedException($"Unsupported transaction boundary '{_commitLedger.TransactionBoundary}'."));
        }
    }

    private void EnsureTenantScope(RuntimeCheckpointCommit commit)
    {
        if (commit.StateChanges.WorkflowExecution is { } workflowExecutionChange)
            _accessContextAccessor.Current.EnsureTenantScope(workflowExecutionChange.State.TenantId);
        foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
            _accessContextAccessor.Current.EnsureTenantScope(dispatch.State.TenantId);
    }

    private async ValueTask<VersionedLedger?> LoadLedgerAsync(
        IDocumentStore store,
        string commitId,
        CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            PreparationDocumentId(commitId),
            cancellationToken);
        if (envelope is null)
            return null;
        var document = _serializer.Deserialize<RuntimeCheckpointLedgerDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.Entry.CommitId, commitId))
            throw new InvalidOperationException($"Groundwork physical document identity collision detected for prepared runtime checkpoint '{commitId}'.");
        return new(document, envelope.Version);
    }

    private async ValueTask<VersionedCoordination?> LoadCoordinationAsync(
        IDocumentStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            CoordinationDocumentId(workflowExecutionId),
            cancellationToken);
        if (envelope is null)
            return null;
        var document = _serializer.Deserialize<RuntimeCheckpointCoordinationDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException($"Groundwork physical document identity collision detected for runtime checkpoint coordination '{workflowExecutionId}'.");
        return new(document, envelope.Version);
    }

    private async ValueTask SaveLedgerAsync(
        IDocumentStore store,
        RuntimeCheckpointLedgerDocument document,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = _serializer.Serialize(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            document);
        var result = await store.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            PreparationDocumentId(document.Entry.CommitId),
            schemaVersion,
            content,
            expectedVersion), cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
            throw new PreparedDocumentConcurrencyException();
        throw new InvalidOperationException($"Groundwork rejected logical checkpoint ledger '{document.Entry.CommitId}' with status '{result.Status}'.");
    }

    private async ValueTask SaveCoordinationAsync(
        IDocumentStore store,
        RuntimeCheckpointCoordinationDocument document,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = _serializer.Serialize(
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            document);
        var result = await store.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            CoordinationDocumentId(document.WorkflowExecutionId),
            schemaVersion,
            content,
            expectedVersion), cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
            throw new PreparedDocumentConcurrencyException();
        throw new InvalidOperationException($"Groundwork rejected checkpoint coordination '{document.WorkflowExecutionId}' with status '{result.Status}'.");
    }

    private async ValueTask<RuntimeCheckpointPreparationResult> ResolvePreparedReplayAsync(
        IDocumentStore store,
        VersionedLedger loaded,
        RuntimeCheckpointPrepareRequest request,
        RuntimeCheckpointPreparationPayloadEnvelope payload,
        CancellationToken cancellationToken)
    {
        var entry = loaded.Document.Entry;
        if (!StringComparer.Ordinal.Equals(entry.InputFingerprint, payload.PayloadSha256))
            return RuntimeCheckpointPreparationResult.Conflict();
        if (entry.Status is RuntimeLogicalCheckpointLedgerStatus.Committed or RuntimeLogicalCheckpointLedgerStatus.Skipped or RuntimeLogicalCheckpointLedgerStatus.Failed)
            return RuntimeCheckpointPreparationResult.Replay(ToToken(entry), entry.Receipt);
        _ = DecodePreparation(entry);
        if (entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared || !Equals(entry.ExpectedFence, request.Commit.ExpectedFence))
            return RuntimeCheckpointPreparationResult.Conflict();

        await ValidateAndTouchExpectedFenceAsync(store, request.Commit, cancellationToken);
        var coordination = await LoadCoordinationAsync(store, request.Commit.WorkflowExecutionId, cancellationToken);
        if (coordination is null ||
            coordination.Document.ReservedOrder < entry.Provenance.WorkflowCheckpointOrder ||
            coordination.Document.CommittedOrder >= entry.Provenance.WorkflowCheckpointOrder ||
            coordination.Document.ContextRevision != entry.ExpectedContextRevision)
            return RuntimeCheckpointPreparationResult.Conflict();

        var refreshed = entry with
        {
            ExpectedOrderRevision = coordination.Document.OrderRevision,
            ExpectedContextRevision = coordination.Document.ContextRevision
        };
        await SaveLedgerAsync(store, new RuntimeCheckpointLedgerDocument(refreshed), loaded.Version, cancellationToken);
        return RuntimeCheckpointPreparationResult.Replay(ToToken(refreshed), refreshed.Receipt);
    }

    private async ValueTask<IReadOnlyList<RuntimeLogicalCheckpointLedgerEntry>> LoadPreparedEntriesThroughAsync(
        IDocumentStore store,
        string workflowExecutionId,
        long throughWorkflowCheckpointOrder,
        CancellationToken cancellationToken)
    {
        var entries = new List<RuntimeLogicalCheckpointLedgerEntry>();
        var query = new RuntimeCheckpointPreparedQuery(
            workflowExecutionId,
            RuntimeCheckpointPreparedQuery.MaximumPageSize);
        while (true)
        {
            var page = await PagePreparedAsync(query, cancellationToken);
            foreach (var reservation in page.Reservations)
            {
                if (reservation.Provenance.WorkflowCheckpointOrder > throughWorkflowCheckpointOrder)
                    return entries;
                var loaded = await LoadLedgerAsync(store, reservation.CommitId, cancellationToken)
                    ?? throw new PreparedDocumentConcurrencyException();
                var entry = loaded.Document.Entry;
                if (entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared ||
                    !StringComparer.Ordinal.Equals(entry.WorkflowExecutionId, workflowExecutionId) ||
                    entry.Provenance.WorkflowCheckpointOrder != reservation.Provenance.WorkflowCheckpointOrder)
                    throw new PreparedDocumentConcurrencyException();
                entries.Add(entry);
            }
            if (page.NextCursor is null)
                return entries;
            query = query with { Cursor = page.NextCursor };
        }
    }

    private static bool IsAdoptionRequestWellFormed(RuntimeCheckpointPreparedAdoptionRequest request)
    {
        if (String.IsNullOrWhiteSpace(request.WorkflowExecutionId) ||
            request.TargetAuthorityFence is null ||
            request.Members is null ||
            request.Members.Count == 0 ||
            request.ThroughWorkflowCheckpointOrder < 1 ||
            !Enum.IsDefined(request.Route))
            return false;

        long previousOrder = 0;
        var commits = new HashSet<string>(StringComparer.Ordinal);
        var ledgerTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in request.Members)
        {
            if (member is null ||
                String.IsNullOrWhiteSpace(member.CommitId) ||
                String.IsNullOrWhiteSpace(member.LedgerToken) ||
                String.IsNullOrWhiteSpace(member.CanonicalInputFingerprint) ||
                String.IsNullOrWhiteSpace(member.CanonicalInputReference) ||
                member.WorkflowCheckpointOrder <= previousOrder ||
                member.OriginalOrderRevision < 0 ||
                member.OriginalContextRevision < 0 ||
                member.ExpectedAuthorityRevision < 1 ||
                !commits.Add(member.CommitId) ||
                !ledgerTokens.Add(member.LedgerToken))
                return false;
            previousOrder = member.WorkflowCheckpointOrder;
        }
        return previousOrder == request.ThroughWorkflowCheckpointOrder;
    }

    private static bool AdoptionScopeMatches(
        IReadOnlyList<RuntimeLogicalCheckpointLedgerEntry> entries,
        RuntimeCheckpointPreparedAdoptionRequest request)
    {
        if (entries.Count != request.Members.Count)
            return false;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var member = request.Members[index];
            if (entry.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared ||
                !StringComparer.Ordinal.Equals(entry.CommitId, member.CommitId) ||
                !StringComparer.Ordinal.Equals(entry.LedgerToken, member.LedgerToken) ||
                entry.Provenance.WorkflowCheckpointOrder != member.WorkflowCheckpointOrder ||
                !StringComparer.Ordinal.Equals(entry.InputFingerprint, member.CanonicalInputFingerprint) ||
                !StringComparer.Ordinal.Equals(entry.CanonicalInputReference, member.CanonicalInputReference) ||
                !Equals(entry.ExpectedFence, member.OriginalFence) ||
                entry.ExpectedOrderRevision != member.OriginalOrderRevision ||
                entry.ExpectedContextRevision != member.OriginalContextRevision ||
                !Equals(entry.RecoveryAuthority, member.RecoveryAuthority) ||
                (request.Route == RuntimeCheckpointRecoveryRoute.SourceBound && entry.RecoveryAuthority is null) ||
                (request.Route == RuntimeCheckpointRecoveryRoute.SourceFree && entry.RecoveryAuthority is not null))
                return false;
        }
        return true;
    }

    private static bool CanAdvanceAuthorityFence(
        IReadOnlyList<RuntimeLogicalCheckpointLedgerEntry> entries,
        RuntimeExecutionFence target)
    {
        var current = entries[0].CurrentAuthorityFence;
        var revision = entries[0].AuthorityRevision;
        if (entries.Any(entry =>
                !Equals(entry.CurrentAuthorityFence, current) ||
                entry.AuthorityRevision != revision))
            return false;
        return new RuntimeCheckpointAuthorityBinding(current, revision).CanAdvanceTo(target);
    }

    private static RuntimeCheckpointCommit CreatePreparedLedgerAuthorityCommit(
        string workflowExecutionId,
        RuntimeExecutionFence? targetAuthorityFence) =>
        new(
            $"prepared-ledger-authority:{workflowExecutionId}",
            new RuntimeCheckpoint(
                $"prepared-ledger-authority:{workflowExecutionId}",
                "PreparedLedgerAuthority",
                workflowExecutionId,
                DateTimeOffset.UnixEpoch,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>())
        {
            ExpectedFence = targetAuthorityFence
        };

    private static RuntimeCheckpointPreparedAdoptionReceipt AdoptionReceipt(
        RuntimeCheckpointPreparedAdoptionStatus status,
        RuntimeCheckpointPreparedAdoptionRequest request) =>
        new(
            status,
            request.WorkflowExecutionId,
            request.Route,
            request.ThroughWorkflowCheckpointOrder,
            request.TargetAuthorityFence,
            request.Members?.Select(member => member.CommitId).ToArray() ?? []);

    private static bool FoldMembersMatch(
        IReadOnlyList<VersionedLedger> loaded,
        RuntimeCheckpointPreparedFoldRequest request)
    {
        if (loaded.Count != request.Members.Count)
            return false;
        for (var index = 0; index < loaded.Count; index++)
        {
            var entry = loaded[index].Document.Entry;
            var member = request.Members[index];
            if (!StringComparer.Ordinal.Equals(entry.CommitId, member.CommitId) ||
                !TokenMatches(entry, member.Token) ||
                !Equals(entry.CurrentAuthorityFence, member.ExpectedCurrentAuthorityFence) ||
                entry.AuthorityRevision != member.ExpectedAuthorityRevision)
                return false;
            if (member.PreparedCommit is { } prepared &&
                (!Equals(prepared.CurrentAuthorityFence, member.ExpectedCurrentAuthorityFence) ||
                 prepared.AuthorityRevision != member.ExpectedAuthorityRevision ||
                 !StringComparer.Ordinal.Equals(prepared.VerifiedCommitFingerprint, RuntimeCheckpointCommitFingerprint.Compute(prepared.Commit)) ||
                 !StringComparer.Ordinal.Equals(prepared.Commit.WorkflowExecutionId, request.WorkflowExecutionId) ||
                 !Equals(prepared.Commit.Checkpoint.Provenance, member.Token.Provenance)))
                return false;
        }
        return request.RecoveryRoute != RuntimeCheckpointRecoveryRoute.SourceFree ||
               loaded.All(item => item.Document.Entry.RecoveryAuthority is null);
    }

    private static bool IsExactFoldScope(
        IReadOnlyList<RuntimeLogicalCheckpointLedgerEntry> entries,
        RuntimeCheckpointPreparedFoldRequest request)
    {
        if (entries.Count != request.Members.Count)
            return false;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var member = request.Members[index];
            if (!StringComparer.Ordinal.Equals(entry.CommitId, member.CommitId) ||
                entry.Provenance.WorkflowCheckpointOrder != member.WorkflowCheckpointOrder ||
                !TokenMatches(entry, member.Token))
                return false;
        }
        return true;
    }

    private static void ValidateCheckpointStateChanges(RuntimeCheckpointCommit commit)
    {
        ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
        ValidateSchedulerStateChange(commit);
        ValidateActivityExecutionStateChanges(commit);
        ValidateActivityExecutionInspectionChanges(commit);
        ValidateBookmarkStateChanges(commit);
        ValidateDurableValueStateChanges(commit);
        ValidateIncidentStateChanges(commit);
        ValidateOperationalStateChanges(commit);
        ValidateActivityScopeCleanups(commit);
        ValidateConsumedSchedulerWorkItems(commit);
        ValidateWorkflowDispatchChanges(commit);
        ValidateWorkflowDispatchCancellations(commit);
    }

    private RuntimeCheckpointPrepareRequest DecodePreparation(RuntimeLogicalCheckpointLedgerEntry entry) =>
        _preparationPayloadCodec.Decode(new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            entry.CanonicalInputPayload ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no canonical recovery input."),
            entry.InputFingerprint));

    private RuntimeCheckpointPreparedReservation ToPreparedReservation(RuntimeLogicalCheckpointLedgerEntry entry)
    {
        var envelope = new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            entry.CanonicalInputPayload ?? throw new InvalidOperationException($"Prepared checkpoint '{entry.CommitId}' has no canonical recovery input."),
            entry.InputFingerprint);
        _ = _preparationPayloadCodec.Decode(envelope);
        return new RuntimeCheckpointPreparedReservation(
            ToToken(entry),
            envelope,
            entry.Status,
            entry.CurrentAuthorityFence,
            entry.AuthorityRevision);
    }

    private static bool IsAfter(RuntimeLogicalCheckpointLedgerEntry entry, RuntimeCheckpointPreparedCursor cursor) =>
        entry.Provenance.WorkflowCheckpointOrder > cursor.WorkflowCheckpointOrder ||
        (entry.Provenance.WorkflowCheckpointOrder == cursor.WorkflowCheckpointOrder &&
         StringComparer.Ordinal.Compare(entry.CommitId, cursor.CommitId) > 0);

    private static bool RawCommitMatches(RuntimeCheckpointCommit prepared, RuntimeCheckpointCommit candidate) =>
        StringComparer.Ordinal.Equals(
            RuntimeCheckpointCommitFingerprint.ComputeInput(prepared),
            RuntimeCheckpointCommitFingerprint.ComputeInput(candidate));

    private static bool TokenMatches(RuntimeLogicalCheckpointLedgerEntry entry, RuntimeCheckpointPreparationToken token) =>
        entry.TerminalPreparationToken is { } terminal
            ? terminal.Equals(token)
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

    private static RuntimeCheckpointCommitStoreResult Conflict() =>
        new([]) { Status = RuntimeCheckpointCommitStoreStatus.Conflict };

    private static string ComputeLedgerToken(
        string commitId,
        string inputFingerprint,
        long order,
        long orderRevision,
        long contextRevision,
        RuntimeExecutionFence? fence)
    {
        var authority = JsonSerializer.Serialize(new
        {
            Domain = "elsa.runtime.checkpoint.preparation.groundwork.v1",
            CommitId = commitId,
            InputFingerprint = inputFingerprint,
            Order = order,
            OrderRevision = orderRevision,
            ContextRevision = contextRevision,
            Fence = fence
        });
        return $"groundwork:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(authority)))}";
    }

    internal static string PreparationDocumentId(string commitId) =>
        GroundworkPhysicalDocumentId.FromLogicalId($"prepared:{commitId}");

    internal static string CoordinationDocumentId(string workflowExecutionId) =>
        GroundworkPhysicalDocumentId.FromLogicalId($"coordination:{workflowExecutionId}");

    private sealed record CheckpointCommitMarker(
        string CommitId,
        string WorkflowExecutionId,
        DateTimeOffset OccurredAt,
        string Collection,
        string Fingerprint,
        IReadOnlyCollection<string> PendingPostCommitWorkIds,
        // Nullable + defaulted so markers written before WU-1 (spec 105) deserialize with no consumed ids.
        IReadOnlyCollection<string>? ConsumedSchedulerWorkItemIds = null);

    internal sealed record RuntimeCheckpointLedgerDocument(RuntimeLogicalCheckpointLedgerEntry Entry);

    internal sealed record RuntimeCheckpointCoordinationDocument(
        string WorkflowExecutionId,
        long ReservedOrder,
        long CommittedOrder,
        long OrderRevision,
        RuntimeExecutionContextSnapshot ExecutionContext,
        long ContextRevision)
    {
        public static RuntimeCheckpointCoordinationDocument Empty(string workflowExecutionId) =>
            new(workflowExecutionId, 0, 0, 0, RuntimeExecutionContextSnapshot.Empty, 0);
    }

    private sealed record VersionedLedger(RuntimeCheckpointLedgerDocument Document, long Version);
    private sealed record VersionedCoordination(RuntimeCheckpointCoordinationDocument Document, long Version);

    private sealed class FenceConcurrencyException : Exception
    {
    }

    private sealed class CheckpointMarkerConcurrencyException : Exception
    {
    }

    private sealed class PreparedDocumentConcurrencyException : Exception
    {
    }

    private sealed record GroundworkApplyStores(
        IWorkflowExecutionStateStore WorkflowExecutionStateStore,
        ISchedulerStateStore SchedulerStateStore,
        IActivityExecutionStateStore ActivityExecutionStateStore,
        IActivityExecutionInspectionWriter ActivityExecutionInspectionWriter,
        IActivityExecutionHierarchyWriter ActivityExecutionHierarchyWriter,
        IBookmarkStateStore BookmarkStateStore,
        IDurableValueStateStore DurableValueStateStore,
        IIncidentStateStore IncidentStateStore,
        IExecutionLivenessStateStore ExecutionLivenessStateStore,
        GroundworkWorkflowAlterationStore AlterationStore,
        GroundworkWorkflowTestScopeStore WorkflowTestScopeStore,
        GroundworkWorkflowDispatchStore WorkflowDispatchStore,
        GroundworkRuntimePostCommitOutboxStore PostCommitOutboxStore,
        IDurableTimerStore DurableTimerStore,
        IWorkflowSchedulerWorkQueue SchedulerWorkQueue)
    {
        public static GroundworkApplyStores Create(
            IDocumentStore store,
            IGroundworkRuntimeDocumentSerializer serializer,
            IPersistenceAccessContextAccessor accessContextAccessor) =>
            new(
                new GroundworkWorkflowExecutionStateStore(store, serializer, accessContextAccessor),
                new GroundworkSchedulerStateStore(store, serializer),
                new GroundworkActivityExecutionStateStore(store, serializer),
                new GroundworkActivityExecutionInspectionStore(store, serializer),
                new GroundworkActivityExecutionHierarchyStore(store, serializer),
                new GroundworkBookmarkStateStore(store, serializer),
                new GroundworkDurableValueStateStore(store, serializer),
                new GroundworkIncidentStateStore(store, serializer),
                new GroundworkExecutionLivenessStateStore(store, serializer),
                new GroundworkWorkflowAlterationStore(store, serializer, accessContextAccessor),
                new GroundworkWorkflowTestScopeStore(store, serializer, accessContextAccessor),
                new GroundworkWorkflowDispatchStore(store, serializer, accessContextAccessor),
                new GroundworkRuntimePostCommitOutboxStore(store, serializer),
                new GroundworkDurableTimerStore(store, serializer),
                new GroundworkWorkflowSchedulerWorkQueue(store, serializer));
    }

}
