using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    /// <summary>
    /// Distinguishes a retryable aggregate-capacity shortage from a run that cannot fit even after
    /// every mutable cursor or currently outstanding reservation has gone away. The lower bound is
    /// deliberately optimistic: it subtracts more state than a real execution is likely to release,
    /// so this helper can miss a permanent shortage but cannot turn a transient one into a terminal
    /// result.
    /// </summary>
    private async ValueTask<EtlStoreStatus> ConnectorMetadataBudgetStatusAsync(EtlConnectorRunRecord connector,
        EtlHttpPolicy policy, int replacedPartitionOrdinal, long unavoidableTransitionBytes, CancellationToken cancellationToken)
    {
        var key = (connector.ScopeKey, connector.RunKey);

        // HTTP continuations can change arbitrarily as pages advance, so their current cursor is
        // conservatively releasable. A file cursor has fixed range bounds and a monotonic ordinal;
        // its JSON length cannot shrink, so retain non-target file cursors in the lower bound.
        // The target cursor is replaced by this transition and is represented in the caller's
        // unavoidable bytes. Partition count is bounded by admission.
        var positions = await db.ConnectorPartitions.AsNoTracking()
            .Where(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey)
            .Select(row => new { row.PartitionOrdinal, row.PositionJson })
            .ToListAsync(cancellationToken);
        var releasable = positions.Where(row => row.PartitionOrdinal == replacedPartitionOrdinal ||
                EtlConnectorRecordCodec.Read<EtlConnectorPosition>(row.PositionJson) is not EtlConnectorFilePosition)
            .Sum(row => MetadataBytes(row.PositionJson));

        // Only current request rows can still have replaceable fetch journals or source headroom.
        // Historical fetch JSON is durable provenance and remains in the lower bound. The target
        // request's full fetch accounting is re-added by the caller as part of the required next
        // state; a peer's active journal is subtracted optimistically because its next attempt may
        // replace it. These rows are bounded by the admitted partition count.
        var currentRequests = await (from partition in db.ConnectorPartitions.AsNoTracking()
                join request in db.ConnectorHttpRequests.AsNoTracking()
                    on new { partition.ScopeKey, partition.RunKey, partition.PartitionOrdinal }
                    equals new { request.ScopeKey, request.RunKey, request.PartitionOrdinal }
                where partition.ScopeKey == key.ScopeKey && partition.RunKey == key.RunKey &&
                      partition.CurrentRequestSequence == request.PageSequence
                select request)
            .ToListAsync(cancellationToken);
        foreach (var request in currentRequests)
        {
            releasable = checked(releasable + request.SourceMetadataReserveBytes);
            if (request.FetchJson is null) continue;

            var fetch = ReadConnectorHttpSource(request, policy)
                ?? throw new InvalidDataException("A current HTTP fetch journal is missing.");
            if (request.PartitionOrdinal == replacedPartitionOrdinal ||
                fetch.Phase is EtlConnectorHttpSourceFetchPhase.InFlight or EtlConnectorHttpSourceFetchPhase.Waiting)
                releasable = checked(releasable + request.FetchMetadataBytes);
        }

        var acceptanceHeadroom = await db.ConnectorBatches.AsNoTracking()
            .Where(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey)
            .Select(row => row.AcceptanceMetadataBytes)
            .SumAsync(cancellationToken);
        releasable = checked(releasable + acceptanceHeadroom);

        // Only an unsettled owed batch can have a mutable delivery journal. There is at most one
        // such batch per partition, so this materialization is bounded by the admitted partition cap.
        var owedDeliveryJson = await (from partition in db.ConnectorPartitions.AsNoTracking()
                join batch in db.ConnectorBatches.AsNoTracking()
                    on new { partition.ScopeKey, partition.RunKey, partition.PartitionOrdinal, BatchId = partition.OwedBatchId }
                    equals new { batch.ScopeKey, batch.RunKey, batch.PartitionOrdinal, batch.BatchId }
                where partition.ScopeKey == key.ScopeKey && partition.RunKey == key.RunKey &&
                      batch.HttpDeliveryJson != null && !batch.Settled
                select batch.HttpDeliveryJson!)
            .ToListAsync(cancellationToken);
        foreach (var json in owedDeliveryJson)
            releasable = checked(releasable + HttpDeliveryMetadataBytes(json));

        if (releasable > connector.RetainedMetadataBytes)
            throw new InvalidDataException("The connector metadata headroom exceeds its retained accounting.");
        var lowerBound = connector.RetainedMetadataBytes - releasable;
        return checked(lowerBound + unavoidableTransitionBytes) > policy.MaxRetainedMetadataBytes
            ? EtlStoreStatus.PermanentBudgetExceeded
            : EtlStoreStatus.BudgetExceeded;
    }
}
