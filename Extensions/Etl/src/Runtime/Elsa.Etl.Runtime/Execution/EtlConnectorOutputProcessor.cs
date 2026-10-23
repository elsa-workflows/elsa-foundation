using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Execution;

internal sealed record EtlConnectorOutputPreparationResult(EtlStoreStatus Status,
    EtlConnectorBatchOutput? Output, string? FailureCode)
{
    public bool Succeeded => Status is (EtlStoreStatus.Applied or EtlStoreStatus.Replay) && Output is not null;
}

internal sealed record EtlConnectorRouteDispatchResult(EtlStoreStatus Status, bool ReadyToAccept,
    bool Yield, string? FailureCode);

/// <summary>Builds and retains immutable connector route bodies, then records their physical delivery evidence.</summary>
/// <remarks>Acceptance is deliberately left to the coordinating worker after this type reports every route ready.</remarks>
internal sealed class EtlConnectorOutputProcessor
{
    private readonly IEtlConnectorRuntimeStore _store;
    private readonly HostFileBindings _fileBindings;
    private readonly HostHttpBindings _httpBindings;
    private readonly IFileChunkPublisher _jsonlPublisher;
    private readonly FileChunkStore _jsonlStore = new(".jsonl");
    private readonly FileChunkStore _jsonStore = new(".json");
    private readonly EtlHttpTransport _transport;
    private readonly string _snapshotRoot;

