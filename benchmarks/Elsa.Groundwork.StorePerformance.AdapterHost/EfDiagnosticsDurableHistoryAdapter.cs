using System.Data.Common;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Temporary SQLite EF Core comparand for the diagnostics durable-history workload. This is deliberately
/// an adapter-host leaf: the benchmark workload remains implementation-blind and drives the same public
/// <see cref="IStructuredLogStore"/> and <see cref="IOpenTelemetryStore"/> contracts as Groundwork.
/// </summary>
/// <remarks>
/// EF has no provider wiring in this repository beyond SQLite. The adapter therefore refuses every other
/// provider before opening a connection. Each logical diagnostics scope gets its own file-backed database;
/// every DbContext still opens its own connection, and reopen uses the same primary file. The separate
/// files are necessary because the retained EF stores have no tenant/scope binding (unlike Groundwork),
/// and make cross-scope isolation explicit rather than pretending the EF implementation supports a
/// scope it does not expose.
/// </remarks>
internal sealed class EfDiagnosticsDurableHistoryAdapter : IBenchmarkAdapter, IDiagnosticsDurableHistoryWorkloadAdapter
{
    internal const string AdapterId = "ef-diagnostics-oracle";
    internal const string PhysicalForm = "efcore-diagnostics-relational-tables";

    private readonly RunRequest request;
    private readonly string connectionString;
    private readonly string outputDirectory;
    private readonly string persistenceToken;
    private readonly EfDiagnosticsRoundTripObserver observer = new();
    private readonly List<EfDiagnosticsComposition> compositions = [];
    private readonly List<DiagnosticsDurableHistoryClient> clients = [];
    private string? primaryDatabase;
    private ProviderProbe.Result? observedProvider;

