using System.Text.Json.Serialization;

namespace Elsa.Etl.Core.Runtime;

/// <summary>Optional typed persistence capability for connector-format finite ETL runs.</summary>
/// <remarks>
/// Shares run identity, accounting and terminal delivery with <see cref="IEtlRuntimeStore"/>. Existing
/// file-only providers need not implement this capability. Pins and shard identities are captured once;
/// page and file positions never share numeric sentinels. Preparation renewal, cancellation and terminal
/// settlement remain on <see cref="IEtlRuntimeStore"/>. Leases authorize durable transitions, not
/// exclusive physical execution or reversal of effects. Caller-owned collections must be snapshotted.
/// </remarks>
public interface IEtlConnectorRuntimeStore
{
    ValueTask<EtlConnectorAdmissionResult> AdmitConnectorAsync(EtlConnectorRunAdmission admission, CancellationToken cancellationToken = default);
    ValueTask<EtlConnectorRunView?> FindConnectorRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default);
    ValueTask<EtlPreparationLease?> ClaimConnectorPreparationAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> CommitConnectorPreparationAsync(EtlPreparationLease lease, EtlConnectorPreparedRun prepared, CancellationToken cancellationToken = default);
    ValueTask<EtlConnectorPartitionLease?> ClaimConnectorPartitionAsync(EtlScope scope, string? runId, string workerId, CancellationToken cancellationToken = default);
    /// <summary>Retains one verified response for the expected request under current partition ownership.</summary>
    /// <remarks>The caller verifies the immutable body first. RequestHash is produced by
    /// <see cref="EtlRuntimeIdentity.HttpRequestHash"/>. Null continuation ends the shard; empty is invalid.
    /// Replays do not charge retained bytes again. Use the returned position for subsequent operations.</remarks>
    ValueTask<EtlConnectorPositionResult> AttachHttpPageAsync(EtlConnectorPartitionLease lease, EtlRetainedHttpPage page, CancellationToken cancellationToken = default);
    /// <summary>Advances only a retained empty page; nonempty pages require batch acceptance.</summary>
    ValueTask<EtlConnectorPositionResult> AdvanceEmptyHttpPageAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default);
    /// <summary>Reserves measured input at the current position. Replacement workers inherit the same owed batch and quota charges.</summary>
    ValueTask<EtlConnectorReservationResult> ReserveConnectorBatchAsync(EtlConnectorPartitionLease lease, EtlConnectorBatchDescriptor batch, CancellationToken cancellationToken = default);
    /// <summary>Retains the complete verified transformation output before any route is dispatched.</summary>
    /// <remarks>The caller verifies immutable bodies before retention. Identical replay does not charge again.
    /// Retention neither acknowledges delivery nor advances input.</remarks>
    ValueTask<EtlConnectorOutputResult> RetainConnectorBatchOutputAsync(EtlConnectorPartitionLease lease, string batchId, EtlConnectorBatchOutput output, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> RenewConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default);
    ValueTask<EtlStoreStatus> ReleaseConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default);

}

public enum EtlConnectorKind { Csv, Http, Jsonl }

/// <summary>A host-resolved connector revision; paths, endpoint URLs and credentials are excluded.</summary>
public sealed record EtlConnectorBindingRevision(EtlConnectorKind Kind, EtlBindingRevision Revision);
/// <summary>Immutable admission pins; HTTP shard IDs refer to seeds in the pinned source binding.</summary>
public sealed record EtlConnectorRunBindings(EtlConnectorBindingRevision Source, EtlConnectorBindingRevision? Lookup,
    EtlConnectorBindingRevision Main, EtlConnectorBindingRevision Errors, IReadOnlyList<string> HttpShardIds);
public sealed record EtlConnectorRunAdmission(EtlInvocation Invocation, EtlConnectorRunBindings Bindings);
public sealed record EtlConnectorAdmissionResult(EtlStoreStatus Status, EtlConnectorRunView? Run);

/// <summary>Verified source/index snapshot metadata for a connector pipeline with a CSV source.</summary>
public sealed record EtlPreparedCsvInput(EtlObjectReference Source, EtlObjectReference Index, long RecordCount);
/// <summary>Optional inputs prepared before partitions become runnable; absence is explicit.</summary>
public sealed record EtlConnectorPreparedRun(EtlPreparedCsvInput? File, EtlObjectReference? Lookup);
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
}

/// <summary>Authoritative position after an applied or identical replayed transition; no lease on refusal.</summary>
public sealed record EtlConnectorPositionResult(EtlStoreStatus Status, EtlConnectorPartitionLease? Lease);
