using System.Text.Json.Serialization;

namespace Elsa.Etl.Core.Runtime;

/// <summary>Optional typed persistence capability for connector-format ETL runs, finite or continuous.</summary>
/// <remarks>
/// Shares run identity, accounting and terminal delivery with <see cref="IEtlRuntimeStore"/>. Existing
/// file-only providers need not implement this capability. Pins and shard identities are captured once;
/// page and file positions never share numeric sentinels. Preparation renewal, cancellation and terminal
/// settlement remain on <see cref="IEtlRuntimeStore"/>. Leases authorize durable transitions, not
/// exclusive physical execution or reversal of effects. Caller-owned collections must be snapshotted.
/// BudgetExceeded means capacity may become available after other outstanding work progresses; callers yield.
/// PermanentBudgetExceeded means the transition cannot fit even after releasable capacity is returned;
/// callers settle a safe diagnostic through the common fenced terminal path, preserving unaccepted evidence.
/// </remarks>
public interface IEtlConnectorRuntimeStore
{
    ValueTask<EtlConnectorAdmissionResult> AdmitConnectorAsync(EtlConnectorRunAdmission admission, CancellationToken cancellationToken = default);
    ValueTask<EtlConnectorRunView?> FindConnectorRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default);
    ValueTask<EtlPreparationLease?> ClaimConnectorPreparationAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default);
    /// <summary>Commits verified preparation and makes the run's partitions claimable.</summary>
    /// <remarks>A file input with zero records has no partition that could complete the run, so the same transaction
    /// settles it as success with its terminal result and owed native delivery. An identical replay returns Replay
    /// without settling again.</remarks>
    ValueTask<EtlStoreStatus> CommitConnectorPreparationAsync(EtlPreparationLease lease, EtlConnectorPreparedRun prepared, CancellationToken cancellationToken = default);
    ValueTask<EtlConnectorPartitionLease?> ClaimConnectorPartitionAsync(EtlScope scope, string? runId, string workerId, CancellationToken cancellationToken = default);
    /// <summary>Reserves source capacity and records a bounded permit before issuing a GET.</summary>
    /// <remarks>Only a returned permit authorizes transport. Retry and replacement preserve the original request identity.</remarks>
    ValueTask<EtlConnectorHttpSourceFetchResult> BeginConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        string requestId, CancellationToken cancellationToken = default);
    /// <summary>Records an issued source attempt's non-success or uncertain outcome without advancing input.</summary>
    ValueTask<EtlConnectorHttpSourceFetchResult> CompleteConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome, CancellationToken cancellationToken = default);
    /// <summary>Retains a verified capture from its exact historical permit under current ownership.</summary>
    /// <remarks>The caller verifies the immutable cache bytes. Capture does not attach or advance the page.</remarks>
    ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page, DateTimeOffset responseReceivedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Retains one verified response for the expected request under current partition ownership.</summary>
    /// <remarks>The caller verifies the immutable body and records its capture first. New attachments require matching capture evidence. RequestHash is produced by
    /// <see cref="EtlRuntimeIdentity.HttpRequestHash"/>. Null continuation ends the shard; empty is invalid.
    /// Replays do not charge retained bytes again. Use the returned position for subsequent operations.</remarks>
    ValueTask<EtlConnectorPositionResult> AttachHttpPageAsync(EtlConnectorPartitionLease lease, EtlRetainedHttpPage page, CancellationToken cancellationToken = default);
    /// <summary>Advances only a retained empty page; nonempty pages require batch acceptance.</summary>
    /// <remarks>In a continuous run a moving empty page makes the next request wait one poll interval, also while a completion drains.</remarks>
    ValueTask<EtlConnectorPositionResult> AdvanceEmptyHttpPageAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default);
    /// <summary>Records a continuous run's caught-up response (no records, no cursor movement) from the live latest attempt.</summary>
    /// <remarks>
    /// Applied clears the request's fetch journal, releases its input and source-metadata reservations, retains no page
    /// and schedules the partition's next poll one interval later, or at once when completion was requested after the
    /// attempt started. Final means completion was requested at or before the attempt started: nothing changes and the
    /// caller retains the response as the shard's final page instead. Providers without continuous support return Invalid.
    /// </remarks>
    ValueTask<EtlHttpCaughtUpResult> RecordConnectorHttpCaughtUpAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt attempt, DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EtlHttpCaughtUpResult(EtlStoreStatus.Invalid, false));
    /// <summary>Durably requests completion of a continuous run and makes its idle partitions due at once.</summary>
    /// <remarks>Retry waits keep their time. The first terminal outcome still wins. A finite run reports NotContinuous whether or not
    /// it has settled; Terminal is only for a settled continuous run. Providers without continuous support report NotContinuous.</remarks>
    ValueTask<EtlRunCompletionOutcome> RequestConnectorRunCompletionAsync(EtlScope scope, string runId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(EtlRunCompletionOutcome.NotContinuous);
    /// <summary>Reserves measured input at the current position. Replacement workers inherit the same owed batch and quota charges.</summary>
    ValueTask<EtlConnectorReservationResult> ReserveConnectorBatchAsync(EtlConnectorPartitionLease lease, EtlConnectorBatchDescriptor batch, CancellationToken cancellationToken = default);
    /// <summary>Retains the complete verified transformation output before any route is dispatched.</summary>
    /// <remarks>The caller verifies immutable bodies before retention. Identical replay does not recharge output.
    /// Call before dispatch, including recovered output: a replay also reserves any missing acceptance capacity for migrated checkpoints.
    /// Retention neither acknowledges delivery nor advances input.</remarks>
    ValueTask<EtlConnectorOutputResult> RetainConnectorBatchOutputAsync(EtlConnectorPartitionLease lease, string batchId, EtlConnectorBatchOutput output, CancellationToken cancellationToken = default);
    /// <summary>Allocates a bounded attempt before transport. Conflict with retained Waiting state means not yet eligible.</summary>
    /// <remarks>Only a returned permit authorizes sending. Identical begin replay can return the original live permit;
    /// the caller must not dispatch it concurrently or interpret it as proof that an earlier send did not happen.
    /// A replacement or expired in-flight attempt is first recorded as uncertain and delayed; it is not immediately resent.</remarks>
    ValueTask<EtlConnectorHttpDeliveryResult> BeginConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease, string batchId,
        EtlOutputRoute route, string requestId, CancellationToken cancellationToken = default);
    /// <summary>Records an exact allocated attempt's synchronous outcome without advancing input.</summary>
    ValueTask<EtlConnectorHttpDeliveryResult> CompleteConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease, string batchId,
        EtlOutputRoute route, EtlHttpDeliveryAttempt attempt, EtlHttpDeliveryResponse response, CancellationToken cancellationToken = default);
    /// <summary>Records a verified immutable file publication matching the retained route body and destination key.</summary>
    /// <remarks>The caller verifies actual published bytes before recording; this operation does not advance input.</remarks>
    ValueTask<EtlStoreStatus> RecordConnectorFilePublicationAsync(EtlConnectorPartitionLease lease, string batchId,
        EtlOutputRoute route, EtlObjectReference publication, CancellationToken cancellationToken = default);
    /// <summary>Accepts all retained route evidence, advances input and releases its reservation exactly once.</summary>
    /// <remarks>Final work also commits the terminal result/outbox. A matching replay never repeats accounting.</remarks>
    ValueTask<EtlConnectorPositionResult> AcceptConnectorBatchAsync(EtlConnectorPartitionLease lease, string batchId,
        CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> RenewConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> ReleaseConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default);
    /// <summary>
    /// The high watermark of the last completed run for a SQL source checkpoint, or null when none completed (spec 1199). A
    /// store raises it only in the transaction that completes a run that routed every record: one settled as succeeded, or as
    /// business error with <c>etl.rows-rejected</c>. A run settled by any other failure, including a rejected delivery,
    /// leaves it unchanged.
    /// </summary>
    ValueTask<long?> FindConnectorSourceWatermarkAsync(EtlScope scope, EtlConnectorSourceWatermarkKey key,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<long?>(null);

}

