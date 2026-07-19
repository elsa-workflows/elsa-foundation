using CShells;
using CShells.Features;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
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
    public void Unified_feature_registers_the_seven_provider_families_without_selecting_identity()
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
            typeof(ISecretRepository),
            typeof(IExecutionPlacementStore),
            typeof(IWorkflowDefinitionStore),
            typeof(IActivityDefinitionStore),
            typeof(IPublicationRecordStore),
            typeof(IStudioPreferenceStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IUserStore));
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
    public async Task Explicit_identity_schema_and_feature_register_the_matching_SQL_Server_composition()
    {
        var services = new ServiceCollection();
        services.AddGroundworkSqlServerUnifiedPersistence<GroundworkAllFeaturesWithIdentityDeploymentSchema>(
            ConnectionString);
        services.AddFoundationAspNetCoreIdentityGroundwork();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserStore>());
        Assert.IsType<GroundworkAllFeaturesWithIdentityDeploymentSchema>(
            provider.GetRequiredService<global::Groundwork.Core.SchemaEvolution.IPhysicalSchemaManifestSource>());
    }

    [Fact]
    public async Task Identity_only_provider_leaf_resolves_without_runtime_history_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundworkIdentityStores();
        services.AddSqlServerGroundworkDocumentStore(ConnectionString);

        await using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IGroundworkRuntimeDocumentSerializer>());
        Assert.NotNull(provider.GetRequiredService<SqlServerGroundworkDocumentStoreInitializer>());
        Assert.NotNull(Assert.Single(provider.GetServices<IHostedService>()
            .OfType<SqlServerGroundworkDocumentStoreInitializer>()));
        Assert.NotNull(Assert.Single(provider.GetServices<CShells.Lifecycle.IShellInitializer>()
            .OfType<SqlServerGroundworkDocumentStoreInitializer>()));
    }

    [Fact]
    public void Conflicting_relational_provider_leaves_are_rejected_deterministically_in_both_orders()
    {
        var sqlServerFirst = new ServiceCollection();
        sqlServerFirst.AddSqlServerGroundworkDocumentStore(ConnectionString);
        var sqlServerThenSqlite = Assert.Throws<InvalidOperationException>(() =>
            sqlServerFirst.AddSqliteGroundworkDocumentStore("Data Source=:memory:"));

        var sqliteFirst = new ServiceCollection();
        sqliteFirst.AddSqliteGroundworkDocumentStore("Data Source=:memory:");
        var sqliteThenSqlServer = Assert.Throws<InvalidOperationException>(() =>
            sqliteFirst.AddSqlServerGroundworkDocumentStore(ConnectionString));

        Assert.Equal(sqlServerThenSqlite.Message, sqliteThenSqlServer.Message);
        Assert.Contains("'sqlite', 'sqlserver'", sqlServerThenSqlite.Message, StringComparison.Ordinal);
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
