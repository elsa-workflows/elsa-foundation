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
public sealed record EtlConnectorBatchReservation(EtlConnectorBatchDescriptor Batch, bool Settled);
public sealed record EtlConnectorReservationResult(EtlStoreStatus Status, EtlConnectorBatchReservation? Reservation);
