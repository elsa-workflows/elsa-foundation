using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Preparation;

internal sealed record EtlConnectorPreparationResult(EtlConnectorPreparedRun? Prepared, EtlPreparationDiagnostic? Diagnostic)
{
    public bool Succeeded => Prepared is not null && Diagnostic is null;

    /// <summary>
    /// Neither prepared nor failed: a source was transiently unavailable, so the caller records nothing and the lapsed
    /// lease lets preparation run again (spec 1198).
    /// </summary>
    public bool Transient => Prepared is null && Diagnostic is null;

    /// <summary>The driver failure behind a transient result, for the caller's log; never persisted or exposed.</summary>
    public Exception? TransientCause { get; init; }

    /// <summary>
    /// A RabbitMQ source's received, unacknowledged messages (spec 1201): the caller acknowledges them only once the prepared
    /// run is recorded, and otherwise disposes the receipt so the broker requeues them.
    /// </summary>
    public IEtlRabbitMqReceipt? Receipt { get; init; }
}

/// <summary>A source the preparer read could not be reached for now; preparation records nothing and runs again.</summary>
internal sealed class EtlSourceTransientException(Exception cause) : Exception("A source read failed transiently.", cause);

/// <summary>Retains and validates only the file-backed connector inputs pinned by one admitted v2 invocation.</summary>
/// <remarks>HTTP source collections are deliberately not snapshotted before dispatch. The object root is created only when admitted preparation runs.</remarks>
internal sealed class EtlConnectorPreparer
{
    private readonly HostFileBindings _bindings;
    private readonly string _snapshotRoot;
    private readonly IFileChunkPublisher _csvPublisher;
    private readonly IFileChunkPublisher _jsonlPublisher;
    private readonly IFileChunkPublisher _indexPublisher;
    private readonly long _maxSourceRecords;

    /// <summary>The host SQL bindings a SQL source reads through; without them a SQL source is unavailable.</summary>
    internal HostSqlBindings? SqlBindings { get; init; }

    internal HostRabbitMqBindings? RabbitMqBindings { get; init; }

    public EtlConnectorPreparer(HostFileBindings bindings, string hostApprovedSnapshotRoot)
        : this(bindings, hostApprovedSnapshotRoot, new FileChunkPublisherFactory())
    {
    }

    public EtlConnectorPreparer(HostFileBindings bindings, string hostApprovedSnapshotRoot,
        IFileChunkPublisherFactory publisherFactory)
        : this(bindings, hostApprovedSnapshotRoot, publisherFactory, int.MaxValue)
    {
    }

    /// <param name="maxSourceRecords">The largest JSON Lines record count; routed record ordinals are 32-bit.</param>
    internal EtlConnectorPreparer(HostFileBindings bindings, string hostApprovedSnapshotRoot,
        IFileChunkPublisherFactory publisherFactory, long maxSourceRecords)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(publisherFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApprovedSnapshotRoot);
        if (!Path.IsPathFullyQualified(hostApprovedSnapshotRoot))
            throw new ArgumentException("The ETL snapshot root must be an absolute host-approved path.", nameof(hostApprovedSnapshotRoot));

