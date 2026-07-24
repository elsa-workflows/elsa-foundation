using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Durable Groundwork implementation of the provider-neutral OpenTelemetry store contract. Immutable signals use
/// four append streams. Resource and instrument catalogs use physical entity tables with declared bounded document-query
/// routes. A durable capture ledger binds a caller-owned drain batch identity to canonical input and tracks a bounded
/// attempt for each stream independently.
/// </summary>
/// <remarks>
/// Catalog identity and diagnostic-record text comparisons consume Groundwork's released Unicode ordinal-ignore-case
/// policy from issue #71. Metric and log records carry the durable resource service name as a bounded comparison
/// projection, so those service filters remain provider-side predicates rather than cross-store joins.
/// </remarks>
public sealed class GroundworkOpenTelemetryStore :
    IOpenTelemetryStore,
    IDiagnosticsPersistenceDrain,
    IDiagnosticsPersistenceStartupResource,
    IDisposable,
    IAsyncDisposable
{
    private const int DrainBatchSize = 64;
    private const int DrainRetentionInterval = 500;
    private const int MaxDrainAttempts = 8;
    private const int MaxLedgerWriteAttempts = 8;
    private const int LedgerSchemaVersion = 2;
    private const int CatalogRetentionPageSize = 1_000;
    private const int MaxCatalogEntriesPerBatch = 1_000;
    private const int MaxCatalogPayloadBytesPerBatch = 1_048_576;
    private static readonly TimeSpan DrainBaseRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DrainMaxRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly string[] StreamKinds = ["traces", "spans", "metric-points", "logs"];
    private static readonly IEqualityComparer<string> CatalogIdentityComparer =
        new UnicodeOrdinalIgnoreCaseIdentityComparer();

    private readonly GroundworkOpenTelemetryStores _stores;
    private readonly GroundworkOpenTelemetryBinding _binding;
    private readonly DiagnosticStorageScope _scope;
    private readonly DiagnosticStreamId _traceStream;
    private readonly DiagnosticStreamId _spanStream;
    private readonly DiagnosticStreamId _metricPointStream;
    private readonly DiagnosticStreamId _logStream;
    private readonly DiagnosticRecordStreamDefinition _traceDefinition;
    private readonly DiagnosticRecordStreamDefinition _spanDefinition;
    private readonly DiagnosticRecordStreamDefinition _metricPointDefinition;
    private readonly DiagnosticRecordStreamDefinition _logDefinition;
    private readonly CanonicalRecordSerializer _recordSerializer = new();
    private readonly CatalogDocumentSerializer _catalogSerializer = new();
    private readonly OpenTelemetryGroundworkDocumentCodec _documentCodec = new();
    private readonly DiagnosticsDrain<OpenTelemetryBatch, bool> _drain;
    private readonly GroundworkOpenTelemetryStartupResource? _startupResource;
    private readonly IOpenTelemetrySourceRegistry? _sourceRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly int _traceCapacity;
    private readonly int _spanCapacity;
    private readonly int _metricPointCapacity;
    private readonly int _logRecordCapacity;
    private readonly int _resourceCapacity;
    private readonly int _metricInstrumentCapacity;
    private readonly int _maxQuerySize;
    private long _droppedTraces;
    private long _droppedSpans;
    private long _droppedMetricPoints;
    private long _droppedLogRecords;

    public GroundworkOpenTelemetryStore(
        GroundworkOpenTelemetryStores stores,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        GroundworkOpenTelemetryBinding binding,
        TimeProvider? timeProvider = null,
        IOpenTelemetrySourceRegistry? sourceRegistry = null,
        IDiagnosticsPersistenceObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(stores.Traces);
        ArgumentNullException.ThrowIfNull(stores.Spans);
        ArgumentNullException.ThrowIfNull(stores.MetricPoints);
        ArgumentNullException.ThrowIfNull(stores.Logs);
        ArgumentNullException.ThrowIfNull(stores.Documents);
        ArgumentNullException.ThrowIfNull(stores.DocumentQueries);
        binding.ValidateAll();

        if (stores.Documents.Access.Kind != DocumentStoreAccessKind.Scoped ||
            stores.Documents.Access.Scope != binding.DocumentStorageScope)
        {
            throw new ArgumentException(
                "The OpenTelemetry document store is not bound to the tenant, scope, and source selected by the adapter binding.",
                nameof(stores));
        }

        _stores = stores;
        _binding = binding;
        _scope = binding.DiagnosticScope;
        _traceStream = new(binding.TraceStreamId);
        _spanStream = new(binding.SpanStreamId);
        _metricPointStream = new(binding.MetricPointStreamId);
        _logStream = new(binding.LogStreamId);
        _traceDefinition = OpenTelemetryRecordStreamDefinitions.CreateTraces(binding.TraceStreamId);
        _spanDefinition = OpenTelemetryRecordStreamDefinitions.CreateSpans(binding.SpanStreamId);
        _metricPointDefinition = OpenTelemetryRecordStreamDefinitions.CreateMetricPoints(binding.MetricPointStreamId);
        _logDefinition = OpenTelemetryRecordStreamDefinitions.CreateLogs(binding.LogStreamId);
        _timeProvider = timeProvider ?? TimeProvider.System;
        var value = options.Value;
        _traceCapacity = ClampCapacity(value.TraceCapacity);
        _spanCapacity = ClampCapacity(value.SpanCapacity);
        _metricPointCapacity = ClampCapacity(value.MetricPointCapacity);
        _logRecordCapacity = ClampCapacity(value.LogRecordCapacity);
        _resourceCapacity = ClampCapacity(value.ResourceCapacity);
        _metricInstrumentCapacity = ClampCapacity(value.MetricInstrumentCapacity);
        _maxQuerySize = ClampCapacity(value.MaxQuerySize);
        _sourceRegistry = sourceRegistry;
        _drain = new(
            new GroundworkOpenTelemetryDrainTarget(this),
            new DiagnosticsDrainOptions
            {
                BatchSize = DrainBatchSize,
                QueueCapacity = Math.Max(DrainBatchSize, value.SubscriberChannelCapacity) * 4,
                RetentionInterval = DrainRetentionInterval,
                MaxAttempts = MaxDrainAttempts,
                BaseRetryDelay = DrainBaseRetryDelay,
                MaxRetryDelay = DrainMaxRetryDelay,
                ShutdownTimeout = value.ShutdownDrainTimeout < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : value.ShutdownDrainTimeout
            },
            observer);
    }

    /// <summary>
    /// Creates the host-composed adapter without opening a provider resource. The diagnostics lifecycle
    /// performs read-only diagnostic admission and opens the one scoped catalog session before capture starts.
    /// </summary>
    public GroundworkOpenTelemetryStore(
        IDiagnosticRecordStoreSessionFactory diagnosticSessionFactory,
        IDiagnosticRecordDeploymentManifestSource deploymentSource,
        IGroundworkStoreSessionSource documentSessionSource,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        GroundworkOpenTelemetryBinding binding,
        TimeProvider? timeProvider = null,
        IOpenTelemetrySourceRegistry? sourceRegistry = null,
        IDiagnosticsPersistenceObserver? observer = null)
        : this(
            new GroundworkOpenTelemetryStartupResource(
                diagnosticSessionFactory,
                deploymentSource,
                documentSessionSource,
                binding),
            options,
            binding,
            timeProvider,
            sourceRegistry,
            observer)
    {
    }

    private GroundworkOpenTelemetryStore(
        GroundworkOpenTelemetryStartupResource startupResource,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        GroundworkOpenTelemetryBinding binding,
        TimeProvider? timeProvider,
        IOpenTelemetrySourceRegistry? sourceRegistry,
        IDiagnosticsPersistenceObserver? observer)
        : this(startupResource.Stores, options, binding, timeProvider, sourceRegistry, observer)
    {
        _startupResource = startupResource;
    }

    /// <summary>Starts the host-owned capture drain. Repeated starts while running are idempotent.</summary>
    public void Start() => _drain.Start();

    ValueTask<IDiagnosticsPersistenceResourceLease> IDiagnosticsPersistenceStartupResource.AcquireAsync(
        CancellationToken cancellationToken) =>
        _startupResource?.AcquireAsync(cancellationToken)
        ?? ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(OpenTelemetryDirectStoreResourceLease.Instance);

    /// <summary>
    /// Accepts a capture without waiting for provider I/O. Durable failures and overload losses are reported through
    /// diagnostics persistence observability; the explicit batch-identity overload remains the durable test/tool seam.
    /// </summary>
    public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        if (_drain.State == DiagnosticsDrainState.Created)
        {
            throw new InvalidOperationException(
                "The Groundwork OpenTelemetry capture drain must be started by the host lifecycle before use.");
        }

        var accepted = _drain.TryEnqueue(batch, out var acknowledgement);
        ObserveAcknowledgement(batch, acknowledgement);
        if (accepted && _sourceRegistry is not null)
        {
            foreach (var resource in batch.Resources)
                _sourceRegistry.MarkSeen(resource);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Stops capture and awaits the bounded drain, including its final retention pass.</summary>
    public async Task CompleteDrainingAsync(CancellationToken cancellationToken = default) =>
        await _drain.StopAsync(cancellationToken);

    Task IDiagnosticsPersistenceDrain.StopAsync(CancellationToken cancellationToken) =>
        _drain.StopIfStartedAsync(cancellationToken);

    /// <summary>
    /// Commits one drain batch. The same identity may be retried with the same canonical input; a different identity
    /// always represents an independent capture, even when its content is byte-for-byte identical.
    /// </summary>
    public async ValueTask WriteAsync(
        DiagnosticsDrainBatchId batchId,
        OpenTelemetryBatch batch,
        CancellationToken cancellationToken = default) =>
        await WriteDurablyAsync(batchId, batch, applyRetention: true, cancellationToken);

    private async ValueTask<int> WriteDurablyAsync(
        DiagnosticsDrainBatchId batchId,
        OpenTelemetryBatch batch,
        bool applyRetention,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batchId.Value == Guid.Empty)
            throw new ArgumentException("The diagnostics drain batch identity cannot be empty.", nameof(batchId));
        cancellationToken.ThrowIfCancellationRequested();

        var context = Context(("batchId", batchId.ToString()));
        DiagnosticRecordInput[] traces;
        DiagnosticRecordInput[] spans;
        DiagnosticRecordInput[] points;
        DiagnosticRecordInput[] logs;
        SaveDocumentRequest[] resources;
        SaveDocumentRequest[] instruments;
        try
        {
            var serviceNames = await ResolveServiceNamesAsync(batch, cancellationToken);
            traces = NormalizeTraces(batch.Traces);
            spans = NormalizeRecords(batch.Spans, span => _recordSerializer.ToRecord(span.Id, span));
            points = NormalizeRecords(batch.MetricPoints, point => _recordSerializer.ToRecord(
                point.Id,
                point,
                ServiceNameFor(point.ResourceId, serviceNames)));
            logs = NormalizeRecords(batch.Logs, log => _recordSerializer.ToRecord(
                log.Id,
                log,
                ServiceNameFor(log.ResourceId, serviceNames)));
            resources = batch.Resources.Select(resource => _catalogSerializer.ToSaveRequest(resource)).ToArray();
            instruments = batch.Instruments
                .Select(instrument => _catalogSerializer.ToSaveRequest(instrument, batchId.IssuedAt))
                .ToArray();
        }
        catch (Exception exception) when (exception is RecordPayloadException or CatalogPayloadException)
        {
            throw new OpenTelemetryPersistenceValidationException(
                "write",
                "The OpenTelemetry persistence operation contains a record or catalog value that cannot be represented canonically.",
                context,
                exception);
        }
        ValidateCatalogBatch(resources, instruments, context);

        try
        {
            var fingerprint = CaptureFingerprint(resources, instruments, traces, spans, points, logs);
            var targets = new[]
            {
                new StreamTarget("traces", _stores.Traces, _traceStream, _traceDefinition, _traceCapacity, PhysicalizeRecordIds(batchId, "traces", traces)),
                new StreamTarget("spans", _stores.Spans, _spanStream, _spanDefinition, _spanCapacity, PhysicalizeRecordIds(batchId, "spans", spans)),
                new StreamTarget("metric-points", _stores.MetricPoints, _metricPointStream, _metricPointDefinition, _metricPointCapacity, PhysicalizeRecordIds(batchId, "metric-points", points)),
                new StreamTarget("logs", _stores.Logs, _logStream, _logDefinition, _logRecordCapacity, PhysicalizeRecordIds(batchId, "logs", logs))
            };
            Preflight(batchId, targets);
            await GetOrCreateCaptureOperationAsync(batchId, fingerprint, cancellationToken);
            await SaveCatalogsAsync(resources, instruments, cancellationToken);

            foreach (var target in targets)
                await AppendAsync(batchId, fingerprint, target, applyRetention, cancellationToken);
            return checked(traces.Length + spans.Length + points.Length + logs.Length);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("write", context, exception);
        }
    }

    public async ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(
        OpenTelemetryResourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.Search, nameof(filter.Search), "query-resources");
        RejectUnsupportedTextFilter(filter.ServiceName, nameof(filter.ServiceName), "query-resources");
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], _sourceRegistry?.DroppedCount ?? 0);

        try
        {
            var query = filter.Status is null
                ? new DocumentQuery(
                    CatalogDocuments.ResourceKind,
                    OpenTelemetryGroundworkStorageSchema.ResourcesByLastSeenQuery,
                    take: take)
                : new DocumentQuery(
                    CatalogDocuments.ResourceKind,
                    OpenTelemetryGroundworkStorageSchema.ResourcesByStatusQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        CatalogDocuments.ResourceStatusPath.TrimStart('/'),
                        ((int)filter.Status.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)))],
                    take: take);
            var page = await _stores.DocumentQueries.QueryAsync(query, cancellationToken);
            return new(
                page.Documents.Select(x => _catalogSerializer.ToResource(x).Resource).ToArray(),
                _sourceRegistry?.DroppedCount ?? 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("query-resources", Context(), exception);
        }
    }

    public async ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(
        OpenTelemetryTraceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.ServiceName, nameof(filter.ServiceName), "query-traces");
        ValidateRange(filter.From, filter.To, nameof(filter));
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], Interlocked.Read(ref _droppedTraces));

        try
        {
            var predicates = new List<DiagnosticRecordPredicate>();
            AddContainsTextPredicate(predicates, RecordFields.TraceId, filter.TraceId);
            AddExactTextPredicate(predicates, RecordFields.ResourceId, filter.ResourceId);
            AddContainsTextPredicate(predicates, RecordFields.WorkflowInstanceId, filter.WorkflowInstanceId);
            if (filter.Status is { } status)
                predicates.Add(DiagnosticRecordPredicate.Equal(RecordFields.Status, DiagnosticFieldValue.Int64((long)status)));
            AddRange(predicates, RecordFields.StartTime, filter.From, filter.To);
            if (!string.IsNullOrWhiteSpace(filter.Search))
                predicates.Add(new DiagnosticRecordPredicate.Any([
                    DiagnosticRecordPredicate.Contains(RecordFields.TraceId, filter.Search),
                    DiagnosticRecordPredicate.Contains(RecordFields.Name, filter.Search)
                ]));

            var page = await _stores.Traces.QueryAsync(new(
                _scope,
                _traceStream,
                take,
                new(RecordFields.StartTime, DiagnosticSortDirection.Descending),
                Predicate: All(predicates),
                LatestPerKeyField: RecordFields.TraceId), cancellationToken);
            return new(
                page.Records.Select(_recordSerializer.ToTrace).Reverse().ToArray(),
                Interlocked.Read(ref _droppedTraces));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("query-traces", Context(), exception);
        }
    }

    public async ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);

        try
        {
            var tracePage = await _stores.Traces.QueryAsync(new(
                _scope,
                _traceStream,
                1,
                new(RecordFields.StartTime, DiagnosticSortDirection.Descending),
                Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String(traceId)),
                LatestPerKeyField: RecordFields.TraceId), cancellationToken);
            var traceRecord = tracePage.Records.SingleOrDefault();
            if (traceRecord is null)
                return null;

            var trace = _recordSerializer.ToTrace(traceRecord);
            var spanPage = await _stores.Spans.QueryAsync(new(
                _scope,
                _spanStream,
                _maxQuerySize,
                new(RecordFields.OrderKey, DiagnosticSortDirection.Ascending),
                Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String(trace.TraceId))), cancellationToken);
            var logPage = await _stores.Logs.QueryAsync(new(
                _scope,
                _logStream,
                _maxQuerySize,
                new(RecordFields.OrderKey, DiagnosticSortDirection.Ascending),
                Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String(trace.TraceId))), cancellationToken);
            var resources = new List<TelemetryResource>(trace.ResourceIds.Count);
            foreach (var resourceId in trace.ResourceIds.Distinct(CatalogIdentityComparer))
            {
                var envelope = await _stores.Documents.LoadAsync(CatalogDocuments.ResourceKind, resourceId, cancellationToken);
                if (envelope is not null)
                    resources.Add(_catalogSerializer.ToResource(envelope).Resource);
            }
            return new(
                trace,
                spanPage.Records.Select(_recordSerializer.ToSpan).ToArray(),
                resources.OrderBy(x => x.ServiceName, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(),
                logPage.Records.Select(_recordSerializer.ToLog).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("get-trace", Context(("traceId", traceId)), exception);
        }
    }

    public async ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(
        OpenTelemetryMetricFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To, nameof(filter));
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], [], Interlocked.Read(ref _droppedMetricPoints));

        try
        {
            var predicates = new List<DiagnosticRecordPredicate>();
            AddExactTextPredicate(predicates, RecordFields.ResourceId, filter.ResourceId);
            AddExactTextPredicate(predicates, RecordFields.ServiceName, filter.ServiceName);
            AddContainsTextPredicate(predicates, RecordFields.InstrumentName, filter.InstrumentName);
            AddRange(predicates, RecordFields.Timestamp, filter.From, filter.To);
            var pointPage = await _stores.MetricPoints.QueryAsync(new(
                _scope,
                _metricPointStream,
                take,
                new(RecordFields.OrderKey, DiagnosticSortDirection.Descending),
                Predicate: All(predicates)), cancellationToken);
            var points = pointPage.Records
                .Select(_recordSerializer.ToMetricPoint)
                .Reverse()
                .ToArray();
            var instruments = new List<MetricInstrument>();
            foreach (var instrumentId in points
                         .Select(point => point.InstrumentId)
                         .Distinct(CatalogIdentityComparer)
                         .OrderBy(
                             id => DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(id),
                             StringComparer.Ordinal))
            {
                var envelope = await _stores.Documents.LoadAsync(
                    CatalogDocuments.InstrumentKind,
                    instrumentId,
                    cancellationToken);
                if (envelope is not null)
                    instruments.Add(_catalogSerializer.ToInstrument(envelope).Instrument);
            }
            return new(
                instruments,
                points,
                Interlocked.Read(ref _droppedMetricPoints));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("query-metrics", Context(), exception);
        }
    }

    public async ValueTask<OpenTelemetryLogResult> QueryLogsAsync(
        OpenTelemetryLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To, nameof(filter));
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], Interlocked.Read(ref _droppedLogRecords));

        try
        {
            var predicates = new List<DiagnosticRecordPredicate>();
            AddExactTextPredicate(predicates, RecordFields.ResourceId, filter.ResourceId);
            AddExactTextPredicate(predicates, RecordFields.ServiceName, filter.ServiceName);
            AddContainsTextPredicate(predicates, RecordFields.TraceId, filter.TraceId);
            AddContainsTextPredicate(predicates, RecordFields.SpanId, filter.SpanId);
            AddContainsTextPredicate(predicates, RecordFields.SeverityText, filter.Severity);
            if (!string.IsNullOrWhiteSpace(filter.Search))
                predicates.Add(DiagnosticRecordPredicate.Contains(RecordFields.Body, filter.Search));
            AddRange(predicates, RecordFields.Timestamp, filter.From, filter.To);
            var page = await _stores.Logs.QueryAsync(new(
                _scope,
                _logStream,
                take,
                new(RecordFields.OrderKey, DiagnosticSortDirection.Descending),
                Predicate: All(predicates)), cancellationToken);
            return new(
                page.Records.Select(_recordSerializer.ToLog).Reverse().ToArray(),
                Interlocked.Read(ref _droppedLogRecords));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("query-logs", Context(), exception);
        }
    }

    public async ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var traces = await _stores.Traces.InspectAsync(new(_scope, _traceStream), cancellationToken);
            var spans = await _stores.Spans.InspectAsync(new(_scope, _spanStream), cancellationToken);
            var points = await _stores.MetricPoints.InspectAsync(new(_scope, _metricPointStream), cancellationToken);
            var logs = await _stores.Logs.InspectAsync(new(_scope, _logStream), cancellationToken);
            var resources = await QueryDocumentsAsync(
                CatalogDocuments.ResourceKind,
                OpenTelemetryGroundworkStorageSchema.ResourcesByLastSeenQuery,
                cancellationToken);
            var instruments = await QueryDocumentsAsync(
                CatalogDocuments.InstrumentKind,
                OpenTelemetryGroundworkStorageSchema.InstrumentsByLastSeenQuery,
                cancellationToken);
            return new(
                _traceCapacity,
                _spanCapacity,
                _metricPointCapacity,
                _logRecordCapacity,
                ToCount(resources.TotalCount),
                ToCount(traces.RetainedCount.Value),
                ToCount(spans.RetainedCount.Value),
                ToCount(instruments.TotalCount),
                ToCount(points.RetainedCount.Value),
                ToCount(logs.RetainedCount.Value),
                Interlocked.Read(ref _droppedTraces),
                Interlocked.Read(ref _droppedSpans),
                Interlocked.Read(ref _droppedMetricPoints),
                Interlocked.Read(ref _droppedLogRecords));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenTelemetryPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateFailure("get-diagnostics", Context(), exception);
        }
    }

    private async Task AppendAsync(
        DiagnosticsDrainBatchId batchId,
        string fingerprint,
        StreamTarget target,
        bool applyRetention,
        CancellationToken cancellationToken)
    {
        if (target.Records.Count == 0)
            return;

        var attempt = await GetOrCreateStreamAttemptAsync(
            batchId,
            fingerprint,
            target,
            cancellationToken);
        if (!attempt.Committed)
        {
            var operationId = OperationId(batchId, target.Kind, attempt.IssuedAt);
            var request = DiagnosticRecordBatch.Create(_scope, target.Stream, operationId, target.Records);
            await target.Store.AppendAsync(request, cancellationToken);
            await MarkStreamCommittedAsync(batchId, fingerprint, target.Kind, CancellationToken.None);
        }

        if (applyRetention)
        {
            await target.Store.TrimAsync(
                DiagnosticTrimRequest.Create(
                    _scope,
                    target.Stream,
                    new DiagnosticOperationId(attempt.IssuedAt, $"otel-trim:{batchId}:{target.Kind}"),
                    target.Capacity),
                cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async ValueTask<int> ApplySignalRetentionAsync(
        RetentionCycle cycle,
        CancellationToken cancellationToken)
    {
        long deleted = 0;
        deleted += (await _stores.Traces.TrimAsync(
            DiagnosticTrimRequest.Create(
                _scope,
                _traceStream,
                cycle.Traces,
                _traceCapacity),
            cancellationToken)).DeletedCount.Value;
        deleted += (await _stores.Spans.TrimAsync(
            DiagnosticTrimRequest.Create(
                _scope,
                _spanStream,
                cycle.Spans,
                _spanCapacity),
            cancellationToken)).DeletedCount.Value;
        deleted += (await _stores.MetricPoints.TrimAsync(
            DiagnosticTrimRequest.Create(
                _scope,
                _metricPointStream,
                cycle.MetricPoints,
                _metricPointCapacity),
            cancellationToken)).DeletedCount.Value;
        deleted += (await _stores.Logs.TrimAsync(
            DiagnosticTrimRequest.Create(
                _scope,
                _logStream,
                cycle.Logs,
                _logRecordCapacity),
            cancellationToken)).DeletedCount.Value;
        return deleted >= int.MaxValue ? int.MaxValue : (int)deleted;
    }

    private async Task GetOrCreateCaptureOperationAsync(
        DiagnosticsDrainBatchId batchId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var documentId = batchId.ToString();
        for (var attempt = 0; attempt < MaxLedgerWriteAttempts; attempt++)
        {
            var existing = await LoadOperationAsync(documentId, cancellationToken);
            if (existing is not null)
            {
                ValidateFingerprint(existing.Operation, batchId, fingerprint);
                return;
            }

            var candidate = new CaptureOperation(
                LedgerSchemaVersion,
                documentId,
                fingerprint,
                _timeProvider.GetUtcNow(),
                _binding.TenantId,
                _binding.ScopeId,
                _binding.SourceId,
                new Dictionary<string, CaptureStreamAttempt>(StringComparer.Ordinal));
            var result = await SaveOperationAsync(candidate, 0, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return;
        }

        throw new InvalidOperationException("The OpenTelemetry capture operation could not be claimed after concurrent retries.");
    }

    private async Task<CaptureStreamAttempt> GetOrCreateStreamAttemptAsync(
        DiagnosticsDrainBatchId batchId,
        string fingerprint,
        StreamTarget target,
        CancellationToken cancellationToken)
    {
        var documentId = batchId.ToString();
        for (var retry = 0; retry < MaxLedgerWriteAttempts; retry++)
        {
            var snapshot = await LoadRequiredOperationAsync(documentId, cancellationToken);
            ValidateFingerprint(snapshot.Operation, batchId, fingerprint);
            if (snapshot.Operation.Streams.TryGetValue(target.Kind, out var existing))
            {
                if (!existing.Committed && _timeProvider.GetUtcNow() > existing.RetryUntil)
                    throw new DiagnosticOperationExpiredException(
                        DiagnosticOperationKind.Append,
                        OperationId(batchId, target.Kind, existing.IssuedAt));
                return existing;
            }

            var issuedAt = _timeProvider.GetUtcNow();
            var candidate = new CaptureStreamAttempt(
                issuedAt,
                issuedAt + target.Definition.AppendIdempotencyWindow + target.Definition.MaxOperationClockSkew,
                Committed: false);
            var streams = new Dictionary<string, CaptureStreamAttempt>(snapshot.Operation.Streams, StringComparer.Ordinal)
            {
                [target.Kind] = candidate
            };
            var result = await SaveOperationAsync(
                snapshot.Operation with { Streams = streams },
                snapshot.Version,
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return candidate;
        }

        throw new InvalidOperationException($"OpenTelemetry stream attempt '{target.Kind}' could not be claimed after concurrent retries.");
    }

    private async Task MarkStreamCommittedAsync(
        DiagnosticsDrainBatchId batchId,
        string fingerprint,
        string streamKind,
        CancellationToken cancellationToken)
    {
        var documentId = batchId.ToString();
        for (var retry = 0; retry < MaxLedgerWriteAttempts; retry++)
        {
            var snapshot = await LoadRequiredOperationAsync(documentId, cancellationToken);
            ValidateFingerprint(snapshot.Operation, batchId, fingerprint);
            if (!snapshot.Operation.Streams.TryGetValue(streamKind, out var attempt))
                throw new InvalidOperationException($"OpenTelemetry stream attempt '{streamKind}' is missing from the capture ledger.");
            if (attempt.Committed)
                return;

            var streams = new Dictionary<string, CaptureStreamAttempt>(snapshot.Operation.Streams, StringComparer.Ordinal)
            {
                [streamKind] = attempt with { Committed = true }
            };
            var result = await SaveOperationAsync(
                snapshot.Operation with { Streams = streams },
                snapshot.Version,
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return;
        }

        throw new InvalidOperationException($"OpenTelemetry stream attempt '{streamKind}' could not be completed after concurrent retries.");
    }

    private async Task<CaptureOperationSnapshot?> LoadOperationAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        var envelope = await _stores.Documents.LoadAsync(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            documentId,
            cancellationToken);
        return envelope is null ? null : ReadOperation(envelope);
    }

    private async Task<CaptureOperationSnapshot> LoadRequiredOperationAsync(
        string documentId,
        CancellationToken cancellationToken) =>
        await LoadOperationAsync(documentId, cancellationToken)
        ?? throw new OpenTelemetryPersistenceDataException(
            "write",
            "The OpenTelemetry capture operation disappeared during an active write.",
            Context(("batchId", documentId)));

    private Task<DocumentStoreWriteResult> SaveOperationAsync(
        CaptureOperation operation,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        _stores.Documents.SaveAsync(_documentCodec.CreateSaveRequest(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            operation.BatchId,
            operation,
            expectedVersion), cancellationToken);

    private CaptureOperationSnapshot ReadOperation(DocumentEnvelope envelope)
    {
        try
        {
            if (!StringComparer.Ordinal.Equals(envelope.DocumentKind, OpenTelemetryGroundworkStorageSchema.OperationLedgerKind))
                throw CorruptOperation(envelope.Id, "The OpenTelemetry capture operation kind is invalid.");
            var operation = _documentCodec.Deserialize<CaptureOperation>(envelope);
            if (operation.LedgerSchemaVersion != LedgerSchemaVersion ||
                !StringComparer.Ordinal.Equals(operation.BatchId, envelope.Id) ||
                !StringComparer.Ordinal.Equals(operation.TenantId, _binding.TenantId) ||
                !StringComparer.Ordinal.Equals(operation.ScopeId, _binding.ScopeId) ||
                !StringComparer.Ordinal.Equals(operation.SourceId, _binding.SourceId) ||
                operation.Streams is null ||
                operation.Streams.Keys.Except(StreamKinds, StringComparer.Ordinal).Any() ||
                operation.Streams.Values.Any(x => x is null || x.RetryUntil < x.IssuedAt))
            {
                throw CorruptOperation(envelope.Id, "The OpenTelemetry capture operation does not match this storage binding.");
            }
            return new(operation, envelope.Version);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or DocumentSchemaVersionException)
        {
            throw new OpenTelemetryPersistenceDataException(
                "write",
                "The OpenTelemetry capture operation is malformed.",
                Context(("batchId", envelope.Id)),
                exception);
        }
    }

    private static void ValidateFingerprint(
        CaptureOperation operation,
        DiagnosticsDrainBatchId batchId,
        string fingerprint)
    {
        if (StringComparer.Ordinal.Equals(operation.Fingerprint, fingerprint))
            return;
        throw new DiagnosticOperationConflictException(
            DiagnosticOperationKind.Append,
            new(operation.CreatedAt, batchId.ToString()));
    }

    private void Preflight(DiagnosticsDrainBatchId batchId, IReadOnlyList<StreamTarget> targets)
    {
        var issuedAt = _timeProvider.GetUtcNow();
        foreach (var target in targets.Where(x => x.Records.Count > 0))
        {
            var request = DiagnosticRecordBatch.Create(
                _scope,
                target.Stream,
                OperationId(batchId, target.Kind, issuedAt),
                target.Records);
            DiagnosticRecordRequestValidator.Validate(request, target.Definition);
        }
    }

    private static DiagnosticOperationId OperationId(
        DiagnosticsDrainBatchId batchId,
        string streamKind,
        DateTimeOffset issuedAt) =>
        new(issuedAt, $"otel-v2:{batchId}:{streamKind}");

    private static DiagnosticRecordInput[] PhysicalizeRecordIds(
        DiagnosticsDrainBatchId batchId,
        string streamKind,
        IReadOnlyList<DiagnosticRecordInput> records) =>
        records.Select(record => record with
        {
            RecordId = $"otel-{Hash($"{batchId}:{streamKind}:{record.RecordId}")}"
        }).ToArray();

    private DiagnosticRecordInput[] NormalizeTraces(IReadOnlyCollection<TelemetryTrace> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return NormalizeRecords(values, trace =>
        {
            var provisional = _recordSerializer.ToRecord(trace.TraceId, trace);
            return provisional with { RecordId = $"trace-{Hash(provisional.Payload)}" };
        });
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveServiceNamesAsync(
        OpenTelemetryBatch batch,
        CancellationToken cancellationToken)
    {
        var services = batch.Resources.ToDictionary(
            resource => resource.Id,
            resource => resource.ServiceName,
            CatalogIdentityComparer);
        var resourceIds = batch.MetricPoints.Select(point => point.ResourceId)
            .Concat(batch.Logs.Select(log => log.ResourceId))
            .Distinct(CatalogIdentityComparer)
            .ToArray();
        foreach (var resourceId in resourceIds.Where(resourceId => !services.ContainsKey(resourceId)))
        {
            var envelope = await _stores.Documents.LoadAsync(
                CatalogDocuments.ResourceKind,
                resourceId,
                cancellationToken);
            if (envelope is not null)
                services.Add(resourceId, _catalogSerializer.ToResource(envelope).Resource.ServiceName);
        }

        return services;
    }

    private static string? ServiceNameFor(string resourceId, IReadOnlyDictionary<string, string> serviceNames) =>
        serviceNames.TryGetValue(resourceId, out var serviceName)
            ? serviceName
            : null;

    private static DiagnosticRecordInput[] NormalizeRecords<T>(
        IReadOnlyCollection<T> values,
        Func<T, DiagnosticRecordInput> map)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Dictionary<string, DiagnosticRecordInput>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var record = map(value);
            if (result.TryGetValue(record.RecordId, out var existing))
            {
                if (!RecordEquals(existing, record))
                    throw new ArgumentException(
                        $"OpenTelemetry record id '{record.RecordId}' identifies conflicting payloads in one batch.",
                        nameof(values));
                continue;
            }
            result.Add(record.RecordId, record);
        }
        return result.Values.OrderBy(x => x.RecordId, StringComparer.Ordinal).ToArray();
    }

    private string CaptureFingerprint(
        IReadOnlyList<SaveDocumentRequest> resources,
        IReadOnlyList<SaveDocumentRequest> instruments,
        IReadOnlyList<DiagnosticRecordInput> traces,
        IReadOnlyList<DiagnosticRecordInput> spans,
        IReadOnlyList<DiagnosticRecordInput> points,
        IReadOnlyList<DiagnosticRecordInput> logs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "elsa-open-telemetry-capture-v2");
        Append(hash, _binding.TenantId);
        Append(hash, _binding.ScopeId);
        Append(hash, _binding.SourceId);
        AppendDocuments(hash, resources);
        AppendDocuments(hash, instruments);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _traceStream, traces).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _spanStream, spans).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _metricPointStream, points).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _logStream, logs).Value);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendDocuments(
        IncrementalHash hash,
        IEnumerable<SaveDocumentRequest> requests)
    {
        foreach (var request in requests.OrderBy(x => x.DocumentKind, StringComparer.Ordinal)
                     .ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            Append(hash, request.DocumentKind);
            Append(hash, request.Id);
            Append(hash, request.SchemaVersion);
            Append(hash, request.ContentJson);
        }
    }

    private static void ValidateCatalogBatch(
        IReadOnlyCollection<SaveDocumentRequest> resources,
        IReadOnlyCollection<SaveDocumentRequest> instruments,
        IReadOnlyDictionary<string, string> context)
    {
        var count = checked(resources.Count + instruments.Count);
        if (count > MaxCatalogEntriesPerBatch)
        {
            throw new OpenTelemetryPersistenceValidationException(
                "write",
                $"An OpenTelemetry batch can contain at most {MaxCatalogEntriesPerBatch} resource and instrument catalog entries.",
                context,
                new ArgumentException("The combined catalog entry count exceeds the bounded batch contract."));
        }

        var payloadBytes = resources.Concat(instruments)
            .Sum(request => (long)Encoding.UTF8.GetByteCount(request.ContentJson));
        if (payloadBytes > MaxCatalogPayloadBytesPerBatch)
        {
            throw new OpenTelemetryPersistenceValidationException(
                "write",
                $"OpenTelemetry catalog payloads can contain at most {MaxCatalogPayloadBytesPerBatch} UTF-8 bytes per batch.",
                context,
                new ArgumentException("The combined catalog payload size exceeds the bounded batch contract."));
        }
    }

    private Task<DocumentQueryResult> QueryDocumentsAsync(
        string kind,
        string queryIdentity,
        CancellationToken cancellationToken) =>
        _stores.DocumentQueries.QueryAsync(new DocumentQuery(kind, queryIdentity, take: 0), cancellationToken);

    private async Task SaveCatalogsAsync(
        IReadOnlyCollection<SaveDocumentRequest> resources,
        IReadOnlyCollection<SaveDocumentRequest> instruments,
        CancellationToken cancellationToken)
    {
        foreach (var request in resources.Concat(instruments))
            await SaveCatalogAsync(request, cancellationToken);

        await EnforceCatalogCapacityAsync(
            CatalogDocuments.ResourceKind,
            OpenTelemetryGroundworkStorageSchema.ResourcesByLastSeenQuery,
            _resourceCapacity,
            cancellationToken);
        await EnforceCatalogCapacityAsync(
            CatalogDocuments.InstrumentKind,
            OpenTelemetryGroundworkStorageSchema.InstrumentsByLastSeenQuery,
            _metricInstrumentCapacity,
            cancellationToken);
    }

    private async Task SaveCatalogAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stores.Documents.SaveAsync(request, cancellationToken);
        if (result.Status is DocumentStoreWriteStatus.IdentityConflict &&
            !string.IsNullOrWhiteSpace(result.AuthoritativeId))
        {
            result = await _stores.Documents.SaveAsync(
                request with { Id = result.AuthoritativeId },
                cancellationToken);
        }

        if (result.Status is not DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException(
                $"The OpenTelemetry catalog write for '{request.DocumentKind}/{request.Id}' returned {result.Status}.");
    }

    /// <summary>
    /// Repeatedly deletes one bounded page beyond the retained prefix. Re-querying the same offset after each
    /// page also converges when a host lowers its configured capacity after a restart.
    /// </summary>
    private async Task EnforceCatalogCapacityAsync(
        string documentKind,
        string queryIdentity,
        int capacity,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var page = await _stores.DocumentQueries.QueryAsync(new DocumentQuery(
                documentKind,
                queryIdentity,
                skip: capacity,
                take: CatalogRetentionPageSize), cancellationToken);
            if (page.Documents.Count == 0)
                return;

            foreach (var envelope in page.Documents)
            {
                var result = await _stores.Documents.DeleteAsync(
                    new DeleteDocumentRequest(documentKind, envelope.Id, envelope.Version),
                    cancellationToken);
                if (result.Status is not (DocumentStoreWriteStatus.Deleted or DocumentStoreWriteStatus.NotFound))
                    throw new InvalidOperationException(
                        $"The OpenTelemetry catalog retention delete for '{documentKind}/{envelope.Id}' returned {result.Status}.");
            }
        }
    }

    private static void AddRange(
        List<DiagnosticRecordPredicate> predicates,
        string field,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from is not null || to is not null)
            predicates.Add(DiagnosticRecordPredicate.RangeInclusive(
                field,
                DiagnosticFieldValue.Timestamp(from ?? DateTimeOffset.MinValue),
                DiagnosticFieldValue.Timestamp(to ?? DateTimeOffset.MaxValue)));
    }

    private static DiagnosticRecordPredicate? All(IReadOnlyList<DiagnosticRecordPredicate> predicates) =>
        predicates.Count switch
        {
            0 => null,
            1 => predicates[0],
            _ => new DiagnosticRecordPredicate.All(predicates)
        };

    private static void RejectUnsupportedTextFilter(string? value, string parameterName, string operation)
    {
        if (!string.IsNullOrWhiteSpace(value))
            throw new OpenTelemetryPersistenceCapabilityException(
                operation,
                "bounded-catalog-join",
                $"Filter '{parameterName}' requires a portable bounded catalog-to-record join, which Groundwork preview.72 does not expose.",
                Context(("filter", parameterName)));
    }

    private static void AddExactTextPredicate(
        ICollection<DiagnosticRecordPredicate> predicates,
        string field,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            predicates.Add(DiagnosticRecordPredicate.Equal(field, DiagnosticFieldValue.String(value)));
    }

    private static void AddContainsTextPredicate(
        ICollection<DiagnosticRecordPredicate> predicates,
        string field,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            predicates.Add(DiagnosticRecordPredicate.Contains(field, value));
    }

    private static void ValidateRange(DateTimeOffset? from, DateTimeOffset? to, string parameterName)
    {
        if (from is { } lower && to is { } upper && lower > upper)
            throw new ArgumentException("The inclusive OpenTelemetry range start cannot be later than its end.", parameterName);
    }

    private static OpenTelemetryPersistenceDataException CorruptOperation(string batchId, string message) =>
        new("write", message, Context(("batchId", batchId)));

    private static OpenTelemetryPersistenceException TranslateFailure(
        string operation,
        IReadOnlyDictionary<string, string> context,
        Exception exception) => exception switch
        {
            DiagnosticOperationConflictException => new OpenTelemetryPersistenceConflictException(
                operation,
                "The OpenTelemetry persistence operation conflicts with an existing request.",
                context,
                exception),
            DiagnosticOperationExpiredException => new OpenTelemetryPersistenceExpiredException(
                operation,
                "The OpenTelemetry persistence operation can no longer be retried safely.",
                context,
                exception),
            DiagnosticRecordValidationException => new OpenTelemetryPersistenceValidationException(
                operation,
                "The OpenTelemetry persistence operation contains an invalid record.",
                context,
                exception),
            RecordPayloadException when operation == "write" => new OpenTelemetryPersistenceValidationException(
                operation,
                "The OpenTelemetry persistence operation contains a record that cannot be represented canonically.",
                context,
                exception),
            RecordPayloadException => new OpenTelemetryPersistenceDataException(
                operation,
                "The OpenTelemetry persistence operation encountered a malformed durable record.",
                context,
                exception),
            JsonException => new OpenTelemetryPersistenceDataException(
                operation,
                "The OpenTelemetry persistence operation encountered malformed durable data.",
                context,
                exception),
            _ => new OpenTelemetryPersistenceUnavailableException(
                operation,
                "The OpenTelemetry persistence operation could not be completed.",
                context,
                exception)
        };

    private static IReadOnlyDictionary<string, string> Context(
        params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private int ClampTake(int? requested) => Math.Clamp(requested ?? _maxQuerySize, 0, _maxQuerySize);

    private static int ClampCapacity(int value) => Math.Max(1, value);

    private static int ToCount(long value) => value >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool RecordEquals(DiagnosticRecordInput first, DiagnosticRecordInput second)
    {
        if (first.OccurredAt != second.OccurredAt || !StringComparer.Ordinal.Equals(first.Payload, second.Payload))
            return false;
        var firstFields = first.Fields ?? new Dictionary<string, IReadOnlyList<DiagnosticFieldValue>>();
        var secondFields = second.Fields ?? new Dictionary<string, IReadOnlyList<DiagnosticFieldValue>>();
        return firstFields.Count == secondFields.Count && firstFields.All(field =>
            secondFields.TryGetValue(field.Key, out var values) && field.Value.SequenceEqual(values));
    }

    private void ObserveAcknowledgement(
        OpenTelemetryBatch batch,
        Task<bool> acknowledgement)
    {
        if (acknowledgement.IsFaulted)
        {
            _ = acknowledgement.Exception;
            CountDropped(batch);
            return;
        }
        if (acknowledgement.IsCompleted)
            return;

        _ = acknowledgement.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                CountDropped(batch);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void CountDropped(OpenTelemetryBatch batch)
    {
        Interlocked.Add(ref _droppedTraces, batch.Traces.Count);
        Interlocked.Add(ref _droppedSpans, batch.Spans.Count);
        Interlocked.Add(ref _droppedMetricPoints, batch.MetricPoints.Count);
        Interlocked.Add(ref _droppedLogRecords, batch.Logs.Count);
    }

    public void Dispose() => _drain.Dispose();

    public ValueTask DisposeAsync() => _drain.DisposeAsync();

    private sealed class GroundworkOpenTelemetryDrainTarget(GroundworkOpenTelemetryStore store)
        : IDiagnosticsDrainTarget<OpenTelemetryBatch, bool>
    {
        private const string RetentionOperationPrefix = "otel-ret-v1:";
        private readonly object _retentionGate = new();
        private RetentionCycle? _retentionCycle;

        public async ValueTask<DiagnosticsDrainCommit<bool>> CommitAsync(
            DiagnosticsDrainBatch<OpenTelemetryBatch> batch,
            CancellationToken cancellationToken = default)
        {
            var results = new bool[batch.Items.Count];
            var retentionUnits = 0;
            for (var index = 0; index < batch.Items.Count; index++)
            {
                var childId = ChildBatchId(batch.Id, index);
                var committed = await store.WriteDurablyAsync(
                    childId,
                    batch.Items[index],
                    applyRetention: false,
                    cancellationToken);
                retentionUnits = checked(retentionUnits + committed);
                results[index] = true;
            }

            return new(results, retentionUnits);
        }

        public async ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            RetentionCycle cycle;
            lock (_retentionGate)
                cycle = _retentionCycle ??= CreateRetentionCycle(store._timeProvider.GetUtcNow());

            var deleted = await store.ApplySignalRetentionAsync(cycle, cancellationToken);
            lock (_retentionGate)
            {
                if (ReferenceEquals(_retentionCycle, cycle))
                    _retentionCycle = null;
            }

            return deleted;
        }

        private static DiagnosticsDrainBatchId ChildBatchId(
            DiagnosticsDrainBatchId parent,
            int index)
        {
            Span<byte> input = stackalloc byte[20];
            parent.Value.TryWriteBytes(input[..16]);
            BinaryPrimitives.WriteInt32BigEndian(input[16..], index);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(input, hash);
            return new(new Guid(hash[..16]), parent.IssuedAt);
        }

        private static RetentionCycle CreateRetentionCycle(DateTimeOffset issuedAt)
        {
            var nonce = Guid.NewGuid().ToString("N");
            return new(
                new(issuedAt, $"{RetentionOperationPrefix}{nonce}:traces"),
                new(issuedAt, $"{RetentionOperationPrefix}{nonce}:spans"),
                new(issuedAt, $"{RetentionOperationPrefix}{nonce}:metric-points"),
                new(issuedAt, $"{RetentionOperationPrefix}{nonce}:logs"));
        }
    }

    private sealed record RetentionCycle(
        DiagnosticOperationId Traces,
        DiagnosticOperationId Spans,
        DiagnosticOperationId MetricPoints,
        DiagnosticOperationId Logs);

    private sealed record CaptureOperation(
        int LedgerSchemaVersion,
        string BatchId,
        string Fingerprint,
        DateTimeOffset CreatedAt,
        string TenantId,
        string ScopeId,
        string SourceId,
        Dictionary<string, CaptureStreamAttempt> Streams);

    private sealed record CaptureStreamAttempt(
        DateTimeOffset IssuedAt,
        DateTimeOffset RetryUntil,
        bool Committed);

    private sealed record CaptureOperationSnapshot(CaptureOperation Operation, long Version);

    private sealed record StreamTarget(
        string Kind,
        IDiagnosticRecordStore Store,
        DiagnosticStreamId Stream,
        DiagnosticRecordStreamDefinition Definition,
        int Capacity,
        IReadOnlyList<DiagnosticRecordInput> Records);

    private sealed class UnicodeOrdinalIgnoreCaseIdentityComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) =>
            ReferenceEquals(x, y) ||
            x is not null &&
            y is not null &&
            StringComparer.Ordinal.Equals(
                DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(x),
                DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(y));

        public int GetHashCode(string value) =>
            StringComparer.Ordinal.GetHashCode(
                DiagnosticStringComparisonKey.CreateUnicodeOrdinalIgnoreCase(value));
    }
}
