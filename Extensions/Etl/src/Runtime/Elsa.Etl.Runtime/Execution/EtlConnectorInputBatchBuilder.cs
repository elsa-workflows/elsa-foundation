using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Execution;

/// <summary>A source item in a bounded connector batch, already typed or carrying a safe parse error.</summary>
internal sealed record EtlConnectorInputRecord(long Index, long RawInputBytes, EtlTypedRow? Row, EtlRowError? Error);

/// <summary>One deterministic source range plus the prepared lookup needed to transform its typed rows.</summary>
internal sealed record EtlConnectorInputBatch(int PartitionOrdinal, EtlConnectorBatchDescriptor Descriptor,
    IReadOnlyList<EtlConnectorInputRecord> Records)
{
    public IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> LookupRows { get; init; } =
        new ReadOnlyDictionary<EtlTypedValue, EtlTypedRow>(new Dictionary<EtlTypedValue, EtlTypedRow>());
}

internal sealed record EtlConnectorInputBatchBuildResult(EtlConnectorInputBatch? Batch, EtlPreparationDiagnostic? Diagnostic)
{
    public bool Succeeded => Diagnostic is null;
}

internal sealed class EtlConnectorInputBatchBuilderException(EtlPreparationDiagnostic diagnostic)
    : Exception(diagnostic.ToString())
{
    public EtlPreparationDiagnostic Diagnostic { get; } = diagnostic;
}

/// <summary>Builds and exactly reconstructs deterministic bounded batches from retained typed connector inputs.</summary>
/// <remarks>CSV rows are parsed from the retained byte index; HTTP records are consumed directly from the verified page cache result.</remarks>
internal sealed class EtlConnectorInputBatchBuilder : IAsyncDisposable
{
    private const int CanonicalFormatVersion = 2;
    private const int IndexEntrySize = EtlConnectorCsvSupport.IndexEntrySize;
    private const int BufferSize = 4096;
    private const string EmptyEnvelope = "{\"formatVersion\":2,\"records\":[]}";
    private readonly string _root;
    private readonly EtlConnectorRunAdmission _admission;
    private readonly EtlConnectorPreparedRun _prepared;
    private readonly EtlPipelineV2 _pipeline;
    private readonly int _partitionOrdinal;
    private readonly EtlCsvSource? _csvSource;
    private readonly FileStream? _source;
    private readonly FileStream? _index;
    private readonly EtlCsvHeaderMap? _sourceHeader;
    private readonly IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> _lookupRows;
    private readonly byte[] _indexEntryBytes = new byte[IndexEntrySize];
    private readonly long _sourceRecordCount;
    private bool _disposed;

    private EtlConnectorInputBatchBuilder(string root, EtlConnectorRunAdmission admission,
        EtlConnectorPreparedRun prepared, EtlPipelineV2 pipeline, int partitionOrdinal,
        FileStream? source, FileStream? index, EtlCsvHeaderMap? sourceHeader,
        IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> lookupRows, long sourceRecordCount)
    {
        _root = root;
        _admission = admission;
        _prepared = prepared;
        _pipeline = pipeline;
        _partitionOrdinal = partitionOrdinal;
        _csvSource = pipeline.Source as EtlCsvSource;
        _source = source;
        _index = index;
        _sourceHeader = sourceHeader;
        _lookupRows = lookupRows;
        _sourceRecordCount = sourceRecordCount;
    }

    /// <summary>Opens and verifies the exact admitted artifact and retained source/index/lookup objects.</summary>
    public static async ValueTask<EtlConnectorInputBatchBuilder> OpenAsync(string hostApprovedSnapshotRoot,
        EtlConnectorRunAdmission admission, EtlConnectorPreparedRun prepared, int partitionOrdinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApprovedSnapshotRoot);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(prepared);
        if (!Path.IsPathFullyQualified(hostApprovedSnapshotRoot))
            throw new ArgumentException("The ETL snapshot root must be an absolute host-approved path.", nameof(hostApprovedSnapshotRoot));
        if (partitionOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(partitionOrdinal));

        admission = SnapshotAdmission(admission);

