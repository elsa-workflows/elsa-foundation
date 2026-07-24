using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.Persistence.Observability;
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

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

internal sealed class OpenTelemetryGroundworkSqliteFixture : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"elsa-open-telemetry-{Guid.NewGuid():N}.db");

    public GroundworkOpenTelemetryBinding Binding { get; } =
        GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a");

    public async Task<GroundworkOpenTelemetryStores> CreateProvidersAsync()
    {
        var connectionString = $"Data Source={_databasePath}";
        var streams = OpenTelemetryGroundworkStorageSchema.CreateStreams(Binding);
        var traces = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[0]);
        var spans = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[1]);
        var points = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[2]);
        var logs = await SqliteDiagnosticRecordStoreFactory.CreateAsync(connectionString, streams[3]);
        var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();
        var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            SqliteGroundworkCapabilities.PhysicalNames);
        var documents = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            DocumentStoreAccess.Scoped(Binding.DocumentStorageScope),
            options: new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });
        var queries = new OpenTelemetryBoundedDocumentStore(
            target.Routes.Select(route => KeyValuePair.Create<string, IBoundedDocumentStore>(
                route.StorageUnit.Value,
                SqlitePhysicalQueryRuntime.Create(documents, manifest, route, target.Provider))));
        return new(traces, spans, points, logs, documents, queries);
    }

    public async ValueTask<GroundworkOpenTelemetryStore> CreateStoreAsync()
    {
        var providers = await CreateProvidersAsync();
        return CreateStore(providers);
    }

    public GroundworkOpenTelemetryStore CreateStore(
        GroundworkOpenTelemetryStores providers,
        TimeProvider? timeProvider = null,
        OpenTelemetryDiagnosticsOptions? options = null,
        IOpenTelemetrySourceRegistry? sourceRegistry = null,
        IDiagnosticsPersistenceObserver? observer = null)
    {
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(options ?? new OpenTelemetryDiagnosticsOptions()),
            Binding,
            timeProvider,
            sourceRegistry,
            observer);
        store.Start();
        return store;
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return ValueTask.CompletedTask;
    }

    private sealed class OpenTelemetryBoundedDocumentStore(
        IEnumerable<KeyValuePair<string, IBoundedDocumentStore>> stores) : IBoundedDocumentStore
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
