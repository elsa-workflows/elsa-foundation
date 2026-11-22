using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Runtime.Execution;

/// <summary>
/// One deterministic, bounded source range. CanonicalInputBytes and ContentHash describe the
/// version-1 payload serialized from Records; the payload itself is reconstructed from retained files.
/// </summary>
internal sealed record EtlInputBatch(
    int PartitionIndex,
    long StartRecordOrdinal,
    long EndRecordOrdinalExclusive,
    int RecordCount,
    long CanonicalInputBytes,
    string ContentHash,
    IReadOnlyList<CsvRecord> Records);

internal sealed record EtlInputBatchBuildResult(EtlInputBatch? Batch, EtlPreparationDiagnostic? Diagnostic)
{
    public bool Succeeded => Diagnostic is null;
}

internal sealed class EtlInputBatchBuilderException(EtlPreparationDiagnostic diagnostic)
    : Exception(diagnostic.ToString())
{
    public EtlPreparationDiagnostic Diagnostic { get; } = diagnostic;
}

/// <summary>
/// Reconstructs bounded input batches from one prepared partition. Open once per live partition attempt;
/// the retained source and index are hashed once and their streams stay owned until disposal.
/// </summary>
/// <remarks>
/// Canonical input v1 is compact UTF-8 JSON with fixed property order and no trailing newline:
/// {"formatVersion":1,"records":[{"ordinal":N,"fields":[...]}]}. Fields remain in source CSV order.
/// This type is single-reader and is not safe for concurrent BuildNextAsync calls.
/// </remarks>
internal sealed class EtlInputBatchBuilder : IAsyncDisposable
{
    private const int IndexEntrySize = sizeof(long) * 3;
    private const int BufferSize = 4096;
    private const int CanonicalFormatVersion = 1;
    private const string EmptyEnvelope = "{\"formatVersion\":1,\"records\":[]}";
    private readonly FileStream _source;
    private readonly FileStream _index;
    private readonly EtlPipeline _pipeline;
    private readonly EtlPreparedDescriptor _prepared;
    private readonly byte[] _indexEntryBytes = new byte[IndexEntrySize];
    private bool _disposed;

    private EtlInputBatchBuilder(FileStream source, FileStream index, EtlPipeline pipeline, EtlPreparedDescriptor prepared)
    {
        _source = source;
        _index = index;
        _pipeline = pipeline;
        _prepared = prepared;
    }

    /// <summary>Opens and verifies the retained source and index once for this partition attempt.</summary>
    public static async ValueTask<EtlInputBatchBuilder> OpenAsync(
        string hostApprovedSnapshotRoot,
        EtlPipeline pipeline,
        EtlPreparedDescriptor prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApprovedSnapshotRoot);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(prepared);
        if (!Path.IsPathFullyQualified(hostApprovedSnapshotRoot))
            throw new ArgumentException("The ETL snapshot root must be an absolute host-approved path.", nameof(hostApprovedSnapshotRoot));

        FileStream? source = null;
        FileStream? index = null;
        try
        {
            try
            {
                pipeline = EtlArtifactJson.Read(EtlArtifactJson.Write(pipeline));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
            {
                throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.batch-descriptor-invalid"));
            }

            ValidateDescriptor(pipeline, prepared);
            var root = Path.GetFullPath(hostApprovedSnapshotRoot);
            source = OpenChunk(root, prepared.Inputs.SourceChunk, ".csv", pipeline.Limits.MaxSnapshotBytes);
            index = OpenChunk(root, prepared.SourceIndex, ".index",
                checked((prepared.SourceRecordCount + 1) * IndexEntrySize), exactLength: true);

            await VerifyFileAsync(source, prepared.Inputs.SourceChunk.ContentHash, cancellationToken);
            await VerifyFileAsync(index, prepared.SourceIndex.ContentHash, cancellationToken);
            source.Position = 0;
            index.Position = 0;
            return new EtlInputBatchBuilder(source, index, pipeline, prepared);
        }
        catch (EtlInputBatchBuilderException)
        {
            if (source is not null)
                await source.DisposeAsync();
            if (index is not null)
                await index.DisposeAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            if (source is not null)
                await source.DisposeAsync();
            if (index is not null)
                await index.DisposeAsync();
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (source is not null)
                await source.DisposeAsync();
            if (index is not null)
                await index.DisposeAsync();
            throw new EtlInputBatchBuilderException(EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (Exception exception) when (exception is OverflowException or InvalidDataException)
        {
            if (source is not null)
                await source.DisposeAsync();
            if (index is not null)
                await index.DisposeAsync();
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.snapshot-integrity-failed"));
        }
    }

    /// <summary>Builds the next range from a partition's durable next-record cursor.</summary>
    public async ValueTask<EtlInputBatchBuildResult> BuildNextAsync(
        int partitionIndex,
        long nextRecordOrdinal,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EtlInputBatchBuilder));

        if (!TryGetPartition(partitionIndex, out var range))
            return Failed("etl.partition-invalid");
        if (nextRecordOrdinal < range.StartRecordOrdinal || nextRecordOrdinal > range.EndRecordOrdinalExclusive)
            return Failed("etl.partition-cursor-invalid");
        if (nextRecordOrdinal == range.EndRecordOrdinalExclusive)
            return new EtlInputBatchBuildResult(null, null);

        return await BuildRangeAsync(partitionIndex, range, nextRecordOrdinal, range.EndRecordOrdinalExclusive,
            requireExactEnd: false, cancellationToken);
    }

