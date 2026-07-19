using System.Diagnostics;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.MongoDb;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Physicalization;
using Groundwork.Core.Queries;
using Groundwork.Core.SchemaEvolution;
using Groundwork.MongoDb;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Red contract tests for Spec 094 T020. Every CLI assertion executes the repository-pinned
/// Groundwork.Tool in a child process and loads a public parameterless schema source from this
/// assembly, matching the deployment loading boundary rather than calling tool internals.
/// </summary>
public sealed class GroundworkSchemaCliContractTests : IDisposable
{
    private const int Success = 0;
    private const int PendingChanges = 2;
    private const int ValidationFailed = 3;
    private const string ConnectionEnvironmentVariable = "ELSA_GROUNDWORK_SCHEMA_CLI_TEST_CONNECTION";
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"elsa-groundwork-schema-cli-{Guid.NewGuid():N}");

    public GroundworkSchemaCliContractTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Runtime_and_tool_report_the_exact_same_target_fingerprint_and_effective_resolved_names()
    {
        var source = new CliFixtureSchemaSource();

        var cli = await RunToolAsync(
            "validate",
            typeof(CliFixtureSchemaSource),
            database: null,
            CancellationToken.None,
            "--offline");

        Assert.Equal(Success, cli.ExitCode);
        Assert.Equal(string.Empty, cli.Error);
        Assert.Equal(
            source.SchemaSource.TargetFingerprint,
            cli.Report.GetProperty("target").GetProperty("fingerprint").GetString());
        Assert.Equal(EffectiveNames(source.SchemaSource.ResolvedNames), EffectiveNames(cli.Report));

        var primary = Assert.Single(cli.Report.GetProperty("resolvedNames").EnumerateArray().Where(x =>
            x.GetProperty("storageUnit").GetString() == "orders" &&
            x.GetProperty("kind").GetString() == "primaryStorage"));
        Assert.Equal("host_orders", primary.GetProperty("logicalName").GetString());
        Assert.Equal("host_orders", primary.GetProperty("identifier").GetString());
    }

    [Fact]
    public async Task Shipped_all_feature_schema_is_parameterless_and_is_the_runtime_and_tool_authority()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<GroundworkAllFeaturesDeploymentSchema>();
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var runtimeSource = await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                GroundworkProviderCapabilitySnapshot.ForFeatureRoutes(
                    SqliteGroundworkCapabilities.Runtime(),
                    new GroundworkProviderTopologySnapshot(
                        SqliteGroundworkCapabilities.Provider.Name,
                        "sqlite-file",
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                        }),
                    RuntimeGroundworkStorageManifestSource.FeatureName,
                    [RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement()]),
                SqliteGroundworkCapabilities.PhysicalNames,
                CancellationToken.None);
        var deploymentSource = serviceProvider.GetRequiredService<IPhysicalSchemaManifestSource>();

        var cli = await RunToolAsync(
            "validate",
            typeof(GroundworkAllFeaturesDeploymentSchema),
            database: null,
            CancellationToken.None,
            "--offline");

        Assert.IsType<GroundworkAllFeaturesDeploymentSchema>(deploymentSource);
        Assert.Equal(Success, cli.ExitCode);
        Assert.Equal(string.Empty, cli.Error);
        Assert.Equal(
            runtimeSource.TargetFingerprint,
            cli.Report.GetProperty("target").GetProperty("fingerprint").GetString());
        Assert.Equal(EffectiveNames(runtimeSource.ResolvedNames), EffectiveNames(cli.Report));
    }

    [Fact]
    public async Task Mongo_mutation_target_and_host_naming_are_identical_at_runtime_and_in_the_tool_process()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<MongoMutationDeploymentSchema>();
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var runtimeSource = await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                new GroundworkProviderCapabilitySnapshot(
                    MongoDbGroundworkCapabilities.Runtime(),
                    new GroundworkProviderTopologySnapshot(
                        MongoDbGroundworkCapabilities.Provider.Name,
                        "transaction-capable-replica-set",
                        new HashSet<string>(StringComparer.Ordinal)),
                    []),
                MongoDbPhysicalNameNormalizer.Instance,
                CancellationToken.None,
                MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance);

        var cli = await RunToolForProviderAsync(
            "validate",
            typeof(MongoMutationDeploymentSchema),
            "mongodb",
            database: null,
            CancellationToken.None,
            "--offline");

        Assert.NotEmpty(runtimeSource.PhysicalTarget.ProviderDefinitions);
        Assert.Equal(Success, cli.ExitCode);
        Assert.Equal(string.Empty, cli.Error);
        Assert.Equal(
            runtimeSource.TargetFingerprint,
            cli.Report.GetProperty("target").GetProperty("fingerprint").GetString());
        Assert.Equal(EffectiveNames(runtimeSource.ResolvedNames), EffectiveNames(cli.Report));
        Assert.Contains(cli.Report.GetProperty("resolvedNames").EnumerateArray(), name =>
            name.GetProperty("kind").GetString() == "primaryStorage" &&
            name.GetProperty("logicalName").GetString() == "host_mutation_documents");
    }

    [Fact]
    public async Task Collision_after_host_transformation_is_rejected_deterministically()
    {
        var first = await RunToolAsync(
            "validate",
            typeof(CollidingCliFixtureSchemaSource),
            database: null,
            CancellationToken.None,
            "--offline");
        var second = await RunToolAsync(
            "validate",
            typeof(CollidingCliFixtureSchemaSource),
            database: null,
            CancellationToken.None,
            "--offline");

        Assert.Equal(ValidationFailed, first.ExitCode);
        Assert.Equal(first.ExitCode, second.ExitCode);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(string.Empty, first.Error);
        var collision = Assert.Single(first.Report.GetProperty("diagnostics").EnumerateArray().Where(x =>
            x.GetProperty("code").GetString() == "GW-PHYSICAL-011"));
        Assert.Contains("host_collision", collision.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("audit", collision.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("orders", collision.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(first.Report.GetProperty("targetMutated").GetBoolean());
    }

    [Fact]
    public async Task Pinned_tool_executes_the_exact_validate_plan_status_apply_sqlite_lifecycle()
    {
        var database = Path.Combine(directory, "lifecycle.db");

        var validatePending = await RunToolAsync(
            "validate",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None);
        Assert.Equal(Success, validatePending.ExitCode);
        Assert.Equal("ready", validatePending.Report.GetProperty("outcome").GetString());
        Assert.True(validatePending.Report.GetProperty("pendingOperations").GetArrayLength() > 0);
        Assert.False(validatePending.Report.GetProperty("targetMutated").GetBoolean());
        Assert.False(File.Exists(database));

        var plan = await RunToolAsync(
            "plan",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None);
        var status = await RunToolAsync(
            "status",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None);
        Assert.Equal(PendingChanges, plan.ExitCode);
        Assert.Equal(PendingChanges, status.ExitCode);
        Assert.Equal("pending", plan.Report.GetProperty("outcome").GetString());
        Assert.Equal("pending", status.Report.GetProperty("outcome").GetString());
        Assert.Equal(
            plan.Report.GetProperty("planFingerprint").GetString(),
            status.Report.GetProperty("planFingerprint").GetString());
        Assert.False(plan.Report.GetProperty("targetMutated").GetBoolean());
        Assert.False(status.Report.GetProperty("targetMutated").GetBoolean());
        Assert.False(await TableExistsAsync(database, "host_orders"));

        var apply = await RunToolAsync(
            "apply",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None,
            "--safe");
        Assert.Equal(Success, apply.ExitCode);
        Assert.Equal("applied", apply.Report.GetProperty("outcome").GetString());
        Assert.True(apply.Report.GetProperty("targetMutated").GetBoolean());
        Assert.True(await TableExistsAsync(database, "host_orders"));

        var validateReady = await RunToolAsync(
            "validate",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None);
        var statusReady = await RunToolAsync(
            "status",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None);
        var applyReady = await RunToolAsync(
            "apply",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None,
            "--safe");
        Assert.Equal(Success, validateReady.ExitCode);
        Assert.Equal(Success, statusReady.ExitCode);
        Assert.Equal(Success, applyReady.ExitCode);
        Assert.Equal(0, validateReady.Report.GetProperty("pendingOperations").GetArrayLength());
        Assert.Equal("ready", statusReady.Report.GetProperty("outcome").GetString());
        Assert.Equal("ready", applyReady.Report.GetProperty("outcome").GetString());
        Assert.False(validateReady.Report.GetProperty("targetMutated").GetBoolean());
        Assert.False(statusReady.Report.GetProperty("targetMutated").GetBoolean());
        Assert.False(applyReady.Report.GetProperty("targetMutated").GetBoolean());

        Assert.All(
            new[] { validatePending, plan, status, apply, validateReady, statusReady, applyReady },
            result => Assert.Equal(string.Empty, result.Error));
    }

    [Fact]
    public async Task Runtime_admission_reports_pending_schema_without_applying_it()
    {
        var database = Path.Combine(directory, "runtime-admission.db");
        var source = new CliFixtureSchemaSource().SchemaSource;
        await using var connection = new SqliteConnection($"Data Source={database}");
        var inspector = new SqlitePhysicalSchemaExecutor(connection);

        var admission = await source.InspectRuntimeAdmissionAsync(
            inspector,
            cancellationToken: CancellationToken.None);

        Assert.False(admission.IsReady);
        Assert.Equal(source.TargetFingerprint, admission.TargetFingerprint);
        Assert.Equal(EffectiveNames(source.ResolvedNames), EffectiveNames(admission.ResolvedNames));
        Assert.NotEmpty(admission.PendingOperations);
        Assert.Single(admission.Diagnostics, x => x.Code == "ELSA-GW-SCHEMA-PENDING");
        Assert.False(File.Exists(database));
    }

    [Fact]
    public async Task Runtime_admission_reports_applied_schema_drift_without_repairing_it()
    {
        var database = Path.Combine(directory, "runtime-drift.db");
        var applied = await RunToolAsync(
            "apply",
            typeof(CliFixtureSchemaSource),
            database,
            CancellationToken.None,
            "--safe");
        Assert.Equal(Success, applied.ExitCode);
        await DropTableAsync(database, "host_orders");

        var source = new CliFixtureSchemaSource().SchemaSource;
        await using var connection = new SqliteConnection($"Data Source={database}");
        var admission = await source.InspectRuntimeAdmissionAsync(
            new SqlitePhysicalSchemaExecutor(connection),
            cancellationToken: CancellationToken.None);

        Assert.False(admission.IsReady);
        Assert.Equal(source.TargetFingerprint, admission.TargetFingerprint);
        Assert.Single(admission.Diagnostics, x => x.Code == "ELSA-GW-SCHEMA-DRIFT");
        Assert.False(await TableExistsAsync(database, "host_orders"));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static async Task<ToolResult> RunToolAsync(
        string command,
        Type sourceType,
        string? database,
        CancellationToken cancellationToken,
        params string[] extraArguments) => await RunToolForProviderAsync(
        command,
        sourceType,
        "sqlite",
        database,
        cancellationToken,
        extraArguments);

    private static async Task<ToolResult> RunToolForProviderAsync(
        string command,
        Type sourceType,
        string provider,
        string? database,
        CancellationToken cancellationToken,
        params string[] extraArguments)
    {
        var repositoryRoot = FindRepositoryRoot();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "tool", "run", "groundwork", "--", command,
                     "--manifest-assembly", sourceType.Assembly.Location,
                     "--manifest-type", sourceType.FullName!,
                     "--provider", provider,
                     "--output", "json"
                 })
            start.ArgumentList.Add(argument);
        if (database is not null)
        {
            start.Environment[ConnectionEnvironmentVariable] = $"Data Source={database}";
            start.ArgumentList.Add("--connection-env");
            start.ArgumentList.Add(ConnectionEnvironmentVariable);
        }
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Groundwork.Tool.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        using var document = JsonDocument.Parse(output);
        return new ToolResult(process.ExitCode, document.RootElement.Clone(), output, error);
    }

    private static IReadOnlyList<string> EffectiveNames(
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> names) => names
        .Select(x => $"{x.LogicalName}\u001f{x.Identifier}")
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> EffectiveNames(JsonElement report) => report
        .GetProperty("resolvedNames")
        .EnumerateArray()
        .Select(x => $"{x.GetProperty("logicalName").GetString()}\u001f{x.GetProperty("identifier").GetString()}")
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToArray();

    private static async Task<bool> TableExistsAsync(string database, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        command.Parameters.AddWithValue("@name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None)) == 1;
    }

    private static async Task DropTableAsync(string database, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE \"{table}\";";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static GroundworkPhysicalSchemaManifestSource CreateSchemaSource()
    {
        var manifest = CreateManifest("elsa-cli-fixture", [("orders", "orderDocument", "orders")]);
        var declaration = new GroundworkStorageManifestDeclaration(
            "cli-fixture",
            manifest,
            [],
            [],
            [],
            []);
        var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        var report = new ProviderCapabilityReport(
            provider,
            new HashSet<CapabilityId>(),
            new HashSet<CapabilityId>(),
            IndexCapabilities.All,
            Enum.GetValues<PortableQueryOperation>().ToHashSet(),
            Enum.GetValues<ConcurrencyKind>().ToHashSet(),
            []);
        var capabilities = new GroundworkProviderCapabilitySnapshot(
            report,
            new GroundworkProviderTopologySnapshot(
                provider.Name,
                "file-backed-distinct-connections",
                new HashSet<string>(["persistent-storage", "independent-clients"], StringComparer.Ordinal)),
            []);
        var naming = new GroundworkStorageNamingPolicyOptions(
            "host-prefix-v1",
            context => $"host_{context.FeatureDefaultLogicalName}");
        var request = new GroundworkStorageCompositionValidationRequest(
            manifest.Identity,
            manifest.Owner,
            manifest.Version,
            [declaration],
            [],
            naming,
            capabilities,
            SqliteGroundworkCapabilities.PhysicalNames);
        var validation = new GroundworkStorageCompositionValidator().Validate(request);
        if (!validation.IsValid || validation.Snapshot is null)
            throw new InvalidOperationException(string.Join("; ", validation.Diagnostics.Select(x => x.Message)));
        return new GroundworkPhysicalSchemaManifestSource(validation.Snapshot);
    }

    private static StorageManifest CreateManifest(
        string identity,
        IReadOnlyCollection<(string Unit, string DocumentKind, string Table)> units)
    {
#pragma warning disable GW0001 // preview.57 has no non-obsolete StorageUnit constructor; PhysicalStorage below is authoritative.
        var storageUnits = units.Select(item => new StorageUnit(
            new StorageUnitIdentity(item.Unit),
            item.DocumentKind,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.DedicatedDocumentTable(item.Table)))
        }).ToArray();
#pragma warning restore GW0001
        return new StorageManifest(
            new StorageManifestIdentity(identity),
            new StorageManifestOwner("elsa.tests"),
            new StorageManifestVersion("1.0.0"),
            storageUnits,
            new HashSet<string>(),
            []);
    }

    private sealed record ToolResult(int ExitCode, JsonElement Report, string Output, string Error);

    /// <summary>
    /// Public and parameterless because Groundwork.Tool creates the selected source in its own
    /// process. The wrapper deliberately delegates both manifest and name policy to the same
    /// production source instance used by the runtime assertions above.
    /// </summary>
    public sealed class CliFixtureSchemaSource : IPhysicalSchemaManifestSource
    {
        public CliFixtureSchemaSource() => SchemaSource = CreateSchemaSource();

        public GroundworkPhysicalSchemaManifestSource SchemaSource { get; }

        public StorageManifest CreateManifest() => SchemaSource.CreateManifest();

        public IPhysicalNamePolicy CreateNamePolicy() => SchemaSource.CreateNamePolicy();
    }

    /// <summary>Deployment-only rejection fixture for a host transform collision.</summary>
    public sealed class CollidingCliFixtureSchemaSource : IPhysicalSchemaManifestSource
    {
        public StorageManifest CreateManifest() => GroundworkSchemaCliContractTests.CreateManifest(
            "elsa-cli-collision-fixture",
            [
                ("orders", "orderDocument", "orders"),
                ("audit", "auditDocument", "audit")
            ]);

        public IPhysicalNamePolicy CreateNamePolicy() => new DelegatePhysicalNamePolicy(context =>
            context.ObjectKind == PhysicalObjectKind.PrimaryStorage
                ? "host_collision"
                : context.FeatureDefaultLogicalName);
    }

    public sealed class MongoMutationDeploymentSchema : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(MongoMutationManifestSource)];

        protected override GroundworkStorageNamingPolicyOptions CreateStorageNamingPolicy() =>
            new("host-prefix-v1", context => $"host_{context.FeatureDefaultLogicalName}");
    }

    public sealed class MongoMutationManifestSource : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "cli-mongodb-mutation-fixture";

        public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = CreateManifest(
                "elsa-cli-mongodb-mutation-fixture",
                [("mutation-work", "mutationWorkDocument", "mutation_documents")]);
            var unit = manifest.StorageUnits.Single();
            var storage = unit.PhysicalStorage!;
            var physicalDefinition = PhysicalTableDefinition.PhysicalEntityTable(
                "mutation_documents",
                [new ProjectedColumnDefinition(
                    "category",
                    "category",
                    PortablePhysicalType.String,
                    Length: 128,
                    IsNullable: false)],
                indexes:
                [
                    new PhysicalIndexDefinition(
                        "mutation_documents-by-category",
                        [
                            new PhysicalIndexColumnDefinition("storage_scope", 0),
                            new PhysicalIndexColumnDefinition("category", 1)
                        ],
                        missingValueBehavior: MissingValueBehavior.Excluded)
                ]);
            var index = new LogicalIndexDeclaration(
                "mutation_documents-by-category",
                [new IndexField("category", IndexValueKind.Keyword)],
                IndexValueKind.Keyword,
                isUnique: false,
                MissingValueBehavior.Excluded);
            var query = new BoundedQueryDeclaration(
                "list-by-category",
                index.Identity,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                QuerySortSupport.None,
                QueryPagingSupport.None,
                BoundedQueryExecutionClass.ScaleBearing,
                supportsTotalCount: true,
                resultOperations: new HashSet<BoundedQueryResultOperation>
                {
                    BoundedQueryResultOperation.Count
                });
            manifest = manifest with
            {
                StorageUnits =
                [
                    unit with
                    {
                        PhysicalStorage = new StorageUnitPhysicalStorage(
                            storage.ProvisioningMode,
                            PhysicalStoragePolicy.Explicit(physicalDefinition),
                            [index],
                            [query],
                            storage.NameOverrides,
                            [new BoundedMutationDeclaration(
                                "prune-by-category",
                                query.Identity,
                                BoundedMutationAction.Delete())])
                    }
                ]
            };
            return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                manifest,
                [],
                [],
                [],
                []));
        }
    }
}