    public EtlConnectorOutputProcessor(IEtlConnectorRuntimeStore store, HostFileBindings fileBindings,
        HostHttpBindings httpBindings, IFileChunkPublisherFactory publishers, EtlHttpTransport transport,
        string snapshotRoot)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fileBindings = fileBindings ?? throw new ArgumentNullException(nameof(fileBindings));
        _httpBindings = httpBindings ?? throw new ArgumentNullException(nameof(httpBindings));
        ArgumentNullException.ThrowIfNull(publishers);
        _jsonlPublisher = publishers.Create(EtlFileChunkFormat.Jsonl);
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (string.IsNullOrWhiteSpace(snapshotRoot) || !Path.IsPathFullyQualified(snapshotRoot))
            throw new ArgumentException("The ETL retained-output root must be an absolute host-approved path.", nameof(snapshotRoot));
        _snapshotRoot = Path.GetFullPath(snapshotRoot);
    }

    /// <summary>Transforms one reserved batch once and durably retains every route body before dispatch.</summary>
    public async ValueTask<EtlConnectorOutputPreparationResult> TransformAndRetainAsync(
        EtlConnectorPartitionLease lease, EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline,
        EtlConnectorInputBatch input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invalid = ValidateInput(lease, admission, pipeline, input);
        if (invalid is not null)
            return Failed(invalid);

        var descriptor = input.Descriptor;
        var owed = lease.OwedBatch;
        if (owed is null || owed.Batch != descriptor || owed.Settled)
            return Failed("etl.connector-output-reservation-invalid");

        // Replaying this transition also restores any acceptance headroom missing from a migrated checkpoint.
        // Never regenerate a retained plan: the store's byte references are the exact retry payloads.
        if (owed.Output is { } retained)
            return FromStore(await _store.RetainConnectorBatchOutputAsync(lease, descriptor.BatchId, retained, cancellationToken));

        EtlConnectorBatchOutput output;
        try
        {
            output = await BuildOutputAsync(admission, pipeline, descriptor, input, cancellationToken);
        }
        catch (EtlEffectException exception)
        {
            return Failed(exception.Code);
        }
        catch (EtlBindingException exception)
        {
            return Failed(exception.Code);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or
                                           JsonException or InvalidDataException)
        {
            return Failed("etl.connector-output-invalid");
        }

        return FromStore(await _store.RetainConnectorBatchOutputAsync(lease, descriptor.BatchId, output, cancellationToken));
    }

    /// <summary>Dispatches retained routes and returns only after every route has durable physical evidence.</summary>
    public async ValueTask<EtlConnectorRouteDispatchResult> DispatchAsync(EtlConnectorPartitionLease lease,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlConnectorBatchOutput output,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batch = lease?.OwedBatch?.Batch;
        if (!ValidDispatch(lease, admission, pipeline, batch, output))
            return DispatchFailed("etl.connector-output-invalid");
        if (lease!.OwedBatch!.Output is not { } owedOutput || owedOutput != output)
            return DispatchFailed("etl.connector-output-conflict");

        try
        {
            var allReady = true;
            foreach (var (routeId, routeOutput, destination) in Routes(pipeline, output))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (routeOutput is null)
                    continue;

                if (routeOutput.Kind == EtlConnectorKind.Jsonl)
                {
                    var fileResult = await PublishFileRouteAsync(lease, admission, pipeline, routeId, routeOutput, cancellationToken);
                    if (fileResult.FailureCode is not null || fileResult.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                        return fileResult;
                    allReady &= fileResult.ReadyToAccept;
                    continue;
                }

                if (routeOutput.Kind != EtlConnectorKind.Http || destination is not EtlHttpDestination httpDestination)
                    return DispatchFailed("etl.connector-output-invalid");

                var delivery = await DispatchHttpRouteAsync(lease, admission, pipeline, routeId, routeOutput,
                    httpDestination, cancellationToken);
                if (delivery.FailureCode is not null || delivery.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                    return delivery;
                allReady &= delivery.ReadyToAccept;
                if (delivery.Yield)
                    return delivery with { ReadyToAccept = false };
            }

            return new(EtlStoreStatus.Applied, allReady, !allReady, null);
        }
        catch (EtlEffectException exception)
        {
            return DispatchFailed(exception.Code);
        }
        catch (EtlBindingException exception)
        {
            return DispatchFailed(exception.Code);
        }
    }

    private async ValueTask<EtlConnectorBatchOutput> BuildOutputAsync(EtlConnectorRunAdmission admission,
        EtlPipelineV2 pipeline, EtlConnectorBatchDescriptor descriptor, EtlConnectorInputBatch input,
        CancellationToken cancellationToken)
    {
        using var main = new RouteBodyBuilder(pipeline.Routes.Main is EtlHttpDestination, pipeline.Limits.MaxOutputBatchBytes);
        using var errors = new RouteBodyBuilder(pipeline.Routes.Errors is EtlHttpDestination, pipeline.Limits.MaxOutputBatchBytes);
        var transformer = new EtlRowTransformer(pipeline, input.LookupRows);
        var accepted = 0L;
        var rejected = 0L;
        var filtered = 0L;
        var remainingBytes = pipeline.Limits.MaxOutputBatchBytes;

        foreach (var record in input.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record is null || record.RawInputBytes < 0 || (record.Row is null) == (record.Error is null))
                throw new InvalidDataException("The typed connector batch contains an invalid record.");

            if (record.Error is { } sourceError)
            {
                remainingBytes -= errors.AppendError(sourceError, remainingBytes, cancellationToken);
                rejected++;
                continue;
            }

            var transformed = transformer.Transform(record.Row!, remainingBytes);
            switch (transformed.Disposition)
            {
                case EtlRowDisposition.Accepted:
                    remainingBytes -= main.AppendRecord(transformed.JsonlBytes, remainingBytes);
                    accepted++;
                    break;
                case EtlRowDisposition.Filtered:
                    filtered++;
                    break;
                case EtlRowDisposition.Rejected:
                    remainingBytes -= errors.AppendError(transformed.Error!, remainingBytes, cancellationToken);
                    rejected++;
                    break;
                default:
                    throw new InvalidDataException("The connector row disposition is unsupported.");
            }
        }

        if (checked(accepted + rejected + filtered) != input.Records.Count)
            throw new InvalidDataException("The connector output does not account for its input records.");

        var (mainBytes, mainFinishBytes) = main.Finish(remainingBytes);
        remainingBytes -= mainFinishBytes;
        var (errorBytes, errorFinishBytes) = errors.Finish(remainingBytes);
        remainingBytes -= errorFinishBytes;
        var totalBytes = checked((long)(mainBytes?.Length ?? 0) + (errorBytes?.Length ?? 0));
        if (totalBytes != pipeline.Limits.MaxOutputBatchBytes - remainingBytes)
            throw new EtlEffectException("etl.output-too-large", "ETL output exceeds the configured byte limit.");

        var mainOutput = mainBytes is null ? null : await RetainRouteBodyAsync(admission, descriptor,
            EtlOutputRoute.Main, EtlConnectorKindFor(pipeline.Routes.Main), accepted, mainBytes,
            pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        var errorOutput = errorBytes is null ? null : await RetainRouteBodyAsync(admission, descriptor,
            EtlOutputRoute.Error, EtlConnectorKindFor(pipeline.Routes.Errors), rejected, errorBytes,
            pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        return new EtlConnectorBatchOutput(accepted, rejected, filtered, mainOutput, errorOutput);
    }

    private async ValueTask<EtlConnectorRouteOutput> RetainRouteBodyAsync(EtlConnectorRunAdmission admission,
        EtlConnectorBatchDescriptor descriptor, EtlOutputRoute route, EtlConnectorKind kind, long records,
        byte[] bytes, long maxBytes, CancellationToken cancellationToken)
    {
        var contentHash = Hash(bytes);
        var effectId = EtlRuntimeIdentity.ConnectorEffectId(admission, descriptor.BatchId, route,
            contentHash, bytes.LongLength, records);
        var storageIdentity = RetainedBodyIdentity(kind, effectId);
        var bodyStore = BodyStore(kind);
        var chunk = await bodyStore.PublishAsync(_snapshotRoot, storageIdentity, maxBytes,
            (destination, token) => destination.WriteAsync(bytes, token), cancellationToken);
        if (chunk.ByteLength != bytes.LongLength || chunk.ContentHash != contentHash)
            throw new EtlEffectException("etl.effect-conflict", "The retained ETL route body conflicts with its content identity.");

        var verified = await bodyStore.ReadAsync(_snapshotRoot, storageIdentity, maxBytes, bytes.LongLength, cancellationToken);
        if (verified is null || !verified.AsSpan().SequenceEqual(bytes) || Hash(verified) != contentHash)
            throw new EtlEffectException("etl.effect-conflict", "The retained ETL route body could not be verified.");
        return new(kind, new EtlObjectReference(chunk.Key, chunk.ContentHash, chunk.ByteLength), records, effectId);
    }

    private async ValueTask<EtlConnectorRouteDispatchResult> PublishFileRouteAsync(EtlConnectorPartitionLease lease,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlOutputRoute routeId,
        EtlConnectorRouteOutput route, CancellationToken cancellationToken)
    {
        var bytes = await ReadRetainedRouteBodyAsync(route, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        var revision = DestinationRevision(admission, routeId);
        var role = FileRole(routeId);
        var outputRoot = _fileBindings.GetOutputRoot(EtlRuntimeMetadata.PinnedBinding(lease.Scope, revision.Revision, role));
        EtlChunk publication;
        try
        {
            publication = await _jsonlPublisher.PublishAsync(outputRoot, route.EffectId,
                pipeline.Limits.MaxOutputBatchBytes,
                (destination, token) => destination.WriteAsync(bytes, token), cancellationToken);
        }
        catch (EtlEffectException exception)
        {
            return DispatchFailed(exception.Code);
        }
        catch (EtlBindingException exception)
        {
            return DispatchFailed(exception.Code);
        }

        if (publication.Key != EtlRuntimeIdentity.ConnectorFileOutputKey(route.EffectId) ||
            publication.ContentHash != route.Body.ContentHash || publication.ByteLength != route.Body.ByteLength)
            return DispatchFailed("etl.output-identity-invalid");

        var verified = await _jsonlStore.ReadAsync(outputRoot, route.EffectId, pipeline.Limits.MaxOutputBatchBytes,
            route.Body.ByteLength, cancellationToken);
        if (verified is null || !verified.AsSpan().SequenceEqual(bytes) || Hash(verified) != route.Body.ContentHash)
            return DispatchFailed("etl.effect-conflict");

        var recorded = await _store.RecordConnectorFilePublicationAsync(lease, lease.OwedBatch!.Batch.BatchId,
            routeId, new EtlObjectReference(publication.Key, publication.ContentHash, publication.ByteLength), cancellationToken);
        return recorded is EtlStoreStatus.Applied or EtlStoreStatus.Replay
            ? new(recorded, ReadyToAccept: true, Yield: false, FailureCode: null)
            : new(recorded, ReadyToAccept: false, Yield: recorded is EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict,
                FailureCode: recorded is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound or
                    EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict ? null : "etl.file-publication-refused");
    }

    private async ValueTask<EtlConnectorRouteDispatchResult> DispatchHttpRouteAsync(EtlConnectorPartitionLease lease,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlOutputRoute routeId,
        EtlConnectorRouteOutput route, EtlHttpDestination destination, CancellationToken cancellationToken)
    {
        var batchId = lease.OwedBatch!.Batch.BatchId;
        var bytes = await ReadRetainedRouteBodyAsync(route, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        var begin = await _store.BeginConnectorHttpDeliveryAsync(lease, batchId, routeId,
            Guid.NewGuid().ToString("N"), cancellationToken);
        if (begin.Status is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound)
            return new(begin.Status, false, false, null);
        if (begin.Status == EtlStoreStatus.PermanentBudgetExceeded)
            return DispatchFailed("etl.http.delivery-retention-limit");
        if (begin.Status is EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict)
            return new(begin.Status, false, true, null);
        if (begin.Delivery is { Phase: EtlHttpDeliveryPhase.Acknowledged })
            return new(begin.Status, true, false, null);
        if (begin.Delivery is { Phase: EtlHttpDeliveryPhase.Failed } failed)
            return DispatchFailed(DeliveryFailureCode(failed.Failure));
        if (begin.Permit is not { } permit)
            return new(begin.Status, false, begin.Delivery?.Phase is EtlHttpDeliveryPhase.InFlight or EtlHttpDeliveryPhase.Waiting,
                begin.Status is EtlStoreStatus.Invalid ? "etl.http.delivery-transition-invalid" : null);

        // A Replay permit is evidence of a still-live physical attempt, not permission to send it again.
        if (begin.Status != EtlStoreStatus.Applied)
            return new(begin.Status, false, true, null);

        EtlHttpDeliveryResponse? response = null;
        string? bindingFailure = null;
        try
        {
            var revision = DestinationRevision(admission, routeId);
            await using (var binding = await _httpBindings.ResolveExactAsync(lease.Scope, destination.Binding,
                             RouteRole(routeId), revision.Revision.Revision, revision.Revision.Handle, cancellationToken))
                response = await _transport.DeliverAsync(binding, permit, bytes, pipeline.Http.MaxPageBytes, cancellationToken);
        }
        catch (EtlBindingException exception)
        {
            bindingFailure = exception.Code;
        }

        // If pin re-resolution or request validation failed after allocation, close the permit as uncertain.
        // This remains conservative if the exception occurred after the receiver may have observed the request.
        var completed = await _store.CompleteConnectorHttpDeliveryAsync(lease, batchId, routeId, permit,
            response ?? new EtlHttpDeliveryResponse(null, null), cancellationToken);
        if (completed.Status is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound)
            return new(completed.Status, false, false, null);
        if (completed.Status == EtlStoreStatus.PermanentBudgetExceeded)
            return DispatchFailed("etl.http.delivery-retention-limit");
        if (completed.Delivery is { Phase: EtlHttpDeliveryPhase.Acknowledged })
            return new(completed.Status, true, false, bindingFailure is null ? null : "etl.binding-unavailable");
        if (completed.Delivery is { Phase: EtlHttpDeliveryPhase.Failed } terminal)
            return DispatchFailed(DeliveryFailureCode(terminal.Failure));
        if (completed.Status is EtlStoreStatus.Applied or EtlStoreStatus.Replay)
            return new(completed.Status, false, true, bindingFailure is null ? null : "etl.binding-unavailable");
        return new(completed.Status, false, completed.Status is EtlStoreStatus.Conflict or EtlStoreStatus.BudgetExceeded,
            completed.Status == EtlStoreStatus.Invalid ? "etl.http.delivery-transition-invalid" : null);
    }

    private async ValueTask<byte[]> ReadRetainedRouteBodyAsync(EtlConnectorRouteOutput route, long maxBytes,
        CancellationToken cancellationToken)
    {
        var store = BodyStore(route.Kind);
        var identity = RetainedBodyIdentity(route.Kind, route.EffectId);
        if (route.Body.Key != store.KeyFor(identity) || route.Body.ByteLength <= 0 || route.Body.ByteLength > maxBytes)
            throw new EtlEffectException("etl.output-read-conflict", "The retained ETL route body reference is invalid.");
        var bytes = await store.ReadAsync(_snapshotRoot, identity, maxBytes, route.Body.ByteLength, cancellationToken);
        if (bytes is null || Hash(bytes) != route.Body.ContentHash)
            throw new EtlEffectException("etl.output-read-conflict", "The retained ETL route body failed integrity verification.");
        return bytes;
    }

    private static string? ValidateInput(EtlConnectorPartitionLease? lease, EtlConnectorRunAdmission? admission,
        EtlPipelineV2? pipeline, EtlConnectorInputBatch? input)
    {
        if (lease is null || admission?.Invocation is null || admission.Bindings is null || pipeline?.Routes is null ||
            pipeline.Limits is null || input?.Descriptor is null || input.Records is null || input.LookupRows is null ||
            lease.Scope != admission.Invocation.Scope || lease.RunId != admission.Invocation.RunId ||
            input.PartitionOrdinal != lease.PartitionOrdinal || pipeline.Limits.MaxOutputBatchBytes <= 0 ||
            input.Descriptor.Range is null || !ValidInputRecordCount(input.Descriptor, input.Records.Count))
            return "etl.connector-output-input-invalid";
        return null;
    }

    private static bool ValidDispatch(EtlConnectorPartitionLease? lease, EtlConnectorRunAdmission? admission,
        EtlPipelineV2? pipeline, EtlConnectorBatchDescriptor? batch, EtlConnectorBatchOutput? output) =>
        lease is not null && admission?.Invocation is not null && admission.Bindings is not null && pipeline?.Routes is not null &&
        pipeline.Limits is not null && batch is not null && output is not null &&
        lease.Scope == admission.Invocation.Scope && lease.RunId == admission.Invocation.RunId && lease.OwedBatch is { Settled: false } &&
        lease.OwedBatch.Batch == batch && batch.BatchId.Length == 64 &&
        output.AcceptedRecords >= 0 && output.RejectedRecords >= 0 && output.FilteredRecords >= 0;

    private static bool ValidInputRecordCount(EtlConnectorBatchDescriptor descriptor, int count) => descriptor.Range switch
    {
        EtlConnectorFileBatchRange file => file.StartOrdinal > 0 && file.EndOrdinalExclusive > file.StartOrdinal &&
            file.EndOrdinalExclusive - file.StartOrdinal == count,
        EtlConnectorHttpBatchRange http => http.PageSequence >= 0 && http.StartItem >= 0 &&
            http.EndItemExclusive > http.StartItem && http.EndItemExclusive - http.StartItem == count,
        _ => false
    };

    private static IEnumerable<(EtlOutputRoute Id, EtlConnectorRouteOutput? Output, EtlDestination Destination)> Routes(
        EtlPipelineV2 pipeline, EtlConnectorBatchOutput output)
    {
        yield return (EtlOutputRoute.Main, output.Main, pipeline.Routes.Main);
        yield return (EtlOutputRoute.Error, output.Errors, pipeline.Routes.Errors);
    }

    private static EtlConnectorBindingRevision DestinationRevision(EtlConnectorRunAdmission admission, EtlOutputRoute route) =>
        route == EtlOutputRoute.Main ? admission.Bindings.Main : admission.Bindings.Errors;

    private static EtlFileRole FileRole(EtlOutputRoute route) => route == EtlOutputRoute.Main
        ? EtlFileRole.MainOutput : EtlFileRole.ErrorOutput;

    private static string RouteRole(EtlOutputRoute route) => route == EtlOutputRoute.Main ? "MainOutput" : "ErrorOutput";

    private static EtlConnectorKind EtlConnectorKindFor(EtlDestination destination) => destination switch
    {
        EtlHttpDestination => EtlConnectorKind.Http,
        EtlJsonlDestination => EtlConnectorKind.Jsonl,
        _ => throw new InvalidDataException("The connector output destination is unsupported.")
    };

    private FileChunkStore BodyStore(EtlConnectorKind kind) => kind switch
    {
        EtlConnectorKind.Http => _jsonStore,
        EtlConnectorKind.Jsonl => _jsonlStore,
        _ => throw new EtlEffectException("etl.connector-output-invalid", "The connector route body kind is unsupported.")
    };

    private static string RetainedBodyIdentity(EtlConnectorKind kind, string effectId) => kind switch
    {
        EtlConnectorKind.Http => "etl-connector-http-route-body-v1:" + effectId,
        EtlConnectorKind.Jsonl => "etl-connector-jsonl-route-body-v1:" + effectId,
        _ => throw new EtlEffectException("etl.connector-output-invalid", "The connector route body kind is unsupported.")
    };

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static EtlConnectorOutputPreparationResult FromStore(EtlConnectorOutputResult result) =>
        new(result.Status, result.Output, result.Status switch
        {
            EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.BudgetExceeded or
                EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound => null,
            EtlStoreStatus.PermanentBudgetExceeded => "etl.connector-output-retention-limit",
            EtlStoreStatus.Invalid => "etl.connector-output-invalid",
            EtlStoreStatus.Conflict => "etl.connector-output-conflict",
            _ => "etl.connector-output-refused"
        });

    private static EtlConnectorOutputPreparationResult Failed(string code) =>
        new(EtlStoreStatus.Invalid, null, code);

    private static EtlConnectorRouteDispatchResult DispatchFailed(string code) =>
        new(EtlStoreStatus.Invalid, false, false, code);

    private static string DeliveryFailureCode(EtlHttpDeliveryFailure failure) => failure switch
    {
        EtlHttpDeliveryFailure.AttemptsExhausted => "etl.http.delivery-attempts-exhausted",
        EtlHttpDeliveryFailure.DeadlineExceeded => "etl.http.delivery-deadline-exceeded",
        EtlHttpDeliveryFailure.ResponseRejected => "etl.http.delivery-rejected",
        _ => "etl.http.delivery-failed"
    };

    private sealed class RouteBodyBuilder(bool jsonArray, long maxBytes) : IDisposable
    {
        private readonly MemoryStream _stream = new();
        private long _count;
        private bool _finished;

        public long AppendRecord(ReadOnlyMemory<byte> jsonlLine, long remainingOutputBytes)
        {
            if (_finished || jsonlLine.IsEmpty || jsonlLine.Span[^1] != (byte)'\n')
                throw new InvalidDataException("The transformed connector row is not one complete JSONL record.");
            var before = _stream.Length;
            var allowance = Math.Min(maxBytes, remainingOutputBytes);
            if (jsonArray)
            {
                if (_count == 0)
                    Write([(byte)'['], before, allowance);
                else
                    Write([(byte)','], before, allowance);
                Write(jsonlLine.Span[..^1], before, allowance);
            }
            else
                Write(jsonlLine.Span, before, allowance);
            _count++;
            return _stream.Length - before;
        }

        public long AppendError(EtlRowError error, long remainingOutputBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var encoded = new MemoryStream();
            using (var bounded = new BoundedWriteStream(encoded, Math.Min(maxBytes, remainingOutputBytes)))
            using (var writer = new Utf8JsonWriter(bounded, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("recordOrdinal", error.RecordOrdinal);
                writer.WriteString("code", error.Code);
                if (error.Field is not null)
                    writer.WriteString("field", error.Field);
                writer.WriteEndObject();
                writer.Flush();
                bounded.WriteByte((byte)'\n');
            }
            return AppendRecord(encoded.ToArray(), remainingOutputBytes);
        }

        public (byte[]? Bytes, long AddedBytes) Finish(long remainingOutputBytes)
        {
            if (_finished)
                throw new InvalidOperationException("The connector route body was already finalized.");
            _finished = true;
            if (_count == 0)
                return (null, 0);
            var before = _stream.Length;
            if (jsonArray)
                Write([(byte)']'], before, Math.Min(maxBytes, remainingOutputBytes));
            return (_stream.ToArray(), _stream.Length - before);
        }

        private void Write(ReadOnlySpan<byte> bytes, long appendStart, long appendLimit)
        {
            if (bytes.Length > maxBytes - _stream.Length || bytes.Length > appendLimit - (_stream.Length - appendStart))
                throw new EtlEffectException("etl.output-too-large", "ETL output exceeds the configured byte limit.");
            _stream.Write(bytes);
        }

        public void Dispose() => _stream.Dispose();
    }
}
