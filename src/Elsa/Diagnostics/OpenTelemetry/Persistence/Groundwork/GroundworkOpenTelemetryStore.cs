using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Durable Groundwork implementation of the provider-neutral OpenTelemetry store contract. Immutable signals use
/// four append streams; current resources and instruments use scoped keyed documents. A durable capture ledger fixes
/// the operation issue time before the first append so retries after a partial multi-stream commit replay exactly.
/// </summary>
public sealed class GroundworkOpenTelemetryStore : IOpenTelemetryStore
{
    private const int MaxCatalogWriteAttempts = 8;
    private const int LedgerSchemaVersion = 1;
    private static readonly DateTimeOffset FingerprintObservationTime = DateTimeOffset.UnixEpoch;
    private static readonly JsonSerializerOptions LedgerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly GroundworkOpenTelemetryStores _stores;
    private readonly GroundworkOpenTelemetryBinding _binding;
    private readonly DiagnosticStorageScope _scope;
    private readonly DiagnosticStreamId _traceStream;
    private readonly DiagnosticStreamId _spanStream;
    private readonly DiagnosticStreamId _metricPointStream;
    private readonly DiagnosticStreamId _logStream;
    private readonly CanonicalRecordSerializer _recordSerializer = new();
    private readonly CatalogDocumentSerializer _catalogSerializer = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _traceCapacity;
    private readonly int _spanCapacity;
    private readonly int _metricPointCapacity;
    private readonly int _logRecordCapacity;
    private readonly int _maxQuerySize;

    public GroundworkOpenTelemetryStore(
        GroundworkOpenTelemetryStores stores,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        GroundworkOpenTelemetryBinding binding,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(stores.Traces);
        ArgumentNullException.ThrowIfNull(stores.Spans);
        ArgumentNullException.ThrowIfNull(stores.MetricPoints);
        ArgumentNullException.ThrowIfNull(stores.Logs);
        ArgumentNullException.ThrowIfNull(stores.Documents);
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
        _timeProvider = timeProvider ?? TimeProvider.System;
        var value = options.Value;
        _traceCapacity = ClampCapacity(value.TraceCapacity);
        _spanCapacity = ClampCapacity(value.SpanCapacity);
        _metricPointCapacity = ClampCapacity(value.MetricPointCapacity);
        _logRecordCapacity = ClampCapacity(value.LogRecordCapacity);
        _maxQuerySize = ClampCapacity(value.MaxQuerySize);
    }

    public async ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        var traces = NormalizeTraces(batch.Traces);
        var spans = NormalizeRecords(batch.Spans, span => _recordSerializer.ToRecord(span.Id, span));
        var points = NormalizeRecords(batch.MetricPoints, point => _recordSerializer.ToRecord(point.Id, point));
        var logs = NormalizeRecords(batch.Logs, log => _recordSerializer.ToRecord(log.Id, log));
        var resources = NormalizeResources(batch.Resources);
        var instruments = NormalizeInstruments(batch.Instruments);
        var fingerprint = CaptureFingerprint(traces, spans, points, logs, resources, instruments);
        var operation = await GetOrCreateCaptureOperationAsync(fingerprint, cancellationToken);

        foreach (var resource in resources)
            await UpsertResourceAsync(resource, cancellationToken);

        var pointTimes = batch.MetricPoints
            .GroupBy(x => x.InstrumentId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Max(point => point.Timestamp), StringComparer.Ordinal);
        foreach (var instrument in instruments)
        {
            var lastSeen = pointTimes.GetValueOrDefault(instrument.Id, operation.IssuedAt);
            await UpsertInstrumentAsync(instrument, lastSeen, cancellationToken);
        }

