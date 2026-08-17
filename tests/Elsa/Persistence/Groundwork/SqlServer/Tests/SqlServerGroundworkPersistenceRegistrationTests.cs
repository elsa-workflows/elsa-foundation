using CShells;
using CShells.Features;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Scoping;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

using static Elsa.Persistence.Groundwork.RegistrationTests.GroundworkProviderRegistrationAssertions;

namespace Elsa.Persistence.Groundwork.SqlServer.Tests;

/// <summary>
/// Direct red tests for the SQL Server provider leaves introduced by Spec 094 T028. They deliberately name the
/// base and unified shell features before those production types exist so T028 has an executable registration
/// contract instead of copying another provider without proof.
/// </summary>
public sealed class SqlServerGroundworkPersistenceRegistrationTests
{
    private const string RegistrationSecret = "sqlserver-registration-secret";
    private const string ConnectionString =
        $"Server=localhost,1433;Database=elsa;User Id=sa;Password={RegistrationSecret};TrustServerCertificate=True";

    [Fact]
    public void Runtime_feature_enables_the_bounded_executable_cache_by_default()
    {
        var services = new ServiceCollection();
        new SqlServerGroundworkRuntimePersistenceShellFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<WorkflowExecutableCacheOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, options.Capacity);
    }

    [Theory]
    [InlineData(false, 19)]
    [InlineData(true, 23)]
    public void Runtime_feature_threads_executable_cache_settings(bool enabled, int capacity)
    {
        var services = new ServiceCollection();
        new SqlServerGroundworkRuntimePersistenceShellFeature
        {
            CacheWorkflowExecutables = enabled,
            WorkflowExecutableCacheCapacity = capacity
        }.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<WorkflowExecutableCacheOptions>();
        Assert.Equal(enabled, options.Enabled);
        Assert.Equal(capacity, options.Capacity);
    }

    [Fact]
    public void Runtime_feature_registers_builds_and_resolves_the_SQL_Server_startup_leaf()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var feature = new SqlServerGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = ConnectionString
        };
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        AssertStartupLeafRegistration<SqlServerGroundworkDocumentStoreInitializer>(services, RegistrationSecret);
        AssertRepresentativeFamilyContracts(services, typeof(IBookmarkStateStore));
        AssertRegistrationDiagnosticsAreSanitized(services, RegistrationSecret, ConnectionString);
        Assert.False(typeof(SqlServerGroundworkRuntimePersistenceShellFeature).IsSealed);
    }

    [Fact]
    public void Unified_feature_registers_the_five_remaining_legacy_provider_families()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var feature = new SqlServerGroundworkUnifiedPersistenceShellFeature(CreateBareShellContext())
        {
            ConnectionString = ConnectionString
        };
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        AssertStartupLeafRegistration<SqlServerGroundworkDocumentStoreInitializer>(services, RegistrationSecret);
        AssertRepresentativeFamilyContracts(
            services,
            typeof(IBookmarkStateStore),
            typeof(IExecutionPlacementStore),
            typeof(IWorkflowDefinitionStore),
            typeof(IActivityDefinitionStore),
            typeof(IPublicationRecordStore));
        AssertRegistrationDiagnosticsAreSanitized(services, RegistrationSecret, ConnectionString);
        Assert.False(typeof(SqlServerGroundworkUnifiedPersistenceShellFeature).IsSealed);
    }

    private static ShellFeatureContext CreateBareShellContext() =>
        new(new ShellSettings(new ShellId("sqlserver-registration"), ["GroundworkUnifiedPersistenceSqlServer"]), []);

    [Fact]
    public async Task Dispatch_physical_routes_fit_SQL_Server_index_limits_without_connecting()
    {
        var capabilityReport = SqlServerGroundworkCapabilities.Runtime();
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            capabilityReport,
            new GroundworkProviderTopologySnapshot(
                capabilityReport.Provider.Name,
                "sqlserver",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            SqlServerGroundworkCapabilities.PhysicalNames);

        var dispatchRoutes = source.PhysicalTarget.Routes
            .Where(route => route.StorageUnit.Value is
                ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind or
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind)
            .ToArray();
        var manifest = source.CreateManifest();
        var dispatchManifest = manifest with
        {
            StorageUnits = manifest.StorageUnits
                .Where(unit => dispatchRoutes.Any(route => route.StorageUnit == unit.Identity))
                .ToArray()
        };
        var store = new SqlServerPhysicalDocumentStore(
            ConnectionString,
            dispatchManifest,
            dispatchRoutes,
            DocumentStoreAccess.Global);

        Assert.Equal(2, dispatchRoutes.Length);
        Assert.NotNull(store);
    }

    [Theory]
    [InlineData(ElsaRuntimeStorageManifest.BookmarkStateDocumentKind)]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind)]
    public async Task Stimulus_lookup_routes_fit_SQL_Server_index_limits_without_connecting(string documentKind)
    {
        var capabilityReport = SqlServerGroundworkCapabilities.Runtime();
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            capabilityReport,
            new GroundworkProviderTopologySnapshot(
                capabilityReport.Provider.Name,
                "sqlserver",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            SqlServerGroundworkCapabilities.PhysicalNames);

        var routes = source.PhysicalTarget.Routes
            .Where(route => StringComparer.Ordinal.Equals(route.StorageUnit.Value, documentKind))
            .ToArray();
        var manifest = source.CreateManifest();
        var scopedManifest = manifest with
        {
            StorageUnits = manifest.StorageUnits
                .Where(unit => routes.Any(route => route.StorageUnit == unit.Identity))
                .ToArray()
        };
        var store = new SqlServerPhysicalDocumentStore(
            ConnectionString,
            scopedManifest,
            routes,
            DocumentStoreAccess.Global);

        Assert.Single(routes);
        Assert.NotNull(store);
    }

    [Fact]
    /// <summary>
    /// Two providers in one host is a supported topology since #1156; what is rejected is a second, different
    /// store CLAIMING AN ALREADY-DECLARED TARGET NAME. Both orders must fail identically, and the loser must
    /// leave no partial registration behind.
    /// </summary>
    public void Conflicting_relational_leaves_on_one_target_are_rejected_deterministically_in_both_orders()
    {
        var sqlServerFirst = new ServiceCollection();
        sqlServerFirst.AddSqlServerGroundworkDocumentStore(ConnectionString);
        var sqlServerThenSqlite = Assert.Throws<GroundworkTargetConflictException>(() =>
            sqlServerFirst.AddSqliteGroundworkDocumentStore("Data Source=:memory:"));

        var sqliteFirst = new ServiceCollection();
        sqliteFirst.AddSqliteGroundworkDocumentStore("Data Source=:memory:");
        var sqliteThenSqlServer = Assert.Throws<GroundworkTargetConflictException>(() =>
            sqliteFirst.AddSqlServerGroundworkDocumentStore(ConnectionString));

        foreach (var conflict in new[] { sqlServerThenSqlite, sqliteThenSqlServer })
        {
            Assert.Contains(
                $"Groundwork target '{GroundworkTargetNames.Default}' was declared twice against different stores:",
                conflict.Message,
                StringComparison.Ordinal);
            Assert.Contains("provider 'sqlite'", conflict.Message, StringComparison.Ordinal);
            Assert.Contains("provider 'sqlserver'", conflict.Message, StringComparison.Ordinal);
            // The diagnostic identifies each store without ever quoting a connection string.
            Assert.Contains("connection fingerprint", conflict.Message, StringComparison.Ordinal);
        }

        Assert.Single(sqlServerFirst, descriptor =>
            descriptor.ServiceType == typeof(SqlServerGroundworkDocumentStoreInitializer));
        Assert.DoesNotContain(sqlServerFirst, descriptor =>
            descriptor.ServiceType == typeof(SqliteGroundworkDocumentStoreInitializer));
        Assert.Single(sqliteFirst, descriptor =>
            descriptor.ServiceType == typeof(SqliteGroundworkDocumentStoreInitializer));
        Assert.DoesNotContain(sqliteFirst, descriptor =>
            descriptor.ServiceType == typeof(SqlServerGroundworkDocumentStoreInitializer));
    }

    [Fact]
    /// <summary>The capability the conflict above deliberately does not block: two providers, two targets.</summary>
    public void Two_relational_leaves_on_distinct_targets_are_accepted()
    {
        var services = new ServiceCollection();
        services.AddSqlServerGroundworkDocumentStore(ConnectionString);
        services.AddSqliteGroundworkDocumentStore("Data Source=:memory:", targetName: "authoring");

        Assert.Equal(
            ["authoring", GroundworkTargetNames.Default],
            services.GroundworkTargets().TargetNames);
    }

    [Fact]
    public void SQL_Server_history_paging_has_no_provider_specific_Elsa_source()
    {
        Assert.False(File.Exists(Path.Combine(
            FindRepositoryRoot(),
            "src/Elsa/Persistence/Groundwork/SqlServer/SqlServerWorkflowExecutionStatePageQuery.cs")));
    }

    [Fact]
    public async Task SQL_Server_history_query_uses_the_transformed_common_physical_route()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new GroundworkStorageNamingPolicyOptions(
            "sqlserver-history-transform-v1",
            context => $"transformed_{context.FeatureDefaultLogicalName}"));
        new SqlServerGroundworkRuntimePersistenceShellFeature { ConnectionString = ConnectionString }
            .ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var capabilityReport = SqlServerGroundworkCapabilities.Runtime();
        var composition = await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                GroundworkProviderCapabilitySnapshot.ForFeatureRoutes(
                    capabilityReport,
                    new GroundworkProviderTopologySnapshot(
                        capabilityReport.Provider.Name,
                        "sqlserver",
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                        }),
                    RuntimeGroundworkStorageManifestSource.FeatureName,
                    [RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement()]),
                SqlServerGroundworkCapabilities.PhysicalNames);
        var route = Assert.Single(composition.PhysicalTarget.Routes.Where(candidate =>
            candidate.StorageUnit.Value == ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));
        var historyPath = Assert.Single(route.CandidateQueryPaths.Where(candidate =>
            candidate.QueryIdentities.Contains(
                ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery,
                StringComparer.Ordinal)));
        var historyIndex = Assert.Single(route.Indexes.Where(candidate =>
            candidate.Identity == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex));

        Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, route.Form);
        Assert.NotEqual("groundwork_documents", route.PrimaryStorage.Name.Identifier);
        Assert.NotEqual("content_json", route.Envelope.CanonicalJson.Identifier);
        Assert.True(historyPath.IsScaleBearing);
        Assert.Equal(historyIndex.Name, historyPath.IndexName);
        Assert.Contains(route.ProjectedColumns, projection =>
            projection.Definition.Path ==
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField);
    }

    [Fact]
    public void Production_initializer_is_admission_only_and_constructs_the_exact_physical_store()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src/Elsa/Persistence/Groundwork/SqlServer/SqlServerGroundworkDocumentStoreInitializer.cs"));

        Assert.Contains("InspectRuntimeAdmissionAsync", source, StringComparison.Ordinal);
        Assert.Contains(nameof(global::Groundwork.SqlServer.Documents.SqlServerPhysicalDocumentStore), source, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalSchemaApplication.ApplyAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlServerDocumentStoreFactory.CreateAsync", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

}
