using System.Text.Json.Serialization;

namespace Elsa.Etl.Core.Runtime;

/// <summary>Source coordinates of a nonempty batch within one prepared partition.</summary>
/// <remarks>HTTP item offsets belong to a retained page, never to a synthetic file ordinal.</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(EtlConnectorFileBatchRange), "file")]
[JsonDerivedType(typeof(EtlConnectorHttpBatchRange), "http")]
public abstract record EtlConnectorBatchRange;
public sealed record EtlConnectorFileBatchRange(long StartOrdinal, long EndOrdinalExclusive) : EtlConnectorBatchRange;
public sealed record EtlConnectorHttpBatchRange(long PageSequence, string RequestHash, string PageContentHash,
    int StartItem, int EndItemExclusive) : EtlConnectorBatchRange;

/// <summary>Measured canonical input, pinned before transformation or output dispatch.</summary>
public sealed record EtlConnectorBatchDescriptor(string BatchId, EtlConnectorBatchRange Range, long InputBytes, string InputHash);
/// <summary>Durable logical responsibility, independent of the physical attempt's local allowances.</summary>
public sealed record EtlConnectorBatchReservation(EtlConnectorBatchDescriptor Batch, bool Settled)
{
    /// <summary>Immutable output plan recovered before delivery; retention is not acknowledgement.</summary>
    public EtlConnectorBatchOutput? Output { get; init; }
}
public sealed record EtlConnectorReservationResult(EtlStoreStatus Status, EtlConnectorBatchReservation? Reservation);

/// <summary>A verified immutable body and stable effect identity for one pinned destination.</summary>
/// <remarks>Body identifies retained input to delivery, not proof of remote receipt. Kind is HTTP or JSONL.</remarks>
public sealed record EtlConnectorRouteOutput(EtlConnectorKind Kind, EtlObjectReference Body, long RecordCount, string EffectId);

/// <summary>Complete transformation result for an owed batch. Absent routes must have zero records.</summary>
/// <remarks>Fixed route slots avoid mutable collection ownership. A fully filtered batch has no route bodies.</remarks>
public sealed record EtlConnectorBatchOutput(long AcceptedRecords, long RejectedRecords, long FilteredRecords,
    EtlConnectorRouteOutput? Main, EtlConnectorRouteOutput? Errors);
public sealed record EtlConnectorOutputResult(EtlStoreStatus Status, EtlConnectorBatchOutput? Output);
