using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Store;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Binds the frozen diagnostics durable-history sequence to the production Groundwork diagnostics stores.
/// Each logical scope is a separate composition over the same provider, and every reopened client is a
/// fresh composition over the same durable database. The workload sees only public Elsa store contracts.
/// </summary>
internal sealed class DiagnosticsDurableHistoryAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IDiagnosticsDurableHistoryWorkloadAdapter
{
    internal const string AdapterId = "groundwork-v2";
    internal const string PhysicalForm = "ordinary-groundwork-diagnostics-units";

    private readonly RunRequest request = request;
    private readonly string connectionString = connectionString;
    private readonly string outputDirectory = outputDirectory;
    private readonly string persistenceScope = PersistenceScopeFor(request);
    private readonly WritePathRoundTripObserver observer = new(request.Provider, captureCommands: true);
    private readonly List<RuntimeStoreComposition> compositions = [];
    private readonly List<DiagnosticsDurableHistoryClient> clients = [];
    private IStorageProviderConnection? connection;
    private ProviderProbe.Result? observedProvider;

    public IProviderRoundTripObserver? RoundTripObserver => observer;

    internal WritePathRoundTripObserver CommandObserver => observer;

    /// <summary>
    /// Diagnostics remains blocked by the frozen absolute-budget admission. Therefore no timed operation
    /// is exposed until a reviewed budget gate exists. The correctness implementation remains available
    /// to a future admitted run, while the current harness rejects diagnostics before child execution.
    /// </summary>
    public IReadOnlyList<IBenchmarkOperation> Operations =>
        throw new PerformanceContractException(
            "The diagnostics-durable-history workload is blocked under 'gate.diagnostics.absolute-budget-required'; " +
            "no timed operation list may be published until the reviewed absolute-budget gate is authorized.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (compositions.Count != 0)
            return;

        observedProvider = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        if (!string.Equals(observedProvider.Topology, request.ProviderTopology, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Provider '{request.Provider}' reports topology '{observedProvider.Topology}', not the requested '{request.ProviderTopology}'.");
        if (!string.Equals(observedProvider.Version, request.ProviderVersion, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Provider '{request.Provider}' reports version '{observedProvider.Version}', not the requested '{request.ProviderVersion}'.");

        connection = ProviderConnections.Open(request.Provider, connectionString);
        try
        {
            await OpenCompositionAsync("primary", "primary", cancellationToken);
            await OpenCompositionAsync("secondary", "secondary", cancellationToken);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        RequirePrepared();
        var evidence = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var result = await new DiagnosticsDurableHistoryWorkload().ExecuteAsync(this, cancellationToken);
        var provider = observedProvider ?? throw new PerformanceContractException(
            "The diagnostics adapter has no live provider handshake; PrepareAsync must run first.");

        return new CorrectnessEvidence(
            result.ResultDigest,
            provider.Version,
            provider.Topology,
            provider.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                evidence.Routes)
            {
                RouteContract = evidence.RouteContract,
                BlockedRoutes = evidence.BlockedRoutes ?? [],
                OracleObservations = evidence.OracleObservations ?? [],
                TraceDetailConstituents = evidence.TraceDetailConstituents ?? []
            });
    }

    public ValueTask<DiagnosticsDurableHistoryScopes> OpenScopedClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequirePrepared();
        var primary = clients[0];
        var secondary = clients[1];
        RequireDistinct(primary, secondary);
        return ValueTask.FromResult(new DiagnosticsDurableHistoryScopes(primary, secondary));
    }

    public async ValueTask<DiagnosticsDurableHistoryClient> ReopenClientAsync(
        CancellationToken cancellationToken = default)
    {
        RequirePrepared();
        // The composition identity is new, but its tenant binding is the same primary scope. Reusing a
        // new tenant here would make a restart appear empty and would only prove isolation from history.
        await OpenCompositionAsync($"reopened-{compositions.Count}", "primary", cancellationToken);
        return clients[^1];
    }

    /// <summary>
    /// Waits for every queued OpenTelemetry batch to become visible through the public inspection
    /// contract. This is an untimed synchronization boundary; it never stops a one-way diagnostics drain,
    /// so later operation phases can continue writing through the same store instance.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        RequirePrepared();
        foreach (var client in clients)
        {
            if (client.StructuredLogs is TrackingStructuredLogStore structuredLogs)
                await structuredLogs.WaitForDurabilityAsync(cancellationToken);
            if (client.OpenTelemetry is TrackingOpenTelemetryStore openTelemetry)
                await openTelemetry.WaitForDurabilityAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var composition in compositions.AsEnumerable().Reverse())
                await composition.DisposeAsync();
        }
        finally
        {
            connection?.Dispose();
            connection = null;
            compositions.Clear();
            clients.Clear();
            observedProvider = null;
        }
    }

    private async Task OpenCompositionAsync(
        string suffix,
        string bindingScope,
        CancellationToken cancellationToken)
    {
        var composition = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            $"{persistenceScope}-{suffix}",
            cancellationToken,
            observer,
            connection ?? throw new PerformanceContractException(
                "The diagnostics adapter has no shared provider connection; PrepareAsync must run first."),
            includeGroundworkDiagnostics: true,
            structuredLogBinding: new StructuredLogStoreBinding(
                TenantFor(bindingScope),
                ScopeFor(bindingScope),
                "structured-logs"),
            openTelemetryBinding: GroundworkOpenTelemetryBinding.Create(
                TenantFor(bindingScope),
                ScopeFor(bindingScope),
                "open-telemetry"),
            structuredLogsOptions: FrozenStructuredLogsOptions(),
            openTelemetryOptions: FrozenOpenTelemetryOptions());

        compositions.Add(composition);
        var stores = composition.CreateDiagnosticsClient();
        clients.Add(new DiagnosticsDurableHistoryClient(
            new TrackingStructuredLogStore(stores.StructuredLogs),
            new TrackingOpenTelemetryStore(stores.OpenTelemetry)));
    }