        _bindings = bindings;
        _snapshotRoot = Path.GetFullPath(hostApprovedSnapshotRoot);
        _csvPublisher = publisherFactory.Create(EtlFileChunkFormat.Csv);
        _jsonlPublisher = publisherFactory.Create(EtlFileChunkFormat.JsonlSnapshot);
        _indexPublisher = publisherFactory.Create(EtlFileChunkFormat.Index);
        _maxSourceRecords = maxSourceRecords;
    }

    /// <summary>Prepares an admitted connector input set and returns the exact durable references used by the store.</summary>
    /// <param name="watermarkFloor">A SQL source's checkpoint: only rows above it are read (spec 1199); null reads every row.</param>
    public async ValueTask<EtlConnectorPreparationResult> PrepareAsync(EtlConnectorRunAdmission admission,
        CancellationToken cancellationToken = default, long? watermarkFloor = null)
    {
        if (!TryReadAdmission(admission, out var pipeline, out var sourcePin, out var lookupPin, out var diagnostic))
            return new(null, diagnostic);

        // Messages a RabbitMQ source received stay unacknowledged; unless they reach the caller with a prepared run, disposing
        // the receipt lets the broker requeue them (spec 1201).
        IEtlRabbitMqReceipt? receipt = null;
        var handedOver = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_snapshotRoot);
            if (!Directory.Exists(_snapshotRoot))
                return Failed("etl.snapshot-write-failed");

            EtlPreparedCsvInput? file = null;
            EtlChunk? sourceChunk = null;
            if (pipeline!.Source is EtlCsvSource or EtlJsonlSource)
            {
                sourceChunk = await PublishInputSnapshotAsync(admission, sourcePin!,
                    pipeline.Limits.MaxSnapshotBytes,
                    pipeline.Source is EtlJsonlSource ? _jsonlPublisher : _csvPublisher, cancellationToken);
            }
            long? sourceWatermark = null;
            if (pipeline.Source is EtlSqlSource sql)
            {
                sourceChunk = await PublishSqlSnapshotAsync(admission, pipeline, sql, watermarkFloor, cancellationToken);
                if (sql.Watermark is { } column)
                    sourceWatermark = await EtlSqlSnapshotReader.ReadHighWatermarkAsync(Path.Combine(_snapshotRoot, sourceChunk.Key),
                        column, cancellationToken);
            }

            EtlObjectReference? lookup = null;
            if (pipeline.Lookup is { } lookupDeclaration)
            {
                var maxLookupBytes = Math.Min(pipeline.Limits.MaxLookupBytes, pipeline.Limits.MaxSnapshotBytes);
                var chunk = await PublishInputSnapshotAsync(admission, lookupPin!, maxLookupBytes, _csvPublisher, cancellationToken);
                // Preparation must reject a malformed lookup before any partition is made runnable.
                _ = await EtlConnectorCsvSupport.ReadLookupRowsAsync(_snapshotRoot, EtlRuntimeMetadata.ToReference(chunk),
                    lookupPin!, lookupDeclaration, pipeline.Limits, cancellationToken);
                lookup = EtlRuntimeMetadata.ToReference(chunk);
            }
            // A queue is drained after the lookup is published, so the drain knows how much of the retained-input budget is left.
            if (pipeline.Source is EtlRabbitMqSource queue)
                sourceChunk = await PublishRabbitMqSnapshotAsync(admission, pipeline, queue, lookup?.ByteLength ?? 0,
                    received => receipt = received, cancellationToken);

            // A malformed lookup must not leave a published source index that could be mistaken
            // for a runnable preparation by a later recovery attempt.
            if (sourceChunk is not null)
            {
                var index = pipeline.Source is EtlCsvSource csvSource
                    ? await PublishSourceIndexAsync(admission, pipeline, csvSource, sourcePin!, sourceChunk, cancellationToken)
                    // A SQL snapshot has no admission digest; its anchor is its own published hash (spec 1198).
                    : await PublishJsonlSourceIndexAsync(admission, pipeline, admission.Bindings.Source.Revision.Handle,
                        sourcePin?.ExpectedSha256 ?? sourceChunk.ContentHash, sourceChunk, cancellationToken);
                file = new EtlPreparedCsvInput(EtlRuntimeMetadata.ToReference(sourceChunk), EtlRuntimeMetadata.ToReference(index),
                    ReadIndexRecordCount(index));
            }

            handedOver = true;
            return new(new EtlConnectorPreparedRun(file, lookup) { SourceWatermark = sourceWatermark }, null) { Receipt = receipt };
        }
        catch (EtlConnectorPreparationFailure failure)
        {
            return new(null, failure.Diagnostic);
        }
        catch (EtlSqlTransientException exception)
        {
            return new(null, null) { TransientCause = exception.InnerException };
        }
        catch (EtlSourceTransientException exception)
        {
            return new(null, null) { TransientCause = exception.InnerException };
        }
        catch (CsvReadException exception)
        {
            var csvDiagnostic = new EtlPreparationDiagnostic(exception.StableCode, exception.RecordOrdinal);
            if (exception.StableCode == "csv.source-read-failed")
                csvDiagnostic = csvDiagnostic with { Kind = EtlPreparationDiagnosticKind.Fault, Cause = exception.InnerException ?? exception };
            return new(null, csvDiagnostic);
        }
        catch (EtlBindingException exception)
        {
            return exception.Code == "etl.binding-unavailable"
                ? Failed(exception.Code)
                : Failed(EtlPreparationDiagnostic.Fault(exception.Code, exception));
        }
        catch (EtlEffectException exception)
        {
            return Failed(EtlPreparationDiagnostic.Fault("etl.snapshot-write-failed", exception));
        }
        catch (InvalidDataException)
        {
            return Failed("etl.snapshot-integrity-failed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(EtlPreparationDiagnostic.Fault("etl.snapshot-write-failed", exception));
        }
        catch (OverflowException)
        {
            return Failed("etl.source-index-too-large");
        }
        finally
        {
            if (!handedOver && receipt is not null)
                await receipt.DisposeAsync();
        }
    }

    /// <summary>
    /// Receives a RabbitMQ source's messages without acknowledging them and writes their bodies as a JSON Lines snapshot
    /// (spec 1201). The snapshot's identity is unique to the attempt: a predecessor's snapshot holds messages the broker
    /// requeued when its connection closed, so reusing it would read them twice.
    /// </summary>
    private async ValueTask<EtlChunk> PublishRabbitMqSnapshotAsync(EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline,
        EtlRabbitMqSource queue, long lookupBytes, Action<IEtlRabbitMqReceipt> received, CancellationToken cancellationToken)
    {
        var revision = admission.Bindings.Source.Revision;
        var identity = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Kind = "connector-input-snapshot",
            Invocation = EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation),
            Role = EtlFileRole.Source.ToString(),
            PinHandle = revision.Handle,
            Attempt = Guid.NewGuid().ToString("N")
        });
        var maxBytes = pipeline.Limits.MaxSnapshotBytes;
        // The store refuses a prepared run whose retained input (the snapshot, its index of one entry per record and a sentinel,
        // and the lookup) passes the retained-input limit, so the drain stops short of that too and the rest stay queued.
        var maxRetainedBytes = pipeline.Http.MaxRetainedInputBytes - lookupBytes;
        var bindings = RabbitMqBindings ?? throw new EtlBindingException("etl.rabbitmq-unavailable", "No ETL RabbitMQ bindings are configured.");
        var binding = bindings.ResolveExact(admission.Invocation.Scope, queue.Binding, EtlFileRole.Source.ToString(),
            revision.Revision, revision.Handle, queue.Queue);
        var consumer = binding.Consumer ?? throw new EtlBindingException("etl.rabbitmq-unavailable", "No ETL RabbitMQ consumer is installed.");
        // Each body is written to the snapshot as it arrives, so preparation never holds the messages in memory; the bound is
        // checked on the written line, and the first body that would pass it goes back to the queue.
        return await _jsonlPublisher.PublishAsync(_snapshotRoot, identity, maxBytes, async (destination, ct) =>
        {
            long written = 0;
            long records = 0;
            var refusedFirst = false;
            // A failure writing the snapshot is the snapshot's, not the broker's, so it is not classified as a read failure.
            Exception? writeFailure = null;
            IEtlRabbitMqReceipt receipt;
            try
            {
                receipt = await consumer.ReceiveAsync(binding.ConnectionString, queue.Queue, queue.MaxMessages, async (body, token) =>
                {
                    var line = EtlRabbitMqSnapshotWriter.Line(body);
                    var snapshotBytes = written + line.Length + 1;
                    if (snapshotBytes > maxBytes ||
                        snapshotBytes + (records + 2) * EtlConnectorCsvSupport.IndexEntrySize > maxRetainedBytes)
                    {
                        refusedFirst = written == 0;
                        return false;
                    }
                    try
                    {
                        await destination.WriteAsync(line, token);
                        await destination.WriteAsync(EtlRabbitMqSnapshotWriter.LineFeed, token);
                    }
                    catch (Exception exception)
                    {
                        writeFailure = exception;
                        throw;
                    }
                    written = snapshotBytes;
                    records++;
                    return true;
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (EtlConnectorPreparationFailure or EtlBindingException) &&
                                              !ReferenceEquals(exception, writeFailure))
            {
                throw consumer.Classify(exception) == EtlRabbitMqErrorKind.Transient
                    ? new EtlSourceTransientException(exception)
                    : new EtlConnectorPreparationFailure(EtlPreparationDiagnostic.Fault("etl.rabbitmq-read-rejected", exception));
            }
            received(receipt);
            if (refusedFirst)
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.snapshot-too-large"));
        }, cancellationToken);
    }

    private async ValueTask<EtlChunk> PublishInputSnapshotAsync(EtlConnectorRunAdmission admission,
        PinnedFileBinding pin, long maxBytes, IFileChunkPublisher publisher, CancellationToken cancellationToken)
    {
        var identity = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Kind = "connector-input-snapshot",
            Invocation = EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation),
            Role = pin.Role.ToString(),
            PinHandle = pin.Handle
        });

        return await publisher.PublishAsync(_snapshotRoot, identity, maxBytes,
            async (destination, ct) =>
            {
                await using var input = _bindings.OpenInput(pin);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[4096];
                long copied = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, ct);
                    if (count == 0)
                        break;
                    if (count > maxBytes - copied)
                        throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.snapshot-too-large"));
                    hash.AppendData(buffer.AsSpan(0, count));
                    await destination.WriteAsync(buffer.AsMemory(0, count), ct);
                    copied += count;
                }

                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!EtlConnectorCsvSupport.HashEquals(pin.ExpectedSha256!, actual))
                    throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.snapshot-digest-mismatch"));
            }, cancellationToken);
    }

    /// <summary>
    /// Reuses the run's published SQL snapshot when one exists, so a replacement never reads a second, different point in
    /// time; otherwise reads the table once (spec 1198). The identity matches a file snapshot's, keyed by the pinned handle.
    /// </summary>
    private async ValueTask<EtlChunk> PublishSqlSnapshotAsync(EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline,
        EtlSqlSource sql, long? watermarkFloor, CancellationToken cancellationToken)
    {
        var revision = admission.Bindings.Source.Revision;
        var identity = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Kind = "connector-input-snapshot",
            Invocation = EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation),
            Role = EtlFileRole.Source.ToString(),
            PinHandle = revision.Handle
        });
        var maxBytes = pipeline.Limits.MaxSnapshotBytes;
        if (await _jsonlPublisher.FindAsync(_snapshotRoot, identity, maxBytes, cancellationToken) is { } published)
            return published;
        var bindings = SqlBindings ?? throw new EtlBindingException("etl.sql-unavailable", "No ETL SQL bindings are configured.");
        var binding = bindings.ResolveExact(admission.Invocation.Scope, sql.Binding, EtlFileRole.Source.ToString(),
            revision.Revision, revision.Handle, sql.Table);
        try
        {
            return await _jsonlPublisher.PublishAsync(_snapshotRoot, identity, maxBytes,
                (destination, ct) => EtlSqlSnapshotReader.WriteAsync(binding, sql, maxBytes, destination, ct, watermarkFloor), cancellationToken);
        }
        catch (EtlEffectException exception) when (exception.Code == "etl.effect-conflict")
        {
            // A predecessor whose lease lapsed mid-read published first; its point in time is the run's, so adopt it.
            return await _jsonlPublisher.FindAsync(_snapshotRoot, identity, maxBytes, cancellationToken) ?? throw exception;
        }
    }

    private async ValueTask<EtlChunk> PublishSourceIndexAsync(EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline,
        EtlCsvSource csv, PinnedFileBinding sourcePin, EtlChunk source, CancellationToken cancellationToken)
    {
        if (source.ByteLength == 0)
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.source-header-missing"));

        var identity = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Kind = "connector-csv-source-index",
            Invocation = EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation),
            SourcePinHandle = sourcePin.Handle,
            SourceKey = source.Key,
            SourceHash = source.ContentHash
        });
        var maxIndexBytes = checked((source.ByteLength + 1) * EtlConnectorCsvSupport.IndexEntrySize);
        return await _indexPublisher.PublishAsync(_snapshotRoot, identity, maxIndexBytes,
            async (destination, ct) =>
            {
                await using var file = EtlConnectorCsvSupport.OpenSnapshot(_snapshotRoot,
                    EtlRuntimeMetadata.ToReference(source), ".csv", pipeline.Limits.MaxSnapshotBytes,
                    sourcePin.ExpectedSha256!);
                using var verified = new EtlConnectorCsvVerifiedReadStream(file, source.ByteLength,
                    pipeline.Limits.MaxSnapshotBytes, sourcePin.ExpectedSha256!, source.ContentHash);
                var reader = new CsvRecordReader(verified, pipeline.Limits.MaxRecordBytes, pipeline.Limits.MaxFields);
                var indexEntry = new byte[EtlConnectorCsvSupport.IndexEntrySize];
                EtlCsvHeaderMap? header = null;
                long sourceRecordCount = 0;
                await foreach (var record in reader.ReadAsync(ct).WithCancellation(ct).ConfigureAwait(false))
                {
                    if (header is null)
                    {
                        if (!EtlRowTransformer.TryCompileHeader(csv.Schema, record, out header, out var error))
                            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic(error!.Code,
                                error.RecordOrdinal, error.Field));
                    }
                    else
                    {
                        sourceRecordCount++;
                    }

                    EtlConnectorCsvSupport.WriteIndexEntry(indexEntry, record.RecordOrdinal,
                        record.StartByteOffset, record.EndByteOffset);
                    await destination.WriteAsync(indexEntry, ct);
                }

                verified.VerifyComplete();
                if (header is null)
                    throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.source-header-missing"));
            }, cancellationToken);
    }

    private async ValueTask<EtlChunk> PublishJsonlSourceIndexAsync(EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline,
        string sourcePinHandle, string expectedSourceHash, EtlChunk source, CancellationToken cancellationToken)
    {
        // Unlike CSV there is no header, so an empty snapshot is a valid zero-record input.
        var identity = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Kind = "connector-jsonl-source-index",
            Invocation = EtlRuntimeIdentity.InvocationFingerprint(admission.Invocation),
            SourcePinHandle = sourcePinHandle,
            SourceKey = source.Key,
            SourceHash = source.ContentHash
        });
        var maxIndexBytes = checked((source.ByteLength + 1) * EtlConnectorCsvSupport.IndexEntrySize);
        return await _indexPublisher.PublishAsync(_snapshotRoot, identity, maxIndexBytes,
            async (destination, ct) =>
            {
                await using var file = EtlConnectorCsvSupport.OpenSnapshot(_snapshotRoot,
                    EtlRuntimeMetadata.ToReference(source), ".jsonl", pipeline.Limits.MaxSnapshotBytes,
                    expectedSourceHash);
                using var verified = new EtlConnectorCsvVerifiedReadStream(file, source.ByteLength,
                    pipeline.Limits.MaxSnapshotBytes, expectedSourceHash, source.ContentHash);
                await EtlJsonlLineIndexer.WriteIndexAsync(verified, destination, _maxSourceRecords, ct);
                verified.VerifyComplete();
            }, cancellationToken);
    }

    private static long ReadIndexRecordCount(EtlChunk index)
    {
        if (index.ByteLength < EtlConnectorCsvSupport.IndexEntrySize ||
            index.ByteLength % EtlConnectorCsvSupport.IndexEntrySize != 0)
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.source-index-invalid"));
        return index.ByteLength / EtlConnectorCsvSupport.IndexEntrySize - 1;
    }

    private static bool TryReadAdmission(EtlConnectorRunAdmission admission, out EtlPipelineV2? pipeline,
        out PinnedFileBinding? sourcePin, out PinnedFileBinding? lookupPin,
        out EtlPreparationDiagnostic? diagnostic)
    {
        pipeline = null;
        sourcePin = null;
        lookupPin = null;
        diagnostic = null;
        try
        {
            if (admission?.Invocation?.Scope is null || admission.Invocation.Artifact is null ||
                admission.Bindings is null || string.IsNullOrWhiteSpace(admission.Invocation.RunId) ||
                admission.Invocation.RunId.Length > 256 || admission.Invocation.RunId.Any(char.IsControl) ||
                admission.Invocation.Artifact.FormatVersion != EtlArtifactV2Json.FormatVersion ||
                !EtlConnectorCsvSupport.IsHash(admission.Invocation.Artifact.ContentHash) ||
                !EtlConnectorCsvSupport.HashEquals(admission.Invocation.Artifact.ContentHash,
                    EtlArtifactJson.Hash(admission.Invocation.Artifact.Pipeline)))
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));

            pipeline = EtlArtifactV2Json.Read(admission.Invocation.Artifact.Pipeline);
            ValidatePolicy(pipeline);
            var source = admission.Bindings.Source;
            if (source?.Revision is null || source.Kind is not (EtlConnectorKind.Csv or EtlConnectorKind.Http or EtlConnectorKind.Jsonl or EtlConnectorKind.Sql or
                    EtlConnectorKind.RabbitMq) ||
                !StringComparer.Ordinal.Equals(source.Revision.Alias, pipeline.Source switch
                {
                    EtlCsvSource csv => csv.Binding,
                    EtlHttpJsonSource http => http.Binding,
                    EtlJsonlSource jsonl => jsonl.Binding,
                    EtlSqlSource sql => sql.Binding,
                    EtlRabbitMqSource queue => queue.Binding,
                    _ => null
                }))
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));

            var expectedSourceKind = pipeline.Source switch
            {
                EtlCsvSource => EtlConnectorKind.Csv,
                EtlJsonlSource => EtlConnectorKind.Jsonl,
                EtlSqlSource => EtlConnectorKind.Sql,
                EtlRabbitMqSource => EtlConnectorKind.RabbitMq,
                _ => EtlConnectorKind.Http
            };
            if (source.Kind != expectedSourceKind)
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));
            if (pipeline.Source is EtlCsvSource or EtlJsonlSource)
            {
                sourcePin = ToPin(admission.Invocation.Scope, source.Revision, EtlFileRole.Source);
                if (!EtlConnectorCsvSupport.IsHash(source.Revision.ExpectedSha256))
                    throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));
            }
            // A SQL source's or a queue's content is unknown until it is read, so its pin carries no digest.
            if (pipeline.Source is EtlSqlSource or EtlRabbitMqSource && (source.Revision.ExpectedSha256 is not null ||
                    !EtlConnectorCsvSupport.IsHash(source.Revision.Handle) || string.IsNullOrWhiteSpace(source.Revision.Revision)))
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));

            if (pipeline.Lookup is { } lookup)
            {
                if (admission.Bindings.Lookup is not { Kind: EtlConnectorKind.Csv, Revision: { } lookupRevision } ||
                    !StringComparer.Ordinal.Equals(lookupRevision.Alias, lookup.Binding) ||
                    !EtlConnectorCsvSupport.IsHash(lookupRevision.ExpectedSha256))
                    throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));
                lookupPin = ToPin(admission.Invocation.Scope, lookupRevision, EtlFileRole.Lookup);
            }
            else if (admission.Bindings.Lookup is not null)
            {
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));
            }

            if (pipeline.Source is EtlHttpJsonSource && admission.Bindings.HttpShardIds is not { Count: > 0 } ||
                pipeline.Source is EtlCsvSource or EtlJsonlSource or EtlSqlSource or EtlRabbitMqSource && admission.Bindings.HttpShardIds is { Count: > 0 })
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));

            return true;
        }
        catch (EtlConnectorPreparationFailure failure)
        {
            diagnostic = failure.Diagnostic;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            diagnostic = new EtlPreparationDiagnostic("etl.pipeline-invalid");
        }

        pipeline = null;
        sourcePin = null;
        lookupPin = null;
        return false;
    }

    private static void ValidatePolicy(EtlPipelineV2 pipeline)
    {
        var limits = pipeline.Limits;
        var http = pipeline.Http;
        if (limits is null || http is null || limits.MaxSnapshotBytes <= 0 || limits.MaxLookupBytes <= 0 ||
            limits.MaxRecordBytes <= 0 || limits.MaxFields <= 0 || limits.MaxLookupRecords <= 0 ||
            limits.FilePartitionCount <= 0 || limits.MaxPartitions < limits.FilePartitionCount ||
            limits.MaxBatchRecords <= 0 || limits.MaxBatchBytes <= 0 ||
            limits.MaxRecordBytes > limits.MaxBatchBytes || http.MaxPageBytes <= 0 || http.MaxPageRecords <= 0)
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));

        if (pipeline.Source is null || pipeline.Source is EtlCsvSource csv &&
            (csv.Schema is null || csv.Schema.Count is 0 || csv.Schema.Count > limits.MaxFields))
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
        if (pipeline.Source is EtlJsonlSource jsonl &&
            (jsonl.Fields is null || jsonl.Fields.Count is 0 || jsonl.Fields.Count > limits.MaxFields))
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
        if (pipeline.Source is EtlSqlSource sql && (sql.Columns is null || sql.Columns.Count is 0 ||
                sql.Columns.Count > limits.MaxFields || sql.Key is not { Count: > 0 } || string.IsNullOrEmpty(sql.Table)))
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
        if (pipeline.Source is EtlRabbitMqSource queue && (queue.Fields is null || queue.Fields.Count is 0 ||
                queue.Fields.Count > limits.MaxFields || string.IsNullOrEmpty(queue.Queue) ||
                queue.MaxMessages is < 1 or > EtlRabbitMqSource.MaxMessagesLimit))
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
        if (pipeline.Lookup is { } lookup &&
            (lookup.Schema is null || lookup.Schema.Count is 0 || lookup.Schema.Count > limits.MaxFields))
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
    }

    private static PinnedFileBinding ToPin(EtlScope scope, EtlBindingRevision revision, EtlFileRole role)
    {
        if (revision.Alias is null || string.IsNullOrWhiteSpace(revision.Revision) ||
            !EtlConnectorCsvSupport.IsHash(revision.Handle))
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.binding-unavailable"));
        return new PinnedFileBinding(scope, revision.Alias, role, revision.Revision, revision.Handle,
            revision.ExpectedSha256?.ToLowerInvariant());
    }

    private static EtlConnectorPreparationResult Failed(string code) =>
        new(null, new EtlPreparationDiagnostic(code));

    private static EtlConnectorPreparationResult Failed(EtlPreparationDiagnostic diagnostic) => new(null, diagnostic);
}

