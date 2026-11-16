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
    private readonly IFileChunkPublisher _csvPublisher;
    private readonly FileChunkStore _jsonlStore = new(".jsonl");
    private readonly FileChunkStore _csvStore = new(".csv");
    private readonly FileChunkStore _jsonStore = new(".json");
    private readonly EtlHttpTransport _transport;
    private readonly string _snapshotRoot;
    private readonly HostSqlBindings? _sqlBindings;
    private readonly HostRabbitMqBindings? _rabbitMqBindings;
    private readonly TimeProvider _clock;

    public EtlConnectorOutputProcessor(IEtlConnectorRuntimeStore store, HostFileBindings fileBindings,
        HostHttpBindings httpBindings, IFileChunkPublisherFactory publishers, EtlHttpTransport transport,
        string snapshotRoot, HostSqlBindings? sqlBindings = null, TimeProvider? clock = null,
        HostRabbitMqBindings? rabbitMqBindings = null)
    {
        _sqlBindings = sqlBindings;
        _rabbitMqBindings = rabbitMqBindings;
        _clock = clock ?? TimeProvider.System;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fileBindings = fileBindings ?? throw new ArgumentNullException(nameof(fileBindings));
        _httpBindings = httpBindings ?? throw new ArgumentNullException(nameof(httpBindings));
        ArgumentNullException.ThrowIfNull(publishers);
        _jsonlPublisher = publishers.Create(EtlFileChunkFormat.Jsonl);
        _csvPublisher = publishers.Create(EtlFileChunkFormat.CsvOutput);
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

                if (routeOutput.Kind is EtlConnectorKind.Jsonl or EtlConnectorKind.Csv)
                {
                    var fileResult = await PublishFileRouteAsync(lease, admission, pipeline, routeId, routeOutput, cancellationToken);
                    if (fileResult.FailureCode is not null || fileResult.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                        return fileResult;
                    allReady &= fileResult.ReadyToAccept;
                    continue;
                }

                if (routeOutput.Kind == EtlConnectorKind.Sql && destination is EtlSqlDestination sqlDestination)
                {
                    var written = await DispatchSqlRouteAsync(lease, admission, pipeline, routeId, routeOutput, sqlDestination, cancellationToken);
                    if (written.FailureCode is not null || written.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                        return written;
                    allReady &= written.ReadyToAccept;
                    if (written.Yield)
                        return written with { ReadyToAccept = false };
                    continue;
                }

                if (routeOutput.Kind == EtlConnectorKind.RabbitMq && destination is EtlRabbitMqDestination rabbitMqDestination)
                {
                    var published = await DispatchRabbitMqRouteAsync(lease, admission, pipeline, routeId, routeOutput,
                        rabbitMqDestination, cancellationToken);
                    if (published.FailureCode is not null || published.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                        return published;
                    allReady &= published.ReadyToAccept;
                    if (published.Yield)
                        return published with { ReadyToAccept = false };
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
        var maxBytes = pipeline.Limits.MaxOutputBatchBytes;
        var mainIsCsv = pipeline.Routes.Main is EtlCsvDestination;
        var mainIsArray = pipeline.Routes.Main is EtlHttpDestination or EtlSqlDestination or EtlRabbitMqDestination;
        using var main = new RouteBodyBuilder(RouteFormatFor(pipeline.Routes.Main), maxBytes,
            mainIsCsv ? EtlCsvRouteWriter.Header(pipeline.Mapping.Select(field => field.Name).ToArray()) : null);
        using var errors = new RouteBodyBuilder(RouteFormatFor(pipeline.Routes.Errors), maxBytes,
            pipeline.Routes.Errors is EtlCsvDestination ? EtlCsvRouteWriter.ErrorHeader() : null);
        // A CSV row is never larger than the transformer's JSON object for it, so the CSV bytes are bounded below and the
        // transformer only gets a memory bound: 8x covers JSON escaping and names already paid for by the header.
        var csvTransformBound = maxBytes > long.MaxValue / 8 ? long.MaxValue : maxBytes * 8;
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

            // A JSON array's first element also carries its reserved closing bracket; bounding the row by one byte less
            // rejects an over-limit first row instead of failing the batch when it is appended.
            var transformed = transformer.Transform(record.Row!, mainIsCsv ? csvTransformBound
                : mainIsArray && main.IsEmpty ? remainingBytes - 1 : remainingBytes);
            switch (transformed.Disposition)
            {
                case EtlRowDisposition.Accepted when mainIsCsv:
                    var csvRow = EtlCsvRouteWriter.RecordRow(transformed.JsonlBytes, pipeline.Mapping.Count);
                    if (main.TryAppendCsvRow(csvRow, remainingBytes, out var csvBytes))
                    {
                        remainingBytes -= csvBytes;
                        accepted++;
                        break;
                    }

                    // Like an over-limit JSON Lines row, the record is rejected; a size rejection names no field.
                    remainingBytes -= errors.AppendError(new EtlRowError(transformed.RecordOrdinal, null, "etl.output-too-large"),
                        remainingBytes, cancellationToken);
                    rejected++;
                    break;
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

        var (mainBytes, mainFinishBytes) = main.Finish();
        remainingBytes -= mainFinishBytes;
        var (errorBytes, errorFinishBytes) = errors.Finish();
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

    /// <summary>
    /// Writes a SQL route in one database transaction whose ledger row makes the effect apply once, then records it like a
    /// published file (spec 1197). A transient database failure yields after a short pause and leaves the route unrecorded.
    /// </summary>
    private async ValueTask<EtlConnectorRouteDispatchResult> DispatchSqlRouteAsync(EtlConnectorPartitionLease lease,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlOutputRoute routeId, EtlConnectorRouteOutput route,
        EtlSqlDestination destination, CancellationToken cancellationToken)
    {
        if (_sqlBindings is null)
            return DispatchFailed("etl.sql-unavailable");
        var bytes = await ReadRetainedRouteBodyAsync(route, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        var pin = DestinationRevision(admission, routeId).Revision;
        var binding = _sqlBindings.ResolveExact(lease.Scope, pin.Alias, RouteRole(routeId), pin.Revision, pin.Handle, destination.Table);
        var batchId = lease.OwedBatch!.Batch.BatchId;
        var outcome = await new EtlSqlRouteWriter(binding.Engine, _clock).WriteAsync(new EtlSqlRouteDelivery(
            binding.ConnectionString, binding.LedgerTable, binding.CreateLedger, destination.Table, destination.Key,
            SqlColumns(pipeline), bytes, route.EffectId, lease.RunId, batchId, route.Body.ContentHash), cancellationToken);
        if (outcome == EtlSqlWriteOutcome.Yield)
        {
            await Task.Delay(SqlRetryDelay, _clock, cancellationToken);
            return new(EtlStoreStatus.Applied, ReadyToAccept: false, Yield: true, FailureCode: null);
        }

        var recorded = await _store.RecordConnectorFilePublicationAsync(lease, batchId, routeId,
            route.Body with { Key = EtlRuntimeIdentity.ConnectorSqlEffectKey(route.EffectId) }, cancellationToken);
        return recorded is EtlStoreStatus.Applied or EtlStoreStatus.Replay
            ? new(recorded, ReadyToAccept: true, Yield: false, FailureCode: null)
            : new(recorded, ReadyToAccept: false, Yield: recorded is EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict,
                FailureCode: recorded is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound or
                    EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict ? null : "etl.sql-delivery-refused");
    }

    /// <summary>
    /// Publishes a RabbitMQ route as one message per record and, once the broker confirmed every message, records it like a
    /// SQL delivery (spec 1200). A transient failure or a negative acknowledgement yields after a short pause and leaves the
    /// route unrecorded, so the whole batch is published again with the same message ids.
    /// </summary>
    private async ValueTask<EtlConnectorRouteDispatchResult> DispatchRabbitMqRouteAsync(EtlConnectorPartitionLease lease,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlOutputRoute routeId, EtlConnectorRouteOutput route,
        EtlRabbitMqDestination destination, CancellationToken cancellationToken)
    {
        if (_rabbitMqBindings is null)
            return DispatchFailed("etl.rabbitmq-unavailable");
        // A recorded route was confirmed in full. Unlike a file or a SQL ledger, a broker cannot recognise a second copy, so a
        // batch dispatched again (an error route retrying, or acceptance deferred) must not publish every message again.
        if (routeId == EtlOutputRoute.Main ? lease.OwedBatch!.FilePublications.Main : lease.OwedBatch!.FilePublications.Errors)
            return new(EtlStoreStatus.Replay, ReadyToAccept: true, Yield: false, FailureCode: null);
        var bytes = await ReadRetainedRouteBodyAsync(route, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        var pin = DestinationRevision(admission, routeId).Revision;
        var binding = _rabbitMqBindings.ResolveExact(lease.Scope, pin.Alias, RouteRole(routeId), pin.Revision, pin.Handle,
            destination.Exchange);
        var batchId = lease.OwedBatch!.Batch.BatchId;
        var outcome = await new EtlRabbitMqRouteWriter(binding.Publisher).WriteAsync(new EtlRabbitMqRouteDelivery(
            binding.ConnectionString, destination.Exchange, destination.RoutingKey, bytes, route.EffectId, lease.RunId, batchId),
            cancellationToken);
        if (outcome == EtlRabbitMqWriteOutcome.Yield)
        {
            await Task.Delay(RabbitMqRetryDelay, _clock, cancellationToken);
            return new(EtlStoreStatus.Applied, ReadyToAccept: false, Yield: true, FailureCode: null);
        }

        var recorded = await _store.RecordConnectorFilePublicationAsync(lease, batchId, routeId,
            route.Body with { Key = EtlRuntimeIdentity.ConnectorRabbitMqEffectKey(route.EffectId) }, cancellationToken);
        return recorded is EtlStoreStatus.Applied or EtlStoreStatus.Replay
            ? new(recorded, ReadyToAccept: true, Yield: false, FailureCode: null)
            : new(recorded, ReadyToAccept: false, Yield: recorded is EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict,
                FailureCode: recorded is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound or
                    EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Conflict ? null : "etl.rabbitmq-delivery-refused");
    }

    /// <summary>How long a transient RabbitMQ failure waits before its lease is released for a retry.</summary>
    internal static readonly TimeSpan RabbitMqRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>How long a transient SQL failure waits before its lease is released for a retry.</summary>
    internal static readonly TimeSpan SqlRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>The mapped output columns with the types of the fields they map, for typed SQL parameters.</summary>
    private static IReadOnlyList<EtlColumn> SqlColumns(EtlPipelineV2 pipeline)
    {
        var source = pipeline.Source switch
        {
            EtlCsvSource csv => csv.Schema.ToDictionary(column => column.Name, column => column.Type, StringComparer.Ordinal),
            EtlHttpJsonSource http => http.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal),
            EtlJsonlSource jsonl => jsonl.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal),
            EtlSqlSource sql => sql.Columns.ToDictionary(column => column.Name, column => column.Type, StringComparer.Ordinal),
            EtlRabbitMqSource queue => queue.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal),
            _ => throw new EtlEffectException("etl.connector-output-invalid", "The connector source is unsupported.")
        };
        var lookup = pipeline.Lookup?.Schema.ToDictionary(column => column.Name, column => column.Type, StringComparer.Ordinal)
                     ?? new Dictionary<string, EtlColumnType>(StringComparer.Ordinal);
        return pipeline.Mapping.Select(field => new EtlColumn(field.Name,
            (field.Origin == EtlFieldOrigin.Source ? source : lookup)[field.Field], false)).ToArray();
    }

    private async ValueTask<EtlConnectorRouteDispatchResult> PublishFileRouteAsync(EtlConnectorPartitionLease lease,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlOutputRoute routeId,
        EtlConnectorRouteOutput route, CancellationToken cancellationToken)
    {
        var bytes = await ReadRetainedRouteBodyAsync(route, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
        var revision = DestinationRevision(admission, routeId);
        var role = FileRole(routeId);
        var outputRoot = _fileBindings.GetOutputRoot(EtlRuntimeMetadata.PinnedBinding(lease.Scope, revision.Revision, role));
        var (publisher, publishedStore) = route.Kind == EtlConnectorKind.Csv ? (_csvPublisher, _csvStore) : (_jsonlPublisher, _jsonlStore);
        EtlChunk publication;
        try
        {
            publication = await publisher.PublishAsync(outputRoot, route.EffectId,
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

        if (publication.Key != EtlRuntimeIdentity.ConnectorFileOutputKey(route.EffectId, route.Kind) ||
            publication.ContentHash != route.Body.ContentHash || publication.ByteLength != route.Body.ByteLength)
            return DispatchFailed("etl.output-identity-invalid");

        var verified = await publishedStore.ReadAsync(outputRoot, route.EffectId, pipeline.Limits.MaxOutputBatchBytes,
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
        EtlCsvDestination => EtlConnectorKind.Csv,
        EtlSqlDestination => EtlConnectorKind.Sql,
        EtlRabbitMqDestination => EtlConnectorKind.RabbitMq,
        _ => throw new InvalidDataException("The connector output destination is unsupported.")
    };

    private static RouteFormat RouteFormatFor(EtlDestination destination) => destination switch
    {
        EtlHttpDestination or EtlSqlDestination or EtlRabbitMqDestination => RouteFormat.JsonArray,
        EtlJsonlDestination => RouteFormat.JsonLines,
        EtlCsvDestination => RouteFormat.Csv,
        _ => throw new InvalidDataException("The connector output destination is unsupported.")
    };

    /// <summary>
    /// Removes an accepted batch's retained route bodies (spec 1196). A replayed acceptance reads only rows, and only an
    /// owed batch reads its bodies, so nothing reads them once the acceptance has committed.
    /// </summary>
    public void DeleteRetainedRouteBodies(EtlConnectorBatchOutput output)
    {
        foreach (var route in new[] { output.Main, output.Errors })
            if (route is not null)
                BodyStore(route.Kind).Delete(_snapshotRoot, RetainedBodyIdentity(route.Kind, route.EffectId));
    }

    private FileChunkStore BodyStore(EtlConnectorKind kind) => kind switch
    {
        EtlConnectorKind.Http or EtlConnectorKind.Sql or EtlConnectorKind.RabbitMq => _jsonStore,
        EtlConnectorKind.Jsonl => _jsonlStore,
        EtlConnectorKind.Csv => _csvStore,
        _ => throw new EtlEffectException("etl.connector-output-invalid", "The connector route body kind is unsupported.")
    };

    private static string RetainedBodyIdentity(EtlConnectorKind kind, string effectId) => kind switch
    {
        EtlConnectorKind.Http => "etl-connector-http-route-body-v1:" + effectId,
        EtlConnectorKind.Jsonl => "etl-connector-jsonl-route-body-v1:" + effectId,
        EtlConnectorKind.Csv => "etl-connector-csv-route-body-v1:" + effectId,
        EtlConnectorKind.Sql => "etl-connector-sql-route-body-v1:" + effectId,
        EtlConnectorKind.RabbitMq => "etl-connector-rabbitmq-route-body-v1:" + effectId,
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

    private enum RouteFormat
    {
        JsonLines,
        JsonArray,
        Csv
    }

    /// <summary>Accumulates one route body. A CSV route's header is written with its first row; a JSON array's closing bracket is reserved with its first element.</summary>
    private sealed class RouteBodyBuilder(RouteFormat format, long maxBytes, byte[]? csvHeader) : IDisposable
    {
        private readonly MemoryStream _stream = new();
        private long _count;
        private bool _finished;

        public bool IsEmpty => _count == 0;

        public long AppendRecord(ReadOnlyMemory<byte> jsonlLine, long remainingOutputBytes)
        {
            if (_finished || format == RouteFormat.Csv || jsonlLine.IsEmpty || jsonlLine.Span[^1] != (byte)'\n')
                throw new InvalidDataException("The transformed connector row is not one complete JSONL record.");
            var before = _stream.Length;
            var allowance = Math.Min(maxBytes, remainingOutputBytes);
            if (format == RouteFormat.JsonArray)
            {
                // Reserve the closing bracket with the first element so a later route cannot consume the byte Finish needs.
                var reserved = _count == 0 ? 1 : 0;
                Write([_count == 0 ? (byte)'[' : (byte)','], before, allowance - reserved);
                Write(jsonlLine.Span[..^1], before, allowance - reserved);
                _count++;
                return _stream.Length - before + reserved;
            }

            Write(jsonlLine.Span, before, allowance);
            _count++;
            return _stream.Length - before;
        }

        /// <summary>Appends one CSV row, with the header if it is the route's first, only if all of it fits.</summary>
        public bool TryAppendCsvRow(byte[] row, long remainingOutputBytes, out long addedBytes)
        {
            if (_finished || format != RouteFormat.Csv || csvHeader is null)
                throw new InvalidOperationException("The connector route is not an open CSV body.");
            var total = row.LongLength + (_count == 0 ? csvHeader.LongLength : 0);
            if (total > Math.Min(maxBytes - _stream.Length, remainingOutputBytes))
            {
                addedBytes = 0;
                return false;
            }

            if (_count == 0)
                _stream.Write(csvHeader);
            _stream.Write(row);
            _count++;
            addedBytes = total;
            return true;
        }

        public long AppendError(EtlRowError error, long remainingOutputBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (format == RouteFormat.Csv)
            {
                var before = _stream.Length;
                var allowance = Math.Min(maxBytes, remainingOutputBytes);
                if (_count == 0)
                    Write(csvHeader!, before, allowance);
                Write(EtlCsvRouteWriter.ErrorRow(error), before, allowance);
                _count++;
                return _stream.Length - before;
            }

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

        /// <summary>Completes the body. Its only possible trailing byte, a JSON array's bracket, was reserved and is not counted again.</summary>
        public (byte[]? Bytes, long AddedBytes) Finish()
        {
            if (_finished)
                throw new InvalidOperationException("The connector route body was already finalized.");
            _finished = true;
            if (_count == 0)
                return (null, 0);
            if (format == RouteFormat.JsonArray)
            {
                if (_stream.Length >= maxBytes)
                    throw new EtlEffectException("etl.output-too-large", "ETL output exceeds the configured byte limit.");
                _stream.WriteByte((byte)']');
            }
            return (_stream.ToArray(), 0);
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
