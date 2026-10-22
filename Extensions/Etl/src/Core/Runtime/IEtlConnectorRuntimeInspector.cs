namespace Elsa.Etl.Core.Runtime;

/// <summary>Optional read-only inspection of connector runs, independent of workers and design services.</summary>
/// <remarks>Each read is a consistent retained-state snapshot, not a live check of remote work or objects.
/// Pages are ordered by partition ordinal and bounded by MaximumPageSize. Missing or foreign runs return
/// null or an empty page. Partition projections exclude raw URLs, continuation tokens, bodies and credentials.
/// Accepted effect counts count settled route bodies, not records, attempts or physical receiver executions.</remarks>
public interface IEtlConnectorRuntimeInspector
{
    int MaximumPageSize { get; }
    ValueTask<EtlConnectorRunInspection?> FindConnectorInspectionAsync(EtlScope scope, string runId,
        CancellationToken cancellationToken = default);
    /// <remarks>A short page means all matching partitions after the cursor have been exhausted.</remarks>
    ValueTask<IReadOnlyList<EtlConnectorPartitionInspection>> ListConnectorPartitionsAsync(EtlScope scope, string runId,
        int afterPartitionOrdinal, int take, CancellationToken cancellationToken = default);
}

/// <summary>Consistent internal run snapshot plus settled route-effect counts.</summary>
/// <remarks>Run includes its retained invocation and is not a public wire DTO. APIs must project approved
/// fields rather than serializing it or its artifact, binding handles and object references.</remarks>
public sealed record EtlConnectorRunInspection(EtlConnectorRunView Run, long AcceptedMainEffects, long AcceptedErrorEffects);

/// <summary>Exactly one of File and Http is present. No file-offset sentinel represents HTTP progress.</summary>
public sealed record EtlConnectorPartitionInspection(int PartitionOrdinal, EtlConnectorFilePosition? File,
    EtlConnectorHttpProgress? Http, bool IsClaimed, long? ClaimFence, DateTimeOffset? ClaimExpiresAt,
    DateTimeOffset? NotBefore, EtlConnectorOwedInspection? OwedBatch, long AcceptedMainEffects, long AcceptedErrorEffects);

/// <summary>Zero-based page/item coordinates within a partition; unknown page size is null with item offset zero.</summary>
public sealed record EtlConnectorHttpProgress(long PageSequence, int NextItem, int? PageRecordCount, bool Exhausted,
    EtlConnectorRetryInspection? Fetch);

/// <summary>Safe current request journal summary; it does not disclose request or effect identifiers.</summary>
/// <remarks>Phase is inFlight, waiting, failed, captured or acknowledged. Null LastStatusCode means no
/// recorded HTTP status, including uncertain transport. NotBefore is present exactly when waiting and precedes Deadline.
/// Fetch supports captured with HTTP 200, delivery supports acknowledged. Attempts are positive. Deadlines do not prove physical termination.</remarks>
public sealed record EtlConnectorRetryInspection(string Phase, int AttemptCount, DateTimeOffset Deadline,
    DateTimeOffset? NotBefore, int? LastStatusCode);

/// <summary>A positive record/input-byte reservation. Route record counts cannot exceed its record count.</summary>
/// <remarks>Unretained output has no effects. JSONL effects have no HTTP retry journal.</remarks>
public sealed record EtlConnectorOwedInspection(long RecordCount, long InputBytes, bool OutputRetained,
    EtlConnectorEffectInspection? Main, EtlConnectorEffectInspection? Errors);

/// <summary>Retained route body and receipt, still awaiting batch acceptance.</summary>
/// <remarks>State is retained, acknowledged or published. An HTTP acknowledgement requires an acknowledged retry journal.
/// Acknowledgement/publication is not accepted progress.</remarks>
public sealed record EtlConnectorEffectInspection(EtlConnectorKind Kind, long RecordCount, string State,
    EtlConnectorRetryInspection? Retry);
