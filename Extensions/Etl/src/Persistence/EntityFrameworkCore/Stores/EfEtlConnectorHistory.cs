using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    private async ValueTask<(EtlPipelineV2 Pipeline, EtlConnectorRunBindings Bindings, EtlConnectorPreparedRun? Prepared)> ReadConnectorStateAsync(
        EtlInvocation invocation, EtlConnectorRunRecord connector, CancellationToken cancellationToken)
    {
        // Partition cardinality is bounded at preparation. Page history may grow to the finite
        // page limit, so hot transitions must never aggregate page rows or deserialize that history.
        var totals = await db.ConnectorPartitions.AsNoTracking()
            .Where(partition => partition.ScopeKey == connector.ScopeKey && partition.RunKey == connector.RunKey)
            .GroupBy(partition => partition.RunKey)
            .Select(group => new
            {
                PageBytes = group.Sum(partition => partition.PageBytes),
                MetadataBytes = group.Sum(partition => partition.MetadataBytes),
                OutputBytes = group.Sum(partition => partition.RetainedOutputBytes),
                AcceptedRecords = group.Sum(partition => partition.AcceptedRecords),
                RejectedRecords = group.Sum(partition => partition.RejectedRecords),
                FilteredRecords = group.Sum(partition => partition.FilteredRecords)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(row => row.ScopeKey == connector.ScopeKey &&
            row.RunKey == connector.RunKey, cancellationToken)
            ?? throw new InvalidDataException("The connector run is missing.");
        if ((totals?.AcceptedRecords ?? 0) != run.AcceptedRecords ||
            (totals?.RejectedRecords ?? 0) != run.RejectedRecords ||
            (totals?.FilteredRecords ?? 0) != run.FilteredRecords)
            throw new InvalidDataException("The connector acceptance counters differ from their partitions.");
        var state = ReadConnectorState(invocation, connector, totals?.PageBytes ?? 0, totals?.MetadataBytes ?? 0, totals?.OutputBytes ?? 0);
        await ValidateConnectorOutstandingAsync(connector, state.Pipeline, cancellationToken);
        return state;
    }

    private async ValueTask<EtlConnectorPosition> ReadConnectorPositionAsync(EtlConnectorPartitionRecord row, EtlPipelineV2 pipeline,
        EtlConnectorRunBindings bindings, EtlConnectorPreparedRun? prepared, CancellationToken cancellationToken, bool auditHistory = false,
        EtlConnectorRunAdmission? admission = null)
    {
        var position = ReadConnectorPosition(row, pipeline, bindings, prepared);
        if (row.OwedBatchId is null && row.NotBeforeUtcTicks is not null || row.RetainedOutputBytes < 0 || row.PageBytes < 0 ||
            row.BatchMetadataBytes < 0 || row.MetadataBytes < checked(MetadataBytes(row.PositionJson) + row.BatchMetadataBytes) ||
            row.AcceptedBatchCount < 0 || row.AcceptedRecords < 0 || row.RejectedRecords < 0 || row.FilteredRecords < 0)
            throw new InvalidDataException("The connector partition accounting is invalid.");
        if (auditHistory && (admission is null || prepared is null))
            throw new InvalidDataException("A full connector history audit requires its admission and prepared input.");
        if (position is not EtlConnectorHttpPosition http)
        {
            if (row.PageCount != 0 || row.PageBytes != 0 || row.CurrentRequestSequence is not null || row.CurrentRequestHash is not null ||
                row.MetadataBytes != checked(MetadataBytes(row.PositionJson) + row.BatchMetadataBytes))
                throw new InvalidDataException("A file partition has HTTP request accounting.");
            var file = (EtlConnectorFilePosition)position;
            if (checked(row.AcceptedRecords + row.RejectedRecords + row.FilteredRecords) != file.NextOrdinal - file.StartOrdinal)
                throw new InvalidDataException("The file partition position differs from its accepted record counters.");
            if (auditHistory)
            {
                if (await Pages(row).AnyAsync(cancellationToken) || await Requests(row).AnyAsync(cancellationToken))
                    throw new InvalidDataException("A file partition has HTTP request history.");
                await AuditConnectorBatchHistoryAsync(row, admission!, pipeline, prepared!, position, cancellationToken);
            }
            return position;
        }
        var expectedCount = checked(http.PageSequence + (http.Page is not null || http.Exhausted ? 1 : 0));
        if (row.PageCount != expectedCount || row.CurrentRequestSequence != http.PageSequence ||
            row.CurrentRequestHash != EtlRuntimeIdentity.HttpRequestHash(http.Request))
            throw new InvalidDataException("The HTTP position differs from its request pointer or page accounting.");
        var requestRow = await Requests(row).SingleOrDefaultAsync(request => request.PageSequence == http.PageSequence, cancellationToken)
            ?? throw new InvalidDataException("The current HTTP request is missing.");
        if (ReadHttpRequest(requestRow) != http.Request)
            throw new InvalidDataException("The current HTTP request differs from its immutable ledger.");
        var currentRow = await HttpPageRowAsync(row, http.PageSequence, cancellationToken);
        if (http.Page is not null || http.Exhausted)
        {
            if (currentRow is null) throw new InvalidDataException("The current retained HTTP page is missing.");
            var page = ReadHttpPage(currentRow);
            ValidateRetainedPage(currentRow, page, http.Request, pipeline);
            if (http.Page is not null && page != http.Page || http.Exhausted && page.NextContinuation is not null)
                throw new InvalidDataException("The HTTP position differs from its retained page.");
        }
        else if (currentRow is not null) throw new InvalidDataException("The HTTP position omitted its retained page.");
        if (http.PageSequence > 0)
        {
            var predecessor = await HttpPageRowAsync(row, http.PageSequence - 1, cancellationToken)
                ?? throw new InvalidDataException("The preceding HTTP page is missing.");
            var page = ReadHttpPage(predecessor);
            if (predecessor.AcceptedItemCount != page.RecordCount || page.NextContinuation is null || page.NextContinuation != http.Request.Continuation ||
                !predecessor.HasContinuation || predecessor.NextRequestHash != requestRow.RequestHash)
                throw new InvalidDataException("The HTTP request has no fully accepted predecessor page.");
        }
        if (currentRow is null)
        {
            if (http.NextItem != 0)
                throw new InvalidDataException("The HTTP position advanced without a retained current page.");
        }
        else if (currentRow.AcceptedItemCount < 0 ||
                 (http.Exhausted
                     ? currentRow.AcceptedItemCount != ReadHttpPage(currentRow).RecordCount
                     : currentRow.AcceptedItemCount != http.NextItem ||
                       http.Page is { } currentPage && currentRow.AcceptedItemCount > currentPage.RecordCount))
            throw new InvalidDataException("The HTTP position differs from its accepted page offset.");
        if (auditHistory)
        {
            await AuditHttpHistoryAsync(row, http, pipeline, cancellationToken);
            await AuditConnectorBatchHistoryAsync(row, admission!, pipeline, prepared!, position, cancellationToken);
        }
        return position;
    }

    private async ValueTask AuditHttpHistoryAsync(EtlConnectorPartitionRecord row, EtlConnectorHttpPosition http,
        EtlPipelineV2 pipeline, CancellationToken cancellationToken)
    {
        long requestCount = 0, metadataBytes = checked(MetadataBytes(row.PositionJson) + row.BatchMetadataBytes), pageBytes = 0, sequence = 0;
        // Full audit at ownership acquisition and successful terminal settlement, including fresh
        // processes. No process-local cache is required for the normal bounded transition path.
        await foreach (var retained in Requests(row).OrderBy(request => request.PageSequence).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var request = ReadHttpRequest(retained);
            if (retained.PageSequence != requestCount || request.ShardId != http.Request.ShardId ||
                retained.PageSequence > http.PageSequence || retained.PageSequence == http.PageSequence && request != http.Request)
                throw new InvalidDataException("The retained HTTP requests do not match their partition.");
            EtlConnectorRecordCodec.ValidateHttpPosition(new(retained.PageSequence, 0, request, null, false), pipeline.Http);
            requestCount++;
            metadataBytes = checked(metadataBytes + retained.MetadataBytes);
        }
        if (requestCount != http.PageSequence + 1) throw new InvalidDataException("The HTTP request history is incomplete.");
        var expectedRequest = new EtlHttpRequest(http.Request.ShardId, null);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var retained in Pages(row).OrderBy(page => page.PageSequence).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var page = ReadHttpPage(retained);
            if (page.PageSequence != sequence || !visited.Add(page.RequestHash) || retained.AcceptedItemCount < 0 ||
                retained.AcceptedItemCount > page.RecordCount)
                throw new InvalidDataException("The HTTP page history does not follow its request chain.");
            ValidateRetainedPage(retained, page, expectedRequest, pipeline);
            if (page.NextContinuation is not null && visited.Contains(retained.NextRequestHash))
                throw new InvalidDataException("The retained continuation repeats a visited request.");
            var acceptedItemCount = await ReadAcceptedItemCountAsync(row, retained.PageSequence, cancellationToken);
            if (acceptedItemCount != retained.AcceptedItemCount)
                throw new InvalidDataException("The HTTP page accepted offset differs from its settled batch ranges.");
            if (sequence == http.PageSequence)
            {
                if (expectedRequest != http.Request || http.Page is not null && page != http.Page ||
                    retained.AcceptedItemCount != (http.Exhausted ? page.RecordCount : http.NextItem) ||
                    http.Exhausted && page.NextContinuation is not null)
                    throw new InvalidDataException("The HTTP position differs from its retained page.");
            }
            else
            {
                if (page.NextContinuation is null || retained.AcceptedItemCount != page.RecordCount)
                    throw new InvalidDataException("The HTTP position has no fully accepted predecessor page.");
                expectedRequest = expectedRequest with { Continuation = page.NextContinuation };
            }
            pageBytes = checked(pageBytes + page.Body.ByteLength);
            metadataBytes = checked(metadataBytes + retained.MetadataBytes);
            sequence++;
        }
        if (sequence != row.PageCount || pageBytes != row.PageBytes || metadataBytes != row.MetadataBytes ||
            http.Page is null && !http.Exhausted && expectedRequest != http.Request)
            throw new InvalidDataException("The HTTP request history or retained accounting is inconsistent.");
    }

    private async ValueTask<int> ReadAcceptedItemCountAsync(EtlConnectorPartitionRecord partition, long pageSequence,
        CancellationToken cancellationToken)
    {
        var acceptedItemCount = 0;
        await foreach (var retained in db.ConnectorBatches.AsNoTracking().Where(batch => batch.ScopeKey == partition.ScopeKey &&
                batch.RunKey == partition.RunKey && batch.PartitionOrdinal == partition.PartitionOrdinal &&
                batch.HttpPageSequence == pageSequence && batch.Settled)
            .OrderBy(batch => batch.BatchOrdinal).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var descriptor = ReadConnectorBatch(retained);
            if (descriptor.Range is not EtlConnectorHttpBatchRange range || range.PageSequence != pageSequence ||
                range.StartItem != acceptedItemCount || range.EndItemExclusive <= range.StartItem)
                throw new InvalidDataException("The settled HTTP batch ranges are not contiguous within their page.");
            acceptedItemCount = range.EndItemExclusive;
        }
        return acceptedItemCount;
    }

    private static void ValidateRetainedPage(EtlConnectorHttpPageRecord row, EtlRetainedHttpPage page, EtlHttpRequest request, EtlPipelineV2 pipeline)
    {
        var position = new EtlConnectorHttpPosition(page.PageSequence, 0, request, page, false);
        EtlConnectorRecordCodec.Complete(position);
        EtlConnectorRecordCodec.ValidateHttpPosition(position, pipeline.Http);
        if (row.NextRequestHash != EtlRuntimeIdentity.HttpRequestHash(request with { Continuation = page.NextContinuation }) ||
            page.NextContinuation == string.Empty ||
            pipeline.Source is EtlHttpJsonSource { Pagination: EtlSinglePagePagination } && page.NextContinuation is not null ||
            page.NextContinuation is not null && page.PageSequence + 1 >= pipeline.Http.MaxPagesPerPartition)
            throw new InvalidDataException("The retained continuation violates its finite request policy.");
    }

    private IQueryable<EtlConnectorHttpPageRecord> Pages(EtlConnectorPartitionRecord row) =>
        db.ConnectorHttpPages.AsNoTracking().Where(page => page.ScopeKey == row.ScopeKey && page.RunKey == row.RunKey && page.PartitionOrdinal == row.PartitionOrdinal);

    private IQueryable<EtlConnectorHttpRequestRecord> Requests(EtlConnectorPartitionRecord row) =>
        db.ConnectorHttpRequests.AsNoTracking().Where(request => request.ScopeKey == row.ScopeKey && request.RunKey == row.RunKey && request.PartitionOrdinal == row.PartitionOrdinal);

    private Task<EtlConnectorHttpPageRecord?> HttpPageRowAsync(EtlConnectorPartitionRecord row, long sequence, CancellationToken cancellationToken) =>
        Pages(row).SingleOrDefaultAsync(page => page.PageSequence == sequence, cancellationToken);

    private static EtlRetainedHttpPage ReadHttpPage(EtlConnectorHttpPageRecord row)
    {
        EnsureSchema(row.SchemaVersion);
        if (row.MetadataBytes != MetadataBytes(row.PageJson) || row.PageDigest != MetadataDigest(row.PageJson))
            throw new InvalidDataException("The retained HTTP page document changed.");
        var page = EtlConnectorRecordCodec.Read<EtlRetainedHttpPage>(row.PageJson);
        if (page.PageSequence != row.PageSequence || page.RequestHash != row.RequestHash || page.Body is null ||
            page.Body.ByteLength != row.ByteLength || row.ByteLength < 0 || row.HasContinuation != (page.NextContinuation is not null))
            throw new InvalidDataException("The HTTP page metadata differs from its retained columns.");
        return page;
    }

    private static EtlHttpRequest ReadHttpRequest(EtlConnectorHttpRequestRecord row)
    {
        EnsureSchema(row.SchemaVersion);
        var request = EtlConnectorRecordCodec.Read<EtlHttpRequest>(row.RequestJson);
        if (row.PageSequence < 0 || row.MetadataBytes != MetadataBytes(row.RequestJson) || row.RequestHash != EtlRuntimeIdentity.HttpRequestHash(request) ||
            (row.PageSequence == 0
                ? request.Continuation is not null || row.PreviousPageSequence is not null || row.PreviousHasContinuation is not null
                : string.IsNullOrEmpty(request.Continuation) || row.PreviousPageSequence != row.PageSequence - 1 || row.PreviousHasContinuation != true))
            throw new InvalidDataException("The retained HTTP request metadata is inconsistent.");
        return request;
    }

    private static EtlConnectorHttpRequestRecord HttpRequestRecord(EtlConnectorPartitionRecord row, EtlConnectorHttpPosition position)
    {
        var json = EtlConnectorRecordCodec.Write(position.Request);
        return new()
        {
            ScopeKey = row.ScopeKey, RunKey = row.RunKey, PartitionOrdinal = row.PartitionOrdinal,
            PageSequence = position.PageSequence, RequestHash = EtlRuntimeIdentity.HttpRequestHash(position.Request),
            RequestJson = json, MetadataBytes = MetadataBytes(json),
            PreviousPageSequence = position.PageSequence == 0 ? null : position.PageSequence - 1,
            PreviousHasContinuation = position.PageSequence == 0 ? null : true,
            SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        };
    }

    private static long MetadataBytes(string json) => Encoding.UTF8.GetByteCount(json);
    private static string MetadataDigest(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