/// <summary>Shared bounded CSV snapshot validation for connector preparation and per-partition recovery.</summary>
internal static class EtlConnectorCsvSupport
{
    internal const int IndexEntrySize = sizeof(long) * 3;

    internal static async ValueTask<IReadOnlyDictionary<EtlTypedValue, EtlTypedRow>> ReadLookupRowsAsync(string root,
        EtlObjectReference chunk, PinnedFileBinding pin, EtlCsvLookup lookup, EtlConnectorLimits limits,
        CancellationToken cancellationToken)
    {
        await using var file = OpenSnapshot(root, chunk, ".csv", Math.Min(limits.MaxLookupBytes, limits.MaxSnapshotBytes),
            pin.ExpectedSha256!);
        using var verified = new EtlConnectorCsvVerifiedReadStream(file, chunk.ByteLength,
            Math.Min(limits.MaxLookupBytes, limits.MaxSnapshotBytes), pin.ExpectedSha256!, chunk.ContentHash);
        var rows = new Dictionary<EtlTypedValue, EtlTypedRow>();
        EtlCsvHeaderMap? header = null;
        long count = 0;
        var reader = new CsvRecordReader(verified, limits.MaxRecordBytes, limits.MaxFields);
        await foreach (var record in reader.ReadAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (header is null)
            {
                if (!EtlRowTransformer.TryCompileHeader(lookup.Schema, record, out header, out var headerError))
                    throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic(headerError!.Code,
                        headerError.RecordOrdinal, headerError.Field));
                continue;
            }

            if (count >= limits.MaxLookupRecords)
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.lookup-too-many-records", record.RecordOrdinal));
            count++;
            if (!EtlRowTransformer.TryParseTypedRow(lookup.Schema, header!, record, out var row, out var rowError))
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic(rowError!.Code,
                    rowError.RecordOrdinal, rowError.Field));
            if (!row!.Values.TryGetValue(lookup.LookupKeyField, out var key) || key.Value is null)
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.lookup-key-missing",
                    record.RecordOrdinal, lookup.LookupKeyField));
            if (!rows.TryAdd(key, FreezeRow(row)))
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.lookup-key-duplicate",
                    record.RecordOrdinal, lookup.LookupKeyField));
        }

        verified.VerifyComplete();
        if (header is null)
            throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.lookup-header-missing"));
        return new ReadOnlyDictionary<EtlTypedValue, EtlTypedRow>(rows);
    }

    internal static FileStream OpenSnapshot(string root, EtlObjectReference chunk, string extension,
        long maxBytes, string expectedPinHash)
    {
        if (!Path.IsPathFullyQualified(root) || !IsChunkKey(chunk.Key, extension) || !IsHash(chunk.ContentHash) ||
            !IsHash(expectedPinHash) || !HashEquals(chunk.ContentHash, expectedPinHash) ||
            chunk.ByteLength < 0 || chunk.ByteLength > maxBytes)
            throw new InvalidDataException("A retained ETL input descriptor is invalid.");

        var stream = new FileStream(Path.Combine(root, chunk.Key), FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length != chunk.ByteLength)
                throw new InvalidDataException("A retained ETL input length does not match its descriptor.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static void WriteIndexEntry(Span<byte> entry, long ordinal, long start, long end)
    {
        BinaryPrimitives.WriteInt64LittleEndian(entry[..sizeof(long)], ordinal);
        BinaryPrimitives.WriteInt64LittleEndian(entry.Slice(sizeof(long), sizeof(long)), start);
        BinaryPrimitives.WriteInt64LittleEndian(entry.Slice(sizeof(long) * 2, sizeof(long)), end);
    }

    internal static EtlTypedRow FreezeRow(EtlTypedRow row) => new(row.RecordOrdinal,
        new ReadOnlyDictionary<string, EtlTypedValue>(row.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsChunkKey(string? key, string extension)
    {
        if (key is null || key.Length != 64 + extension.Length || !key.EndsWith(extension, StringComparison.Ordinal))
            return false;
        for (var index = 0; index < 64; index++)
            if (key[index] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }

    internal static bool HashEquals(string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

}

internal sealed class EtlConnectorPreparationFailure(EtlPreparationDiagnostic diagnostic) : Exception(diagnostic.ToString())
{
    internal EtlPreparationDiagnostic Diagnostic { get; } = diagnostic;
}

/// <summary>Hashes precisely the bounded CSV bytes consumed and verifies both pin and object descriptor at EOF.</summary>
internal sealed class EtlConnectorCsvVerifiedReadStream(Stream source, long expectedLength, long maxLength,
    string expectedPinHash, string expectedChunkHash) : Stream
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
        if (_bytesRead != expectedLength || !EtlConnectorCsvSupport.HashEquals(expectedPinHash, actual) ||
            !EtlConnectorCsvSupport.HashEquals(expectedChunkHash, actual))
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