    internal EfDiagnosticsDurableHistoryAdapter(RunRequest request, string connectionString, string outputDirectory)
    {
        this.request = request;
        this.connectionString = connectionString;
        this.outputDirectory = outputDirectory;
        persistenceToken = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('|',
                request.ComparisonCohortId,
                request.MeasurementSetId,
                request.WorkloadId,
                request.WorkloadVersion,
                request.Provider,
                request.ProcessKind,
                request.ProcessIndex)))).ToLowerInvariant()[..24];
    }

    public IProviderRoundTripObserver RoundTripObserver => observer;

    internal EfDiagnosticsRoundTripObserver CommandObserver => observer;
    internal string PrimaryDatabaseConnectionString => primaryDatabase ?? throw new PerformanceContractException("The EF diagnostics primary database is not prepared.");

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        throw new PerformanceContractException(
            "The diagnostics-durable-history workload is blocked under 'gate.diagnostics.absolute-budget-required'; " +
            "no timed operation list may be published until the reviewed absolute-budget gate is authorized.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (observedProvider is not null)
            return;

        if (!string.Equals(request.Provider, "sqlite", StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The temporary EF diagnostics comparator only supports sqlite; received '{request.Provider}'.");

        var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, cancellationToken);
        if (!string.Equals(observed.Topology, request.ProviderTopology, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Provider 'sqlite' reports topology '{observed.Topology}', not the requested '{request.ProviderTopology}'.");

        try
        {
            await OpenCompositionAsync("primary", cancellationToken);
            await OpenCompositionAsync("secondary", cancellationToken);
            observedProvider = observed;
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
        var result = await new DiagnosticsDurableHistoryWorkload().ExecuteAsync(this, cancellationToken);
        var provider = observedProvider!;
        var evidence = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
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
                OracleObservations = evidence.OracleObservations ?? []
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
        await OpenCompositionAsync($"reopened-{compositions.Count}", cancellationToken);
        return clients[^1];
    }

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
        foreach (var composition in compositions.AsEnumerable().Reverse())
            await composition.DisposeAsync();
        compositions.Clear();
        clients.Clear();
        primaryDatabase = null;
        observedProvider = null;
    }

    private async Task OpenCompositionAsync(string scope, CancellationToken cancellationToken)
    {
        var database = DatabaseFor(scope);
        var composition = await EfDiagnosticsComposition.CreateAsync(
            database,
            FrozenStructuredLogsOptions(),
            FrozenOpenTelemetryOptions(),
            observer,
            cancellationToken);
        compositions.Add(composition);
        clients.Add(new DiagnosticsDurableHistoryClient(
            new TrackingStructuredLogStore(composition.StructuredLogs),
            new TrackingOpenTelemetryStore(composition.OpenTelemetry)));
    }

    private string DatabaseFor(string scope)
    {
        if (!string.Equals(request.Provider, "sqlite", StringComparison.Ordinal))
            throw new PerformanceContractException("The EF diagnostics comparator cannot derive a database for a non-SQLite provider.");

        var sqlite = new SqliteConnectionStringBuilder(connectionString);
        var source = sqlite.DataSource;
        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, ":memory:", StringComparison.OrdinalIgnoreCase) ||
            sqlite.Mode == SqliteOpenMode.Memory)
            throw new PerformanceContractException(
                "The EF diagnostics comparator requires the file-backed-distinct-connections SQLite topology; an in-memory data source is not admissible.");

        if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            throw new PerformanceContractException("The EF diagnostics comparator requires a regular file-backed SQLite data source.");

        if (scope.StartsWith("reopened-", StringComparison.Ordinal) && primaryDatabase is not null)
            return primaryDatabase;

        var fullSource = Path.GetFullPath(source);
        var directory = Path.GetDirectoryName(fullSource);
        if (string.IsNullOrWhiteSpace(directory))
            throw new PerformanceContractException("The EF diagnostics comparator could not resolve the SQLite database directory.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(fullSource)}.diagnostics-{persistenceToken}-{scope}.db");
        if (File.Exists(file))
        {
            File.Delete(file);
            DeleteIfExists($"{file}-wal");
            DeleteIfExists($"{file}-shm");
        }

        sqlite.DataSource = file;
        sqlite.Mode = SqliteOpenMode.ReadWriteCreate;
        sqlite.Cache = SqliteCacheMode.Default;
        var result = sqlite.ConnectionString;
        if (string.Equals(scope, "primary", StringComparison.Ordinal))
            primaryDatabase = result;
        return result;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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

    private void RequirePrepared()
    {
        if (compositions.Count < 2 || clients.Count < 2 || observedProvider is null)
            throw new PerformanceContractException("The EF diagnostics comparator has no independent composed scopes; PrepareAsync must run first.");
    }

    private static void RequireDistinct(DiagnosticsDurableHistoryClient first, DiagnosticsDurableHistoryClient second)
    {
        if (ReferenceEquals(first, second) ||
            ReferenceEquals(first.StructuredLogs, second.StructuredLogs) ||
            ReferenceEquals(first.OpenTelemetry, second.OpenTelemetry))
            throw new PerformanceContractException("EF diagnostics correctness requires independent public store clients.");
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
                    throw new PerformanceContractException("EF structured-log durability did not become visible within the untimed flush budget.");
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
                    throw new PerformanceContractException("EF OpenTelemetry durability did not become visible within the untimed flush budget.");
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}

/// <summary>One EF diagnostics composition. Every factory-created context uses a fresh SQLite connection.</summary>
internal sealed class EfDiagnosticsComposition : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly EfDiagnosticsDbContextFactory<StructuredLogsDbContext> structuredFactory;
    private readonly EfDiagnosticsDbContextFactory<OpenTelemetryDbContext> openTelemetryFactory;

    private EfDiagnosticsComposition(
        ServiceProvider services,
        EfDiagnosticsDbContextFactory<StructuredLogsDbContext> structuredFactory,
        EfDiagnosticsDbContextFactory<OpenTelemetryDbContext> openTelemetryFactory,
        EfCoreStructuredLogStore structuredLogs,
        EfCoreOpenTelemetryStore openTelemetry)
    {
        this.services = services;
        this.structuredFactory = structuredFactory;
        this.openTelemetryFactory = openTelemetryFactory;
        StructuredLogs = structuredLogs;
        OpenTelemetry = openTelemetry;
    }

    public EfCoreStructuredLogStore StructuredLogs { get; }
    public EfCoreOpenTelemetryStore OpenTelemetry { get; }

    public static async Task<EfDiagnosticsComposition> CreateAsync(
        string connectionString,
        StructuredLogsOptions structuredOptions,
        OpenTelemetryDiagnosticsOptions openTelemetryOptions,
        EfDiagnosticsRoundTripObserver observer,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        try
        {
            var structuredFactory = new EfDiagnosticsDbContextFactory<StructuredLogsDbContext>(
                connectionString,
                services,
                observer,
                static (options, provider) => new StructuredLogsDbContext(options, provider));
            var openTelemetryFactory = new EfDiagnosticsDbContextFactory<OpenTelemetryDbContext>(
                connectionString,
                services,
                observer,
                static (options, provider) => new OpenTelemetryDbContext(options, provider));

            await using (var db = structuredFactory.CreateDbContext())
                await db.Database.EnsureCreatedAsync(cancellationToken);
            await using (var db = openTelemetryFactory.CreateDbContext())
                await db.Database.EnsureCreatedAsync(cancellationToken);

            var structuredLogs = new EfCoreStructuredLogStore(
                structuredFactory,
                Options.Create(structuredOptions),
                maxRetainedEntries: DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                pruneInterval: DiagnosticsDurableHistoryWorkload.StructuredLogBatchSize,
                NullLogger<EfCoreStructuredLogStore>.Instance);
            var sourceRegistry = new OpenTelemetrySourceRegistry(Options.Create(openTelemetryOptions));
            var openTelemetry = new EfCoreOpenTelemetryStore(
                openTelemetryFactory,
                Options.Create(openTelemetryOptions),
                sourceRegistry,
                pruneInterval: DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch,
                NullLogger<EfCoreOpenTelemetryStore>.Instance);
            structuredLogs.StartDraining();
            openTelemetry.StartDraining();
            return new EfDiagnosticsComposition(services, structuredFactory, openTelemetryFactory, structuredLogs, openTelemetry);
        }
        catch
        {
            await services.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StructuredLogs.CompleteDrainingIfStartedAsync();
        await OpenTelemetry.CompleteDrainingIfStartedAsync();
        await StructuredLogs.DisposeAsync();
        await OpenTelemetry.DisposeAsync();
        structuredFactory.Dispose();
        openTelemetryFactory.Dispose();
        await services.DisposeAsync();
    }
}

internal sealed class EfDiagnosticsDbContextFactory<TContext> : IDbContextFactory<TContext>, IDisposable
    where TContext : DbContext
{
    private readonly string connectionString;
    private readonly IServiceProvider serviceProvider;
    private readonly DbCommandInterceptor observer;
    private readonly Func<DbContextOptions<TContext>, IServiceProvider, TContext> create;

    public EfDiagnosticsDbContextFactory(
        string connectionString,
        IServiceProvider serviceProvider,
        DbCommandInterceptor observer,
        Func<DbContextOptions<TContext>, IServiceProvider, TContext> create)
    {
        this.connectionString = connectionString;
        this.serviceProvider = serviceProvider;
        this.observer = observer;
        this.create = create;
    }

    public TContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connectionString)
            .UseLoggerFactory(NullLoggerFactory.Instance)
            .AddInterceptors(observer)
            .Options;
        return create(options, serviceProvider);
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }

    public void Dispose()
    {
    }
}

internal sealed class EfDiagnosticsRoundTripObserver : DbCommandInterceptor, IProviderRoundTripObserver
{
    private long count;
    private readonly List<EfCommandSnapshot> commands = [];

    public string Provider => "sqlite";
    public string Instrumentation => "ef-core:DbCommandInterceptor";
    public bool IsExact => true;
    public long Snapshot() => Interlocked.Read(ref count);
    internal IReadOnlyList<EfCommandSnapshot> Commands { get { lock (commands) return commands.ToArray(); } }
    internal void ClearCommands() { lock (commands) commands.Clear(); }

    private void Observe(DbCommand command)
    {
        Interlocked.Increment(ref count);
        lock (commands)
            commands.Add(EfCommandSnapshot.Capture(command));
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Observe(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Observe(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Observe(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
