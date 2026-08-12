using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.DiagnosticRecords;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Extensions.Options;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The <c>diagnostics-durable-history</c> leaf: binds the frozen Spec 094 diagnostics workload to real
/// Groundwork <see cref="IStructuredLogStore"/> and <see cref="IOpenTelemetryStore"/> implementations over
/// file-backed SQLite.
/// </summary>
/// <remarks>
/// <para>
/// <b>SQLite only, and that is a contract gap rather than a shortcut.</b> The workload declares all four
/// providers in <c>requiredProviderEvidence</c>, and Groundwork ships a diagnostic-record store factory for
/// each. What it does not ship is a way for this leaf to reach the connection those providers were started
/// on: <c>GroundworkProviderDriver</c> keeps its connection string behind a <c>protected</c>
/// <c>SchemaToolConnectionCore()</c>, and the diagnostics stores are built from a connection string rather
/// than from the driver's <c>GroundworkProviderClient</c> (which carries only the runtime document
/// manifest, not the OpenTelemetry one). Opening a second, adapter-owned database on a container provider
/// would put a topology claim in the retained artifact that the measured storage does not satisfy, so the
/// leaf fails closed instead.
/// </para>
/// <para>
/// <b>Why the frozen scale cannot be prepared.</b> <see cref="GroundworkOpenTelemetryStore"/> refuses any
/// <see cref="OpenTelemetryDiagnosticsOptions.TraceCapacity"/> above
/// <c>OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity</c> (5,000), because the trace stream's
/// grouped-query reduction profile is sized against that same constant. The frozen scenario requires
/// <c>retainedRecordsPerStream = 100,000</c> retained traces and asserts the inspected count equals it, so
/// the capacity obligation documented on <see cref="IDiagnosticsDurableHistoryWorkloadAdapter"/> is
/// unsatisfiable on this stack. This leaf still configures the frozen capacities, because configuring
/// anything else would silently measure a different scenario: the resulting
/// <see cref="ArgumentOutOfRangeException"/> is the finding, not a defect in this file.
/// </para>
/// </remarks>
internal sealed class DiagnosticsDurableHistoryAdapter : IBenchmarkAdapter, IDiagnosticsDurableHistoryWorkloadAdapter
{
    private const string SupportedProvider = "sqlite";

    /// <summary>
    /// Two tenants, because <c>tenantCount</c> is 2 and the isolation assertion is only meaningful across a
    /// genuine storage scope. Both diagnostics bindings derive their scope from the tenant, so a leak would
    /// have to cross Groundwork's own scoping rather than a naming convention this leaf invented.
    /// </summary>
    private const string PrimaryTenant = "tenant-diagnostics-primary";

    private const string SecondaryTenant = "tenant-diagnostics-secondary";
    private const string DiagnosticsScopeId = "spec094";
    private const string OpenTelemetrySourceId = "spec094-diagnostics";
    private const string StructuredLogStreamId = "structured-logs";

    private readonly RunRequest _request;
    private readonly string _directory;
    private readonly List<DiagnosticsClientLease> _leases = [];
    private GroundworkProviderDriver? _driver;
    private IReadOnlyDictionary<string, string> _observedConfiguration = new Dictionary<string, string>(StringComparer.Ordinal);

    private DiagnosticsDurableHistoryAdapter(RunRequest request)
    {
        _request = request;
        _directory = Directory.CreateTempSubdirectory("elsa-diagnostics-durable-history").FullName;
    }

    /// <summary>The one SQLite database every scope and every reopened client shares.</summary>
    private string ConnectionString => $"Data Source={Path.Combine(_directory, "diagnostics.db")}";

