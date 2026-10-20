namespace Elsa.Etl.Core.Runtime;

/// <summary>Owns the short atomic transitions of one scoped finite ETL execution.</summary>
/// <remarks>
/// Implementations snapshot caller-owned values, use a trusted clock and configured lease durations, and
/// recheck the persisted fence, expiry and terminal state inside every guarded transaction; reserving or
/// accepting work also compares the expected cursor. Renewal and release follow ownership across cursor advancement.
/// A lease is authority to attempt work, not proof that another task stopped. Physical chunks are not accepted output.
/// </remarks>
public interface IEtlRuntimeStore
{
    /// <summary>Positive maximum accepted take for each list call; consumers may compose bounded keyset pages.</summary>
    int MaximumPageSize { get; }

    ValueTask<EtlAdmissionResult> AdmitAsync(EtlRunAdmission admission, CancellationToken cancellationToken = default);
    /// <summary>Durably returns a safe admission refusal to the waiting parent, even when no binding can be resolved.</summary>
    ValueTask<EtlTerminalSettlementResult> RejectAdmissionAsync(EtlInvocation invocation, EtlRunDiagnostic diagnostic, CancellationToken cancellationToken = default);
    ValueTask<EtlRunView?> FindRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default);
    ValueTask<EtlPreparationLease?> ClaimPreparationAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> RenewPreparationAsync(EtlPreparationLease lease, CancellationToken cancellationToken = default);
    /// <summary>Commits verified input and ranges; empty input atomically settles success and its terminal outbox.</summary>
    ValueTask<EtlStoreStatus> CommitPreparationAsync(EtlPreparationLease lease, EtlPreparedRun prepared, CancellationToken cancellationToken = default);
    /// <summary>Claims work in the exact scope; a null run ID selects across runnable runs in that scope.</summary>
    ValueTask<EtlPartitionLease?> ClaimPartitionAsync(EtlScope scope, string? runId, string workerId, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> RenewPartitionAsync(EtlPartitionLease lease, CancellationToken cancellationToken = default);
    /// <summary>Relinquishes only the current claim; owed reservations and quota survive. The old fence becomes invalid.</summary>
    ValueTask<EtlStoreStatus> ReleasePartitionAsync(EtlPartitionLease lease, CancellationToken cancellationToken = default);
    ValueTask<EtlReservationResult> ReserveBatchAsync(EtlPartitionLease lease, EtlBatchDescriptor batch, CancellationToken cancellationToken = default);
    /// <summary>Accepts effects/cursor/quota atomically; final work also settles the run and outbox in that transaction.</summary>
    ValueTask<EtlAcceptanceResult> AcceptBatchAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance, CancellationToken cancellationToken = default);
    ValueTask<EtlTerminalSettlementResult> SettleTerminalAsync(EtlTerminalProposal proposal, CancellationToken cancellationToken = default);
    ValueTask<EtlTerminalSettlementResult> CancelAsync(EtlInvocation invocation, CancellationToken cancellationToken = default);
    ValueTask<EtlDeliveryLease?> ClaimDeliveryAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> AcknowledgeDeliveryAsync(EtlDeliveryLease lease, EtlDeliveryDisposition disposition, CancellationToken cancellationToken = default);
    /// <summary>Lists exact-scope runs in a stable total identity order. The supplied run ID is an exclusive boundary in that same provider order, not necessarily raw-string order.</summary>
    /// <remarks>Return up to take rows; a short page means the current query is exhausted. Consumers must preserve this order. Pages are live views, not a frozen snapshot.</remarks>
    ValueTask<IReadOnlyList<EtlRunView>> ListRunsAsync(EtlScope scope, string? afterRunId, int take, CancellationToken cancellationToken = default);
    /// <summary>Lists exact-scope run partitions by ascending ordinal, strictly after the supplied ordinal (-1 starts the traversal).</summary>
    /// <remarks>Return up to take rows; a short page means the current query is exhausted. Retained claim metadata does not prove worker liveness.</remarks>
    ValueTask<IReadOnlyList<EtlPartitionView>> ListPartitionsAsync(EtlScope scope, string runId, int afterPartitionOrdinal, int take, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<EtlManifestEntry>> ListManifestAsync(EtlScope scope, string runId, EtlManifestPosition? after, int take, CancellationToken cancellationToken = default);
}

/// <summary>Persisted transition outcomes; a replay never repeats its accounting effect.</summary>
public enum EtlStoreStatus { Applied, Replay, Conflict, NotFound, LeaseLost, BudgetExceeded, Terminal, Invalid }
public enum EtlRunStatus { Preparing, Running, Succeeded, BusinessError, Faulted, Cancelled }
public enum EtlTerminalOutcome { Succeeded, BusinessError, Faulted, Cancelled }
public enum EtlOutputRoute { Main, Error }
/// <summary>Proof that native delivery responsibility ended. Merely queueing a resume is insufficient.</summary>
public enum EtlDeliveryDisposition { Consumed, AlreadyConsumed, ProvenParentTerminal }

/// <summary>A host-approved immutable binding revision. Paths and credentials are never part of this value.</summary>
public sealed record EtlBindingRevision(string Alias, string Revision, string Handle, string? ExpectedSha256);
public sealed record EtlRunBindings(EtlBindingRevision Source, EtlBindingRevision Lookup, EtlBindingRevision Output, EtlBindingRevision Errors);