/// <remarks>Append only: identities hash the integer value (spec 1197).</remarks>
public enum EtlConnectorKind { Csv, Http, Jsonl, Sql, RabbitMq }

/// <summary>A host-resolved connector revision; paths, endpoint URLs and credentials are excluded.</summary>
public sealed record EtlConnectorBindingRevision(EtlConnectorKind Kind, EtlBindingRevision Revision);
/// <summary>Immutable admission pins; HTTP shard IDs refer to seeds in the pinned source binding.</summary>
public sealed record EtlConnectorRunBindings(EtlConnectorBindingRevision Source, EtlConnectorBindingRevision? Lookup,
    EtlConnectorBindingRevision Main, EtlConnectorBindingRevision Errors, IReadOnlyList<string> HttpShardIds);
public sealed record EtlConnectorRunAdmission(EtlInvocation Invocation, EtlConnectorRunBindings Bindings);
public sealed record EtlConnectorAdmissionResult(EtlStoreStatus Status, EtlConnectorRunView? Run);

/// <summary>Verified source/index snapshot metadata for a connector pipeline with a CSV or JSON Lines file source.</summary>
/// <remarks>The name predates JSON Lines input; the shape is format-neutral. For JSON Lines, index entry 0 is a sentinel covering only a byte order mark.</remarks>
public sealed record EtlPreparedCsvInput(EtlObjectReference Source, EtlObjectReference Index, long RecordCount);
/// <summary>Optional inputs prepared before partitions become runnable; absence is explicit.</summary>
public sealed record EtlConnectorPreparedRun(EtlPreparedCsvInput? File, EtlObjectReference? Lookup)
{
    /// <summary>
    /// The highest watermark in a SQL source's snapshot (spec 1199), raised into the source checkpoint when the run completes;
    /// null when the source has no watermark column or the snapshot has no row with a non-null watermark. Omitted from JSON
    /// when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceWatermark { get; init; }
}