    /// <summary>
    /// Rebuilds a previously admitted half-open range and refuses if its canonical byte charge or hash changed.
    /// </summary>
    public async ValueTask<EtlInputBatchBuildResult> RebuildAsync(
        int partitionIndex,
        long startRecordOrdinal,
        long endRecordOrdinalExclusive,
        long expectedCanonicalInputBytes,
        string expectedContentHash,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EtlInputBatchBuilder));

        if (!TryGetPartition(partitionIndex, out var range) ||
            startRecordOrdinal < range.StartRecordOrdinal ||
            endRecordOrdinalExclusive <= startRecordOrdinal ||
            endRecordOrdinalExclusive > range.EndRecordOrdinalExclusive ||
            endRecordOrdinalExclusive - startRecordOrdinal > _pipeline.Limits.MaxBatchRecords ||
            expectedCanonicalInputBytes <= 0 || expectedCanonicalInputBytes > _pipeline.Limits.MaxBatchBytes ||
            !IsHash(expectedContentHash))
            return Failed("etl.batch-identity-invalid");

        var result = await BuildRangeAsync(partitionIndex, range, startRecordOrdinal, endRecordOrdinalExclusive,
            requireExactEnd: true, cancellationToken);
        if (!result.Succeeded || result.Batch is null)
            return result;
        if (result.Batch.CanonicalInputBytes != expectedCanonicalInputBytes ||
            !HashEquals(result.Batch.ContentHash, expectedContentHash))
            return Failed("etl.batch-reconstruction-mismatch");
        return result;
    }

    private async ValueTask<EtlInputBatchBuildResult> BuildRangeAsync(
        int partitionIndex,
        EtlPartitionRange partitionRange,
        long startOrdinal,
        long maximumEndOrdinalExclusive,
        bool requireExactEnd,
        CancellationToken cancellationToken)
    {
        try
        {
            var previousEntry = await ReadIndexEntryAsync(startOrdinal - 1, cancellationToken);
            long expectedStartOffset = previousEntry.EndByteOffset;
            var records = new List<CsvRecord>();
            var canonicalBytes = EmptyEnvelopeBytes;
            var ordinal = startOrdinal;

            while (ordinal < maximumEndOrdinalExclusive && records.Count < _pipeline.Limits.MaxBatchRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var indexed = await ReadIndexEntryAsync(ordinal, cancellationToken);
                if (indexed.StartByteOffset != expectedStartOffset || indexed.EndByteOffset <= indexed.StartByteOffset ||
                    indexed.EndByteOffset > _prepared.Inputs.SourceChunk.ByteLength)
                    return Failed("etl.source-index-invalid", SafeOrdinal(ordinal));

                var record = await ReadSourceRecordAsync(indexed, cancellationToken);
                var separatorBytes = records.Count == 0 ? 0 : 1;
                var availableRecordBytes = _pipeline.Limits.MaxBatchBytes - canonicalBytes - separatorBytes;
                if (availableRecordBytes <= 0 ||
                    !TryMeasureRecord(record, availableRecordBytes, out var recordBytes))
                {
                    if (requireExactEnd || records.Count == 0)
                        return Failed("etl.batch-record-too-large", record.RecordOrdinal);
                    break;
                }

                records.Add(record);
                canonicalBytes += separatorBytes + recordBytes;
                expectedStartOffset = indexed.EndByteOffset;
                ordinal++;
            }

            if (requireExactEnd && ordinal != maximumEndOrdinalExclusive)
                return Failed("etl.batch-reconstruction-mismatch");
            if (records.Count == 0)
                return new EtlInputBatchBuildResult(null, null);

            var payload = HashCanonicalBatch(records, _pipeline.Limits.MaxBatchBytes);
            if (payload.ByteLength != canonicalBytes)
                return Failed("etl.batch-canonicalization-failed");

            var batch = new EtlInputBatch(
                partitionIndex,
                startOrdinal,
                ordinal,
                records.Count,
                payload.ByteLength,
                payload.ContentHash,
                records.AsReadOnly());
            return new EtlInputBatchBuildResult(batch, null);
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
            return new EtlInputBatchBuildResult(null, diagnostic);
        }
        catch (EtlInputBatchBuilderException exception)
        {
            return new EtlInputBatchBuildResult(null, exception.Diagnostic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new EtlInputBatchBuildResult(null, EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (OverflowException)
        {
            return Failed("etl.source-index-invalid");
        }
    }

    private async ValueTask<CsvRecord> ReadSourceRecordAsync(IndexEntry entry, CancellationToken cancellationToken)
    {
        _source.Position = entry.StartByteOffset;
        await using var limited = new EtlBoundedRangeReadStream(_source,
            entry.EndByteOffset - entry.StartByteOffset, flushSupported: false);
        await using var records = new CsvRecordReader(
                limited,
                _pipeline.Limits.MaxRecordBytes,
                _pipeline.Limits.MaxFields,
                initialRecordOrdinal: checked((int)entry.Ordinal),
                initialByteOffset: entry.StartByteOffset,
                detectBom: false)
            .ReadAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        if (!await records.MoveNextAsync())
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid", SafeOrdinal(entry.Ordinal)));

        var record = records.Current;
        if (record.RecordOrdinal != entry.Ordinal || record.StartByteOffset != entry.StartByteOffset ||
            record.EndByteOffset != entry.EndByteOffset)
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid", SafeOrdinal(entry.Ordinal)));
        if (await records.MoveNextAsync())
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid", SafeOrdinal(entry.Ordinal)));

        return record;
    }

    private async ValueTask<IndexEntry> ReadIndexEntryAsync(long ordinal, CancellationToken cancellationToken)
    {
        if (ordinal < 0 || ordinal > _prepared.SourceRecordCount)
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid", SafeOrdinal(ordinal)));

        var entryOffset = checked(ordinal * IndexEntrySize);
        if (entryOffset > _index.Length - IndexEntrySize)
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid", SafeOrdinal(ordinal)));

        _index.Position = entryOffset;
        await _index.ReadExactlyAsync(_indexEntryBytes, cancellationToken);
        var indexedOrdinal = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(_indexEntryBytes.AsSpan(0, sizeof(long)));
        var startOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(_indexEntryBytes.AsSpan(sizeof(long), sizeof(long)));
        var endOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(_indexEntryBytes.AsSpan(sizeof(long) * 2, sizeof(long)));
        if (indexedOrdinal != ordinal || startOffset < 0 || endOffset < 0)
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid", SafeOrdinal(ordinal)));
        return new IndexEntry(indexedOrdinal, startOffset, endOffset);
    }

    private bool TryGetPartition(int partitionIndex, out EtlPartitionRange range)
    {
        if (partitionIndex < 0 || partitionIndex >= _prepared.Partitions.Count)
        {
            range = default;
            return false;
        }

        range = _prepared.Partitions[partitionIndex];
        return true;
    }

    private static async ValueTask VerifyFileAsync(FileStream stream, string expectedHash, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            hash.AppendData(buffer.AsSpan(0, read));
        }

        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!HashEquals(actualHash, expectedHash))
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.snapshot-integrity-failed"));
    }

    private static FileStream OpenChunk(string root, EtlChunk chunk, string extension, long maxBytes, bool exactLength = false)
    {
        if (!IsChunkKey(chunk.Key, extension) || !IsHash(chunk.ContentHash) ||
            chunk.ByteLength < 0 || chunk.ByteLength > maxBytes || (exactLength && chunk.ByteLength != maxBytes))
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.snapshot-integrity-failed"));

        var path = Path.Combine(root, chunk.Key);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            if (stream.Length != chunk.ByteLength)
            {
                stream.Dispose();
                throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.snapshot-integrity-failed"));
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void ValidateDescriptor(EtlPipeline pipeline, EtlPreparedDescriptor prepared)
    {
        if (pipeline.Bindings is null || pipeline.SourceSchema is null || pipeline.LookupSchema is null ||
            pipeline.Limits is null || prepared.Inputs is null || prepared.SourceHeader is null ||
            prepared.LookupHeader is null || prepared.LookupRows is null || prepared.Partitions is null ||
            pipeline.Limits.MaxBatchRecords <= 0 || pipeline.Limits.MaxBatchBytes <= 0 ||
            pipeline.Limits.MaxRecordBytes <= 0 || pipeline.Limits.MaxFields <= 0 || pipeline.Limits.MaxSnapshotBytes <= 0 ||
            prepared.SourceRecordCount < 0 || prepared.Inputs.SourcePin.Role != EtlFileRole.Source ||
            prepared.Inputs.LookupPin.Role != EtlFileRole.Lookup || prepared.Inputs.SourcePin.Scope != prepared.Inputs.Scope ||
            prepared.Inputs.LookupPin.Scope != prepared.Inputs.Scope ||
            !string.Equals(prepared.Inputs.SourcePin.Alias, pipeline.Bindings.Source, StringComparison.Ordinal) ||
            !string.Equals(prepared.Inputs.LookupPin.Alias, pipeline.Bindings.Lookup, StringComparison.Ordinal) ||
            !IsHash(prepared.Inputs.SourcePin.ExpectedSha256) ||
            !HashEquals(prepared.Inputs.SourcePin.ExpectedSha256!, prepared.Inputs.SourceChunk.ContentHash) ||
            !prepared.SourceHeader.MatchesSchema(pipeline.SourceSchema))
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.batch-descriptor-invalid"));

        var pipelineHash = EtlArtifactJson.Hash(EtlArtifactJson.Write(pipeline));
        if (!HashEquals(prepared.PipelineHash, pipelineHash))
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.pipeline-mismatch"));

        try
        {
            _ = checked((prepared.SourceRecordCount + 1) * IndexEntrySize);
        }
        catch (OverflowException)
        {
            throw new EtlInputBatchBuilderException(new EtlPreparationDiagnostic("etl.source-index-invalid"));
        }
    }

    private static bool IsChunkKey(string? key, string extension)
    {
        if (key is null || key.Length != 64 + extension.Length || !key.EndsWith(extension, StringComparison.Ordinal))
            return false;
        for (var i = 0; i < 64; i++)
        {
            if (key[i] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }
        return true;
    }

    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool HashEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static int SafeOrdinal(long ordinal) => (int)Math.Clamp(ordinal, 0, int.MaxValue);

    private static long EmptyEnvelopeBytes { get; } = Encoding.UTF8.GetByteCount(EmptyEnvelope);

    private static bool TryMeasureRecord(CsvRecord record, long maxBytes, out long byteLength)
    {
        using var stream = new EtlBoundedCountingStream(maxBytes);
        try
        {
            using var writer = NewJsonWriter(stream);
            WriteRecord(writer, record);
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

    private static (long ByteLength, string ContentHash) HashCanonicalBatch(IReadOnlyList<CsvRecord> records, long maxBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new EtlBoundedHashingStream(hash, maxBytes);
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
                WriteRecord(writer, records[index]);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        return (stream.ByteLength, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static Utf8JsonWriter NewJsonWriter(Stream stream) => new(stream, new JsonWriterOptions
    {
        Indented = false,
        Encoder = JavaScriptEncoder.Default
    });

    private static void WriteRecord(Utf8JsonWriter writer, CsvRecord record)
    {
        writer.WriteStartObject();
        writer.WriteNumber("ordinal", record.RecordOrdinal);
        writer.WritePropertyName("fields");
        writer.WriteStartArray();
        foreach (var field in record.Fields)
            writer.WriteStringValue(field);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static EtlInputBatchBuildResult Failed(string code, int recordOrdinal = 0) =>
        new(null, new EtlPreparationDiagnostic(code, recordOrdinal));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _source.DisposeAsync();
        await _index.DisposeAsync();
    }

    private readonly record struct IndexEntry(long Ordinal, long StartByteOffset, long EndByteOffset);
}