    public static async ValueTask<IBenchmarkAdapter> CreateAsync(AdapterContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var provider = context.Request.Provider;
        if (!context.Workload.RequiredProviderEvidence.ContainsKey(provider))
            throw new PerformanceContractException(
                $"Provider '{provider}' is not admitted by the frozen topology contract for '{context.Workload.Id}'.");
        if (!string.Equals(provider, SupportedProvider, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{context.Workload.Id}' leaf can only bind the diagnostics stores to '{SupportedProvider}'. " +
                $"Provider '{provider}' declares evidence '{context.Workload.RequiredProviderEvidence[provider]}', but the " +
                "provider driver exposes no connection this leaf can build the diagnostics stores on, and opening a " +
                "second adapter-owned database would misdescribe the measured storage. A blocked provider is a blocked " +
                "run, not a substituted one.");

        var adapter = new DiagnosticsDurableHistoryAdapter(context.Request);
        try
        {
            await adapter.OpenDriverAsync(cancellationToken);
            return adapter;
        }
        catch
        {
            await adapter.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Deliberately empty, and it fails the run closed rather than quietly reporting nothing to measure.
    /// The workload is admission-blocked under <c>gate.diagnostics.absolute-budget-required</c>, so the
    /// harness never reaches the measurement phase; publishing an operation list that has never been
    /// executed would be a claim this leaf cannot support.
    /// </summary>
    public IReadOnlyList<IBenchmarkOperation> Operations =>
        throw new PerformanceContractException(
            "The 'diagnostics-durable-history' leaf exposes no timed operations. The workload is admission-blocked " +
            "under 'gate.diagnostics.absolute-budget-required', so no measurement phase has ever run against it, and " +
            "an unexecuted operation list would be an unverified claim in a retained artifact.");

    /// <summary>
    /// Untimed by contract: every workload input carries <c>"timedSetup": "excluded"</c>. Only the provider
    /// configuration capture happens here — the frozen sequence opens its own scoped clients, so seeding is
    /// the workload's job rather than this leaf's.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var driver = RequireDriver();
        _observedConfiguration = await CheckpointCommitAdapter.CaptureProviderConfigurationAsync(driver, cancellationToken);
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        var driver = RequireDriver();
        var result = await new DiagnosticsDurableHistoryWorkload().ExecuteAsync(this, cancellationToken);

        return new CorrectnessEvidence(
            result.ResultDigest,
            driver.Descriptor.ProviderVersion,
            driver.Descriptor.Topology.Description,
            _observedConfiguration,
            new NativePlanEvidence(
                _request.NativePlanIdentity,
                _request.NativePlanEvidenceReference,
                _request.NativePlanContentSha256,
                []));
    }

    public async ValueTask<DiagnosticsDurableHistoryScopes> OpenScopedClientsAsync(CancellationToken cancellationToken = default)
    {
        var primary = await OpenLeaseAsync(PrimaryTenant, cancellationToken);
        var secondary = await OpenLeaseAsync(SecondaryTenant, cancellationToken);
        return new(primary.Client, secondary.Client);
    }

    public async ValueTask<DiagnosticsDurableHistoryClient> ReopenClientAsync(CancellationToken cancellationToken = default) =>
        (await OpenLeaseAsync(PrimaryTenant, cancellationToken)).Client;

    /// <summary>
    /// Completes the OpenTelemetry capture drain on every open client, then drops it so the next write
    /// starts a fresh one.
    /// </summary>
    /// <remarks>
    /// Only the OpenTelemetry side needs this. <see cref="IStructuredLogStore.AppendAsync"/> completes only
    /// after Groundwork returns the committed cursor, so a structured-log append is already durable when it
    /// returns; <see cref="IOpenTelemetryStore.WriteAsync"/> is a bounded <c>TryEnqueue</c> that returns
    /// immediately. Stopping is one-way — <c>DiagnosticsDrain.StopAsync</c> completes the channel writer
    /// permanently — and the frozen sequence flushes three times with writes in between, so a flush that
    /// merely stopped would poison the drain the sixth operation still has to use. Recreating the store over
    /// the same provider stores is safe because <c>GroundworkOpenTelemetryStore.DisposeAsync</c> disposes
    /// its drain only, never the underlying record or document stores.
    /// </remarks>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        foreach (var lease in _leases)
            await lease.OpenTelemetry.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lease in _leases)
            await lease.DisposeAsync();
        _leases.Clear();

        if (_driver is not null)
            await _driver.DisposeAsync();
        _driver = null;

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private async Task OpenDriverAsync(CancellationToken cancellationToken)
    {
        var driver = GroundworkProviderDriverFactory.Create(_request.Provider);
        _driver = driver;
        await driver.InitializeAsync(cancellationToken);

        if (!string.Equals(driver.Descriptor.Topology.Description, _request.ProviderTopology, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{_request.Provider}' driver reports topology '{driver.Descriptor.Topology.Description}', not the requested '{_request.ProviderTopology}'.");
        if (!string.Equals(driver.Descriptor.ProviderVersion, _request.ProviderVersion, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{_request.Provider}' driver reports provider version '{driver.Descriptor.ProviderVersion}', not the requested '{_request.ProviderVersion}'.");
    }

    private GroundworkProviderDriver RequireDriver() =>
        _driver ?? throw new PerformanceContractException("The diagnostics adapter was used before its provider driver was opened.");

    private async Task<DiagnosticsClientLease> OpenLeaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        var lease = await DiagnosticsStoreComposition.OpenAsync(
            ConnectionString,
            tenantId,
            DiagnosticsScopeId,
            OpenTelemetrySourceId,
            StructuredLogStreamId,
            DiagnosticsStoreComposition.FrozenStructuredLogOptions(),
            DiagnosticsStoreComposition.FrozenOpenTelemetryOptions(),
            cancellationToken);
        _leases.Add(lease);
        return lease;
    }
}

/// <summary>
/// Stands up the pair of real Groundwork diagnostics stores over one file-backed SQLite database.
/// </summary>
/// <remarks>
/// Lifted from <c>OpenTelemetryDifferentialTarget</c> and <c>StructuredLogDifferentialTarget</c> rather than
/// re-derived: those are the constructions the #646 differential already runs, so reusing their shape keeps
/// the benchmark measuring the same composition the divergence ledger describes. They live in test projects
/// the adapter host cannot reference, which is the only reason this is a copy.
/// </remarks>
internal static class DiagnosticsStoreComposition
{
    /// <summary>
    /// The frozen structured-log budget. <c>MaxRecentQuerySize</c> is the query clamp the runner's bounded
    /// reads sit behind: below <c>queryLimit</c> the reads return short pages and correctness fails at
    /// <c>read-structured-log-recent</c> for a configuration reason that reads like a storage defect.
    /// Retention is left at the store's own 100,000-entry default, which already equals the frozen
    /// <c>retainedRecordsPerStream</c>.
    /// </summary>
    public static StructuredLogsOptions FrozenStructuredLogOptions() => new()
    {
        MaxRecentQuerySize = DiagnosticsDurableHistoryWorkload.QueryLimit,
        ShutdownDrainTimeout = DrainCompletionBudget
    };

    /// <summary>
    /// How long a flush may wait for the capture drain to finish.
    /// </summary>
    /// <remarks>
    /// Host configuration, not a contract change — the option exists so a host can size it to its own unit
    /// of work — but getting it wrong is silent. Both stores implement drain completion as
    /// <c>DiagnosticsDrain.StopAsync</c>, which waits at most this long and then fails every still-queued
    /// item as <c>ShutdownTimeout</c> loss. At the default ten seconds the frozen volume does not finish, so
    /// a flush truncates the history and the runner's exact-count assertions then measure how much of the
    /// queue happened to drain rather than what storage retained.
    /// </remarks>
    private static readonly TimeSpan DrainCompletionBudget = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The frozen OpenTelemetry budget. Every capacity equals the frozen per-stream retention, and the two
    /// catalog capacities equal their frozen catalog sizes, because the runner inspects those counts
    /// exactly. <c>SubscriberChannelCapacity</c> is sized to the whole frozen batch count so the bounded
    /// capture queue cannot shed: the runner requires a zero drop total, and a shed batch is real evidence
    /// of loss rather than a tuning artefact.
    /// </summary>
    /// <remarks>
    /// <b>This throws on Groundwork.</b> <c>TraceCapacity</c> above 5,000 is refused by
    /// <see cref="GroundworkOpenTelemetryStore"/>'s constructor. Setting it to the frozen value anyway is
    /// deliberate — see the type remarks on <see cref="DiagnosticsDurableHistoryAdapter"/>.
    /// </remarks>
    public static OpenTelemetryDiagnosticsOptions FrozenOpenTelemetryOptions() =>
        OpenTelemetryOptionsFor(
            DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
            DiagnosticsDurableHistoryWorkload.ResourceCount,
            DiagnosticsDurableHistoryWorkload.InstrumentCount,
            DiagnosticsDurableHistoryWorkload.QueryLimit,
            BatchesFor(
                DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream + DiagnosticsDurableHistoryWorkload.RetentionOverflowRecords,
                DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch));

    public static OpenTelemetryDiagnosticsOptions OpenTelemetryOptionsFor(
        int retainedRecordsPerStream,
        int resourceCount,
        int instrumentCount,
        int queryLimit,
        int queuedBatches) => new()
    {
        TraceCapacity = retainedRecordsPerStream,
        SpanCapacity = retainedRecordsPerStream,
        MetricPointCapacity = retainedRecordsPerStream,
        LogRecordCapacity = retainedRecordsPerStream,
        ResourceCapacity = resourceCount,
        MetricInstrumentCapacity = instrumentCount,
        MaxQuerySize = queryLimit,
        SubscriberChannelCapacity = queuedBatches,
        ShutdownDrainTimeout = DrainCompletionBudget
    };

    public static int BatchesFor(int records, int batchSize) => ((records + batchSize - 1) / batchSize) + 1;

    public static async Task<DiagnosticsClientLease> OpenAsync(
        string connectionString,
        string tenantId,
        string scopeId,
        string sourceId,
        string structuredLogStreamId,
        StructuredLogsOptions structuredLogOptions,
        OpenTelemetryDiagnosticsOptions openTelemetryOptions,
        CancellationToken cancellationToken = default)
    {
        var structuredLogBinding = new StructuredLogStoreBinding(tenantId, scopeId, structuredLogStreamId);
        var structuredLogProvider = await SqliteDiagnosticRecordStoreFactory.CreateAsync(
            connectionString,
            GroundworkStructuredLogStore.CreateStreamDefinition(structuredLogBinding.StreamId));
        var structuredLogs = new GroundworkStructuredLogStore(
            structuredLogProvider,
            Options.Create(structuredLogOptions),
            structuredLogBinding);
        structuredLogs.Start();

        var openTelemetryBinding = GroundworkOpenTelemetryBinding.Create(tenantId, scopeId, sourceId);
        var providerStores = await CreateOpenTelemetryProvidersAsync(connectionString, openTelemetryBinding);

        // Constructed eagerly: the capacity and query-clamp obligations are validated in this constructor,
        // and deferring it to the first write would surface a misconfiguration in the middle of the frozen
        // sequence instead of before it starts.
        var openTelemetry = new RotatingOpenTelemetryStore(() =>
        {
            var store = new GroundworkOpenTelemetryStore(
                providerStores,
                Options.Create(openTelemetryOptions),
                openTelemetryBinding);
            store.Start();
            return store;
        });
        openTelemetry.Open();

        return new(structuredLogs, openTelemetry);
    }

    internal static async Task<GroundworkOpenTelemetryStores> CreateOpenTelemetryProvidersAsync(
        string connectionString,
        GroundworkOpenTelemetryBinding binding)
    {
        var streams = OpenTelemetryGroundworkStorageSchema.CreateStreams(binding);
        var traces = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[0]);
        var spans = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[1]);
        var points = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[2]);
        var logs = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[3]);

        var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();
        var provider = new global::Groundwork.Core.Capabilities.ProviderIdentity("groundwork-sqlite", "1.0.0");
        var target = PhysicalSchemaTargetCompiler.Compile(manifest, provider, SqliteGroundworkCapabilities.PhysicalNames);
        var documents = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            DocumentStoreAccess.Scoped(binding.DocumentStorageScope),
            options: new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

        var queries = new RoutedBoundedDocumentStore(
            target.Routes.Select(route => KeyValuePair.Create<string, IBoundedDocumentStore>(
                route.StorageUnit.Value,
                SqlitePhysicalQueryRuntime.Create(documents, manifest, route, target.Provider))));

        return new(traces, spans, points, logs, documents, queries);
    }

