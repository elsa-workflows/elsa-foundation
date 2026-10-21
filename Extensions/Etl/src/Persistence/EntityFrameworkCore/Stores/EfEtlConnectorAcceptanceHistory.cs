using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    private async ValueTask ReadAcceptedConnectorBatchAsync(EtlConnectorPartitionRecord partition,
        EtlConnectorBatchRecord retained, EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline,
        EtlConnectorPreparedRun prepared, EtlConnectorPosition currentPosition, CancellationToken cancellationToken)
    {
        var descriptor = ReadConnectorBatch(retained);
        if (!retained.Settled || retained.BatchOrdinal >= partition.AcceptedBatchCount ||
            retained.HttpPageSequence != (descriptor.Range as EtlConnectorHttpBatchRange)?.PageSequence)
            throw new InvalidDataException("The connector batch is not part of the accepted history.");

        var initialPosition = InitialPositions(pipeline, admission.Bindings, prepared)
            .ElementAtOrDefault(partition.PartitionOrdinal)
            ?? throw new InvalidDataException("The connector partition is outside its prepared input.");
        EtlConnectorPosition batchPosition;
        switch (initialPosition, descriptor.Range, currentPosition)
        {
            case (EtlConnectorFilePosition initial, EtlConnectorFileBatchRange range, EtlConnectorFilePosition current):
                if (initial.StartOrdinal != current.StartOrdinal || initial.EndOrdinalExclusive != current.EndOrdinalExclusive ||
                    range.StartOrdinal < initial.StartOrdinal || range.EndOrdinalExclusive > current.NextOrdinal)
                    throw new InvalidDataException("The accepted file batch is beyond its current position.");
                batchPosition = initial with { NextOrdinal = range.StartOrdinal };
                break;
            case (EtlConnectorHttpPosition, EtlConnectorHttpBatchRange range, EtlConnectorHttpPosition current):
                if (range.PageSequence < 0 || range.StartItem < 0 || range.PageSequence > current.PageSequence)
                    throw new InvalidDataException("The accepted HTTP batch is beyond its current page.");
                var pageRow = await HttpPageRowAsync(partition, range.PageSequence, cancellationToken)
                    ?? throw new InvalidDataException("The accepted HTTP batch has no retained source page.");
                var page = ReadHttpPage(pageRow);
                var requestRow = await Requests(partition).SingleOrDefaultAsync(request =>
                    request.PageSequence == range.PageSequence, cancellationToken)
                    ?? throw new InvalidDataException("The accepted HTTP batch has no retained request.");
                var request = ReadHttpRequest(requestRow);
                ValidateRetainedPage(pageRow, page, request, pipeline);
                if (pageRow.AcceptedItemCount < range.EndItemExclusive || pageRow.AcceptedItemCount > page.RecordCount ||
                    range.PageSequence < current.PageSequence &&
                    (pageRow.AcceptedItemCount != page.RecordCount || page.NextContinuation is null) ||
                    range.PageSequence == current.PageSequence &&
                    (current.Exhausted
                        ? current.NextItem != 0 || pageRow.AcceptedItemCount != page.RecordCount || page.NextContinuation is not null
                        : current.Page is null || current.NextItem != pageRow.AcceptedItemCount ||
                          pageRow.AcceptedItemCount < range.EndItemExclusive))
                    throw new InvalidDataException("The accepted HTTP batch is beyond its current page offset.");
                batchPosition = new EtlConnectorHttpPosition(range.PageSequence, range.StartItem, request, page, false);
                break;
            default:
                throw new InvalidDataException("The accepted connector batch differs from its prepared source type.");
        }

        var records = ValidateConnectorBatch(descriptor, admission, prepared, partition.PartitionOrdinal, batchPosition);
        var output = ReadConnectorBatchOutput(retained);
        var deliveries = ReadConnectorHttpDeliveries(retained, output, pipeline.Http);
        var publications = ReadConnectorFilePublications(retained, output);
        if (output is null || records != retained.RecordCount || records > pipeline.Limits.MaxBatchRecords ||
            descriptor.InputBytes > pipeline.Limits.MaxBatchBytes)
            throw new InvalidDataException("The accepted connector batch exceeds its admitted input bounds.");
        var outputBytes = ValidateConnectorOutput(output, records, admission, descriptor.BatchId);
        if (outputBytes != retained.OutputBytes || outputBytes > pipeline.Limits.MaxOutputBatchBytes ||
            !ConnectorOutputIsDelivered(output, deliveries, publications))
            throw new InvalidDataException("The accepted connector batch has invalid output or delivery evidence.");
    }

    private async ValueTask AuditConnectorBatchHistoryAsync(EtlConnectorPartitionRecord partition,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlConnectorPreparedRun prepared,
        EtlConnectorPosition position, CancellationToken cancellationToken)
    {
        var initialPosition = InitialPositions(pipeline, admission.Bindings, prepared)
            .ElementAtOrDefault(partition.PartitionOrdinal)
            ?? throw new InvalidDataException("The connector partition is outside its prepared input.");

        long metadataBytes = 0, outputBytes = 0, nextBatchOrdinal = 0, settledCount = 0;
        long acceptedRecords = 0, rejectedRecords = 0, filteredRecords = 0;
        long nextFileOrdinal = initialPosition is EtlConnectorFilePosition file ? file.StartOrdinal : 0;
        long? lastHttpPageSequence = null;
        var nextHttpItem = 0;
        var owedFound = false;

        await foreach (var retained in db.ConnectorBatches.AsNoTracking()
            .Where(batch => batch.ScopeKey == partition.ScopeKey && batch.RunKey == partition.RunKey &&
                batch.PartitionOrdinal == partition.PartitionOrdinal)
            .OrderBy(batch => batch.BatchOrdinal).ThenBy(batch => batch.BatchId)
            .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (retained.BatchOrdinal != nextBatchOrdinal++)
                throw new InvalidDataException("The connector batch history has a gap or duplicate ordinal.");

            var descriptor = ReadConnectorBatch(retained);
            if (retained.HttpPageSequence != (descriptor.Range as EtlConnectorHttpBatchRange)?.PageSequence)
                throw new InvalidDataException("The connector batch source type differs from its page pointer.");
            var output = ReadConnectorBatchOutput(retained);
            var deliveries = ReadConnectorHttpDeliveries(retained, output, pipeline.Http);
            var publications = ReadConnectorFilePublications(retained, output);
            metadataBytes = checked(metadataBytes + retained.MetadataBytes);
            outputBytes = checked(outputBytes + retained.OutputBytes);

            if (retained.Settled)
            {
                if (owedFound || retained.BatchOrdinal >= partition.AcceptedBatchCount ||
                    retained.BatchId == partition.OwedBatchId || output is null)
                    throw new InvalidDataException("The settled connector history is not a complete accepted prefix.");

                EtlConnectorPosition batchPosition;
                switch (initialPosition, descriptor.Range)
                {
                    case (EtlConnectorFilePosition filePosition, EtlConnectorFileBatchRange):
                        batchPosition = filePosition with { NextOrdinal = nextFileOrdinal };
                        break;
                    case (EtlConnectorHttpPosition, EtlConnectorHttpBatchRange range):
                        if (lastHttpPageSequence is { } priorPage && range.PageSequence < priorPage)
                            throw new InvalidDataException("The connector HTTP batch history moved to an earlier page.");
                        if (lastHttpPageSequence is null || range.PageSequence > lastHttpPageSequence.Value)
                        {
                            var firstUnverifiedPage = lastHttpPageSequence is { } lastPage ? checked(lastPage + 1) : 0;
                            await ValidateAcceptedHttpPagesAsync(partition, firstUnverifiedPage, range.PageSequence, cancellationToken);
                            nextHttpItem = 0;
                        }

                        var pageRow = await HttpPageRowAsync(partition, range.PageSequence, cancellationToken)
                            ?? throw new InvalidDataException("A settled HTTP batch has no retained source page.");
                        var page = ReadHttpPage(pageRow);
                        var requestRow = await Requests(partition).SingleOrDefaultAsync(request =>
                            request.PageSequence == range.PageSequence, cancellationToken)
                            ?? throw new InvalidDataException("A settled HTTP batch has no retained request.");
                        var request = ReadHttpRequest(requestRow);
                        batchPosition = new EtlConnectorHttpPosition(range.PageSequence, nextHttpItem, request, page, false);
                        break;
                    default:
                        throw new InvalidDataException("The connector batch range differs from its prepared source type.");
                }

                var records = ValidateConnectorBatch(descriptor, admission, prepared, partition.PartitionOrdinal, batchPosition);
                var batchOutputBytes = ValidateConnectorOutput(output, records, admission, descriptor.BatchId);
                if (records != retained.RecordCount || records > pipeline.Limits.MaxBatchRecords ||
                    descriptor.InputBytes > pipeline.Limits.MaxBatchBytes || batchOutputBytes > pipeline.Limits.MaxOutputBatchBytes ||
                    batchOutputBytes != retained.OutputBytes ||
                    !ConnectorOutputIsDelivered(output, deliveries, publications))
                    throw new InvalidDataException("A settled connector batch has invalid output or delivery evidence.");

                acceptedRecords = checked(acceptedRecords + output.AcceptedRecords);
                rejectedRecords = checked(rejectedRecords + output.RejectedRecords);
                filteredRecords = checked(filteredRecords + output.FilteredRecords);

                switch (descriptor.Range, batchPosition)
                {
                    case (EtlConnectorFileBatchRange range, EtlConnectorFilePosition):
                        nextFileOrdinal = range.EndOrdinalExclusive;
                        break;
                    case (EtlConnectorHttpBatchRange range, EtlConnectorHttpPosition):
                        lastHttpPageSequence = range.PageSequence;
                        nextHttpItem = range.EndItemExclusive;
                        break;
                }
                settledCount++;
                continue;
            }

            if (owedFound || retained.BatchOrdinal != partition.AcceptedBatchCount ||
                retained.BatchId != partition.OwedBatchId)
                throw new InvalidDataException("The unsettled connector batch is not the final owed ordinal.");
            owedFound = true;
            var owedRecords = ValidateConnectorBatch(descriptor, admission, prepared, partition.PartitionOrdinal, position);
            var owedOutputBytes = output is null ? 0 : ValidateConnectorOutput(output, owedRecords, admission, descriptor.BatchId);
            if (retained.AcceptanceMetadataBytes != 0 && retained.AcceptanceMetadataBytes != ConnectorAcceptanceReserve(partition, position, descriptor) ||
                owedRecords != retained.RecordCount || owedRecords > pipeline.Limits.MaxBatchRecords ||
                descriptor.InputBytes > pipeline.Limits.MaxBatchBytes || owedOutputBytes > pipeline.Limits.MaxOutputBatchBytes ||
                owedOutputBytes != retained.OutputBytes ||
                partition.NotBeforeUtcTicks != ConnectorNotBefore(output, deliveries, publications))
                throw new InvalidDataException("The owed connector batch differs from its current position or output state.");
        }

        if (settledCount != partition.AcceptedBatchCount || owedFound != (partition.OwedBatchId is not null) ||
            nextBatchOrdinal != checked(partition.AcceptedBatchCount + (owedFound ? 1 : 0)) ||
            acceptedRecords != partition.AcceptedRecords || rejectedRecords != partition.RejectedRecords ||
            filteredRecords != partition.FilteredRecords || metadataBytes != partition.BatchMetadataBytes ||
            outputBytes != partition.RetainedOutputBytes)
            throw new InvalidDataException("The connector batch history or acceptance accounting is incomplete.");

        switch (initialPosition, position)
        {
            case (EtlConnectorFilePosition, EtlConnectorFilePosition current):
                if (nextFileOrdinal != current.NextOrdinal)
                    throw new InvalidDataException("The file position differs from its accepted batch ranges.");
                break;
            case (EtlConnectorHttpPosition, EtlConnectorHttpPosition current):
                if (lastHttpPageSequence is { } acceptedPage)
                {
                    if (current.PageSequence < acceptedPage || current.PageSequence > acceptedPage && current.NextItem != 0)
                        throw new InvalidDataException("The HTTP position differs from its accepted batch ranges.");
                    if (current.PageSequence == acceptedPage)
                    {
                        if (current.Exhausted)
                        {
                            var finalPage = await HttpPageRowAsync(partition, acceptedPage, cancellationToken)
                                ?? throw new InvalidDataException("The exhausted HTTP position has no retained final page.");
                            var page = ReadHttpPage(finalPage);
                            if (current.NextItem != 0 || page.RecordCount != nextHttpItem || page.NextContinuation is not null)
                                throw new InvalidDataException("The exhausted HTTP position differs from its accepted final page.");
                        }
                        else if (current.Page is not { } page || current.NextItem != nextHttpItem ||
                                 current.NextItem >= page.RecordCount)
                            throw new InvalidDataException("The current HTTP page does not follow its accepted batch ranges.");
                    }
                }
                else if (current.NextItem != 0)
                    throw new InvalidDataException("The HTTP position advanced without an accepted batch range.");
                break;
            default:
                throw new InvalidDataException("The connector position differs from its prepared source type.");
        }
    }

    private async ValueTask ValidateAcceptedHttpPagesAsync(EtlConnectorPartitionRecord partition, long startSequence,
        long endSequenceExclusive, CancellationToken cancellationToken)
    {
        for (var sequence = startSequence; sequence < endSequenceExclusive; sequence++)
        {
            var retained = await HttpPageRowAsync(partition, sequence, cancellationToken)
                ?? throw new InvalidDataException("The HTTP batch history skipped a missing retained page.");
            var page = ReadHttpPage(retained);
            if (retained.AcceptedItemCount != page.RecordCount)
                throw new InvalidDataException("The HTTP batch history advanced past an unaccepted page.");
        }
    }
}
