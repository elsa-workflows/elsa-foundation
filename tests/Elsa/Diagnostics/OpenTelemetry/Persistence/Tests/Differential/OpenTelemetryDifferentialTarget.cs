using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.DiagnosticRecords;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests.Differential;

/// <summary>
/// One comparand in the #646 EF-vs-Groundwork differential for <see cref="IOpenTelemetryStore"/>, bound
/// to a private on-disk SQLite database that survives <see cref="CloseAsync"/> so restart and
/// ungraceful-stop probes observe real durability.
/// </summary>
/// <remarks>
/// SQLite on both sides is forced, not chosen: EF Core has no PostgreSQL or SQL Server wiring anywhere
/// in <c>src/</c>. Groundwork's other three providers are covered by its own conformance matrix, which
/// is strictly less evidence than a differential and must not be reported as equivalent assurance.
/// </remarks>
internal abstract class OpenTelemetryDifferentialTarget : IAsyncDisposable
{
    private readonly string _databasePath;

    protected OpenTelemetryDifferentialTarget(string identity)
    {
        Identity = identity;
        _databasePath = Path.Combine(Path.GetTempPath(), $"elsa-otel-differential-{identity}-{Guid.NewGuid():N}.db");
    }

    /// <summary>Stable comparand identity recorded in the divergence ledger.</summary>
    public string Identity { get; }

    protected string DatabasePath => _databasePath;

    /// <summary>Shared capacity/query options so neither stack is compared under a different budget.</summary>
    protected static OpenTelemetryDiagnosticsOptions StoreOptions() => new()
    {
        MaxQuerySize = 200
    };

    /// <summary>Opens a started store over the durable storage, materializing schema on first use.</summary>
    public abstract Task<IOpenTelemetryStore> OpenAsync();

    /// <summary>
    /// Completes the capture drain so everything written is durable and queryable. Both stacks queue
    /// <see cref="IOpenTelemetryStore.WriteAsync"/> onto a bounded drain, so a probe must flush before it
    /// reads or it measures scheduling instead of storage. Writes after a flush are not supported.
    /// </summary>
    public abstract Task FlushAsync();

    /// <summary>Releases provider resources, leaving storage intact.</summary>
    public abstract Task CloseAsync();

    /// <summary>
    /// Releases provider resources <em>without</em> completing the capture drain, modelling process loss
    /// between a queued write and a graceful shutdown.
    /// </summary>
    public abstract Task AbandonAsync();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync();
        }
        catch
        {
            // Teardown must not mask a probe's own failure; storage deletion below is what matters.
        }

        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static OpenTelemetryDifferentialTarget EfCore() => new EfCoreTarget();

    public static OpenTelemetryDifferentialTarget Groundwork() => new GroundworkTarget();

    private sealed class EfCoreTarget() : OpenTelemetryDifferentialTarget("efcore.sqlite")
    {
        private OpenTelemetryTestHost? _host;
        private EfCoreOpenTelemetryStore? _store;

        public override Task<IOpenTelemetryStore> OpenAsync()
        {
            var options = StoreOptions();
            _host = OpenTelemetryTestHost.Create(DatabasePath);
            _store = new(_host, Options.Create(options), new OpenTelemetrySourceRegistry(Options.Create(options)));
            _store.StartDraining();
            return Task.FromResult<IOpenTelemetryStore>(_store);
        }

        public override Task FlushAsync() => _store?.CompleteDrainingAsync() ?? Task.CompletedTask;

        public override async Task CloseAsync()
        {
            if (_store is not null)
            {
                await _store.DisposeAsync();
                _store = null;
            }

            _host?.Dispose();
            _host = null;
        }

        public override Task AbandonAsync()
        {
            // Drop the drain loop without completing it, then release the connection the file depends on.
            _store = null;
            _host?.Dispose();
            _host = null;
            return Task.CompletedTask;
        }
    }

    private sealed class GroundworkTarget() : OpenTelemetryDifferentialTarget("groundwork.sqlite")
    {
        private static readonly GroundworkOpenTelemetryBinding Binding =
            GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a");

        private GroundworkOpenTelemetryStore? _store;

        public override async Task<IOpenTelemetryStore> OpenAsync()
        {
            var options = StoreOptions();
            _store = new(
                await CreateProvidersAsync(),
                Options.Create(options),
                Binding,
                sourceRegistry: new OpenTelemetrySourceRegistry(Options.Create(options)));
            _store.Start();
            return _store;
        }

        public override Task FlushAsync() => _store?.CompleteDrainingAsync() ?? Task.CompletedTask;

        public override async Task CloseAsync()
        {
            if (_store is not null)
            {
                await _store.DisposeAsync();
                _store = null;
            }
        }

        public override Task AbandonAsync()
        {
            _store = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Mirrors <c>OpenTelemetryGroundworkSqliteFixture</c>: four diagnostic-record streams plus the
        /// document store and its bounded query routes. Duplicated rather than shared because that
        /// fixture lives in the Groundwork-only test project, and referencing it here would drag EF into
        /// a project the surface ratchet keeps EF-free.
        /// </summary>
        private async Task<GroundworkOpenTelemetryStores> CreateProvidersAsync()
        {
            var connectionString = $"Data Source={DatabasePath}";
            var streams = OpenTelemetryGroundworkStorageSchema.CreateStreams(Binding);
            var traces = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[0]);
            var spans = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[1]);
            var points = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[2]);
            var logs = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[3]);

            var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();
            var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
            var target = PhysicalSchemaTargetCompiler.Compile(manifest, provider, SqliteGroundworkCapabilities.PhysicalNames);
            var documents = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
                connectionString,
                manifest,
                provider,
                DocumentStoreAccess.Scoped(Binding.DocumentStorageScope),
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
}
