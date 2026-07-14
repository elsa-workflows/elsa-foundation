using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.Persistence.Draining;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Durable Groundwork implementation of the provider-neutral OpenTelemetry store contract. Immutable signals use
/// four append streams. The adapter rejects catalog identity writes and case-equivalence-dependent reads until
/// Groundwork supplies the portable comparison-key route tracked by issues #70 and #71. A durable capture ledger binds
/// a caller-owned drain batch identity to canonical input and tracks a bounded attempt for each stream independently.
/// </summary>
/// <remarks>
/// The interim exact read surface is storage diagnostics plus unfiltered telemetry-log restart reads (with either no
/// time range or a closed inclusive range). Resource results, trace results/detail, metric results, catalog writes, and
/// every string-filtered log query fail before provider I/O. Those paths must not be enabled until #70/#71 are consumed.
/// </remarks>
public sealed class GroundworkOpenTelemetryStore : IOpenTelemetryStore
{
    private const int MaxLedgerWriteAttempts = 8;
    private const int LedgerSchemaVersion = 2;
    private static readonly JsonSerializerOptions LedgerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly string[] StreamKinds = ["traces", "spans", "metric-points", "logs"];

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
        _maxQuerySize = ClampCapacity(value.MaxQuerySize);
    }

    public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) =>
        WriteAsync(DiagnosticsDrainBatchId.New(), batch, cancellationToken);

    /// <summary>
    /// Commits one drain batch. The same identity may be retried with the same canonical input; a different identity
    /// always represents an independent capture, even when its content is byte-for-byte identical.
    /// </summary>
    public async ValueTask WriteAsync(
        DiagnosticsDrainBatchId batchId,
        OpenTelemetryBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batchId.Value == Guid.Empty)
            throw new ArgumentException("The diagnostics drain batch identity cannot be empty.", nameof(batchId));
        cancellationToken.ThrowIfCancellationRequested();
        RejectCatalogIdentityWrites(batch);

        var context = Context(("batchId", batchId.ToString()));
        DiagnosticRecordInput[] traces;
        DiagnosticRecordInput[] spans;
        DiagnosticRecordInput[] points;
        DiagnosticRecordInput[] logs;
        try
        {
            traces = NormalizeTraces(batch.Traces);
            spans = NormalizeRecords(batch.Spans, span => _recordSerializer.ToRecord(span.Id, span));
            points = NormalizeRecords(batch.MetricPoints, point => _recordSerializer.ToRecord(point.Id, point));
            logs = NormalizeRecords(batch.Logs, log => _recordSerializer.ToRecord(log.Id, log));
        }
        catch (RecordPayloadException exception)
        {
            throw new OpenTelemetryPersistenceValidationException(
                "write",
                "The OpenTelemetry persistence operation contains a record that cannot be represented canonically.",
                context,
                exception);
        }

        try
        {
            var fingerprint = CaptureFingerprint(traces, spans, points, logs);
            var targets = new[]
            {
                new StreamTarget("traces", _stores.Traces, _traceStream, _traceDefinition, PhysicalizeRecordIds(batchId, "traces", traces)),
                new StreamTarget("spans", _stores.Spans, _spanStream, _spanDefinition, PhysicalizeRecordIds(batchId, "spans", spans)),
                new StreamTarget("metric-points", _stores.MetricPoints, _metricPointStream, _metricPointDefinition, PhysicalizeRecordIds(batchId, "metric-points", points)),
                new StreamTarget("logs", _stores.Logs, _logStream, _logDefinition, PhysicalizeRecordIds(batchId, "logs", logs))
            };
            Preflight(batchId, targets);
            await GetOrCreateCaptureOperationAsync(batchId, fingerprint, cancellationToken);

            foreach (var target in targets)
                await AppendAsync(batchId, fingerprint, target, cancellationToken);
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

    public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(
        OpenTelemetryResourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        throw UnsupportedCaseIdentity(
            "query-resources",
            "Resource catalog results require case-insensitive resource identity.");
    }

    public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(
        OpenTelemetryTraceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        throw UnsupportedCaseIdentity(
            "query-traces",
            "Trace results require case-insensitive latest-per-trace identity.");
    }

    public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        throw UnsupportedCaseIdentity(
            "get-trace",
            "Trace detail lookup requires case-insensitive trace identity and catalog joins.");
    }

    public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(
        OpenTelemetryMetricFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        throw UnsupportedCaseIdentity(
            "query-metrics",
            "Metric results require case-insensitive instrument identity and catalog joins.");
    }

    public async ValueTask<OpenTelemetryLogResult> QueryLogsAsync(
        OpenTelemetryLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RejectUnsupportedTextFilter(filter.ServiceName, nameof(filter.ServiceName));
        RejectUnsupportedTextFilter(filter.Search, nameof(filter.Search));
        RejectUnsupportedTextFilter(filter.ResourceId, nameof(filter.ResourceId));
        RejectUnsupportedTextFilter(filter.TraceId, nameof(filter.TraceId));
        RejectUnsupportedTextFilter(filter.SpanId, nameof(filter.SpanId));
        RejectUnsupportedTextFilter(filter.Severity, nameof(filter.Severity));
        RejectOneSidedRange(filter.From, filter.To);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new([], 0);

        try
        {
            var predicates = new List<DiagnosticRecordPredicate>();
            AddRange(predicates, RecordFields.Timestamp, filter.From, filter.To);
            var page = await _stores.Logs.QueryAsync(new(
                _scope,
                _logStream,
                take,
                new(RecordFields.Timestamp, DiagnosticSortDirection.Descending),
                Predicate: All(predicates)), cancellationToken);
            return new(page.Records.Select(_recordSerializer.ToLog).Reverse().ToArray(), 0);
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
        CancellationToken cancellationToken)
    {
        if (target.Records.Count == 0)
            return;

        var attempt = await GetOrCreateStreamAttemptAsync(
            batchId,
            fingerprint,
            target,
            cancellationToken);
        if (attempt.Committed)
            return;

        var operationId = OperationId(batchId, target.Kind, attempt.IssuedAt);
        var request = DiagnosticRecordBatch.Create(_scope, target.Stream, operationId, target.Records);
        await target.Store.AppendAsync(request, cancellationToken);
        await MarkStreamCommittedAsync(batchId, fingerprint, target.Kind, CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
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
        _stores.Documents.SaveAsync(new(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            operation.BatchId,
            OpenTelemetryGroundworkStorageSchema.SchemaVersion,
            JsonSerializer.Serialize(operation, LedgerJsonOptions),
            expectedVersion), cancellationToken);

    private CaptureOperationSnapshot ReadOperation(DocumentEnvelope envelope)
    {
        try
        {
            if (!StringComparer.Ordinal.Equals(envelope.SchemaVersion, OpenTelemetryGroundworkStorageSchema.SchemaVersion))
                throw CorruptOperation(envelope.Id, "The OpenTelemetry capture operation schema is unsupported.");
            if (!StringComparer.Ordinal.Equals(envelope.DocumentKind, OpenTelemetryGroundworkStorageSchema.OperationLedgerKind))
                throw CorruptOperation(envelope.Id, "The OpenTelemetry capture operation kind is invalid.");
            var operation = JsonSerializer.Deserialize<CaptureOperation>(envelope.ContentJson, LedgerJsonOptions)
                            ?? throw CorruptOperation(envelope.Id, "The OpenTelemetry capture operation is empty.");
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
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
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

    private static void RejectCatalogIdentityWrites(OpenTelemetryBatch batch)
    {
        if (batch.Resources.Count > 0 || batch.Instruments.Count > 0)
        {
            throw UnsupportedCaseIdentity(
                "write",
                "Resource and instrument catalog writes require provider-neutral case-insensitive document identity.");
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
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _traceStream, traces).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _spanStream, spans).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _metricPointStream, points).Value);
        Append(hash, DiagnosticRequestFingerprint.ForAppend(_scope, _logStream, logs).Value);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

#pragma warning disable GW0004
    private Task<DocumentQueryResult> QueryDocumentsAsync(string kind, int take, CancellationToken cancellationToken) =>
        _stores.Documents.QueryAsync(new PortableDocumentQuery(kind, take: take), cancellationToken);
#pragma warning restore GW0004

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
            throw new OpenTelemetryPersistenceCapabilityException(
                "query-logs",
                "portable-text-filter",
                $"Filter '{parameterName}' requires the portable comparison-key or long-text query work tracked separately from restart persistence.",
                Context(("filter", parameterName)));
    }

    private static OpenTelemetryPersistenceCapabilityException UnsupportedCaseIdentity(
        string operation,
        string detail) =>
        new(
            operation,
            "portable-case-equivalence",
            $"{detail} This operation is unavailable until the portable case-equivalence route is available.");

    private static void RejectOneSidedRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if ((from is null) != (to is null))
            throw new OpenTelemetryPersistenceCapabilityException(
                "query-logs",
                "one-sided-time-range",
                "One-sided OpenTelemetry ranges require the portable query-at-scale capability.");
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
        IReadOnlyList<DiagnosticRecordInput> Records);
}