        FileStream? source = null;
        FileStream? index = null;
        var streamsTransferred = false;
        try
        {
            var pipeline = ReadPipeline(admission);
            var root = Path.GetFullPath(hostApprovedSnapshotRoot);
            if (!Directory.Exists(root))
                throw Invalid("etl.snapshot-read-failed");
            ValidatePrepared(admission, prepared, pipeline, partitionOrdinal);

            IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> lookupRows = EmptyLookupRows;
            if (pipeline.Lookup is { } lookup)
            {
                var pin = PreparedPin(admission, lookup.Binding, EtlFileRole.Lookup);
                lookupRows = await EtlConnectorCsvSupport.ReadLookupRowsAsync(root, prepared.Lookup!, pin,
                    lookup, pipeline.Limits, cancellationToken);
            }

            EtlCsvHeaderMap? sourceHeader = null;
            long sourceRecordCount = 0;
            if (pipeline.Source is EtlCsvSource csv)
            {
                var file = prepared.File!;
                var sourcePin = PreparedPin(admission, csv.Binding, EtlFileRole.Source);
                source = EtlConnectorCsvSupport.OpenSnapshot(root, file.Source, ".csv",
                    pipeline.Limits.MaxSnapshotBytes, sourcePin.ExpectedSha256!);
                index = OpenGeneratedChunk(root, file.Index, ".index",
                    checked((file.RecordCount + 1) * IndexEntrySize), exactLength: true);
                await VerifyFileAsync(source, file.Source.ContentHash, cancellationToken);
                await VerifyFileAsync(index, file.Index.ContentHash, cancellationToken);
                source.Position = 0;
                index.Position = 0;
                sourceRecordCount = file.RecordCount;

                var entry = await ReadIndexEntryAsync(index, file.RecordCount, 0, new byte[IndexEntrySize], cancellationToken);
                var headerRecord = await ReadSourceRecordAsync(source, file.Source.ByteLength, pipeline, entry, cancellationToken);
                if (!EtlRowTransformer.TryCompileHeader(csv.Schema, headerRecord, out sourceHeader, out var headerError))
                    throw Invalid(headerError!.Code, headerError.RecordOrdinal, headerError.Field);
            }

            var builder = new EtlConnectorInputBatchBuilder(root, admission, prepared, pipeline, partitionOrdinal,
                source, index, sourceHeader, lookupRows, sourceRecordCount);
            streamsTransferred = true;
            return builder;
        }
        catch (EtlConnectorInputBatchBuilderException)
        {
            throw;
        }
        catch (EtlConnectorPreparationFailure failure)
        {
            throw new EtlConnectorInputBatchBuilderException(failure.Diagnostic);
        }
        catch (CsvReadException exception)
        {
            var diagnostic = exception.StableCode == "csv.source-read-failed"
                ? EtlPreparationDiagnostic.Fault(exception.StableCode, exception.InnerException ?? exception)
                : new EtlPreparationDiagnostic(exception.StableCode, exception.RecordOrdinal);
            throw new EtlConnectorInputBatchBuilderException(diagnostic);
        }
        catch (InvalidDataException)
        {
            throw new EtlConnectorInputBatchBuilderException(new EtlPreparationDiagnostic("etl.snapshot-integrity-failed"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EtlConnectorInputBatchBuilderException(EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException or OverflowException)
        {
            throw new EtlConnectorInputBatchBuilderException(new EtlPreparationDiagnostic("etl.batch-descriptor-invalid"));
        }
        finally
        {
            if (!streamsTransferred)
            {
                if (source is not null)
                    await source.DisposeAsync();
                if (index is not null)
                    await index.DisposeAsync();
            }
        }
    }

    /// <summary>Prepared lookup rows reloaded from the exact immutable lookup reference.</summary>
    public IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> LookupRows => _lookupRows;

    /// <summary>Builds the next bounded range at the current durable connector position.</summary>
    public async ValueTask<EtlConnectorInputBatchBuildResult> BuildNextAsync(EtlConnectorPosition position,
        EtlCachedHttpPage? retainedHttpPage = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EtlConnectorInputBatchBuilder));
        if (!TryValidatePosition(position, retainedHttpPage, out var diagnostic))
            return Failed(diagnostic!.Code);

        if (position is EtlConnectorFilePosition file)
        {
            if (file.NextOrdinal == file.EndOrdinalExclusive)
                return new(null, null);
            return await BuildFileRangeAsync(file.NextOrdinal, null, null, cancellationToken);
        }

        var http = (EtlConnectorHttpPosition)position;
        var page = retainedHttpPage!;
        if (http.NextItem == page.Descriptor.RecordCount)
            return new(null, null);
        return await BuildHttpRangeAsync(http, page, http.NextItem, null, cancellationToken);
    }