    private void RequirePrepared()
    {
        if (compositions.Count < 2 || clients.Count < 2)
            throw new PerformanceContractException(
                "The diagnostics adapter has no independent composed scopes; PrepareAsync must run first.");
    }

    private static void RequireDistinct(
        DiagnosticsDurableHistoryClient first,
        DiagnosticsDurableHistoryClient second)
    {
        if (ReferenceEquals(first, second) ||
            ReferenceEquals(first.StructuredLogs, second.StructuredLogs) ||
            ReferenceEquals(first.OpenTelemetry, second.OpenTelemetry))
            throw new PerformanceContractException(
                "Diagnostics correctness requires independently composed public store clients.");
    }

    private string TenantFor(string scope) => BindingTenantFor(request, scope);

    private string ScopeFor(string scope) => BindingScopeFor(request, scope);

    internal static string PersistenceScopeForTesting(RunRequest request) => PersistenceScopeFor(request);

    internal static string BindingTenantForTesting(RunRequest request, string scope) =>
        BindingTenantFor(request, scope);

    internal static string BindingStorageScopeForTesting(RunRequest request, string scope) =>
        BindingScopeFor(request, scope);

    internal static string BindingScopeForTesting(RunRequest request, string scope) =>
        $"{BindingTenantFor(request, scope)}|{BindingScopeFor(request, scope)}";

    private static string BindingTenantFor(RunRequest request, string scope) =>
        $"diagnostics-{ProcessTokenFor(request)}-{scope}";

    private static string BindingScopeFor(RunRequest request, string scope) =>
        $"spec094-{ProcessTokenFor(request)}-{scope}";

    private static string ProcessTokenFor(RunRequest request)
    {
        var identity = string.Join(
            '|',
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.ProcessKind,
            request.ProcessIndex);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
    }

    private static StructuredLogsOptions FrozenStructuredLogsOptions() => new()
    {
        BufferCapacity = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
        MaxRecentQuerySize = DiagnosticsDurableHistoryWorkload.QueryLimit,
        ShutdownDrainTimeout = TimeSpan.FromMinutes(30)
    };

    private static OpenTelemetryDiagnosticsOptions FrozenOpenTelemetryOptions() => new()
    {
        TraceCapacity = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
        SpanCapacity = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
        MetricPointCapacity = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
        LogRecordCapacity = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
        ResourceCapacity = DiagnosticsDurableHistoryWorkload.ResourceCount,
        MetricInstrumentCapacity = DiagnosticsDurableHistoryWorkload.InstrumentCount,
        SubscriberChannelCapacity = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerOtlpBatch,
        MaxQuerySize = DiagnosticsDurableHistoryWorkload.QueryLimit,
        ShutdownDrainTimeout = TimeSpan.FromMinutes(30)
    };

