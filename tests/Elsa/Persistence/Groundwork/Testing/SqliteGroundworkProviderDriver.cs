using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Testing;

public sealed class SqliteGroundworkProviderDriver : GroundworkProviderDriver
{
    private const string ProviderKey = "sqlite";
    private const string ProviderIdentity = "groundwork-sqlite";
    private const string IdentityIndex = "ux_groundwork_documents_identity_lookup";
    private const string PlanProbeId = "provider-plan-probe";
    private static readonly GroundworkCompositionFingerprint FixtureComposition =
        GroundworkCompositionFingerprint.Create("elsa-runtime-provider-fixture:v1");
    private static readonly string PackageVersion =
        GroundworkProviderDriverSupport.PackageVersion(typeof(SqliteDocumentStore).Assembly);
    private static readonly GroundworkProviderDescriptor ProviderDescriptor = new(
        ProviderKey,
        ProviderIdentity,
        PackageVersion,
        new GroundworkProviderTopology(
            ProviderKey,
            "file-backed-distinct-connections",
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions |
            GroundworkTopologyCapabilities.ExternalProcessRestart));

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"elsa-groundwork-sqlite-provider-{Guid.NewGuid():N}");
    private readonly GroundworkProcessProbeRunner _processProbeRunner = new();
    private GroundworkProcessLaunchDescriptor? _processLaunchDescriptor;
    private string? _connectionString;
    private GroundworkPhysicalSchemaManifestSource? _physicalSource;

    public override GroundworkProviderDescriptor Descriptor => ProviderDescriptor;

    public override GroundworkTopologyCapabilities RequiredTopology =>
        GroundworkTopologyCapabilities.PersistentStorage |
        GroundworkTopologyCapabilities.IndependentClients |
        GroundworkTopologyCapabilities.MultiDocumentTransactions |
        GroundworkTopologyCapabilities.ExternalProcessRestart;

    public override GroundworkCompositionFingerprint CompositionFingerprint => FixtureComposition;

    public override GroundworkProcessLaunchDescriptor ProcessLaunchDescriptor =>
        _processLaunchDescriptor ??= _processProbeRunner.CreateLaunchDescriptor();

    public override string ProbeDocumentKind => ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind;

    protected override async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "groundwork.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 30
        };
        _connectionString = builder.ToString();
        await ResetCoreAsync(cancellationToken);
    }

    protected override async ValueTask ResetCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDatabaseFiles();
        _physicalSource = null;

        _ = await CreateStoreAsync(cancellationToken);
    }

    protected override async ValueTask ResetPhysicalCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDatabaseFiles();
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            SqliteGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                SqliteGroundworkCapabilities.Provider.Name,
                "sqlite-file",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            SqliteGroundworkCapabilities.PhysicalNames,
            cancellationToken: cancellationToken);
        await using (var connection = new SqliteConnection(RequireConnectionString().ConnectionString))
        {
            var applied = await PhysicalSchemaApplication.ApplyAsync(
                source.PhysicalTarget,
                new SqlitePhysicalSchemaExecutor(connection),
                cancellationToken: cancellationToken);
            EnsureSchemaApplied(applied);
        }

        await using (var inspectionConnection = new SqliteConnection(RequireConnectionString().ConnectionString))
        {
            var admission = await source.InspectRuntimeAdmissionAsync(
                new SqlitePhysicalSchemaExecutor(inspectionConnection),
                cancellationToken: cancellationToken);
            if (!admission.IsReady)
                throw new InvalidOperationException("SQLite physical provider driver did not admit its applied runtime target.");
        }

        _physicalSource = source;
    }

    protected override ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        CancellationToken cancellationToken) =>
        OpenClientCoreAsync(
            clientId,
            ElsaRuntimeStorageManifest.Create(),
            GroundworkTestAccess.DefaultScoped,
            cancellationToken);

    protected override async ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var store = await CreateStoreAsync(manifest, access, cancellationToken);
        var services = new ServiceCollection()
            .AddSingleton<IDocumentStore>(store)
            .BuildServiceProvider();
        return new GroundworkProviderClient(
            clientId,
            services,
            services.GetRequiredService<IDocumentStore>(),
            services.DisposeAsync);
    }

    protected override ValueTask<GroundworkProviderClient> OpenPhysicalClientCoreAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _physicalSource ?? throw new InvalidOperationException("The SQLite physical target has not been applied.");
        var store = new SqlitePhysicalDocumentStore(
            RequireConnectionString().ConnectionString,
            source.CreateManifest(),
            source.PhysicalTarget.Routes,
            GroundworkTestAccess.DefaultScoped);
        var services = new ServiceCollection()
            .AddSingleton<IDocumentStore>(store)
            .BuildServiceProvider();
        return ValueTask.FromResult(new GroundworkProviderClient(
            clientId,
            services,
            store,
            services.DisposeAsync));
    }

    protected override ValueTask<GroundworkProcessProbeResult> RunInNewProcessCoreAsync(
        GroundworkProcessProbeRequest request,
        CancellationToken cancellationToken) =>
        _processProbeRunner.RunAsync(
            ProcessLaunchDescriptor,
            Descriptor,
            ProbeDocumentKind,
            new GroundworkProcessProbeState(RequireConnectionString().ConnectionString),
            request,
            cancellationToken: cancellationToken);

    protected override async ValueTask<GroundworkSanitizedEvidence> CaptureDiagnosticsCoreAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(RequireConnectionString().ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var engineVersion = await ScalarAsync(connection, "SELECT sqlite_version();", cancellationToken);
        var schemaObjectCount = await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE name LIKE 'groundwork_%';",
            cancellationToken);
        return GroundworkSanitizedEvidence.Create(
            "diagnostics",
            $"provider={ProviderKey}\n" +
            $"provider-package-version={PackageVersion}\n" +
            $"topology={Descriptor.Topology.Description}\n" +
            $"engine-version={engineVersion}\n" +
            $"schema-object-count={schemaObjectCount}");
    }

    protected override async ValueTask<GroundworkNativePlanEvidence> CaptureNativePlanCoreAsync(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        await EnsurePlanProbeAsync(cancellationToken);
        await using var connection = new SqliteConnection(RequireConnectionString().ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await SeedPlanNoiseAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT document_kind, storage_scope, id, schema_version, version, content_json
            FROM groundwork_documents
            WHERE document_kind = @kind
              AND storage_scope = @scope
              AND id_lookup_key = @id;
            """;
        command.Parameters.AddWithValue("@kind", ProbeDocumentKind);
        command.Parameters.AddWithValue("@scope", GroundworkTestAccess.DefaultScopeValue);
        command.Parameters.AddWithValue("@id", PlanProbeId);
        var details = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                details.Add(reader.GetString(3));
        }

        if (!details.Any(detail => detail.Contains(IdentityIndex, StringComparison.Ordinal)))
            throw new InvalidOperationException("SQLite substrate plan smoke did not use the document identity index.");

        var evidence = GroundworkSanitizedEvidence.Create(
            "native-plan",
            "evidence-class=substrate-only-plan-smoke\n" +
            "admitted-route-proof=false\n" +
            $"expected-index={IdentityIndex}\n" +
            string.Join('\n', details));
        return GroundworkNativePlanEvidence.Create(executionPath, scenarioId, evidence);
    }

    protected override ValueTask DisposeCoreAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        _connectionString = null;
        return ValueTask.CompletedTask;
    }

    private void DeleteDatabaseFiles()
    {
        var databasePath = RequireConnectionString().DataSource;
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private static void EnsureSchemaApplied(PhysicalSchemaApplicationResult result)
    {
        if (result.Outcome is PhysicalSchemaApplicationOutcome.Rejected or PhysicalSchemaApplicationOutcome.AuthorizationRequired)
            throw new InvalidOperationException($"SQLite physical schema application was not accepted: {result.Outcome}.");
    }

    private Task<SqliteDocumentStore> CreateStoreAsync(CancellationToken cancellationToken) =>
        CreateStoreAsync(
            ElsaRuntimeStorageManifest.Create(),
            GroundworkTestAccess.DefaultScoped,
            cancellationToken);

    private Task<SqliteDocumentStore> CreateStoreAsync(
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken) =>
        SqliteDocumentStoreFactory.CreateAsync(
            RequireConnectionString().ConnectionString,
            manifest,
            new ProviderIdentity(ProviderIdentity, PackageVersion),
            access,
            cancellationToken: cancellationToken);

    private async Task EnsurePlanProbeAsync(CancellationToken cancellationToken)
    {
        var store = await CreateStoreAsync(cancellationToken);
        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                ProbeDocumentKind,
                PlanProbeId,
                GroundworkProcessProbeProtocol.CurrentVersion,
                "{\"value\":\"plan-smoke\"}",
                ExpectedVersion: 0),
            cancellationToken);
        if (result.Status is not (DocumentStoreWriteStatus.Saved or DocumentStoreWriteStatus.ConcurrencyConflict))
            throw new InvalidOperationException($"Unable to seed SQLite plan smoke: {result.Status}.");
    }

    private async Task SeedPlanNoiseAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE sequence(value) AS (
                SELECT 1
                UNION ALL
                SELECT value + 1 FROM sequence WHERE value < 1024
            )
            INSERT OR IGNORE INTO groundwork_documents
                (document_kind, storage_scope, id, id_comparison_key, id_lookup_key,
                 schema_version, version, content_json, created_utc, updated_utc)
            SELECT document_kind,
                   storage_scope,
                   id || '-noise-' || sequence.value,
                   id_comparison_key || '-noise-' || sequence.value,
                   id_lookup_key || '-noise-' || sequence.value,
                   schema_version,
                   version,
                   content_json,
                   created_utc,
                   updated_utc
            FROM groundwork_documents
            CROSS JOIN sequence
            WHERE document_kind = @kind
              AND storage_scope = @scope
              AND id_lookup_key = @id;
            ANALYZE groundwork_documents;
            """;
        command.Parameters.AddWithValue("@kind", ProbeDocumentKind);
        command.Parameters.AddWithValue("@scope", GroundworkTestAccess.DefaultScopeValue);
        command.Parameters.AddWithValue("@id", PlanProbeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnectionStringBuilder RequireConnectionString() =>
        _connectionString is not null
            ? new SqliteConnectionStringBuilder(_connectionString)
            : throw new InvalidOperationException("The SQLite provider driver has not been initialized.");

    private static async Task<string> ScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
    }
}