    /// <summary>Rebuilds the exact range and rejects any changed retained input bytes or canonical hash.</summary>
    public async ValueTask<EtlConnectorInputBatchBuildResult> RebuildAsync(EtlConnectorBatchDescriptor owed,
        EtlConnectorPosition currentPosition, EtlCachedHttpPage? retainedHttpPage = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EtlConnectorInputBatchBuilder));
        if (owed is null || !EtlConnectorCsvSupport.IsHash(owed.InputHash) || owed.InputBytes <= 0 ||
            owed.InputBytes > _pipeline.Limits.MaxBatchBytes || !TryValidatePosition(currentPosition, retainedHttpPage, out _))
            return Failed("etl.batch-identity-invalid");

        EtlConnectorInputBatchBuildResult result;
        switch (owed.Range)
        {
            case EtlConnectorFileBatchRange fileRange when currentPosition is EtlConnectorFilePosition file &&
                 fileRange.StartOrdinal == file.NextOrdinal && fileRange.EndOrdinalExclusive <= file.EndOrdinalExclusive:
                result = await BuildFileRangeAsync(fileRange.StartOrdinal, fileRange.EndOrdinalExclusive, owed, cancellationToken);
                break;
            case EtlConnectorHttpBatchRange httpRange when currentPosition is EtlConnectorHttpPosition http &&
                 retainedHttpPage is not null && httpRange.PageSequence == http.PageSequence &&
                 httpRange.RequestHash == retainedHttpPage.Descriptor.RequestHash &&
                 httpRange.PageContentHash == retainedHttpPage.Descriptor.Body.ContentHash &&
                 httpRange.StartItem == http.NextItem && httpRange.EndItemExclusive <= retainedHttpPage.Descriptor.RecordCount:
                result = await BuildHttpRangeAsync(http, retainedHttpPage, httpRange.StartItem, httpRange.EndItemExclusive, cancellationToken);
                break;
            default:
                return Failed("etl.batch-identity-invalid");
        }

        if (!result.Succeeded || result.Batch is null)
            return result;
        return result.Batch.Descriptor.InputBytes == owed.InputBytes &&
               StringComparer.Ordinal.Equals(result.Batch.Descriptor.InputHash, owed.InputHash) &&
               StringComparer.Ordinal.Equals(result.Batch.Descriptor.BatchId, owed.BatchId)
            ? result
            : Failed("etl.batch-reconstruction-mismatch");
    }

    private async ValueTask<EtlConnectorInputBatchBuildResult> BuildFileRangeAsync(long startOrdinal,
        long? requestedEndOrdinal, EtlConnectorBatchDescriptor? expected, CancellationToken cancellationToken)
    {
        try
        {
            var file = _prepared.File!;
            var partitionEnd = ResolveFilePartitionEnd();
            var partitionStart = ResolveFilePartitionStart();
            if (startOrdinal < partitionStart || startOrdinal >= partitionEnd)
                return Failed("etl.batch-identity-invalid");
            var endLimit = requestedEndOrdinal ?? startOrdinal + Math.Min((long)_pipeline.Limits.MaxBatchRecords,
                partitionEnd - startOrdinal);
            if (endLimit > partitionEnd ||
                endLimit <= startOrdinal || endLimit > _sourceRecordCount + 1 ||
                endLimit - startOrdinal > _pipeline.Limits.MaxBatchRecords)
                return Failed("etl.batch-identity-invalid");

            var records = new List<EtlConnectorInputRecord>();
            var canonicalBytes = EmptyEnvelopeBytes;
            var previous = await ReadIndexEntryAsync(startOrdinal - 1, cancellationToken);
            var expectedStartOffset = previous.EndByteOffset;

            var ordinalToRead = startOrdinal;
            while (ordinalToRead < endLimit && records.Count < _pipeline.Limits.MaxBatchRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var indexed = await ReadIndexEntryAsync(ordinalToRead, cancellationToken);
                if (indexed.StartByteOffset != expectedStartOffset || indexed.EndByteOffset <= indexed.StartByteOffset ||
                    indexed.EndByteOffset > file.Source.ByteLength)
                    return Failed("etl.source-index-invalid");

                var csvRecord = await ReadSourceRecordAsync(_source!, file.Source.ByteLength, _pipeline, indexed, cancellationToken);
                EtlConnectorInputRecord input;
                if (EtlRowTransformer.TryParseTypedRow(_csvSource!.Schema, _sourceHeader!, csvRecord, out var typedRow, out var rowError))
                {
                    input = new EtlConnectorInputRecord(indexed.Ordinal, indexed.EndByteOffset - indexed.StartByteOffset,
                        EtlConnectorCsvSupport.FreezeRow(typedRow!), null);
                }
                else
                {
                    input = new EtlConnectorInputRecord(indexed.Ordinal, indexed.EndByteOffset - indexed.StartByteOffset,
                        null, rowError!);
                }

                if (!TryMeasureAndAppend(records, input, ref canonicalBytes))
                {
                    if (records.Count == 0 || expected is not null)
                        return Failed("etl.batch-record-too-large", SafeOrdinal(indexed.Ordinal));
                    break;
                }
                expectedStartOffset = indexed.EndByteOffset;
                ordinalToRead++;
            }

            if (expected is not null && ordinalToRead != endLimit)
                return Failed("etl.batch-reconstruction-mismatch");
            if (records.Count == 0)
                return new(null, null);
            return BuildBatch(new EtlConnectorFileBatchRange(startOrdinal, ordinalToRead), records, canonicalBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CsvReadException exception)
        {
            var diagnostic = exception.StableCode == "csv.source-read-failed"
                ? EtlPreparationDiagnostic.Fault(exception.StableCode, exception.InnerException ?? exception)
                : new EtlPreparationDiagnostic(exception.StableCode, exception.RecordOrdinal);
            return new(null, diagnostic);
        }
        catch (EtlConnectorInputBatchBuilderException exception)
        {
            return new(null, exception.Diagnostic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (OverflowException)
        {
            return Failed("etl.source-index-invalid");
        }
    }

    private ValueTask<EtlConnectorInputBatchBuildResult> BuildHttpRangeAsync(EtlConnectorHttpPosition position,
        EtlCachedHttpPage page, int startItem, int? requestedEndItem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (startItem < 0 || startItem >= page.Descriptor.RecordCount)
            return ValueTask.FromResult(Failed("etl.batch-identity-invalid"));
        var endLimit = requestedEndItem ?? (int)Math.Min(page.Descriptor.RecordCount,
            (long)startItem + _pipeline.Limits.MaxBatchRecords);
        if (endLimit <= startItem || endLimit > page.Descriptor.RecordCount ||
            endLimit - startItem > _pipeline.Limits.MaxBatchRecords)
            return ValueTask.FromResult(Failed("etl.batch-identity-invalid"));

        var records = new List<EtlConnectorInputRecord>();
        var canonicalBytes = EmptyEnvelopeBytes;
        for (var index = startItem; index < endLimit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = page.DecodedPage.Records[index];
            if (record.Index != index || record.RawInputBytes <= 0 || (record.Row is null) == (record.Error is null))
                return ValueTask.FromResult(Failed("etl.http.page-cache-corrupt"));
            var input = new EtlConnectorInputRecord(record.Index, record.RawInputBytes,
                record.Row is null ? null : EtlConnectorCsvSupport.FreezeRow(record.Row), record.Error);
            if (!TryMeasureAndAppend(records, input, ref canonicalBytes))
            {
                if (records.Count == 0 || requestedEndItem is not null)
                    return ValueTask.FromResult(Failed("etl.batch-record-too-large", record.Error?.RecordOrdinal ?? record.Row?.RecordOrdinal ?? 0));
                break;
            }
        }

        if (records.Count == 0)
            return ValueTask.FromResult(Failed("etl.batch-record-too-large"));

        var range = new EtlConnectorHttpBatchRange(position.PageSequence, page.Descriptor.RequestHash,
            page.Descriptor.Body.ContentHash, startItem, startItem + records.Count);
        return ValueTask.FromResult(BuildBatch(range, records, canonicalBytes));
    }

    private EtlConnectorInputBatchBuildResult BuildBatch(EtlConnectorBatchRange range,
        IReadOnlyList<EtlConnectorInputRecord> records, long measuredBytes)
    {
        try
        {
            var payload = HashCanonicalBatch(records, _pipeline);
            if (payload.ByteLength != measuredBytes || payload.ByteLength > _pipeline.Limits.MaxBatchBytes)
                return Failed("etl.batch-canonicalization-failed");
            var batchId = EtlRuntimeIdentity.ConnectorBatchId(_admission, _prepared, _partitionOrdinal,
                range, payload.ByteLength, payload.ContentHash);
            var descriptor = new EtlConnectorBatchDescriptor(batchId, range, payload.ByteLength, payload.ContentHash);
            var frozen = Array.AsReadOnly(records.Select(record => record with
            {
                Row = record.Row is null ? null : EtlConnectorCsvSupport.FreezeRow(record.Row)
            }).ToArray());
            return new(new EtlConnectorInputBatch(_partitionOrdinal, descriptor, frozen) { LookupRows = _lookupRows }, null);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return Failed("etl.batch-canonicalization-failed");
        }
    }

    private bool TryMeasureAndAppend(List<EtlConnectorInputRecord> records, EtlConnectorInputRecord record,
        ref long canonicalBytes)
    {
        if (record.RawInputBytes <= 0 || (record.Row is null) == (record.Error is null))
            throw Invalid("etl.input-record-invalid");
        var separator = records.Count == 0 ? 0 : 1;
        var available = _pipeline.Limits.MaxBatchBytes - canonicalBytes - separator;
        if (available <= 0 || !TryMeasureRecord(record, _pipeline, available, out var recordBytes))
            return false;
        canonicalBytes += separator + recordBytes;
        records.Add(record);
        return true;
    }

    private bool TryValidatePosition(EtlConnectorPosition position, EtlCachedHttpPage? page,
        out EtlPreparationDiagnostic? diagnostic)
    {
        diagnostic = null;
        switch (position)
        {
            case EtlConnectorFilePosition file when _csvSource is not null && page is null && _prepared.File is not null:
                if (file.StartOrdinal < 1 || file.EndOrdinalExclusive < file.StartOrdinal ||
                    file.EndOrdinalExclusive > _sourceRecordCount + 1 || file.NextOrdinal < file.StartOrdinal ||
                    file.NextOrdinal > file.EndOrdinalExclusive || file.StartOrdinal != ResolveFilePartitionStart() ||
                    file.EndOrdinalExclusive != ResolveFilePartitionEnd())
                    diagnostic = new EtlPreparationDiagnostic("etl.partition-cursor-invalid");
                break;
            case EtlConnectorHttpPosition http when _csvSource is null && _pipeline.Source is EtlHttpJsonSource && _prepared.File is null:
                if (http.Exhausted || http.Page is null || page is null ||
                    !StringComparer.Ordinal.Equals(http.Request.ShardId, _admission.Bindings.HttpShardIds[_partitionOrdinal]) ||
                    http.PageSequence != page.Descriptor.PageSequence || http.Page != page.Descriptor ||
                    http.NextItem < 0 || http.NextItem > page.Descriptor.RecordCount ||
                    page.DecodedPage.Records.Count != page.Descriptor.RecordCount ||
                    page.Descriptor.RecordCount > _pipeline.Http.MaxPageRecords ||
                    page.RawBody.Length != page.Descriptor.Body.ByteLength ||
                    page.RawBody.Length > _pipeline.Http.MaxPageBytes ||
                    !EtlConnectorCsvSupport.IsHash(page.Descriptor.Body.ContentHash) ||
                    Convert.ToHexString(SHA256.HashData(page.RawBody.Span)).ToLowerInvariant() != page.Descriptor.Body.ContentHash ||
                    page.Descriptor.RequestHash != EtlRuntimeIdentity.HttpRequestHash(http.Request) ||
                    !StringComparer.Ordinal.Equals(page.Descriptor.NextContinuation, page.DecodedPage.Continuation))
                    diagnostic = new EtlPreparationDiagnostic("etl.http.page-cache-corrupt");
                break;
            default:
                diagnostic = new EtlPreparationDiagnostic("etl.batch-position-invalid");
                break;
        }
        return diagnostic is null;
    }

    private long ResolveFilePartitionStart() => new EtlPartitionPlan(_sourceRecordCount,
        _pipeline.Limits.FilePartitionCount)[_partitionOrdinal].StartRecordOrdinal;

    private long ResolveFilePartitionEnd() => new EtlPartitionPlan(_sourceRecordCount,
        _pipeline.Limits.FilePartitionCount)[_partitionOrdinal].EndRecordOrdinalExclusive;

    private async ValueTask<IndexEntry> ReadIndexEntryAsync(long ordinal, CancellationToken cancellationToken) =>
        await ReadIndexEntryAsync(_index!, _sourceRecordCount, ordinal, _indexEntryBytes, cancellationToken);

    private static async ValueTask<IndexEntry> ReadIndexEntryAsync(FileStream index, long sourceRecordCount,
        long ordinal, byte[] buffer, CancellationToken cancellationToken)
    {
        if (ordinal < 0 || ordinal > sourceRecordCount)
            throw Invalid("etl.source-index-invalid", SafeOrdinal(ordinal));
        var entryOffset = checked(ordinal * IndexEntrySize);
        if (entryOffset > index.Length - IndexEntrySize)
            throw Invalid("etl.source-index-invalid", SafeOrdinal(ordinal));
        index.Position = entryOffset;
        await index.ReadExactlyAsync(buffer, cancellationToken);
        var indexedOrdinal = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(0, sizeof(long)));
        var startOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(sizeof(long), sizeof(long)));
        var endOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(sizeof(long) * 2, sizeof(long)));
        if (indexedOrdinal != ordinal || startOffset < 0 || endOffset < 0)
            throw Invalid("etl.source-index-invalid", SafeOrdinal(ordinal));
        return new IndexEntry(indexedOrdinal, startOffset, endOffset);
    }

    private static async ValueTask<CsvRecord> ReadSourceRecordAsync(FileStream source, long sourceByteLength,
        EtlPipelineV2 pipeline, IndexEntry entry, CancellationToken cancellationToken)
    {
        if (entry.StartByteOffset < 0 || entry.EndByteOffset <= entry.StartByteOffset || entry.EndByteOffset > sourceByteLength)
            throw Invalid("etl.source-index-invalid", SafeOrdinal(entry.Ordinal));
        source.Position = entry.StartByteOffset;
        await using var limited = new EtlBoundedRangeReadStream(source,
            entry.EndByteOffset - entry.StartByteOffset, flushSupported: true);
        await using var records = new CsvRecordReader(limited, pipeline.Limits.MaxRecordBytes, pipeline.Limits.MaxFields,
                initialRecordOrdinal: checked((int)entry.Ordinal), initialByteOffset: entry.StartByteOffset,
                detectBom: entry.Ordinal == 0 && entry.StartByteOffset == 0)
            .ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        if (!await records.MoveNextAsync())
            throw Invalid("etl.source-index-invalid", SafeOrdinal(entry.Ordinal));
        var record = records.Current;
        if (record.RecordOrdinal != entry.Ordinal || record.StartByteOffset != entry.StartByteOffset ||
            record.EndByteOffset != entry.EndByteOffset || await records.MoveNextAsync())
            throw Invalid("etl.source-index-invalid", SafeOrdinal(entry.Ordinal));
        return record;
    }

    private static async ValueTask VerifyFileAsync(FileStream stream, string expectedHash, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;
            hash.AppendData(buffer.AsSpan(0, count));
        }
        if (!StringComparer.Ordinal.Equals(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), expectedHash))
            throw Invalid("etl.snapshot-integrity-failed");
    }

    private static FileStream OpenGeneratedChunk(string root, EtlObjectReference chunk, string extension,
        long maxBytes, bool exactLength)
    {
        if (!EtlConnectorCsvSupport.IsChunkKey(chunk.Key, extension) || !EtlConnectorCsvSupport.IsHash(chunk.ContentHash) ||
            chunk.ByteLength < 0 || chunk.ByteLength > maxBytes || exactLength && chunk.ByteLength != maxBytes)
            throw Invalid("etl.snapshot-integrity-failed");
        var stream = new FileStream(Path.Combine(root, chunk.Key), FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            if (stream.Length != chunk.ByteLength)
                throw Invalid("etl.snapshot-integrity-failed");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static EtlPipelineV2 ReadPipeline(EtlConnectorRunAdmission admission)
    {
        try
        {
            var invocation = admission.Invocation;
            if (invocation?.Scope is null || invocation.Artifact is null || admission.Bindings is null ||
                invocation.Artifact.FormatVersion != EtlArtifactV2Json.FormatVersion ||
                !EtlConnectorCsvSupport.IsHash(invocation.Artifact.ContentHash) ||
                !StringComparer.Ordinal.Equals(invocation.Artifact.ContentHash, EtlArtifactJson.Hash(invocation.Artifact.Pipeline)))
                throw Invalid("etl.batch-descriptor-invalid");
            return EtlArtifactV2Json.Read(invocation.Artifact.Pipeline);
        }
        catch (EtlConnectorInputBatchBuilderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            throw Invalid("etl.batch-descriptor-invalid");
        }
    }

    private static EtlConnectorRunAdmission SnapshotAdmission(EtlConnectorRunAdmission admission)
    {
        var bindings = admission.Bindings;
        if (bindings?.HttpShardIds is not { } shardIds)
            throw Invalid("etl.batch-descriptor-invalid");
        return admission with
        {
            Bindings = bindings with
            {
                HttpShardIds = Array.AsReadOnly(shardIds.ToArray())
            }
        };
    }

    private static void ValidatePrepared(EtlConnectorRunAdmission admission, EtlConnectorPreparedRun prepared,
        EtlPipelineV2 pipeline, int partitionOrdinal)
    {
        if (pipeline.Limits is null || pipeline.Http is null || partitionOrdinal < 0)
            throw Invalid("etl.batch-descriptor-invalid");
        var isCsv = pipeline.Source is EtlCsvSource;
        if (isCsv != (prepared.File is not null) || (pipeline.Lookup is null) != (prepared.Lookup is null))
            throw Invalid("etl.prepared-run-mismatch");

        if ((pipeline.Lookup is null) != (admission.Bindings.Lookup is null))
            throw Invalid("etl.binding-unavailable");

        if (isCsv)
        {
            var file = prepared.File!;
            var pin = PreparedPin(admission, ((EtlCsvSource)pipeline.Source).Binding, EtlFileRole.Source);
            if (file.RecordCount < 0 || file.Source.ByteLength < 0 || file.Source.ByteLength > pipeline.Limits.MaxSnapshotBytes ||
                !EtlConnectorCsvSupport.IsHash(file.Source.ContentHash) || !EtlConnectorCsvSupport.HashEquals(pin.ExpectedSha256!, file.Source.ContentHash) ||
                !EtlConnectorCsvSupport.IsChunkKey(file.Source.Key, ".csv") ||
                !EtlConnectorCsvSupport.IsChunkKey(file.Index.Key, ".index") || !EtlConnectorCsvSupport.IsHash(file.Index.ContentHash) ||
                file.Index.ByteLength != checked((file.RecordCount + 1) * IndexEntrySize) || partitionOrdinal >= Math.Min(file.RecordCount, pipeline.Limits.FilePartitionCount))
                throw Invalid("etl.prepared-run-mismatch");
        }
        else
        {
            var source = (EtlHttpJsonSource)pipeline.Source;
            var revision = admission.Bindings.Source?.Revision;
            var shardIds = admission.Bindings.HttpShardIds;
            if (admission.Bindings.Source?.Kind != EtlConnectorKind.Http || revision is null ||
                !StringComparer.Ordinal.Equals(revision.Alias, source.Binding) ||
                string.IsNullOrWhiteSpace(revision.Handle) || string.IsNullOrWhiteSpace(revision.Revision) ||
                shardIds is null || partitionOrdinal >= shardIds.Count || shardIds.Any(string.IsNullOrWhiteSpace) ||
                shardIds.Distinct(StringComparer.Ordinal).Count() != shardIds.Count)
                throw Invalid("etl.prepared-run-mismatch");
        }

        if (pipeline.Lookup is { } lookup)
        {
            var pin = PreparedPin(admission, lookup.Binding, EtlFileRole.Lookup);
            var descriptor = prepared.Lookup!;
            if (!EtlConnectorCsvSupport.IsChunkKey(descriptor.Key, ".csv") ||
                !EtlConnectorCsvSupport.IsHash(descriptor.ContentHash) ||
                !EtlConnectorCsvSupport.HashEquals(pin.ExpectedSha256!, descriptor.ContentHash) ||
                descriptor.ByteLength < 0 || descriptor.ByteLength > Math.Min(pipeline.Limits.MaxLookupBytes, pipeline.Limits.MaxSnapshotBytes))
                throw Invalid("etl.prepared-run-mismatch");
        }
    }

    private static PinnedFileBinding PreparedPin(EtlConnectorRunAdmission admission, string alias, EtlFileRole role)
    {
        var revision = role == EtlFileRole.Source ? admission.Bindings.Source?.Revision : admission.Bindings.Lookup?.Revision;
        var expectedKind = role == EtlFileRole.Source
            ? admission.Bindings.Source?.Kind
            : admission.Bindings.Lookup?.Kind;
        if (revision is null || expectedKind != EtlConnectorKind.Csv || revision.Alias != alias ||
            !EtlConnectorCsvSupport.IsHash(revision.ExpectedSha256) || !EtlConnectorCsvSupport.IsHash(revision.Handle) ||
            string.IsNullOrWhiteSpace(revision.Revision))
            throw Invalid("etl.binding-unavailable");
        return new PinnedFileBinding(admission.Invocation.Scope, revision.Alias, role, revision.Revision,
            revision.Handle, revision.ExpectedSha256!.ToLowerInvariant());
    }

    private static bool TryMeasureRecord(EtlConnectorInputRecord record, EtlPipelineV2 pipeline,
        long maxBytes, out long byteLength)
    {
        using var stream = new EtlBoundedCountingStream(maxBytes);
        try
        {
            using var writer = NewJsonWriter(stream);
            WriteRecord(writer, record, SourceFieldNames(pipeline));
            writer.Flush();
            byteLength = stream.ByteLength;
            return true;
        }
        catch (EtlPayloadLimitExceededException)
        {
            byteLength = 0;
            return false;
        }
    }

    private static (long ByteLength, string ContentHash) HashCanonicalBatch(IReadOnlyList<EtlConnectorInputRecord> records,
        EtlPipelineV2 pipeline)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new EtlBoundedHashingStream(hash, pipeline.Limits.MaxBatchBytes);
        using (var writer = NewJsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", CanonicalFormatVersion);
            writer.WritePropertyName("records");
            writer.WriteStartArray();
            for (var index = 0; index < records.Count; index++)
            {
                if (index > 0)
                    writer.Flush();
                WriteRecord(writer, records[index], SourceFieldNames(pipeline));
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        return (stream.ByteLength, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static IReadOnlyList<(string Name, EtlColumnType Type)> SourceFieldNames(EtlPipelineV2 pipeline) =>
        pipeline.Source switch
        {
            EtlCsvSource csv => csv.Schema.Select(column => (column.Name, column.Type)).ToArray(),
            EtlHttpJsonSource http => http.Fields.Select(field => (field.Name, field.Type)).ToArray(),
            _ => throw new InvalidDataException("The ETL source declaration is unsupported.")
        };

    private static void WriteRecord(Utf8JsonWriter writer, EtlConnectorInputRecord record,
        IReadOnlyList<(string Name, EtlColumnType Type)> schema)
    {
        if (record.RawInputBytes <= 0 || (record.Row is null) == (record.Error is null))
            throw new InvalidDataException("The connector input record is invalid.");
        writer.WriteStartObject();
        writer.WriteNumber("index", record.Index);
        writer.WriteNumber("rawInputBytes", record.RawInputBytes);
        if (record.Error is { } error)
        {
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("code", error.Code);
            if (error.Field is not null)
                writer.WriteString("field", error.Field);
            writer.WriteNumber("recordOrdinal", error.RecordOrdinal);
            writer.WriteEndObject();
        }
        else
        {
            var row = record.Row!;
            writer.WriteNumber("recordOrdinal", row.RecordOrdinal);
            writer.WritePropertyName("fields");
            writer.WriteStartArray();
            foreach (var field in schema)
            {
                if (!row.Values.TryGetValue(field.Name, out var value) || value.Type != field.Type)
                    throw new InvalidDataException("The connector input row does not match its pinned schema.");
                writer.WriteStartObject();
                writer.WriteString("name", field.Name);
                writer.WriteString("type", ScalarName(field.Type));
                writer.WritePropertyName("value");
                WriteValue(writer, value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static string ScalarName(EtlColumnType type) => type switch
    {
        EtlColumnType.String => "string",
        EtlColumnType.Int64 => "int64",
        EtlColumnType.Decimal => "decimal",
        EtlColumnType.Boolean => "boolean",
        _ => throw new InvalidDataException("The connector input field type is unsupported.")
    };

    private static void WriteValue(Utf8JsonWriter writer, EtlTypedValue value)
    {
        if (value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }
        switch (value.Type)
        {
            case EtlColumnType.String when value.Value is string text: writer.WriteStringValue(text); break;
            case EtlColumnType.Int64 when value.Value is long integer: writer.WriteNumberValue(integer); break;
            case EtlColumnType.Decimal when value.Value is decimal number: writer.WriteNumberValue(number); break;
            case EtlColumnType.Boolean when value.Value is bool boolean: writer.WriteBooleanValue(boolean); break;
            default: throw new InvalidDataException("The connector input scalar type is invalid.");
        }
    }

    private static Utf8JsonWriter NewJsonWriter(Stream stream) => new(stream, new JsonWriterOptions
    {
        Indented = false,
        Encoder = JavaScriptEncoder.Default
    });

    private static long EmptyEnvelopeBytes { get; } = System.Text.Encoding.UTF8.GetByteCount(EmptyEnvelope);

    private static EtlConnectorInputBatchBuildResult Failed(string code, int recordOrdinal = 0) =>
        new(null, new EtlPreparationDiagnostic(code, recordOrdinal));

    private static EtlConnectorInputBatchBuilderException Invalid(string code, int recordOrdinal = 0, string? field = null) =>
        new(new EtlPreparationDiagnostic(code, recordOrdinal, field));

    private static int SafeOrdinal(long ordinal) => (int)Math.Clamp(ordinal, 0, int.MaxValue);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_source is not null)
            await _source.DisposeAsync();
        if (_index is not null)
            await _index.DisposeAsync();
    }

    private readonly record struct IndexEntry(long Ordinal, long StartByteOffset, long EndByteOffset);

    private static readonly IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> EmptyLookupRows =
        new ReadOnlyDictionary<EtlTypedValue, EtlTypedRow>(new Dictionary<EtlTypedValue, EtlTypedRow>());
}