    private static string PersistenceScopeFor(RunRequest request)
    {
        var identity = string.Join(
            '|',
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.ProviderVersion,
            request.ProviderTopology,
            string.Join(';', request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")),
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            request.NativePlanEvidenceReference,
            request.NativePlanContentSha256,
            request.ProcessKind,
            request.ProcessIndex);
        return $"benchmark-diagnostics-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()}";
    }

    private sealed class TrackingStructuredLogStore(IStructuredLogStore inner) : IStructuredLogStore
    {
        private long expectedHighWater;

        public async ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            var committed = await inner.AppendAsync(entry, cancellationToken);
            Interlocked.Increment(ref expectedHighWater);
            return committed;
        }

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) => inner.GetHighWaterMarkAsync(cancellationToken);
        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) => inner.GetRecentAsync(filter, cancellationToken);
        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) => inner.GetTailCursorAsync(cancellationToken);
        public Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default) => inner.ReadAfterAsync(afterCursor, filter, maxCount, cancellationToken);
        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) => inner.TrimAsync(keepNewest, cancellationToken);

        public async Task WaitForDurabilityAsync(CancellationToken cancellationToken)
        {
            var target = Interlocked.Read(ref expectedHighWater);
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);
            while (await inner.GetHighWaterMarkAsync(cancellationToken) < target)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new PerformanceContractException("Structured-log durability did not become visible within the untimed flush budget.");
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private sealed class TrackingOpenTelemetryStore(IOpenTelemetryStore inner) : IOpenTelemetryStore
    {
        private int expectedTraces;
        private int expectedSpans;
        private int expectedPoints;
        private int expectedLogs;
        private int expectedResources;
        private int expectedInstruments;
        private string? expectedLastTraceId;

        public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
        {
            Interlocked.Add(ref expectedTraces, batch.Traces.Count);
            Interlocked.Add(ref expectedSpans, batch.Spans.Count);
            Interlocked.Add(ref expectedPoints, batch.MetricPoints.Count);
            Interlocked.Add(ref expectedLogs, batch.Logs.Count);
            Interlocked.Add(ref expectedResources, batch.Resources.Count);
            Interlocked.Add(ref expectedInstruments, batch.Instruments.Count);
            if (batch.Traces.Count > 0)
                expectedLastTraceId = batch.Traces.Last().TraceId;
            return inner.WriteAsync(batch, cancellationToken);
        }

        public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) => inner.QueryResourcesAsync(filter, cancellationToken);
        public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) => inner.QueryTracesAsync(filter, cancellationToken);
        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default) => inner.GetTraceAsync(traceId, cancellationToken);
        public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) => inner.QueryMetricsAsync(filter, cancellationToken);
        public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) => inner.QueryLogsAsync(filter, cancellationToken);
        public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);

        public async Task WaitForDurabilityAsync(CancellationToken cancellationToken)
        {
            var traces = Volatile.Read(ref expectedTraces);
            var spans = Volatile.Read(ref expectedSpans);
            var points = Volatile.Read(ref expectedPoints);
            var logs = Volatile.Read(ref expectedLogs);
            var resources = Volatile.Read(ref expectedResources);
            var instruments = Volatile.Read(ref expectedInstruments);
            var lastTraceId = expectedLastTraceId;
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);
            while (true)
            {
                var diagnostics = await inner.GetDiagnosticsAsync(cancellationToken);
                if (diagnostics.TraceCount >= Math.Min(traces, diagnostics.TraceCapacity) &&
                    diagnostics.SpanCount >= Math.Min(spans, diagnostics.SpanCapacity) &&
                    diagnostics.MetricPointCount >= Math.Min(points, diagnostics.MetricPointCapacity) &&
                    diagnostics.LogRecordCount >= Math.Min(logs, diagnostics.LogRecordCapacity) &&
                    diagnostics.ResourceCount >= Math.Min(resources, DiagnosticsDurableHistoryWorkload.ResourceCount) &&
                    diagnostics.MetricInstrumentCount >= Math.Min(instruments, DiagnosticsDurableHistoryWorkload.InstrumentCount) &&
                    (lastTraceId is null || await inner.GetTraceAsync(lastTraceId, cancellationToken) is not null))
                    return;
                if (DateTime.UtcNow >= deadline)
                    throw new PerformanceContractException("OpenTelemetry durability did not become visible within the untimed flush budget.");
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