/// <summary>Identifies a SQL source's watermark checkpoint within a scope (spec 1199).</summary>
/// <param name="DefinitionId">The ETL definition the runs belong to; its versions share the checkpoint.</param>
/// <param name="SourceAlias">The source binding alias.</param>
/// <param name="SourceHandle">
/// The pinned, secret-free handle of the source binding, so pointing the alias at another database or user starts again
/// from every row rather than filtering the new source by the old checkpoint.
/// </param>
/// <param name="Table">The source table.</param>
/// <param name="Column">The watermark column.</param>
public sealed record EtlConnectorSourceWatermarkKey(string DefinitionId, string SourceAlias, string SourceHandle, string Table, string Column);
public sealed record EtlConnectorRunView(EtlRunView Run, EtlConnectorRunBindings Bindings,
    EtlConnectorPreparedRun? Prepared, long RetainedInputBytes);

/// <summary>A typed connector input position. Consumers must handle only supported concrete variants.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(EtlConnectorFilePosition), "file")]
[JsonDerivedType(typeof(EtlConnectorHttpPosition), "http")]
public abstract record EtlConnectorPosition;
public sealed record EtlConnectorFilePosition(long StartOrdinal, long EndOrdinalExclusive, long NextOrdinal) : EtlConnectorPosition;
/// <summary>A request within a pinned shard. Continuation is protected runtime data, never an inspection label.</summary>
public sealed record EtlHttpRequest(string ShardId, string? Continuation);
/// <summary>One verified immutable HTTP response page bound to the request that produced it.</summary>
public sealed record EtlRetainedHttpPage(long PageSequence, string RequestHash, EtlObjectReference Body,
    int RecordCount, string? NextContinuation);
public sealed record EtlConnectorHttpPosition(long PageSequence, int NextItem, EtlHttpRequest Request,
    EtlRetainedHttpPage? Page, bool Exhausted) : EtlConnectorPosition;
public sealed record EtlConnectorPartitionLease(EtlScope Scope, string RunId, int PartitionOrdinal, string Owner,
    long Fence, DateTimeOffset ExpiresAt, EtlConnectorPosition Position)
{
    /// <summary>Retained responsibility returned on acquisition; null only when no batch is owed.</summary>
    public EtlConnectorBatchReservation? OwedBatch { get; init; }

    /// <summary>
    /// The caught-up polls this continuous shard had recorded when it was claimed; 0 for every other shard. Above 0 it
    /// is part of the page-cache key, so a stalled attempt's late page never lands under a later poll's key (spec 1196).
    /// </summary>
    public long PollGeneration { get; init; }
}

/// <summary>Authoritative position after an applied or identical replayed transition; no lease on refusal.</summary>
public sealed record EtlConnectorPositionResult(EtlStoreStatus Status, EtlConnectorPartitionLease? Lease);
/// <summary>The result of recording a caught-up response; Final asks the caller to retain it as the shard's last page.</summary>
public sealed record EtlHttpCaughtUpResult(EtlStoreStatus Status, bool Final);
/// <summary>The outcome of a completion request for a connector run.</summary>
public enum EtlRunCompletionOutcome { Accepted, AlreadyRequested, Terminal, NotContinuous, NotFound }