    private sealed class RoutedBoundedDocumentStore(IEnumerable<KeyValuePair<string, IBoundedDocumentStore>> stores)
        : IBoundedDocumentStore
    {
        private readonly IReadOnlyDictionary<string, IBoundedDocumentStore> _stores =
            stores.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Store(query).QueryAsync(query, cancellationToken);

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Store(query).CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Store(query).FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Store(query).AnyAsync(query, cancellationToken);

        private IBoundedDocumentStore Store(DocumentQuery query) =>
            _stores.TryGetValue(query.DocumentKind, out var store)
                ? store
                : throw new InvalidOperationException($"No bounded OpenTelemetry document route is registered for '{query.DocumentKind}'.");
    }
}

/// <summary>One scope-bound pair of real diagnostics stores, plus the flush the frozen sequence needs.</summary>
internal sealed class DiagnosticsClientLease(IStructuredLogStore structuredLogs, RotatingOpenTelemetryStore openTelemetry)
    : IAsyncDisposable
{
    public RotatingOpenTelemetryStore OpenTelemetry { get; } = openTelemetry;

    public DiagnosticsDurableHistoryClient Client { get; } = new(structuredLogs, openTelemetry);

    public async ValueTask DisposeAsync()
    {
        await OpenTelemetry.DisposeAsync();
        if (structuredLogs is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}

/// <summary>
/// An <see cref="IOpenTelemetryStore"/> whose backing store is recreated after each flush.
/// </summary>
/// <remarks>
/// Groundwork's capture drain stops one way: <c>CompleteDrainingAsync</c> completes the channel writer and
/// nothing reopens it. The frozen sequence flushes three times with writes in between, so a flush that only
/// stopped would leave the store unwritable before the operation that has to write to it. Creating the inner
/// store lazily also keeps a flush from stopping a drain that has never been used.
/// </remarks>
internal sealed class RotatingOpenTelemetryStore(Func<GroundworkOpenTelemetryStore> factory)
    : IOpenTelemetryStore, IAsyncDisposable
{
    private GroundworkOpenTelemetryStore? _inner;

    private GroundworkOpenTelemetryStore Inner => _inner ??= factory();

    /// <summary>Materializes the backing store now, so its option validation runs before the workload does.</summary>
    public void Open() => _ = Inner;

    public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) =>
        Inner.WriteAsync(batch, cancellationToken);

    public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) =>
        Inner.QueryResourcesAsync(filter, cancellationToken);

    public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) =>
        Inner.QueryTracesAsync(filter, cancellationToken);

    public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default) =>
        Inner.GetTraceAsync(traceId, cancellationToken);

    public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) =>
        Inner.QueryMetricsAsync(filter, cancellationToken);

    public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) =>
        Inner.QueryLogsAsync(filter, cancellationToken);

    public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        Inner.GetDiagnosticsAsync(cancellationToken);

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_inner is not { } inner)
            return;
        await inner.CompleteDrainingAsync(cancellationToken);
        await inner.DisposeAsync();
        _inner = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_inner is not { } inner)
            return;
        await inner.DisposeAsync();
        _inner = null;
    }
}
