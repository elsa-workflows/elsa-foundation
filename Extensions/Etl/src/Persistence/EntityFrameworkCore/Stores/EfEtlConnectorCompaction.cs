using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    /// <summary>
    /// Deletes one continuous shard's rows behind its retained window and folds their counts into the partition's
    /// compacted summary (spec 1196). The window keeps the current request and the predecessor page and request with
    /// that page's batches, which the transition checks, the batch audit and a same-fence replay read.
    /// </summary>
    /// <remarks>
    /// Every claim in a continuous run calls this for every shard, in the claim transaction after the claimed shard's
    /// audit, so it always precedes the work a lease is charged for; budgets are per run, so peers are compacted too. The
    /// request and page rows form a chain of restrict foreign keys (request k + 1 to page k to request k), so predecessor
    /// pointers at and below the new base are cleared first, then batches, pages and requests are deleted as sets.
    /// </remarks>
    private async ValueTask CompactConnectorHttpHistoryAsync(EtlConnectorPartitionRecord row, EtlConnectorRunRecord connector,
        EtlPipelineV2 pipeline, CancellationToken cancellationToken)
    {
        if (pipeline.Continuous is null || row.CurrentRequestSequence is not { } current) return;
        var basePage = current - 1;
        if (basePage <= row.CompactedPageCount) return;

        var pages = await db.ConnectorHttpPages.AsNoTracking()
            .Where(page => page.ScopeKey == row.ScopeKey && page.RunKey == row.RunKey &&
                page.PartitionOrdinal == row.PartitionOrdinal && page.PageSequence < basePage)
            .GroupBy(page => page.PartitionOrdinal)
            .Select(group => new { Count = group.LongCount(), Bytes = group.Sum(page => page.ByteLength), Metadata = group.Sum(page => page.MetadataBytes) })
            .SingleOrDefaultAsync(cancellationToken);
        var requests = await db.ConnectorHttpRequests.AsNoTracking()
            .Where(request => request.ScopeKey == row.ScopeKey && request.RunKey == row.RunKey &&
                request.PartitionOrdinal == row.PartitionOrdinal && request.PageSequence < basePage)
            .GroupBy(request => request.PartitionOrdinal)
            .Select(group => new { Count = group.LongCount(), Metadata = group.Sum(request => request.MetadataBytes), Reserved = group.Sum(request => request.InputReservationBytes) })
            .SingleOrDefaultAsync(cancellationToken);
        var batches = await db.ConnectorBatches.AsNoTracking()
            .Where(batch => batch.ScopeKey == row.ScopeKey && batch.RunKey == row.RunKey &&
                batch.PartitionOrdinal == row.PartitionOrdinal && batch.HttpPageSequence < basePage)
            .ToListAsync(cancellationToken);

        var removedPages = pages?.Count ?? 0;
        if (removedPages != basePage - row.CompactedPageCount || (requests?.Count ?? 0) != removedPages || requests!.Reserved != 0 ||
            batches.Any(batch => !batch.Settled || batch.BatchId == row.OwedBatchId))
            throw new InvalidDataException("The compacted connector history is not a fully accepted contiguous prefix.");

        long accepted = 0, rejected = 0, filtered = 0, main = 0, errors = 0, batchMetadata = 0, outputBytes = 0;
        foreach (var batch in batches)
        {
            var output = ReadConnectorBatchOutput(batch)
                ?? throw new InvalidDataException("A compacted connector batch has no output.");
            accepted = checked(accepted + output.AcceptedRecords);
            rejected = checked(rejected + output.RejectedRecords);
            filtered = checked(filtered + output.FilteredRecords);
            main = checked(main + (batch.MainEffectKind is null ? 0 : 1));
            errors = checked(errors + (batch.ErrorEffectKind is null ? 0 : 1));
            batchMetadata = checked(batchMetadata + batch.MetadataBytes);
            outputBytes = checked(outputBytes + batch.OutputBytes);
        }
        var metadata = checked(pages!.Metadata + requests.Metadata + batchMetadata);

        await db.ConnectorHttpRequests
            .Where(request => request.ScopeKey == row.ScopeKey && request.RunKey == row.RunKey &&
                request.PartitionOrdinal == row.PartitionOrdinal && request.PageSequence <= basePage)
            .ExecuteUpdateAsync(update => update
                .SetProperty(request => request.PreviousPageSequence, (long?)null)
                .SetProperty(request => request.PreviousHasContinuation, (bool?)null), cancellationToken);
        await db.ConnectorBatches
            .Where(batch => batch.ScopeKey == row.ScopeKey && batch.RunKey == row.RunKey &&
                batch.PartitionOrdinal == row.PartitionOrdinal && batch.HttpPageSequence < basePage)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ConnectorHttpPages
            .Where(page => page.ScopeKey == row.ScopeKey && page.RunKey == row.RunKey &&
                page.PartitionOrdinal == row.PartitionOrdinal && page.PageSequence < basePage)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ConnectorHttpRequests
            .Where(request => request.ScopeKey == row.ScopeKey && request.RunKey == row.RunKey &&
                request.PartitionOrdinal == row.PartitionOrdinal && request.PageSequence < basePage)
            .ExecuteDeleteAsync(cancellationToken);

        row.CompactedPageCount = basePage;
        row.CompactedBatchCount = checked(row.CompactedBatchCount + batches.Count);
        row.CompactedAcceptedRecords = checked(row.CompactedAcceptedRecords + accepted);
        row.CompactedRejectedRecords = checked(row.CompactedRejectedRecords + rejected);
        row.CompactedFilteredRecords = checked(row.CompactedFilteredRecords + filtered);
        row.CompactedMainEffects = checked(row.CompactedMainEffects + main);
        row.CompactedErrorEffects = checked(row.CompactedErrorEffects + errors);
        row.PageBytes = checked(row.PageBytes - pages.Bytes);
        row.BatchMetadataBytes = checked(row.BatchMetadataBytes - batchMetadata);
        row.MetadataBytes = checked(row.MetadataBytes - metadata);
        row.RetainedOutputBytes = checked(row.RetainedOutputBytes - outputBytes);
        row.Revision = checked(row.Revision + 1);
        connector.RetainedInputBytes = checked(connector.RetainedInputBytes - pages.Bytes);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes - metadata);
        connector.RetainedOutputBytes = checked(connector.RetainedOutputBytes - outputBytes);
        connector.Revision = checked(connector.Revision + 1);
    }
}
