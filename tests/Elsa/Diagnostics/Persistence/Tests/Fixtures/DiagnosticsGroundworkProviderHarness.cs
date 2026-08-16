using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.MongoDb.DiagnosticRecords;
using Groundwork.MongoDb.Documents;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.DiagnosticRecords;
using Groundwork.PostgreSql.Documents;
using Groundwork.Sqlite;
using Groundwork.Sqlite.DiagnosticRecords;
using Groundwork.Sqlite.Documents;
using Groundwork.SqlServer;
using Groundwork.SqlServer.DiagnosticRecords;
using Groundwork.SqlServer.Documents;

namespace Elsa.Diagnostics.Persistence.Tests.Fixtures;

internal static class DiagnosticsGroundworkProviderHarness
{
    public static async Task CreateDiagnosticRecordStreamAsync(
        DiagnosticsProviderLease provider,
        DiagnosticRecordStreamDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var lease = await CreateRecordStoreAsync(provider, definition, cancellationToken);
        if (lease.Owner is not null)
            await lease.Owner.DisposeAsync();
    }

    public static async Task<GroundworkOpenTelemetryProviderLease> CreateOpenTelemetryAsync(
        DiagnosticsProviderLease provider,
        GroundworkOpenTelemetryBinding binding,
        CancellationToken cancellationToken = default)
    {
        var definitions = OpenTelemetryGroundworkStorageSchema.CreateStreams(binding);
        var recordStores = new List<IDiagnosticRecordStore>(definitions.Count);
        var owners = new List<IAsyncDisposable>();
        foreach (var definition in definitions)
        {
            var lease = await CreateRecordStoreAsync(provider, definition, cancellationToken);
            recordStores.Add(lease.Store);
            if (lease.Owner is not null)
                owners.Add(lease.Owner);
        }

        var documents = await CreateDocumentStoresAsync(provider, binding, cancellationToken);
        if (documents.Owner is not null)
            owners.Add(documents.Owner);
        return new(
            new(
                recordStores[0],
                recordStores[1],
                recordStores[2],
                recordStores[3],
                documents.Documents,
                documents.Queries),
            owners);
    }

