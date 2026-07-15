using System.Reflection;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public void Unified_feature_registers_builds_and_resolves_the_selected_seven_family_startup_leaf()
    {
        var services = new ServiceCollection();
        var feature = new SqlServerGroundworkUnifiedPersistenceShellFeature
        {
            ConnectionString = ConnectionString
        };
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        AssertStartupLeafRegistration<SqlServerGroundworkDocumentStoreInitializer>(services, RegistrationSecret);
        AssertRepresentativeFamilyContracts(
            services,
            typeof(IBookmarkStateStore),
            typeof(IUserStore),
            typeof(ISecretRepository),
            typeof(IExecutionPlacementStore),
            typeof(IWorkflowDefinitionStore),
            typeof(IActivityDefinitionStore),
            typeof(IPublicationRecordStore));
        AssertRegistrationDiagnosticsAreSanitized(services, RegistrationSecret, ConnectionString);
        Assert.False(typeof(SqlServerGroundworkUnifiedPersistenceShellFeature).IsSealed);
    }

    [Fact]
    public async Task Identity_only_provider_leaf_resolves_without_runtime_history_services()
    {
        var services = new ServiceCollection();
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
    public void SQL_Server_history_query_uses_bounded_TOP_syntax_and_no_LIMIT_clause()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src/Elsa/Persistence/Groundwork/SqlServer/SqlServerWorkflowExecutionStatePageQuery.cs"));

        Assert.Contains("TOP (@pageLimit)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n        LIMIT ", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SQL_Server_history_query_uses_the_exact_transformed_admitted_route_names()
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
                new GroundworkProviderCapabilitySnapshot(
                    capabilityReport,
                    new GroundworkProviderTopologySnapshot(
                        capabilityReport.Provider.Name,
                        "sqlserver",
                        new HashSet<string>()),
                    []),
                SqlServerGroundworkCapabilities.PhysicalNames);
        var route = Assert.Single(composition.PhysicalTarget.Routes.Where(candidate =>
            candidate.StorageUnit.Value == ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));
        var query = provider.GetRequiredService<IGroundworkWorkflowExecutionStatePageQuery>();
        query.Bind(route);

        var pageTable = ReadProtectedString(query, "PageTableExpression");
        var pageColumns = ReadProtectedString(query, "PageSelectColumns");
        var canonicalJson = ReadProtectedString(query, "CanonicalJsonExpression");
        var pageSql = (string)query.GetType()
            .GetMethod("BuildPageSelectSql", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(query, ["1 = 1", "1 ASC"])!;

        Assert.NotEqual("groundwork_documents", route.PrimaryStorage.Name.Identifier);
        Assert.NotEqual("content_json", route.Envelope.CanonicalJson.Identifier);
        Assert.Contains($"[{route.PrimaryStorage.Name.Identifier}] d", pageTable, StringComparison.Ordinal);
        Assert.Contains($"[{route.Envelope.Id.Identifier}]", pageColumns, StringComparison.Ordinal);
        Assert.Contains($"[{route.Envelope.SchemaVersion.Identifier}]", pageColumns, StringComparison.Ordinal);
        Assert.Contains($"[{route.Envelope.Version.Identifier}]", pageColumns, StringComparison.Ordinal);
        Assert.Contains($"[{route.Envelope.CanonicalJson.Identifier}]", pageColumns, StringComparison.Ordinal);
        Assert.Contains($"[{route.Envelope.CanonicalJson.Identifier}]", canonicalJson, StringComparison.Ordinal);
        Assert.Contains(pageTable, pageSql, StringComparison.Ordinal);
        Assert.Contains(pageColumns, pageSql, StringComparison.Ordinal);
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

    private static string ReadProtectedString(object instance, string propertyName)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null)
                return (string)property.GetValue(instance)!;
        }

        throw new MissingMemberException(instance.GetType().FullName, propertyName);
    }
}
