using CShells;
using CShells.Features;
using Elsa.Persistence.Groundwork.MongoDb.Unified;
using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.Unified;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

public sealed class GroundworkReferenceDeploymentSchemaSelectorTests
{
    [Fact]
    public void Reference_selector_has_one_clean_break_schema()
    {
        Assert.Equal(
            typeof(GroundworkAllFeaturesDeploymentSchema),
            GroundworkReferenceDeploymentSchemaSelector.Select(Context([], [])));
    }

    [Fact]
    public void Complete_reference_schema_passes_the_sql_server_physical_store_validator_without_io()
    {
        var manifest = new GroundworkAllFeaturesDeploymentSchema().CreateManifest();
        var resolution = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            SqlServerGroundworkCapabilities.PhysicalNames);
        Assert.True(resolution.IsValid, string.Join("; ", resolution.Diagnostics.Select(x => x.Message)));
        var compilation = ExecutableStorageRouteCompiler.Compile(resolution.Definitions);
        Assert.True(compilation.IsValid, string.Join("; ", compilation.Diagnostics.Select(x => x.Message)));

        var store = new SqlServerPhysicalDocumentStore(
            "Server=localhost;Database=groundwork;Integrated Security=true;Encrypt=False",
            manifest,
            compilation.Routes,
            DocumentStoreAccess.Scoped(new StorageScope("sql-server-reference-validator")));

        Assert.Equal(new StorageScope("sql-server-reference-validator"), store.Access.Scope);
    }

    [Fact]
    public void All_unified_provider_features_select_the_single_clean_break_schema()
    {
        var context = Context([], []);
        var providers = new Action<IServiceCollection>[]
        {
            services => new SqliteGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "Data Source=:memory:"
            }.ConfigureServices(services),
            services => new PostgreSqlGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "Host=localhost;Database=elsa;Username=elsa;Password=secret"
            }.ConfigureServices(services),
            services => new SqlServerGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "Server=localhost;Database=elsa;User Id=sa;Password=secret;TrustServerCertificate=True"
            }.ConfigureServices(services),
            services => new MongoDbGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "mongodb://localhost:27017/?replicaSet=rs0",
                DatabaseName = "elsa"
            }.ConfigureServices(services)
        };

        foreach (var register in providers)
        {
            var services = new ServiceCollection();
            register(services);
            using var provider = services.BuildServiceProvider();

            Assert.IsType<GroundworkAllFeaturesDeploymentSchema>(
                provider.GetRequiredService<IPhysicalSchemaManifestSource>());
        }
    }

    private static ShellFeatureContext Context(
        IReadOnlyList<string> enabled,
        IEnumerable<ShellFeatureDescriptor> descriptors) =>
        new(new ShellSettings(new ShellId("schema-selection"), enabled), descriptors);

}
