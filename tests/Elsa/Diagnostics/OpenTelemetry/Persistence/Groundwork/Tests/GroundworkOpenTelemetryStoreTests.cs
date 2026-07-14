using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryStoreTests : IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

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
    public async Task Partial_multi_stream_retry_after_restart_reuses_operation_identity_and_does_not_duplicate_records_or_catalog_revisions()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var traces = new ObservingRecordStore(providers.Traces);
        var points = new ObservingRecordStore(providers.MetricPoints, failFirstAppend: true);
        var store = _fixture.CreateStore(providers with
        {
            Traces = traces,
            MetricPoints = points
        });
        var batch = CreateBatch();

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(batch).AsTask());
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
        await restarted.WriteAsync(batch);

        var completed = await restarted.GetDiagnosticsAsync();
        Assert.Equal((1, 1, 1, 1),
            (completed.TraceCount, completed.SpanCount, completed.MetricPointCount, completed.LogRecordCount));
        Assert.Single(traces.Requests);
        Assert.Single(restartedTraces.Requests);
        Assert.Equal(traces.Requests[0].OperationId, restartedTraces.Requests[0].OperationId);
        Assert.Equal(traces.Requests[0].RequestFingerprint, restartedTraces.Requests[0].RequestFingerprint);
        Assert.Equal([DiagnosticAppendStatus.Committed], traces.Outcomes);
        Assert.Equal([DiagnosticAppendStatus.Replayed], restartedTraces.Outcomes);
        Assert.Single(points.Requests);
        Assert.Single(restartedPoints.Requests);
        Assert.Equal(points.Requests[0].OperationId, restartedPoints.Requests[0].OperationId);
        Assert.Equal(points.Requests[0].RequestFingerprint, restartedPoints.Requests[0].RequestFingerprint);
        Assert.Empty(points.Outcomes);
        Assert.Equal([DiagnosticAppendStatus.Committed], restartedPoints.Outcomes);

        var resource = await restartedProviders.Documents.LoadAsync(CatalogDocuments.ResourceKind, "resource-1");
        var instrument = await restartedProviders.Documents.LoadAsync(CatalogDocuments.InstrumentKind, "instrument-1");
        Assert.Equal(1, resource!.Version);
        Assert.Equal(1, instrument!.Version);
        Assert.Equal(1, (await CaptureOperationsAsync(restartedProviders.Documents)).Single().Version);
    }

#pragma warning disable GW0004
    private static async Task<IReadOnlyList<DocumentEnvelope>> CaptureOperationsAsync(IDocumentStore documents) =>
        (await documents.QueryAsync(new PortableDocumentQuery(
            OpenTelemetryGroundworkStorageSchema.OperationLedgerKind))).Documents;
#pragma warning restore GW0004

    private static OpenTelemetryBatch CreateBatch()
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
        return new([resource], [trace], [span], [instrument], [point], [log]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private sealed class ObservingRecordStore(
        IDiagnosticRecordStore inner,
        bool failFirstAppend = false) : IDiagnosticRecordStore
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
            return result;
        }
    }
}
