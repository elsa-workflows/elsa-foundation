using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>Copies pinned files into immutable run snapshots and prepares a bounded lookup and source index.</summary>
/// <remarks>The constructor's snapshot root and binding registry are trusted host configuration.</remarks>
internal sealed class EtlFilePreparer
{
    private const int BufferSize = 4096;
    private const int IndexEntrySize = sizeof(long) * 3;
    private const int SnapshotFormatVersion = 1;
    private const int SourceIndexFormatVersion = 1;
    private readonly HostFileBindings _bindings;
    private readonly string _snapshotRoot;
    private readonly FileChunkStore _snapshotStore = new(".csv");
    private readonly FileChunkStore _indexStore = new(".index");

    public EtlFilePreparer(HostFileBindings bindings, string hostApprovedSnapshotRoot)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApprovedSnapshotRoot);
        if (!Path.IsPathFullyQualified(hostApprovedSnapshotRoot))
            throw new ArgumentException("The ETL snapshot root must be an absolute host-approved path.", nameof(hostApprovedSnapshotRoot));

        _bindings = bindings;
        _snapshotRoot = Path.GetFullPath(hostApprovedSnapshotRoot);
    }

    /// <summary>Retains exactly the source and lookup revisions resolved at admission.</summary>
    public async ValueTask<EtlSnapshotResult> CreateSnapshotsAsync(
        EtlScope scope,
        string runId,
        EtlPipeline pipeline,
        PinnedFileBinding sourcePin,
        PinnedFileBinding lookupPin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(sourcePin);
        ArgumentNullException.ThrowIfNull(lookupPin);

        var capturedPipeline = CapturePipeline(pipeline, out _);
        if (capturedPipeline is null)
            return new EtlSnapshotResult(null, new EtlPreparationDiagnostic("etl.pipeline-invalid"));
        pipeline = capturedPipeline;

        var invalid = ValidateInvocation(scope, runId, pipeline, sourcePin, lookupPin);
        if (invalid is not null)
            return new EtlSnapshotResult(null, invalid);

        try
        {
            var source = await PublishInputSnapshotAsync(sourcePin, runId, pipeline.Limits.MaxSnapshotBytes, cancellationToken);
            var lookup = await PublishInputSnapshotAsync(lookupPin, runId,
                Math.Min(pipeline.Limits.MaxLookupBytes, pipeline.Limits.MaxSnapshotBytes), cancellationToken);
            return new EtlSnapshotResult(new EtlInputSnapshots(scope, runId, sourcePin, lookupPin, source, lookup), null);
        }
        catch (PreparationFailure failure)
        {
            return new EtlSnapshotResult(null, failure.Diagnostic);
        }
        catch (EtlBindingException exception)
        {
            return new EtlSnapshotResult(null, exception.Code == "etl.binding-unavailable"
                ? new EtlPreparationDiagnostic(exception.Code)
                : EtlPreparationDiagnostic.Fault(exception.Code, exception));
        }
        catch (EtlEffectException exception)
        {
            return new EtlSnapshotResult(null, EtlPreparationDiagnostic.Fault(exception.Code, exception));
        }
        catch (IOException exception)
        {
            return new EtlSnapshotResult(null, EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return new EtlSnapshotResult(null, EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
    }

    /// <summary>Revalidates retained snapshots, fully prepares the lookup, and publishes an index only on success.</summary>
    public async ValueTask<EtlPreparationResult> PrepareAsync(
        EtlPipeline pipeline,
        EtlInputSnapshots snapshots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(snapshots);

        var capturedPipeline = CapturePipeline(pipeline, out var pipelineHash);
        if (capturedPipeline is null || pipelineHash is null)
            return FailedPreparation("etl.pipeline-invalid");
        pipeline = capturedPipeline;

        var invalid = ValidateInvocation(snapshots.Scope, snapshots.RunId, pipeline, snapshots.SourcePin, snapshots.LookupPin);
        if (invalid is not null)
            return new EtlPreparationResult(null, invalid);

        try
        {
            var lookupResult = await ReadLookupAsync(pipeline, snapshots, cancellationToken);
            if (lookupResult.Diagnostic is not null)
                return new EtlPreparationResult(null, lookupResult.Diagnostic);

            if (snapshots.SourceChunk.ByteLength == 0)
                return FailedPreparation("etl.source-header-missing");

            var indexLimit = checked(snapshots.SourceChunk.ByteLength * IndexEntrySize);
            EtlCsvHeaderMap? sourceHeader = null;
            long sourceRecordCount = 0;
            var identity = SourceIndexIdentity(snapshots, pipelineHash);
            var sourceIndex = await _indexStore.PublishAsync(_snapshotRoot, identity, indexLimit,
                async (destination, ct) =>
                {
                    await using var file = OpenRetainedSnapshot(snapshots.SourceChunk, snapshots.SourcePin,
                        ".csv", pipeline.Limits.MaxSnapshotBytes);
                    using var verified = new VerifiedReadStream(file, snapshots.SourceChunk.ByteLength,
                        pipeline.Limits.MaxSnapshotBytes, snapshots.SourcePin.ExpectedSha256!, snapshots.SourceChunk.ContentHash);
                    var reader = new CsvRecordReader(verified, pipeline.Limits.MaxRecordBytes, pipeline.Limits.MaxFields);
                    var indexEntry = new byte[IndexEntrySize];
                    await foreach (var record in reader.ReadAsync(ct).WithCancellation(ct).ConfigureAwait(false))
                    {
                        if (sourceHeader is null)
                        {
                            if (!EtlRowTransformer.TryCompileHeader(pipeline.SourceSchema, record, out sourceHeader, out var headerError))
                                throw new PreparationFailure(new EtlPreparationDiagnostic(headerError!.Code,
                                    headerError.RecordOrdinal, headerError.Field));
                        }
                        else
                        {
                            sourceRecordCount++;
                        }

                        WriteIndexEntry(indexEntry, record.RecordOrdinal, record.StartByteOffset, record.EndByteOffset);
                        await destination.WriteAsync(indexEntry, ct);
                    }

                    verified.VerifyComplete();
                    if (sourceHeader is null)
                        throw new PreparationFailure(new EtlPreparationDiagnostic("etl.source-header-missing"));
                }, cancellationToken);

            var prepared = new EtlPreparedDescriptor(
                pipelineHash,
                snapshots,
                sourceIndex,
                sourceHeader!,
                lookupResult.Header!,
                lookupResult.Rows!,
                sourceRecordCount,
                new EtlPartitionPlan(sourceRecordCount, pipeline.Limits.PartitionCount));
            return new EtlPreparationResult(prepared, null);
        }
        catch (PreparationFailure failure)
        {
            return new EtlPreparationResult(null, failure.Diagnostic);
        }
        catch (CsvReadException exception)
        {
            var diagnostic = new EtlPreparationDiagnostic(exception.StableCode, exception.RecordOrdinal);
            if (exception.StableCode == "csv.source-read-failed")
                diagnostic = diagnostic with { Kind = EtlPreparationDiagnosticKind.Fault, Cause = exception.InnerException ?? exception };
            return new EtlPreparationResult(null, diagnostic);
        }
        catch (EtlEffectException exception)
        {
            return new EtlPreparationResult(null, EtlPreparationDiagnostic.Fault(exception.Code, exception));
        }
        catch (InvalidDataException)
        {
            return FailedPreparation("etl.snapshot-integrity-failed");
        }
        catch (IOException exception)
        {
            return FailedPreparation(EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return FailedPreparation(EtlPreparationDiagnostic.Fault("etl.snapshot-read-failed", exception));
        }
        catch (OverflowException)
        {
            return FailedPreparation("etl.source-index-too-large");
        }
    }

    private async ValueTask<EtlChunk> PublishInputSnapshotAsync(
        PinnedFileBinding pin,
        string runId,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var identity = JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            SnapshotFormatVersion,
            RunId = runId,
            Role = pin.Role.ToString(),
            PinHandle = pin.Handle
        });

        return await _snapshotStore.PublishAsync(_snapshotRoot, identity, maxBytes,
            async (destination, ct) =>
            {
                await using var input = _bindings.OpenInput(pin);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[BufferSize];
                long bytesCopied = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(), ct);
                    if (count == 0)
                        break;
                    if (count > maxBytes - bytesCopied)
                        throw new PreparationFailure(new EtlPreparationDiagnostic("etl.snapshot-too-large"));

                    hash.AppendData(buffer.AsSpan(0, count));
                    await destination.WriteAsync(buffer.AsMemory(0, count), ct);
                    bytesCopied += count;
                }

                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!HashEquals(pin.ExpectedSha256!, actualHash))
                    throw new PreparationFailure(new EtlPreparationDiagnostic("etl.snapshot-digest-mismatch"));
            }, cancellationToken);
    }

    private async ValueTask<LookupPreparation> ReadLookupAsync(
        EtlPipeline pipeline,
        EtlInputSnapshots snapshots,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<EtlTypedValue, EtlTypedRow>();
        EtlCsvHeaderMap? header = null;
        long recordCount = 0;

        try
        {
            await using var file = OpenRetainedSnapshot(snapshots.LookupChunk, snapshots.LookupPin,
                ".csv", Math.Min(pipeline.Limits.MaxLookupBytes, pipeline.Limits.MaxSnapshotBytes));
            using var verified = new VerifiedReadStream(file, snapshots.LookupChunk.ByteLength,
                Math.Min(pipeline.Limits.MaxLookupBytes, pipeline.Limits.MaxSnapshotBytes),
                snapshots.LookupPin.ExpectedSha256!, snapshots.LookupChunk.ContentHash);
            var reader = new CsvRecordReader(verified, pipeline.Limits.MaxRecordBytes, pipeline.Limits.MaxFields);
            await foreach (var record in reader.ReadAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (header is null)
                {
                    if (!EtlRowTransformer.TryCompileHeader(pipeline.LookupSchema, record, out header, out var headerError))
                        throw new PreparationFailure(new EtlPreparationDiagnostic(headerError!.Code,
                            headerError.RecordOrdinal, headerError.Field));
                    continue;
                }

                if (recordCount >= pipeline.Limits.MaxLookupRecords)
                    throw new PreparationFailure(new EtlPreparationDiagnostic("etl.lookup-too-many-records", record.RecordOrdinal));
                recordCount++;

                if (!EtlRowTransformer.TryParseTypedRow(pipeline.LookupSchema, header!, record,
                        out var row, out var rowError))
                    throw new PreparationFailure(new EtlPreparationDiagnostic(rowError!.Code,
                        rowError.RecordOrdinal, rowError.Field));

                var typedRow = row!;
                if (!typedRow.Values.TryGetValue(pipeline.LookupKeyField, out var key))
                    throw new PreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
                if (key.Value is null)
                    throw new PreparationFailure(new EtlPreparationDiagnostic("etl.lookup-key-missing",
                        record.RecordOrdinal, pipeline.LookupKeyField));
                if (!rows.TryAdd(key, FreezeRow(typedRow)))
                    throw new PreparationFailure(new EtlPreparationDiagnostic("etl.lookup-key-duplicate",
                        record.RecordOrdinal, pipeline.LookupKeyField));
            }

            verified.VerifyComplete();
            if (header is null)
                throw new PreparationFailure(new EtlPreparationDiagnostic("etl.lookup-header-missing"));
            return new LookupPreparation(header, new ReadOnlyDictionary<EtlTypedValue, EtlTypedRow>(rows), null);
        }
        catch (PreparationFailure failure)
        {
            return new LookupPreparation(null, null, failure.Diagnostic);
        }
        catch (CsvReadException exception)
        {
            var diagnostic = new EtlPreparationDiagnostic(exception.StableCode, exception.RecordOrdinal);
            if (exception.StableCode == "csv.source-read-failed")
                diagnostic = diagnostic with { Kind = EtlPreparationDiagnosticKind.Fault, Cause = exception.InnerException ?? exception };
            return new LookupPreparation(null, null, diagnostic);
        }
        catch (InvalidDataException)
        {
            return new LookupPreparation(null, null, new EtlPreparationDiagnostic("etl.snapshot-integrity-failed"));
        }
    }

    private FileStream OpenRetainedSnapshot(EtlChunk chunk, PinnedFileBinding pin, string extension, long maxBytes)
    {
        if (!IsChunkKey(chunk.Key, extension) || chunk.ByteLength < 0 || chunk.ByteLength > maxBytes ||
            !ValidHash(chunk.ContentHash) || !ValidHash(pin.ExpectedSha256) ||
            !HashEquals(chunk.ContentHash, pin.ExpectedSha256!))
            throw new InvalidDataException("A retained ETL input descriptor is invalid.");

        var path = Path.Combine(_snapshotRoot, chunk.Key);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("A retained ETL input is unavailable.", exception);
        }

        long length;
        try
        {
            length = stream.Length;
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        if (length > maxBytes)
        {
            stream.Dispose();
            throw new InvalidDataException("A retained ETL input exceeds its permitted size.");
        }

        return stream;
    }

    private static EtlTypedRow FreezeRow(EtlTypedRow row) =>
        new(row.RecordOrdinal, new ReadOnlyDictionary<string, EtlTypedValue>(
            row.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));

    private static void WriteIndexEntry(byte[] entry, int ordinal, long start, long end)
    {
        BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(0, sizeof(long)), ordinal);
        BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(sizeof(long), sizeof(long)), start);
        BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(sizeof(long) * 2, sizeof(long)), end);
    }

    private static string SourceIndexIdentity(EtlInputSnapshots snapshots, string pipelineHash) =>
        JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            SourceIndexFormatVersion,
            RunId = snapshots.RunId,
            PipelineHash = pipelineHash,
            SourcePinHandle = snapshots.SourcePin.Handle,
            LookupPinHandle = snapshots.LookupPin.Handle
        });

    private static EtlPipeline? CapturePipeline(EtlPipeline pipeline, out string? hash)
    {
        try
        {
            var document = EtlArtifactJson.Write(pipeline);
            hash = EtlArtifactJson.Hash(document);
            return EtlArtifactJson.Read(document);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            hash = null;
            return null;
        }
    }

    private static EtlPreparationDiagnostic? ValidateInvocation(
        EtlScope scope,
        string runId,
        EtlPipeline pipeline,
        PinnedFileBinding sourcePin,
        PinnedFileBinding lookupPin)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 256 || runId.Any(char.IsControl))
            return new EtlPreparationDiagnostic("etl.run-id-invalid");
        if (pipeline.Bindings is null || pipeline.SourceSchema is null || pipeline.LookupSchema is null || pipeline.Limits is null ||
            pipeline.Limits.MaxSnapshotBytes <= 0 || pipeline.Limits.MaxLookupBytes <= 0 ||
            pipeline.Limits.MaxRecordBytes <= 0 || pipeline.Limits.MaxFields <= 0 ||
            pipeline.Limits.MaxLookupRecords <= 0 || pipeline.Limits.PartitionCount <= 0)
            return new EtlPreparationDiagnostic("etl.pipeline-invalid");
        if (sourcePin.Scope != scope || sourcePin.Role != EtlFileRole.Source ||
            sourcePin.Alias != pipeline.Bindings.Source || !ValidHash(sourcePin.ExpectedSha256) || !ValidHash(sourcePin.Handle) ||
            lookupPin.Scope != scope || lookupPin.Role != EtlFileRole.Lookup ||
            lookupPin.Alias != pipeline.Bindings.Lookup || !ValidHash(lookupPin.ExpectedSha256) || !ValidHash(lookupPin.Handle))
            return new EtlPreparationDiagnostic("etl.binding-unavailable");
        return null;
    }

    private static bool IsChunkKey(string? key, string extension)
    {
        if (key is null || key.Length != 64 + extension.Length || !key.EndsWith(extension, StringComparison.Ordinal))
            return false;

        for (var index = 0; index < 64; index++)
        {
            if (!Uri.IsHexDigit(key[index]))
                return false;
        }

        return true;
    }

    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static bool HashEquals(string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static EtlPreparationResult FailedPreparation(string code) =>
        new(null, new EtlPreparationDiagnostic(code));

    private static EtlPreparationResult FailedPreparation(EtlPreparationDiagnostic diagnostic) =>
        new(null, diagnostic);

    private sealed record LookupPreparation(
        EtlCsvHeaderMap? Header,
        IReadOnlyDictionary<EtlTypedValue, EtlTypedRow>? Rows,
        EtlPreparationDiagnostic? Diagnostic);

    private sealed class PreparationFailure(EtlPreparationDiagnostic diagnostic) : Exception
    {
        public EtlPreparationDiagnostic Diagnostic { get; } = diagnostic;
    }

    /// <summary>Hashes exactly the bytes consumed by the parser and checks digest and length at EOF.</summary>
    private sealed class VerifiedReadStream(
        Stream source,
        long expectedLength,
        long maxLength,
        string expectedPinHash,
        string expectedChunkHash) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _bytesRead;
        private bool _complete;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_complete)
                return 0;

            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                VerifyComplete();
                return 0;
            }

            if (count > maxLength - _bytesRead)
                throw new InvalidDataException("A retained ETL input exceeds its permitted size.");
            _hash.AppendData(buffer.Span[..count]);
            _bytesRead += count;
            return count;
        }

        public void VerifyComplete()
        {
            if (_complete)
                return;

            _complete = true;
            var actual = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            if (_bytesRead != expectedLength || !HashEquals(expectedPinHash, actual) || !HashEquals(expectedChunkHash, actual))
                throw new InvalidDataException("A retained ETL input failed integrity validation.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _hash.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
