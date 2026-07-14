using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.Persistence.Draining;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryStoreTests : IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

    [Fact]
    public void Tenant_scope_and_source_are_all_part_of_non_aliasable_stream_identity()
    {
        var bindings = new[]
        {
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a"),
            GroundworkOpenTelemetryBinding.Create("tenant-b", "shell-a", "collector-a"),
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-b", "collector-a"),
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-b")
        };

        Assert.Empty(typeof(GroundworkOpenTelemetryBinding).GetConstructors());
        var streams = bindings.SelectMany(binding =>
            new[] { binding.TraceStreamId, binding.SpanStreamId, binding.MetricPointStreamId, binding.LogStreamId }).ToArray();
        Assert.Equal(streams.Length, streams.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Document_session_must_match_the_explicit_tenant_scope_and_source_binding()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var wrongSource = GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-b");

        Assert.Throws<ArgumentException>(() => new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            wrongSource));
    }

    [Fact]
    public async Task Partial_multi_stream_retry_after_restart_reuses_batch_identity_and_does_not_duplicate_records()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new ObservingRecordStore(providers.Traces);
        var points = new ObservingRecordStore(providers.MetricPoints, failFirstAppend: true);
        var store = _fixture.CreateStore(providers with
        {
            Traces = traces,
            MetricPoints = points
        });
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.WriteAsync(batchId, batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.ProviderFailure, failure.Reason);
        Assert.Equal("write", failure.Operation);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<IOException>(failure.InnerException);
        var partial = await store.GetDiagnosticsAsync();
        Assert.Equal(1, partial.TraceCount);
        Assert.Equal(1, partial.SpanCount);
        Assert.Equal(0, partial.MetricPointCount);
        Assert.Equal(0, partial.LogRecordCount);

        var restartedProviders = await _fixture.CreateProvidersAsync();
        var restartedTraces = new ObservingRecordStore(restartedProviders.Traces);
        var restartedPoints = new ObservingRecordStore(restartedProviders.MetricPoints);
        var restarted = _fixture.CreateStore(restartedProviders with
        {
            Traces = restartedTraces,
            MetricPoints = restartedPoints
        });
        await restarted.WriteAsync(batchId, batch);

        var completed = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (completed.TraceCount, completed.SpanCount, completed.MetricPointCount, completed.LogRecordCount));
        Assert.Single(traces.Requests);
        Assert.Empty(restartedTraces.Requests);
        Assert.Equal([DiagnosticAppendStatus.Committed], traces.Outcomes);
        Assert.Empty(restartedTraces.Outcomes);
        Assert.Single(points.Requests);
        Assert.Single(restartedPoints.Requests);
        Assert.Equal(points.Requests[0].OperationId, restartedPoints.Requests[0].OperationId);
        Assert.Equal(points.Requests[0].RequestFingerprint, restartedPoints.Requests[0].RequestFingerprint);
        Assert.Empty(points.Outcomes);
        Assert.Equal([DiagnosticAppendStatus.Committed], restartedPoints.Outcomes);

        Assert.True((await CaptureOperationsAsync(restartedProviders.Documents)).Single().Version > 1);
    }

    [Fact]
    public async Task Identical_independent_captures_do_not_collapse()
    {
        var store = await _fixture.CreateStoreAsync();
        var batch = CreateBatch(includeCatalogs: false);

        await store.WriteAsync(batch);
        await store.WriteAsync(batch);

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((2, 2, 2, 2),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Reusing_batch_identity_for_different_canonical_input_fails_without_mutation()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batchId = DiagnosticsDrainBatchId.New();
        var first = CreateBatch(includeCatalogs: false);
        await store.WriteAsync(batchId, first);
        var changed = first with
        {
            Logs = first.Logs.Select(x => x with { Body = $"{x.Body}-changed" }).ToArray()
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceConflictException>(() =>
            store.WriteAsync(batchId, changed).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.ConflictingOperation, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<DiagnosticOperationConflictException>(failure.InnerException);

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Expired_pending_stream_attempt_fails_explicitly_after_restart_without_duplicate_or_late_progress()
    {
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers with
        {
            MetricPoints = new ObservingRecordStore(providers.MetricPoints, failFirstAppend: true)
        }, clock);
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);
        var appendFailure = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.WriteAsync(batchId, batch).AsTask());
        Assert.IsType<IOException>(appendFailure.InnerException);
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1));

        var restartedProviders = await _fixture.CreateProvidersAsync();
        var restarted = _fixture.CreateStore(restartedProviders, clock);
        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceExpiredException>(() =>
            restarted.WriteAsync(batchId, batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.ExpiredOperation, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<DiagnosticOperationExpiredException>(failure.InnerException);

        var diagnostics = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 0, 0),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Catalog_identity_writes_are_rejected_before_mutation_until_portable_case_equivalence_is_available()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceCapabilityException>(() =>
            store.WriteAsync(CreateBatch()).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.CapabilityUnavailable, failure.Reason);
        Assert.Equal("portable-case-equivalence", failure.Context["capability"]);

        await AssertNoMutationAsync(store, providers.Documents);
    }

    [Fact]
    public async Task Affected_case_insensitive_reads_are_rejected_before_provider_io()
    {
        var store = await _fixture.CreateStoreAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OpenTelemetryPersistenceCapabilityException>(() => store.QueryResourcesAsync(
            new() { ServiceName = "API" }, cancellation.Token).AsTask());
        await Assert.ThrowsAsync<OpenTelemetryPersistenceCapabilityException>(() => store.QueryTracesAsync(
            new(), cancellation.Token).AsTask());
        await Assert.ThrowsAsync<OpenTelemetryPersistenceCapabilityException>(() => store.GetTraceAsync(
            "TRACE-1", cancellation.Token).AsTask());
        await Assert.ThrowsAsync<OpenTelemetryPersistenceCapabilityException>(() => store.QueryMetricsAsync(
            new(), cancellation.Token).AsTask());
        await Assert.ThrowsAsync<OpenTelemetryPersistenceCapabilityException>(() => store.QueryLogsAsync(
            new() { TraceId = "TRACE-1" }, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Oversized_normalized_stream_batch_is_rejected_before_ledger_or_record_mutation()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batch = CreateBatch(includeCatalogs: false);
        var template = Assert.Single(batch.Logs);
        batch = batch with
        {
            Traces = [],
            Spans = [],
            MetricPoints = [],
            Logs = Enumerable.Range(0, 1_001)
                .Select(index => template with { Id = $"log-{index:D4}" })
                .ToArray()
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(() =>
            store.WriteAsync(batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.InvalidRecord, failure.Reason);
        Assert.IsType<DiagnosticRecordValidationException>(failure.InnerException);

        await AssertNoMutationAsync(store, providers.Documents);
    }

    [Fact]
    public async Task Provider_invalid_normalized_record_is_rejected_before_ledger_or_record_mutation()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batch = CreateBatch(includeCatalogs: false);
        batch = batch with
        {
            Traces = [],
            Spans = [],
            MetricPoints = [],
            Logs = batch.Logs.Select(x => x with { SeverityText = new string('s', 4_097) }).ToArray()
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(() =>
            store.WriteAsync(batch).AsTask());
        Assert.Equal(OpenTelemetryPersistenceFailureReason.InvalidRecord, failure.Reason);
        Assert.IsType<DiagnosticRecordValidationException>(failure.InnerException);

        await AssertNoMutationAsync(store, providers.Documents);
    }

    [Fact]
    public async Task Canonical_payload_failures_are_translated_but_caller_argument_failures_remain_argument_exceptions()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batch = CreateBatch(includeCatalogs: false);
        var log = Assert.Single(batch.Logs);
        var invalidPayload = batch with
        {
            Logs = [log with { Attributes = null! }]
        };

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(() =>
            store.WriteAsync(invalidPayload).AsTask());
        Assert.IsType<RecordPayloadException>(failure.InnerException);

        var conflictingInput = batch with
        {
            Logs = [log, log with { Body = "different" }]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(conflictingInput).AsTask());

        await AssertNoMutationAsync(store, providers.Documents);
    }

    [Fact]
    public async Task Concurrent_same_batch_writers_converge_through_capture_ledger_cas()
    {
        var first = _fixture.CreateStore(await _fixture.CreateProvidersAsync());
        var second = _fixture.CreateStore(await _fixture.CreateProvidersAsync());
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);

        await Task.WhenAll(
            first.WriteAsync(batchId, batch).AsTask(),
            second.WriteAsync(batchId, batch).AsTask());

        var diagnostics = await first.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Cancellation_after_one_committed_stream_records_partial_progress_and_retry_resumes()
    {
        using var cancellation = new CancellationTokenSource();
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new ObservingRecordStore(providers.Traces, afterSuccessfulAppend: cancellation.Cancel);
        var store = _fixture.CreateStore(providers with { Traces = traces });
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = CreateBatch(includeCatalogs: false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.WriteAsync(batchId, batch, cancellation.Token).AsTask());

        var restarted = _fixture.CreateStore(await _fixture.CreateProvidersAsync());
        await restarted.WriteAsync(batchId, batch);
        var diagnostics = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Malformed_capture_ledger_is_reported_as_provider_neutral_corrupt_data()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(providers);
        var batchId = DiagnosticsDrainBatchId.New();
        var result = await providers.Documents.SaveAsync(new(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind,
            batchId.ToString(),
            OpenTelemetryGroundworkStorageSchema.SchemaVersion,
            "[]",
            0));
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);

        var failure = await Assert.ThrowsAsync<OpenTelemetryPersistenceDataException>(() =>
            store.WriteAsync(batchId, CreateBatch(includeCatalogs: false)).AsTask());

        Assert.Equal(OpenTelemetryPersistenceFailureReason.CorruptData, failure.Reason);
        Assert.Equal(batchId.ToString(), failure.Context["batchId"]);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    [Fact]
    public async Task Query_and_diagnostics_provider_failures_are_translated_at_the_public_boundary()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var queryFailure = new IOException("Injected query failure.");
        var inspectFailure = new IOException("Injected inspect failure.");
        var store = _fixture.CreateStore(providers with
        {
            Logs = new ObservingRecordStore(providers.Logs, queryFailure: queryFailure),
            Traces = new ObservingRecordStore(providers.Traces, inspectFailure: inspectFailure)
        });

        var queryError = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.QueryLogsAsync(new()).AsTask());
        Assert.Equal("query-logs", queryError.Operation);
        Assert.Same(queryFailure, queryError.InnerException);

        var diagnosticsError = await Assert.ThrowsAsync<OpenTelemetryPersistenceUnavailableException>(() =>
            store.GetDiagnosticsAsync().AsTask());
        Assert.Equal("get-diagnostics", diagnosticsError.Operation);
        Assert.Same(inspectFailure, diagnosticsError.InnerException);
    }

#pragma warning disable GW0004
    private static async Task<IReadOnlyList<DocumentEnvelope>> CaptureOperationsAsync(IDocumentStore documents) =>
        (await documents.QueryAsync(new PortableDocumentQuery(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind))).Documents;
#pragma warning restore GW0004

    private static async Task AssertNoMutationAsync(
        GroundworkOpenTelemetryStore store,
        IDocumentStore documents)
    {
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((0, 0, 0, 0, 0, 0),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Empty(await CaptureOperationsAsync(documents));
    }

    private static OpenTelemetryBatch CreateBatch(bool includeCatalogs = true)
    {
        var timestamp = new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.Zero);
        var resource = new TelemetryResource(
            "resource-1", "api", "api-1", "dotnet", new Dictionary<string, string?>(), timestamp,
            TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace(
            "trace-1", "span-root", "request", timestamp, timestamp.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(10), SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan(
            "span-record-1", trace.TraceId, "span-1", null, resource.Id, "request", "internal",
            timestamp, timestamp.AddMilliseconds(10), SpanStatus.Ok, null,
            new Dictionary<string, string?>(), [], []);
        var instrument = new MetricInstrument(
            "instrument-1", resource.Id, "request.duration", "ms", null, MetricKind.Gauge,
            new Dictionary<string, string?>());
        var point = new MetricPoint(
            "point-1", instrument.Id, instrument.Name, resource.Id, timestamp, 10, null, null,
            new Dictionary<string, string?>(), trace.TraceId, span.SpanId);
        var log = new OtlpLogRecord(
            "log-1", resource.Id, timestamp, "Information", null, "request completed", trace.TraceId,
            span.SpanId, new Dictionary<string, string?>());
        return new(
            includeCatalogs ? [resource] : [],
            [trace],
            [span],
            includeCatalogs ? [instrument] : [],
            [point],
            [log]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private sealed class ObservingRecordStore(
        IDiagnosticRecordStore inner,
        bool failFirstAppend = false,
        Action? afterSuccessfulAppend = null,
        Exception? queryFailure = null,
        Exception? inspectFailure = null) : IDiagnosticRecordStore
    {
        private int _failNext = failFirstAppend ? 1 : 0;

        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;
        public List<DiagnosticRecordBatch> Requests { get; } = [];
        public List<DiagnosticAppendStatus> Outcomes { get; } = [];

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(batch);
            if (Interlocked.Exchange(ref _failNext, 0) != 0)
                throw new IOException("Injected metric append failure after earlier streams committed.");

            var result = await inner.AppendAsync(batch, cancellationToken);
            Outcomes.Add(result.Status);
            afterSuccessfulAppend?.Invoke();
            return result;
        }

        public ValueTask<DiagnosticRecordPage> QueryAsync(
            DiagnosticRecordQuery query,
            CancellationToken cancellationToken = default) =>
            queryFailure is null
                ? inner.QueryAsync(query, cancellationToken)
                : ValueTask.FromException<DiagnosticRecordPage>(queryFailure);

        public ValueTask<DiagnosticStreamStatistics> InspectAsync(
            DiagnosticStreamInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            inspectFailure is null
                ? inner.InspectAsync(request, cancellationToken)
                : ValueTask.FromException<DiagnosticStreamStatistics>(inspectFailure);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
