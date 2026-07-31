using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
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
using ElsaRuntimeSchemaAdmissionResult = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionResult;

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
    public override string? PhysicalTargetFingerprint => _physicalSource?.PhysicalTarget.Fingerprint;

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

    protected override async ValueTask ResetPhysicalCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ResetPhysicalStorageCoreAsync(cancellationToken);
        var source = await CreatePhysicalSchemaSourceAsync(manifestSources, cancellationToken);
        await ApplyPhysicalSchemaCoreAsync(source, cancellationToken);
    }

    protected override ValueTask ResetPhysicalStorageCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDatabaseFiles();
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask ApplyPhysicalSchemaCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
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
            cancellationToken: cancellationToken);

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
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _physicalSource ?? throw new InvalidOperationException("The SQLite physical target has not been applied.");
        var store = new SqlitePhysicalDocumentStore(
            RequireConnectionString().ConnectionString,
            source.CreateManifest(),
            source.PhysicalTarget.Routes,
            access);
        var boundedStore = new GroundworkBoundedDocumentStoreRouter(
            source.PhysicalTarget.Routes.Select(route =>
                KeyValuePair.Create<string, IBoundedDocumentStore>(
                    route.StorageUnit.Value,
                    SqlitePhysicalQueryRuntime.Create(
                        store,
                        source.CreateManifest(),
                        route,
                        source.PhysicalTarget.Provider))));
        var services = new ServiceCollection()
            .AddSingleton<IDocumentStore>(store)
            .AddSingleton(boundedStore)
            .AddSingleton<IBoundedDocumentStore>(boundedStore)
            .BuildServiceProvider();
        return ValueTask.FromResult(new GroundworkProviderClient(
            clientId,
            services,
            store,
            services.DisposeAsync,
            boundedStore));
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreatePhysicalSchemaSourceAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken)
    {
        var topology = new GroundworkProviderTopologySnapshot(
            SqliteGroundworkCapabilities.Provider.Name,
            "sqlite-file",
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });
        return manifestSources is null
            ? GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
                SqliteGroundworkCapabilities.Runtime(),
                topology,
                SqliteGroundworkCapabilities.PhysicalNames,
                cancellationToken: cancellationToken)
            : GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
                SqliteGroundworkCapabilities.Runtime(),
                topology,
                SqliteGroundworkCapabilities.PhysicalNames,
                manifestSources,
                cancellationToken: cancellationToken);
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

    protected override async ValueTask PrepareNativeRoutePlanDatasetCoreAsync(
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(RequireConnectionString().ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var group in requests.GroupBy(request => (request.PhysicalName, request.DocumentKind)))
            await EnsureNativeRouteDatasetAsync(connection, group.ToArray(), cancellationToken);
    }

    protected override async ValueTask<GroundworkNativeRoutePlanResult> CaptureNativeRoutePlanCoreAsync(
        GroundworkNativeRoutePlanRequest request,
        PhysicalDocumentQueryExplanation explanation,
        int materializedCandidateCount,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(RequireConnectionString().ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var expectedIndex = await ResolveNativeRouteIndexAsync(connection, request, cancellationToken);
        var lookupIndexes = await ListNativeRouteIndexesAsync(connection, request.PhysicalName, cancellationToken);
        var commands = explanation.Commands.Select((command, ordinal) =>
        {
            var indexes = ExtractNativePlanIndexes(command.NativePlan);
            if (!string.Equals(command.NativePlanFormat, "sqlite-query-plan", StringComparison.Ordinal) ||
                HasFallbackCollectionScan(command.NativePlan) ||
                !command.NativePlan.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) ||
                !indexes.Intersect(lookupIndexes, StringComparer.Ordinal).Any())
            {
                throw new InvalidOperationException(
                    $"SQLite route '{request.QueryIdentity}' command '{command.Identity}' was not a scan-free search " +
                    $"through '{expectedIndex}': {command.NativePlan}");
            }
            return GroundworkNativeRouteCommandEvidence.Create(
                ordinal,
                command,
                "index-search",
                indexes);
        }).ToArray();
        var selectedIndex = GroundworkNativeRoutePlanResult.SelectCompiledIndex(
            request,
            lookupIndexes,
            commands);
        var cardinality = await CountPhysicalRowsAsync(connection, request.PhysicalName, cancellationToken);
        return GroundworkNativeRoutePlanResult.Create(
            request,
            ProviderKey,
            cardinality,
            "index-search",
            selectedIndex,
            request.Limit,
            materializedCandidateCount,
            commands);
    }

    protected override async ValueTask<GroundworkPhysicalSchemaManifestSource> PrepareSchemaParityCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources,
        CancellationToken cancellationToken)
    {
        DeleteDatabaseFiles();
        _physicalSource = null;
        return await CreatePhysicalSchemaSourceAsync(manifestSources, cancellationToken);
    }

    protected override async ValueTask<GroundworkProviderSchemaStateSnapshot> CaptureSchemaStateCoreAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(RequireConnectionString().ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var catalog = new List<string>();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT type, name, tbl_name, COALESCE(sql, '')
                FROM sqlite_schema
                WHERE name NOT LIKE 'sqlite_%'
                ORDER BY type, name;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                catalog.Add($"catalog\u001f{type}\u001f{name}\u001f{reader.GetString(2)}\u001f{reader.GetString(3)}");
                if (string.Equals(type, "table", StringComparison.Ordinal))
                    tables.Add(name);
            }
        }
        return await GroundworkRelationalSchemaStateReader.CaptureAsync(
            ProviderKey,
            connection,
            catalog,
            tables,
            QuoteIdentifier,
            cancellationToken);
    }

    protected override async ValueTask<ElsaRuntimeSchemaAdmissionResult> InspectSchemaParityAdmissionCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(RequireConnectionString().ConnectionString);
        return await source.InspectRuntimeAdmissionAsync(
            new SqlitePhysicalSchemaExecutor(connection),
            cancellationToken: cancellationToken);
    }

    protected override SchemaToolConnection SchemaToolConnectionCore() =>
        new(RequireConnectionString().ConnectionString);

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
        if (result.Outcome is not (PhysicalSchemaApplicationOutcome.Applied or PhysicalSchemaApplicationOutcome.NoChanges))
            throw new InvalidOperationException($"SQLite physical schema application did not complete: {result.Outcome}.");
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

    private static async Task EnsureNativeRouteDatasetAsync(
        SqliteConnection connection,
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken)
    {
        var dataset = GroundworkNativeRouteDataset.Create(requests);
        var projectedColumns = string.Join(", ", dataset.ProjectedValues.Keys.Select(QuoteIdentifier));
        var projectedValues = string.Join(", ", dataset.ProjectedValues.Values.Select((value, index) =>
        {
            var varies = dataset.VaryingProjectedFields.Contains(value.Field, StringComparer.Ordinal);
            var isPredicate = dataset.PredicateFields.Contains(value.Field);
            return
            $"CASE WHEN value <= @matchingCardinality THEN " +
            $"{NativeMatchingExpression(value, index, varies && dataset.MatchingCardinality > 1)} " +
            $"ELSE {(varies || !isPredicate ? NativeNoiseExpression(value.Kind, index) : $"@noise{index}")} END";
        }));
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            var sequence = """
                WITH digits(value) AS (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)),
                sequence(value) AS (
                    SELECT d0.value + 10*d1.value + 100*d2.value + 1000*d3.value + 10000*d4.value
                    FROM digits d0 CROSS JOIN digits d1 CROSS JOIN digits d2 CROSS JOIN digits d3 CROSS JOIN digits d4
                )
                """;
            var primaryName = dataset.PrimaryPhysicalName ?? dataset.PhysicalName;
            var primaryInsert = $"""
                {sequence}
                INSERT INTO {QuoteIdentifier(primaryName)}
                    (document_kind, storage_scope, id, id_comparison_key, id_lookup_key,
                     schema_version, version, {QuoteIdentifier(dataset.ContentColumn)}, created_utc, updated_utc)
                SELECT @kind,
                       CASE WHEN value = 1 THEN @crossScope ELSE @scope END,
                       CASE WHEN value = 0 THEN @candidateId ELSE printf('native-%06d', value) END,
                       CASE WHEN value = 0 THEN @candidateComparison ELSE printf('noise-comparison-%06d', value) END,
                       CASE WHEN value = 0 THEN @candidateLookup ELSE printf('noise-lookup-%06d', value) END,
                       @schemaVersion,
                       1,
                       CASE WHEN value = 0 THEN @candidateContent ELSE @emptyContent END,
                       '2000-01-01T00:00:00.0000000+00:00',
                       '2000-01-01T00:00:00.0000000+00:00'
                FROM sequence
                WHERE value < @cardinality;
                """;
            var lookupInsert = $"""
                {sequence}
                INSERT INTO {QuoteIdentifier(dataset.PhysicalName)}
                    (document_kind, storage_scope, document_id, document_id_comparison_key,
                     document_id_lookup_key, {projectedColumns})
                SELECT @kind,
                       CASE WHEN value = 1 THEN @crossScope ELSE @scope END,
                       CASE WHEN value = 0 THEN @candidateId ELSE printf('native-%06d', value) END,
                       CASE WHEN value = 0 THEN @candidateComparison ELSE printf('noise-comparison-%06d', value) END,
                       CASE WHEN value = 0 THEN @candidateLookup ELSE printf('noise-lookup-%06d', value) END,
                       {projectedValues}
                FROM sequence
                WHERE value < @cardinality;
                """;
            command.CommandText = dataset.PrimaryPhysicalName is null
                ? $"""
                    DELETE FROM {QuoteIdentifier(dataset.PhysicalName)};
                    {sequence}
                    INSERT INTO {QuoteIdentifier(dataset.PhysicalName)}
                        (document_kind, storage_scope, id, id_comparison_key, id_lookup_key,
                         schema_version, version, {QuoteIdentifier(dataset.ContentColumn)}, created_utc, updated_utc, {projectedColumns})
                    SELECT @kind,
                           CASE WHEN value = 1 THEN @crossScope ELSE @scope END,
                           CASE WHEN value = 0 THEN @candidateId ELSE printf('native-%06d', value) END,
                           CASE WHEN value = 0 THEN @candidateComparison ELSE printf('noise-comparison-%06d', value) END,
                           CASE WHEN value = 0 THEN @candidateLookup ELSE printf('noise-lookup-%06d', value) END,
                           @schemaVersion,
                           1,
                           CASE WHEN value = 0 THEN @candidateContent ELSE @emptyContent END,
                           '2000-01-01T00:00:00.0000000+00:00',
                           '2000-01-01T00:00:00.0000000+00:00',
                           {projectedValues}
                    FROM sequence
                    WHERE value < @cardinality;
                    """
                : $"""
                    DELETE FROM {QuoteIdentifier(dataset.PhysicalName)};
                    DELETE FROM {QuoteIdentifier(primaryName)} WHERE document_kind = @kind;
                    {primaryInsert}
                    {lookupInsert}
                    """;
            command.Parameters.AddWithValue("@kind", dataset.DocumentKind);
            command.Parameters.AddWithValue("@scope", dataset.StorageScope);
            command.Parameters.AddWithValue("@crossScope", dataset.CrossScope);
            command.Parameters.AddWithValue("@candidateId", dataset.CandidateDocumentId);
            command.Parameters.AddWithValue("@candidateComparison", dataset.CandidateComparisonKey);
            command.Parameters.AddWithValue("@candidateLookup", dataset.CandidateLookupKey);
            command.Parameters.AddWithValue("@schemaVersion", dataset.CandidateSchemaVersion);
            command.Parameters.AddWithValue("@candidateContent", dataset.CandidateContentJson);
            command.Parameters.AddWithValue("@emptyContent", "{}");
            command.Parameters.AddWithValue("@cardinality", dataset.AcceptanceCardinality);
            command.Parameters.AddWithValue("@matchingCardinality", dataset.MatchingCardinality);
            var index = 0;
            foreach (var value in dataset.ProjectedValues.Values)
            {
                command.Parameters.AddWithValue($"@target{index}", SqliteProviderValue(value, noise: false));
                command.Parameters.AddWithValue($"@noise{index}", SqliteProviderValue(value, noise: true));
                index++;
            }
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await using var analyze = connection.CreateCommand();
        analyze.CommandText = dataset.PrimaryPhysicalName is null
            ? $"ANALYZE {QuoteIdentifier(dataset.PhysicalName)};"
            : $"ANALYZE {QuoteIdentifier(dataset.PhysicalName)}; " +
              $"ANALYZE {QuoteIdentifier(dataset.PrimaryPhysicalName)};";
        await analyze.ExecuteNonQueryAsync(cancellationToken);

        var cardinality = await CountPhysicalRowsAsync(connection, dataset.PhysicalName, cancellationToken);
        if (cardinality != dataset.AcceptanceCardinality)
            throw new InvalidOperationException(
                $"SQLite seeded {cardinality} rows in '{dataset.PhysicalName}', expected {dataset.AcceptanceCardinality}.");
    }

    private static async Task<string> ResolveNativeRouteIndexAsync(
        SqliteConnection connection,
        GroundworkNativeRoutePlanRequest request,
        CancellationToken cancellationToken)
    {
        var indexes = await ListNativeRouteIndexesAsync(
            connection,
            request.PhysicalName,
            cancellationToken);

        foreach (var index in indexes)
        {
            if (string.Equals(index, request.ExpectedIndexName, StringComparison.Ordinal))
                return index;
            var columns = (await DescribeIndexColumnsAsync(connection, index, cancellationToken))
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            var expected = new[] { "storage_scope" }.Concat(request.IndexFields).ToArray();
            if (columns.Take(expected.Length).SequenceEqual(expected, StringComparer.Ordinal))
                return index;
        }

        throw new InvalidOperationException(
            $"SQLite route '{request.QueryIdentity}' has no physical index prefix ({string.Join(", ", new[] { "storage_scope" }.Concat(request.IndexFields))}).");
    }

    private static async Task<IReadOnlyList<string>> ListNativeRouteIndexesAsync(
        SqliteConnection connection,
        string physicalName,
        CancellationToken cancellationToken)
    {
        var indexes = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_index_list({QuoteLiteral(physicalName)}) ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            indexes.Add(reader.GetString(0));
        return indexes;
    }

    private static string NativeNoiseExpression(
        GroundworkNativeRouteProjectedValueKind kind,
        int targetIndex) =>
        kind switch
        {
            GroundworkNativeRouteProjectedValueKind.String => "printf('noise-%06d', value)",
            GroundworkNativeRouteProjectedValueKind.Boolean =>
                $"CASE WHEN @target{targetIndex} THEN 0 ELSE 1 END",
            GroundworkNativeRouteProjectedValueKind.Int64 =>
                $"CAST(@target{targetIndex} AS INTEGER) + value + 1",
            GroundworkNativeRouteProjectedValueKind.DateTime =>
                $"CAST(@target{targetIndex} AS INTEGER) + value + 1",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown native-route projected value kind.")
        };

    private static string NativeMatchingExpression(
        GroundworkNativeRouteProjectedValue value,
        int targetIndex,
        bool varies) =>
        !varies
            ? $"@target{targetIndex}"
            : value.Kind switch
            {
                GroundworkNativeRouteProjectedValueKind.String =>
                    $"@target{targetIndex} || '-' || printf('%06d', value)",
                GroundworkNativeRouteProjectedValueKind.Int64 or
                    GroundworkNativeRouteProjectedValueKind.DateTime =>
                    $"CAST(@target{targetIndex} AS INTEGER) + value",
                _ => throw new InvalidOperationException(
                    $"Projected field '{value.Field}' cannot vary uniquely at acceptance scale.")
            };

    private static object SqliteProviderValue(
        GroundworkNativeRouteProjectedValue value,
        bool noise)
    {
        var providerValue = noise ? value.ToProviderNoiseValue() : value.ToProviderValue();
        return value.Kind == GroundworkNativeRouteProjectedValueKind.DateTime
            ? ((DateTimeOffset)providerValue).UtcDateTime.Ticks
            : providerValue;
    }

    private static IReadOnlyList<string> ExtractNativePlanIndexes(string nativePlan) =>
        nativePlan
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                const string marker = "INDEX ";
                var start = line.IndexOf(marker, StringComparison.Ordinal);
                return start < 0
                    ? null
                    : line[(start + marker.Length)..]
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool HasFallbackCollectionScan(string nativePlan) =>
        nativePlan
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains("SCAN", StringComparison.OrdinalIgnoreCase))
            .Any(line =>
                line.StartsWith("SCAN p", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("SCAN l", StringComparison.OrdinalIgnoreCase));

    private static async Task<long> CountPhysicalRowsAsync(
        SqliteConnection connection,
        string physicalName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(physicalName)};";
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static async Task<string> DescribeIndexColumnsAsync(
        SqliteConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_info({QuoteLiteral(indexName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(2));
        return string.Join(",", columns);
    }

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

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

/// <summary>
/// Owns the construction and deterministic cleanup of SQLite physical-provider test clients.
/// </summary>
public static class SqlitePhysicalClientResourceOwner
{
    /// <summary>
    /// Opens a connection, constructs its store and service provider, and returns a client that owns all resources.
    /// </summary>
    public static async ValueTask<GroundworkProviderClient> OpenAsync<TConnection>(
        Guid clientId,
        Func<TConnection> createConnection,
        Func<TConnection, CancellationToken, ValueTask> openConnection,
        Func<TConnection, IDocumentStore> createStore,
        Func<TConnection, ValueTask> disposeConnection,
        CancellationToken cancellationToken,
        Func<ServiceProvider, ValueTask>? disposeServices = null)
        where TConnection : class
    {
        TConnection? connection = null;
        ServiceProvider? services = null;
        try
        {
            connection = createConnection();
            await openConnection(connection, cancellationToken);
            var store = createStore(connection);
            services = new ServiceCollection()
                .AddSingleton<IDocumentStore>(store)
                .BuildServiceProvider();
            return new GroundworkProviderClient(
                clientId,
                services,
                store,
                () => DisposeAsync(
                    () => (disposeServices ?? (static provider => provider.DisposeAsync()))(services),
                    () => disposeConnection(connection)));
        }
        catch (Exception failure)
        {
            throw await CleanupAfterFailureAsync(
                failure,
                services is null
                    ? null
                    : () => (disposeServices ?? (static provider => provider.DisposeAsync()))(services),
                connection is null ? null : () => disposeConnection(connection));
        }
    }

    private static async ValueTask DisposeAsync(
        Func<ValueTask> disposeServices,
        Func<ValueTask> disposeConnection)
    {
        Exception? servicesFailure = null;
        try
        {
            await disposeServices();
        }
        catch (Exception exception)
        {
            servicesFailure = exception;
        }

        try
        {
            await disposeConnection();
        }
        catch (Exception connectionFailure) when (servicesFailure is not null)
        {
            throw new AggregateException(
                "SQLite physical provider client service and connection disposal both failed.",
                servicesFailure,
                connectionFailure);
        }

        if (servicesFailure is not null)
            throw servicesFailure;
    }

    private static async ValueTask<Exception> CleanupAfterFailureAsync(
        Exception failure,
        Func<ValueTask>? disposeServices,
        Func<ValueTask>? disposeConnection)
    {
        if (disposeServices is null && disposeConnection is null)
            return failure;

        try
        {
            await DisposeAsync(
                disposeServices ?? (() => ValueTask.CompletedTask),
                disposeConnection ?? (() => ValueTask.CompletedTask));
            return failure;
        }
        catch (Exception cleanupFailure)
        {
            return new AggregateException(
                "SQLite physical provider client creation and cleanup both failed.",
                failure,
                cleanupFailure);
        }
    }
}