    public static IDiagnosticRecordPlanInspector CreateDiagnosticRecordPlanInspector(
        DiagnosticsProviderLease provider) =>
        provider.Provider switch
        {
            DiagnosticsProviderKind.Sqlite => SqliteDiagnosticRecordStoreFactory.CreatePlanInspector(provider.ConnectionString),
            DiagnosticsProviderKind.SqlServer => SqlServerDiagnosticRecordStoreFactory.CreatePlanInspector(provider.ConnectionString),
            DiagnosticsProviderKind.PostgreSql => PostgreSqlDiagnosticRecordStoreFactory.CreatePlanInspector(provider.ConnectionString),
            DiagnosticsProviderKind.MongoDb => MongoDbDiagnosticRecordStoreFactory.CreatePlanInspector(
                provider.ConnectionString,
                provider.DatabaseName),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider.Provider, null)
        };

    private static async Task<RecordStoreLease> CreateRecordStoreAsync(
        DiagnosticsProviderLease provider,
        DiagnosticRecordStreamDefinition definition,
        CancellationToken cancellationToken)
    {
        switch (provider.Provider)
        {
            case DiagnosticsProviderKind.Sqlite:
                return new(
                    await SqliteDiagnosticRecordStoreFactory.CreateAsync(
                        provider.ConnectionString,
                        definition,
                        cancellationToken),
                    null);
            case DiagnosticsProviderKind.SqlServer:
                return new(
                    await SqlServerDiagnosticRecordStoreFactory.CreateAsync(
                        provider.ConnectionString,
                        definition,
                        cancellationToken),
                    null);
            case DiagnosticsProviderKind.PostgreSql:
                return new(
                    await PostgreSqlDiagnosticRecordStoreFactory.CreateAsync(
                        provider.ConnectionString,
                        definition,
                        cancellationToken),
                    null);
            case DiagnosticsProviderKind.MongoDb:
            {
                var handle = await MongoDbDiagnosticRecordStoreFactory.CreateAsync(
                    provider.ConnectionString,
                    provider.DatabaseName,
                    definition,
                    cancellationToken: cancellationToken);
                return new(handle.Store, handle);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider.Provider, null);
        }
    }

    private static async Task<DocumentStoresLease> CreateDocumentStoresAsync(
        DiagnosticsProviderLease provider,
        GroundworkOpenTelemetryBinding binding,
        CancellationToken cancellationToken)
    {
        var manifest = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();
        var access = DocumentStoreAccess.Scoped(binding.DocumentStorageScope);
        return provider.Provider switch
        {
            DiagnosticsProviderKind.Sqlite => await CreateSqliteDocumentsAsync(
                provider.ConnectionString, manifest, access, cancellationToken),
            DiagnosticsProviderKind.SqlServer => await CreateSqlServerDocumentsAsync(
                provider.ConnectionString, manifest, access, cancellationToken),
            DiagnosticsProviderKind.PostgreSql => await CreatePostgreSqlDocumentsAsync(
                provider.ConnectionString, manifest, access, cancellationToken),
            DiagnosticsProviderKind.MongoDb => await CreateMongoDbDocumentsAsync(
                provider.ConnectionString, provider.DatabaseName, manifest, access, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider.Provider, null)
        };
    }

    private static async Task<DocumentStoresLease> CreateSqliteDocumentsAsync(
        string connectionString,
        Groundwork.Core.Manifests.StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            SqliteGroundworkCapabilities.PhysicalNames);
        var store = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            access,
            options: AutoApply(),
            cancellationToken: cancellationToken);
        return new(
            store,
            new RoutedBoundedDocumentStore(target.Routes.Select(route =>
                KeyValuePair.Create<string, IBoundedDocumentStore>(
                    route.StorageUnit.Value,
                    SqlitePhysicalQueryRuntime.Create(store, manifest, route, target.Provider)))),
            null);
    }

    private static async Task<DocumentStoresLease> CreateSqlServerDocumentsAsync(
        string connectionString,
        Groundwork.Core.Manifests.StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var provider = new ProviderIdentity("groundwork-sqlserver", "1.0.0");
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            SqlServerGroundworkCapabilities.PhysicalNames);
        var store = await SqlServerDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            access,
            options: AutoApply(),
            cancellationToken: cancellationToken);
        return new(
            store,
            new RoutedBoundedDocumentStore(target.Routes.Select(route =>
                KeyValuePair.Create<string, IBoundedDocumentStore>(
                    route.StorageUnit.Value,
                    SqlServerPhysicalQueryRuntime.Create(store, manifest, route, target.Provider)))),
            null);
    }

    private static async Task<DocumentStoresLease> CreatePostgreSqlDocumentsAsync(
        string connectionString,
        Groundwork.Core.Manifests.StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var provider = new ProviderIdentity("groundwork-postgresql", "1.0.0");
        var target = PhysicalSchemaTargetCompiler.Compile(
            manifest,
            provider,
            PostgreSqlGroundworkCapabilities.PhysicalNames);
        var store = await PostgreSqlDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            manifest,
            provider,
            access,
            options: AutoApply(),
            cancellationToken: cancellationToken);
        return new(
            store,
            new RoutedBoundedDocumentStore(target.Routes.Select(route =>
                KeyValuePair.Create<string, IBoundedDocumentStore>(
                    route.StorageUnit.Value,
                    PostgreSqlPhysicalQueryRuntime.Create(store, manifest, route, target.Provider)))),
            null);
    }

    private static async Task<DocumentStoresLease> CreateMongoDbDocumentsAsync(
        string connectionString,
        string databaseName,
        Groundwork.Core.Manifests.StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            databaseName,
            manifest,
            new ProviderIdentity("groundwork-mongodb", "1.0.0"),
            access,
            options: new MongoDbPhysicalDocumentStoreOptions { AutoApplyOnStartup = true },
            cancellationToken: cancellationToken);
        return new(handle.Store, handle.Store, handle);
    }

    private static GroundworkRuntimeSchemaAdmissionOptions AutoApply() =>
        new() { AutoApplyOnStartup = true };

    private sealed record RecordStoreLease(
        IDiagnosticRecordStore Store,
        IAsyncDisposable? Owner);

    private sealed record DocumentStoresLease(
        IDocumentStore Documents,
        IBoundedDocumentStore Queries,
        IAsyncDisposable? Owner);

    private sealed class RoutedBoundedDocumentStore(
        IEnumerable<KeyValuePair<string, IBoundedDocumentStore>> stores) : IBoundedDocumentStore, IPhysicalDocumentQueryExplainer
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

        public PhysicalQueryPlan ResolvePlan(
            DocumentQuery query,
            BoundedQueryResultOperation operation = BoundedQueryResultOperation.Documents) =>
            Explainer(query).ResolvePlan(query, operation);

        public Task<PhysicalDocumentQueryExplanation> ExplainAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Explainer(query).ExplainAsync(query, cancellationToken);

        private IBoundedDocumentStore Store(DocumentQuery query) =>
            _stores.TryGetValue(query.DocumentKind, out var store)
                ? store
                : throw new InvalidOperationException(
                    $"No diagnostics bounded document route is registered for '{query.DocumentKind}'.");

        private IPhysicalDocumentQueryExplainer Explainer(DocumentQuery query) =>
            Store(query) as IPhysicalDocumentQueryExplainer
            ?? throw new NotSupportedException(
                $"The diagnostics document route for '{query.DocumentKind}' does not expose native query explanations.");
    }
}

internal abstract class DiagnosticsGroundworkProviderLease(
    IReadOnlyCollection<IAsyncDisposable> owners) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        foreach (var owner in owners.Reverse())
            await owner.DisposeAsync();
    }
}

internal sealed class GroundworkOpenTelemetryProviderLease(
    GroundworkOpenTelemetryStores stores,
    IReadOnlyCollection<IAsyncDisposable> owners) : DiagnosticsGroundworkProviderLease(owners)
{
    public GroundworkOpenTelemetryStores Stores { get; } = stores;
}