/// <summary>The exact address retained from an actual committed native bookmark, never authored input.</summary>
public sealed record EtlNativeResultAddress(
    int FormatVersion,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableId,
    string ExecutableHash,
    string NodeId,
    string BookmarkId,
    string RegistrationId,
    string ResumeTargetId,
    string StimulusType,
    string StimulusHash,
    string PayloadTypeAlias,
    int PayloadSchemaVersion,
    string DeduplicationPolicy,
    string ProviderId)
{
    public const int CurrentFormatVersion = 1;
    public const string CompletionProviderId = "elsa.etl.completion.v1";
}

/// <summary>One immutable invocation identity, including its executable, frozen address and chosen host revisions.</summary>
/// <remarks>RunId is the native invocation's stable identifier, reused by start and cancellation replay.</remarks>
public sealed record EtlInvocation(EtlScope Scope, string RunId, EtlArtifact Artifact, EtlNativeResultAddress ResultAddress);
/// <summary>Chosen host revisions are fixed by admission; cancellation can precede their resolution.</summary>
public sealed record EtlRunAdmission(EtlInvocation Invocation, EtlRunBindings Bindings);
public sealed record EtlAdmissionResult(EtlStoreStatus Status, EtlRunView? Run);

/// <summary>A verified immutable object in the host-approved ETL object store, addressed without raw paths.</summary>
public sealed record EtlObjectReference(string Key, string ContentHash, long ByteLength);
/// <summary>Durable preparation metadata; lookup dictionaries and source records are rebuilt rather than persisted here.</summary>
public sealed record EtlPreparedRun(int FormatVersion, EtlObjectReference Source, EtlObjectReference Lookup, EtlObjectReference SourceIndex, long SourceRecordCount)
{
    public const int CurrentFormatVersion = 1;
}
public sealed record EtlPreparationLease(EtlScope Scope, string RunId, string Owner, long Fence, DateTimeOffset ExpiresAt);
public sealed record EtlPartitionLease(EtlScope Scope, string RunId, int PartitionOrdinal, string Owner, long Fence,
    DateTimeOffset ExpiresAt, long StartOrdinal, long EndOrdinalExclusive, long NextOrdinal);

/// <summary>A deterministic input descriptor, measured before dispatch and independent of worker, attempt and fence.</summary>
public sealed record EtlBatchDescriptor(string BatchId, long StartOrdinal, long EndOrdinalExclusive, long InputBytes, string InputHash);
/// <summary>An owed logical reservation; a retry retains its identity and both quota charges.</summary>
public sealed record EtlBatchReservation(EtlBatchDescriptor Batch, bool Settled);
public sealed record EtlReservationResult(EtlStoreStatus Status, EtlBatchReservation? Reservation);
/// <summary>One complete physical route effect, which becomes authoritative only through acceptance.</summary>
public sealed record EtlOutputEffect(EtlOutputRoute Route, EtlObjectReference Chunk, long RecordCount);
public sealed record EtlBatchAcceptance(EtlBatchDescriptor Batch, long AcceptedRecords, long RejectedRecords, long FilteredRecords, IReadOnlyList<EtlOutputEffect> Effects);
public sealed record EtlAcceptanceResult(EtlStoreStatus Status, EtlPartitionLease? Lease);

/// <summary>A safe bounded diagnostic. It must not contain source values, paths, credentials or exception text.</summary>
public sealed record EtlRunDiagnostic(string Code, long? RecordOrdinal = null, string? Field = null);
/// <summary>Success requires all work settled; failure requires exactly one current unexpired preparation or partition claim.</summary>
public sealed record EtlTerminalProposal(EtlScope Scope, string RunId, EtlTerminalOutcome Outcome, EtlRunDiagnostic? Diagnostic,
    string? PreparationOwner = null, long? PreparationFence = null, int? PartitionOrdinal = null, string? PartitionOwner = null, long? PartitionFence = null);
public sealed record EtlTerminalResult(string ResultId, EtlTerminalOutcome Outcome, long AcceptedRecords, long RejectedRecords,
    long FilteredRecords, EtlRunDiagnostic? Diagnostic, DateTimeOffset SettledAt);
public sealed record EtlTerminalSettlementResult(EtlStoreStatus Status, EtlTerminalResult? Result);
/// <summary>Durable result delivery; the same address and terminal payload identity are used on every retry.</summary>
public sealed record EtlDeliveryLease(EtlScope Scope, string RunId, string DeliveryId, string Owner, long Fence,
    DateTimeOffset ExpiresAt, EtlNativeResultAddress Address, EtlTerminalResult Result, string PayloadHash);

/// <summary>Bounded inspection of one run. Manifest entries and partitions are separately paginated.</summary>
public sealed record EtlRunView(EtlInvocation Invocation, EtlRunBindings? Bindings, EtlRunStatus Status, EtlPreparedRun? Prepared,
    long OutstandingRecords, long OutstandingBytes, long AcceptedRecords, long RejectedRecords, long FilteredRecords,
    EtlTerminalResult? TerminalResult);
public sealed record EtlPartitionView(int PartitionOrdinal, long StartOrdinal, long EndOrdinalExclusive, long NextOrdinal,
    string? Owner, long Fence, DateTimeOffset? ExpiresAt, EtlBatchReservation? OwedBatch);
public sealed record EtlManifestPosition(int PartitionOrdinal, long BatchStartOrdinal, EtlOutputRoute Route);
public sealed record EtlManifestEntry(EtlManifestPosition Position, string BatchId, EtlOutputEffect Effect, DateTimeOffset AcceptedAt);
