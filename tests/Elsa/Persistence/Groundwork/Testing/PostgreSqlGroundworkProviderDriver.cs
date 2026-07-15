using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.PostgreSql;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.Documents;
using Groundwork.PostgreSql.PhysicalStorage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Elsa.Persistence.Groundwork.Testing;

public sealed class PostgreSqlGroundworkProviderDriver : GroundworkProviderDriver
{
    public const string PinnedImage = "postgres:17.6-alpine3.22";
    private const string ProviderKey = "postgresql";
    private const string ProviderIdentity = "groundwork-postgresql";
    private const string IdentityIndex = "ux_groundwork_documents_identity_lookup";
    private const string PlanProbeId = "provider-plan-probe";
    private static readonly GroundworkCompositionFingerprint FixtureComposition =
        GroundworkCompositionFingerprint.Create("elsa-runtime-provider-fixture:v1");
    private static readonly string PackageVersion =
        GroundworkProviderDriverSupport.PackageVersion(typeof(PostgreSqlDocumentStore).Assembly);
    private static readonly GroundworkProviderDescriptor ProviderDescriptor = new(
        ProviderKey,
        ProviderIdentity,
        PackageVersion,
        new GroundworkProviderTopology(
            ProviderKey,
            "real-postgresql-container",
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions |
            GroundworkTopologyCapabilities.ExternalProcessRestart));

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PinnedImage)
        .WithDatabase("elsa_admin")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private readonly GroundworkProcessProbeRunner _processProbeRunner = new();
    private GroundworkProcessLaunchDescriptor? _processLaunchDescriptor;
    private string? _connectionString;
    private bool _containerStarted;
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

    public async Task<string> CreateIsolatedDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (!_containerStarted)
            throw new InvalidOperationException("The PostgreSQL provider driver has not been initialized.");

        var databaseName = $"elsa_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(databaseName, cancellationToken);
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
    }

    protected override async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await _container.StartAsync(cancellationToken);
        _containerStarted = true;
        _connectionString = await CreateIsolatedDatabaseAsync(cancellationToken);
        await ResetCoreAsync(cancellationToken);
    }

    protected override async ValueTask ResetCoreAsync(CancellationToken cancellationToken)
    {
        await ResetSchemaAsync(cancellationToken);
        _physicalSource = null;
        _ = await CreateStoreAsync(cancellationToken);
    }

    protected override async ValueTask ResetPhysicalCoreAsync(CancellationToken cancellationToken)
    {
        await ResetSchemaAsync(cancellationToken);
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            PostgreSqlGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                PostgreSqlGroundworkCapabilities.Provider.Name,
                "postgresql-server",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            PostgreSqlGroundworkCapabilities.PhysicalNames,
            cancellationToken: cancellationToken);
        var executor = new PostgreSqlPhysicalSchemaExecutor(RequireConnectionString());
        var applied = await PhysicalSchemaApplication.ApplyAsync(
            source.PhysicalTarget,
            executor,
            cancellationToken: cancellationToken);
        EnsureSchemaApplied(applied);
        var admission = await source.InspectRuntimeAdmissionAsync(executor, cancellationToken);
        if (!admission.IsReady)
            throw new InvalidOperationException("PostgreSQL physical provider driver did not admit its applied runtime target.");
        _physicalSource = source;
    }

    private async Task ResetSchemaAsync(CancellationToken cancellationToken)
    {
        var connectionString = RequireConnectionString();
        ClearPool(connectionString);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP SCHEMA IF EXISTS public CASCADE;
                CREATE SCHEMA public AUTHORIZATION CURRENT_USER;
                GRANT ALL ON SCHEMA public TO PUBLIC;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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
        var source = _physicalSource ?? throw new InvalidOperationException("The PostgreSQL physical target has not been applied.");
        var store = new PostgreSqlPhysicalDocumentStore(
            RequireConnectionString(),
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
            new GroundworkProcessProbeState(RequireConnectionString()),
            request,
            cancellationToken: cancellationToken);

    protected override async ValueTask<GroundworkSanitizedEvidence> CaptureDiagnosticsCoreAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(RequireConnectionString());
        await connection.OpenAsync(cancellationToken);
        var engineVersion = await ScalarAsync(connection, "SHOW server_version;", cancellationToken);
        var schemaObjectCount = await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name LIKE 'groundwork_%';",
            cancellationToken);
        return GroundworkSanitizedEvidence.Create(
            "diagnostics",
            $"provider={ProviderKey}\n" +
            $"provider-package-version={PackageVersion}\n" +
            $"topology={Descriptor.Topology.Description}\n" +
            $"container-image={PinnedImage}\n" +
            $"engine-version={engineVersion}\n" +
            $"schema-object-count={schemaObjectCount}");
    }

    protected override async ValueTask<GroundworkNativePlanEvidence> CaptureNativePlanCoreAsync(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        await EnsurePlanProbeAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(RequireConnectionString());
        await connection.OpenAsync(cancellationToken);
        var lookupKey = await ReadLookupKeyAsync(connection, cancellationToken);
        await SeedPlanNoiseAsync(connection, lookupKey, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN (FORMAT JSON, ANALYZE FALSE, COSTS FALSE, VERBOSE FALSE, SETTINGS FALSE, SUMMARY FALSE)
            SELECT document_kind, storage_scope, id, schema_version, version, content_json
            FROM groundwork_documents
            WHERE document_kind = @kind
              AND storage_scope = @scope
              AND id_lookup_key = @id;
            """;
        command.Parameters.AddWithValue("kind", ProbeDocumentKind);
        command.Parameters.AddWithValue("scope", GroundworkTestAccess.DefaultScopeValue);
        command.Parameters.AddWithValue("id", lookupKey);
        var plan = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        if (!plan.Contains(IdentityIndex, StringComparison.Ordinal))
            throw new InvalidOperationException("PostgreSQL substrate plan smoke did not use the document identity index.");

        var evidence = GroundworkSanitizedEvidence.Create(
            "native-plan",
            "evidence-class=substrate-only-plan-smoke\n" +
            "admitted-route-proof=false\n" +
            $"expected-index={IdentityIndex}\n" +
            plan);
        return GroundworkNativePlanEvidence.Create(executionPath, scenarioId, evidence);
    }

    protected override async ValueTask DisposeCoreAsync()
    {
        if (_connectionString is not null)
            ClearPool(_connectionString);
        _connectionString = null;
        if (_containerStarted)
        {
            await _container.DisposeAsync();
            _containerStarted = false;
        }
    }

    private Task<PostgreSqlDocumentStore> CreateStoreAsync(CancellationToken cancellationToken) =>
        CreateStoreAsync(
            ElsaRuntimeStorageManifest.Create(),
            GroundworkTestAccess.DefaultScoped,
            cancellationToken);

    private Task<PostgreSqlDocumentStore> CreateStoreAsync(
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken) =>
        PostgreSqlDocumentStoreFactory.CreateAsync(
            RequireConnectionString(),
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
            throw new InvalidOperationException($"Unable to seed PostgreSQL plan smoke: {result.Status}.");
    }

    private async Task<string> ReadLookupKeyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_lookup_key
            FROM groundwork_documents
            WHERE document_kind = @kind
              AND storage_scope = @scope
              AND id = @id;
            """;
        command.Parameters.AddWithValue("kind", ProbeDocumentKind);
        command.Parameters.AddWithValue("scope", GroundworkTestAccess.DefaultScopeValue);
        command.Parameters.AddWithValue("id", PlanProbeId);
        return Convert.ToString(
                   await command.ExecuteScalarAsync(cancellationToken),
                   System.Globalization.CultureInfo.InvariantCulture)
               ?? throw new InvalidOperationException("PostgreSQL plan smoke could not resolve the probe lookup key.");
    }

    private async Task SeedPlanNoiseAsync(
        NpgsqlConnection connection,
        string lookupKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO groundwork_documents
                (document_kind, storage_scope, id, id_comparison_key, id_lookup_key,
                 schema_version, version, content_json, created_utc, updated_utc)
            SELECT document_kind,
                   storage_scope,
                   id || '-noise-' || sequence.value::text,
                   id_comparison_key || '-noise-' || sequence.value::text,
                   id_lookup_key || '-noise-' || sequence.value::text,
                   schema_version,
                   version,
                   content_json,
                   created_utc,
                   updated_utc
            FROM groundwork_documents
            CROSS JOIN generate_series(1, 4096) AS sequence(value)
            WHERE document_kind = @kind
              AND storage_scope = @scope
              AND id_lookup_key = @id
            ON CONFLICT DO NOTHING;
            ANALYZE groundwork_documents;
            """;
        command.Parameters.AddWithValue("kind", ProbeDocumentKind);
        command.Parameters.AddWithValue("scope", GroundworkTestAccess.DefaultScopeValue);
        command.Parameters.AddWithValue("id", lookupKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        if (databaseName.Length != 37 ||
            !databaseName.StartsWith("elsa_", StringComparison.Ordinal) ||
            databaseName[5..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new ArgumentException("The generated PostgreSQL database name is invalid.", nameof(databaseName));

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireConnectionString() =>
        _connectionString ?? throw new InvalidOperationException("The PostgreSQL provider driver has not been initialized.");

    private static async Task<string> ScalarAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static void ClearPool(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    private static void EnsureSchemaApplied(PhysicalSchemaApplicationResult result)
    {
        if (result.Outcome is PhysicalSchemaApplicationOutcome.Rejected or PhysicalSchemaApplicationOutcome.AuthorizationRequired)
            throw new InvalidOperationException($"PostgreSQL physical schema application was not accepted: {result.Outcome}.");
    }
}