        await AppendAsync(_stores.Traces, _traceStream, "traces", traces, operation, cancellationToken);
        await AppendAsync(_stores.Spans, _spanStream, "spans", spans, operation, cancellationToken);
        await AppendAsync(_stores.MetricPoints, _metricPointStream, "metric-points", points, operation, cancellationToken);
        await AppendAsync(_stores.Logs, _logStream, "logs", logs, operation, cancellationToken);
    }

    public async ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(
        OpenTelemetryResourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.Search, nameof(filter.Search));
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], 0);

        IReadOnlyList<DocumentEnvelope> documents;
        if (!string.IsNullOrWhiteSpace(filter.ServiceName))
        {
            documents = await QueryCatalogByIndexAsync(
                CatalogDocuments.ResourceKind,
                OpenTelemetryGroundworkStorageSchema.ByServiceNameIndex,
                filter.ServiceName,
                cancellationToken);
        }
        else if (filter.Status is { } status)
        {
            documents = await QueryCatalogByIndexAsync(
                CatalogDocuments.ResourceKind,
                OpenTelemetryGroundworkStorageSchema.ByResourceStatusIndex,
                ((int)status).ToString(CultureInfo.InvariantCulture),
                cancellationToken);
        }
        else
        {
            documents = (await QueryDocumentsAsync(CatalogDocuments.ResourceKind, _maxQuerySize, cancellationToken)).Documents;
        }

        var resources = documents
            .Select(_catalogSerializer.ToResource)
            .Select(x => x.Resource)
            .Where(x => filter.Status is null || x.Status == filter.Status)
            .OrderByDescending(x => x.LastSeen)
            .ThenBy(x => x.ServiceName, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(take)
            .ToArray();
        return new(resources, 0);
    }

    public async ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(
        OpenTelemetryTraceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.ServiceName, nameof(filter.ServiceName));
        RejectUnsupportedTextFilter(filter.Search, nameof(filter.Search));
        RejectOneSidedRange(filter.From, filter.To);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], 0);

        var predicates = new List<DiagnosticRecordPredicate>();
        AddContains(predicates, RecordFields.TraceId, filter.TraceId);
        AddEqual(predicates, RecordFields.ResourceId, filter.ResourceId);
        AddContains(predicates, RecordFields.WorkflowInstanceId, filter.WorkflowInstanceId);
        if (filter.Status is { } status)
            predicates.Add(DiagnosticRecordPredicate.Equal(RecordFields.Status, DiagnosticFieldValue.Int64((long)status)));
        AddRange(predicates, RecordFields.StartTime, filter.From, filter.To);

        var page = await _stores.Traces.QueryAsync(new(
            _scope,
            _traceStream,
            take,
            new(RecordFields.StartTime, DiagnosticSortDirection.Descending),
            Predicate: All(predicates),
            LatestPerKeyField: RecordFields.TraceId), cancellationToken);
        var traces = page.Records.Select(_recordSerializer.ToTrace).Reverse().ToArray();
        return new(traces, 0);
    }

    public async ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        var tracePage = await _stores.Traces.QueryAsync(new(
            _scope,
            _traceStream,
            1,
            new(RecordFields.StartTime, DiagnosticSortDirection.Descending),
            Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String(traceId)),
            LatestPerKeyField: RecordFields.TraceId), cancellationToken);
        if (tracePage.Records.Count == 0)
            return null;

        var trace = _recordSerializer.ToTrace(tracePage.Records[0]);
        var spansPage = await _stores.Spans.QueryAsync(new(
            _scope,
            _spanStream,
            _maxQuerySize,
            new(RecordFields.StartTime),
            Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String(traceId))), cancellationToken);
        var logsPage = await _stores.Logs.QueryAsync(new(
            _scope,
            _logStream,
            _maxQuerySize,
            new(RecordFields.Timestamp),
            Predicate: DiagnosticRecordPredicate.Equal(RecordFields.TraceId, DiagnosticFieldValue.String(traceId))), cancellationToken);

        var resources = new List<TelemetryResource>();
        foreach (var resourceId in trace.ResourceIds.Order(StringComparer.Ordinal))
        {
            var document = await _stores.Documents.LoadAsync(CatalogDocuments.ResourceKind, resourceId, cancellationToken);
            if (document is not null)
                resources.Add(_catalogSerializer.ToResource(document).Resource);
        }

        return new(
            trace,
            spansPage.Records.Select(_recordSerializer.ToSpan).ToArray(),
            resources,
            logsPage.Records.Select(_recordSerializer.ToLog).ToArray());
    }

    public async ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(
        OpenTelemetryMetricFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.ServiceName, nameof(filter.ServiceName));
        RejectOneSidedRange(filter.From, filter.To);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], [], 0);

        var predicates = new List<DiagnosticRecordPredicate>();
        AddEqual(predicates, RecordFields.ResourceId, filter.ResourceId);
        AddContains(predicates, RecordFields.InstrumentName, filter.InstrumentName);
        AddRange(predicates, RecordFields.Timestamp, filter.From, filter.To);
        var page = await _stores.MetricPoints.QueryAsync(new(
            _scope,
            _metricPointStream,
            take,
            new(RecordFields.Timestamp, DiagnosticSortDirection.Descending),
            Predicate: All(predicates)), cancellationToken);
        var points = page.Records.Select(_recordSerializer.ToMetricPoint).Reverse().ToArray();

        var instruments = new List<MetricInstrument>();
        foreach (var instrumentId in points.Select(x => x.InstrumentId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var document = await _stores.Documents.LoadAsync(CatalogDocuments.InstrumentKind, instrumentId, cancellationToken);
            if (document is not null)
                instruments.Add(_catalogSerializer.ToInstrument(document).Instrument);
        }
        return new(instruments, points, 0);
    }

    public async ValueTask<OpenTelemetryLogResult> QueryLogsAsync(
        OpenTelemetryLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.ServiceName, nameof(filter.ServiceName));
        RejectUnsupportedTextFilter(filter.Search, nameof(filter.Search));
        RejectOneSidedRange(filter.From, filter.To);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], 0);

        var predicates = new List<DiagnosticRecordPredicate>();
        AddEqual(predicates, RecordFields.ResourceId, filter.ResourceId);
        AddContains(predicates, RecordFields.TraceId, filter.TraceId);
        AddContains(predicates, RecordFields.SpanId, filter.SpanId);
        AddContains(predicates, RecordFields.SeverityText, filter.Severity);
        AddRange(predicates, RecordFields.Timestamp, filter.From, filter.To);
        var page = await _stores.Logs.QueryAsync(new(
            _scope,
            _logStream,
            take,
            new(RecordFields.Timestamp, DiagnosticSortDirection.Descending),
            Predicate: All(predicates)), cancellationToken);
        return new(page.Records.Select(_recordSerializer.ToLog).Reverse().ToArray(), 0);
    }

    public async ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var traces = await _stores.Traces.InspectAsync(new(_scope, _traceStream), cancellationToken);
        var spans = await _stores.Spans.InspectAsync(new(_scope, _spanStream), cancellationToken);
        var points = await _stores.MetricPoints.InspectAsync(new(_scope, _metricPointStream), cancellationToken);
        var logs = await _stores.Logs.InspectAsync(new(_scope, _logStream), cancellationToken);
        var resources = await QueryDocumentsAsync(CatalogDocuments.ResourceKind, 0, cancellationToken);
        var instruments = await QueryDocumentsAsync(CatalogDocuments.InstrumentKind, 0, cancellationToken);
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
            0,
            0,
            0,
            0);
    }

    private async Task AppendAsync(
        IDiagnosticRecordStore store,
        DiagnosticStreamId stream,
        string streamKind,
        IReadOnlyList<DiagnosticRecordInput> records,
        CaptureOperation operation,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return;

        var operationId = new DiagnosticOperationId(operation.IssuedAt, $"otel-v1:{operation.Fingerprint}:{streamKind}");
        await store.AppendAsync(DiagnosticRecordBatch.Create(_scope, stream, operationId, records), cancellationToken);
    }

    private async Task<CaptureOperation> GetOrCreateCaptureOperationAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await _stores.Documents.LoadAsync(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            fingerprint,
            cancellationToken);
        if (existing is not null)
            return ReadOperation(existing, fingerprint);

        var candidate = new CaptureOperation(
            LedgerSchemaVersion,
            fingerprint,
            _timeProvider.GetUtcNow(),
            _binding.TenantId,
            _binding.ScopeId,
            _binding.SourceId);
        var content = JsonSerializer.Serialize(candidate, LedgerJsonOptions);
        for (var attempt = 0; attempt < MaxCatalogWriteAttempts; attempt++)
        {
            var result = await _stores.Documents.SaveAsync(new(
                OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
                fingerprint,
                OpenTelemetryGroundworkStorageSchema.SchemaVersion,
                content,
                ExpectedVersion: 0), cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return candidate;

            existing = await _stores.Documents.LoadAsync(
                OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
                fingerprint,
                cancellationToken);
            if (existing is not null)
                return ReadOperation(existing, fingerprint);
        }

        throw new InvalidOperationException("The OpenTelemetry capture operation could not be claimed after concurrent retries.");
    }

    private CaptureOperation ReadOperation(DocumentEnvelope envelope, string fingerprint)
    {
        try
        {
            if (!StringComparer.Ordinal.Equals(envelope.SchemaVersion, OpenTelemetryGroundworkStorageSchema.SchemaVersion))
                throw new InvalidOperationException("The OpenTelemetry capture operation schema is unsupported.");
            if (!StringComparer.Ordinal.Equals(envelope.DocumentKind, OpenTelemetryGroundworkStorageSchema.OperationLedgerKind))
                throw new InvalidOperationException("The OpenTelemetry capture operation kind is invalid.");
            var operation = JsonSerializer.Deserialize<CaptureOperation>(envelope.ContentJson, LedgerJsonOptions)
                            ?? throw new InvalidOperationException("The OpenTelemetry capture operation is empty.");
            if (operation.LedgerSchemaVersion != LedgerSchemaVersion ||
                !StringComparer.Ordinal.Equals(operation.Fingerprint, fingerprint) ||
                !StringComparer.Ordinal.Equals(envelope.Id, fingerprint) ||
                !StringComparer.Ordinal.Equals(operation.TenantId, _binding.TenantId) ||
                !StringComparer.Ordinal.Equals(operation.ScopeId, _binding.ScopeId) ||
                !StringComparer.Ordinal.Equals(operation.SourceId, _binding.SourceId))
            {
                throw new InvalidOperationException("The OpenTelemetry capture operation does not match this storage binding.");
            }
            return operation;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The OpenTelemetry capture operation is malformed.", exception);
        }
    }

    private async Task UpsertResourceAsync(TelemetryResource incoming, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCatalogWriteAttempts; attempt++)
        {
            var existing = await _stores.Documents.LoadAsync(CatalogDocuments.ResourceKind, incoming.Id, cancellationToken);
            var target = incoming;
            long expectedVersion = 0;
            if (existing is not null)
            {
                var entry = _catalogSerializer.ToResource(existing);
                expectedVersion = entry.Revision;
                if (entry.Resource.LastSeen > incoming.LastSeen)
                    return;
                if (entry.Resource.LastSeen == incoming.LastSeen)
                {
                    var incomingContent = _catalogSerializer.ToSaveRequest(incoming).ContentJson;
                    var existingContent = _catalogSerializer.ToSaveRequest(entry.Resource).ContentJson;
                    if (StringComparer.Ordinal.Compare(existingContent, incomingContent) <= 0)
                        return;
                }
                target = incoming with { LastSeen = Max(incoming.LastSeen, entry.Resource.LastSeen) };
            }

            var request = _catalogSerializer.ToSaveRequest(target, expectedVersion);
            var result = await _stores.Documents.SaveAsync(request, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return;
        }
        throw new InvalidOperationException($"OpenTelemetry resource '{incoming.Id}' could not be updated after concurrent retries.");
    }

    private async Task UpsertInstrumentAsync(
        MetricInstrument incoming,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCatalogWriteAttempts; attempt++)
        {
            var existing = await _stores.Documents.LoadAsync(CatalogDocuments.InstrumentKind, incoming.Id, cancellationToken);
            var targetObservedAt = observedAt;
            long expectedVersion = 0;
            if (existing is not null)
            {
                var entry = _catalogSerializer.ToInstrument(existing);
                expectedVersion = entry.Revision;
                if (entry.LastSeen > observedAt)
                    return;
                if (entry.LastSeen == observedAt)
                {
                    var incomingContent = _catalogSerializer.ToSaveRequest(incoming, observedAt).ContentJson;
                    var existingContent = _catalogSerializer.ToSaveRequest(entry.Instrument, entry.LastSeen).ContentJson;
                    if (StringComparer.Ordinal.Compare(existingContent, incomingContent) <= 0)
                        return;
                }
                targetObservedAt = Max(observedAt, entry.LastSeen);
            }

            var request = _catalogSerializer.ToSaveRequest(incoming, targetObservedAt, expectedVersion);
            var result = await _stores.Documents.SaveAsync(request, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return;
        }
        throw new InvalidOperationException($"OpenTelemetry metric instrument '{incoming.Id}' could not be updated after concurrent retries.");
    }

    private DiagnosticRecordInput[] NormalizeTraces(IReadOnlyCollection<TelemetryTrace> values) =>
        NormalizeRecords(values, trace =>
        {
            var provisional = _recordSerializer.ToRecord(trace.TraceId, trace);
            return provisional with { RecordId = $"trace-{Hash(provisional.Payload)}" };
        });

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
                    throw new InvalidOperationException($"OpenTelemetry record id '{record.RecordId}' identifies conflicting payloads in one batch.");
                continue;
            }
            result.Add(record.RecordId, record);
        }
        return result.Values.OrderBy(x => x.RecordId, StringComparer.Ordinal).ToArray();
    }

    private TelemetryResource[] NormalizeResources(IReadOnlyCollection<TelemetryResource> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values
            .Select(value => (Value: value, Content: _catalogSerializer.ToSaveRequest(value).ContentJson))
            .GroupBy(x => x.Value.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(x => x.Value.LastSeen)
                .ThenBy(x => x.Content, StringComparer.Ordinal)
                .First().Value)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private MetricInstrument[] NormalizeInstruments(IReadOnlyCollection<MetricInstrument> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values
            .Select(value => (Value: value, Content: _catalogSerializer.ToSaveRequest(value, FingerprintObservationTime).ContentJson))
            .GroupBy(x => x.Value.Id, StringComparer.Ordinal)
            .Select(group => group.OrderBy(x => x.Content, StringComparer.Ordinal).First().Value)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private string CaptureFingerprint(
        IReadOnlyList<DiagnosticRecordInput> traces,
        IReadOnlyList<DiagnosticRecordInput> spans,
        IReadOnlyList<DiagnosticRecordInput> points,
        IReadOnlyList<DiagnosticRecordInput> logs,
        IReadOnlyList<TelemetryResource> resources,
        IReadOnlyList<MetricInstrument> instruments)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "elsa-open-telemetry-capture-v1");
        Append(hash, _binding.TenantId);
        Append(hash, _binding.ScopeId);
        Append(hash, _binding.SourceId);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _traceStream, traces).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _spanStream, spans).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _metricPointStream, points).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _logStream, logs).Value);
        Append(hash, resources.Count);
        foreach (var request in resources.Select(x => _catalogSerializer.ToSaveRequest(x)))
        {
            Append(hash, request.DocumentKind);
            Append(hash, request.Id);
            Append(hash, request.ContentJson);
        }
        Append(hash, instruments.Count);
        foreach (var request in instruments.Select(x => _catalogSerializer.ToSaveRequest(x, FingerprintObservationTime)))
        {
            Append(hash, request.DocumentKind);
            Append(hash, request.Id);
            Append(hash, request.ContentJson);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

#pragma warning disable GW0004
    private Task<IReadOnlyList<DocumentEnvelope>> QueryCatalogByIndexAsync(
        string kind,
        string index,
        string value,
        CancellationToken cancellationToken) =>
        _stores.Documents.QueryAsync(new DocumentStoreQuery(kind, index, value, take: _maxQuerySize), cancellationToken);

    private Task<DocumentQueryResult> QueryDocumentsAsync(string kind, int take, CancellationToken cancellationToken) =>
        _stores.Documents.QueryAsync(new PortableDocumentQuery(kind, take: take), cancellationToken);
#pragma warning restore GW0004

    private static void AddEqual(List<DiagnosticRecordPredicate> predicates, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            predicates.Add(DiagnosticRecordPredicate.Equal(field, DiagnosticFieldValue.String(value)));
    }

    private static void AddContains(List<DiagnosticRecordPredicate> predicates, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            predicates.Add(DiagnosticRecordPredicate.Contains(field, value));
    }

    private static void AddRange(
        List<DiagnosticRecordPredicate> predicates,
        string field,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from is not null && to is not null)
            predicates.Add(DiagnosticRecordPredicate.RangeInclusive(
                field,
                DiagnosticFieldValue.Timestamp(from.Value),
                DiagnosticFieldValue.Timestamp(to.Value)));
    }

    private static DiagnosticRecordPredicate? All(IReadOnlyList<DiagnosticRecordPredicate> predicates) =>
        predicates.Count switch
        {
            0 => null,
            1 => predicates[0],
            _ => new DiagnosticRecordPredicate.All(predicates)
        };

    private static void RejectUnsupportedTextFilter(string? value, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            throw new NotSupportedException(
                $"Filter '{parameterName}' requires the portable comparison-key or long-text query work tracked separately from restart persistence.");
    }

    private static void RejectOneSidedRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if ((from is null) != (to is null))
            throw new NotSupportedException("One-sided OpenTelemetry ranges require the provider query-at-scale work tracked separately.");
    }

    private int ClampTake(int? requested) => Math.Clamp(requested ?? _maxQuerySize, 0, _maxQuerySize);

    private static int ClampCapacity(int value) => Math.Max(1, value);

    private static int ToCount(long value) => value >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first >= second ? first : second;

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

    private static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
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

    private sealed record CaptureOperation(
        int LedgerSchemaVersion,
        string Fingerprint,
        DateTimeOffset IssuedAt,
        string TenantId,
        string ScopeId,
        string SourceId);
}
