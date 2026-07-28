using System.Reflection;
using CShells;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork;
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
using Groundwork.Core.SchemaEvolution;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

public sealed class GroundworkReferenceDeploymentSchemaSelectorTests
{
    private const string IdentityFeatureId = "FoundationIdentityAspNetCoreIdentityGroundwork";
    private const string DiagnosticsFeatureId = "DiagnosticsGroundworkPersistence";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identity_selection_is_order_independent(bool identityDescriptorFirst)
    {
        var identity = SchemaFeature(IdentityFeatureId, GroundworkSchemaFeatureMetadata.Identity);
        var ordinary = new ShellFeatureDescriptor("OrdinaryFeature");
        var descriptors = identityDescriptorFirst
            ? new[] { identity, ordinary }
            : new[] { ordinary, identity };
        var enabled = identityDescriptorFirst
            ? new[] { "OrdinaryFeature", IdentityFeatureId }
            : new[] { IdentityFeatureId, "OrdinaryFeature" };

        var selected = GroundworkReferenceDeploymentSchemaSelector.Select(Context(enabled, descriptors));

        Assert.Equal(typeof(GroundworkAllFeaturesWithIdentityDeploymentSchema), selected);
    }

    [Fact]
    public void Disabled_or_non_enabled_schema_features_do_not_affect_selection()
    {
        var context = Context(
            ["OrdinaryFeature"],
            [
                new ShellFeatureDescriptor("OrdinaryFeature"),
                SchemaFeature(IdentityFeatureId, GroundworkSchemaFeatureMetadata.Identity),
                SchemaFeature("FutureGroundworkFeature", "elsa-future")
            ]);

        Assert.Equal(
            typeof(GroundworkAllFeaturesDeploymentSchema),
            GroundworkReferenceDeploymentSchemaSelector.Select(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Diagnostics_selection_is_order_independent(bool diagnosticsDescriptorFirst)
    {
        var diagnostics = SchemaFeature(DiagnosticsFeatureId, GroundworkSchemaFeatureMetadata.Diagnostics);
        var ordinary = new ShellFeatureDescriptor("OrdinaryFeature");
        var descriptors = diagnosticsDescriptorFirst
            ? new[] { diagnostics, ordinary }
            : new[] { ordinary, diagnostics };
        var enabled = diagnosticsDescriptorFirst
            ? new[] { "OrdinaryFeature", DiagnosticsFeatureId }
            : new[] { DiagnosticsFeatureId, "OrdinaryFeature" };

        var selected = GroundworkReferenceDeploymentSchemaSelector.Select(Context(enabled, descriptors));

        Assert.Equal(typeof(GroundworkAllFeaturesWithDiagnosticsDeploymentSchema), selected);
    }

    [Fact]
    public void Identity_and_diagnostics_select_the_combined_schema()
    {
        var selected = GroundworkReferenceDeploymentSchemaSelector.Select(Context(
            [DiagnosticsFeatureId, IdentityFeatureId],
            [
                SchemaFeature(IdentityFeatureId, GroundworkSchemaFeatureMetadata.Identity),
                SchemaFeature(DiagnosticsFeatureId, GroundworkSchemaFeatureMetadata.Diagnostics)
            ]));

        Assert.Equal(typeof(GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema), selected);
    }

    [Fact]
    public void Enabled_unknown_schema_marker_fails_before_provider_registration()
    {
        var context = Context(
            ["FutureGroundworkFeature"],
            [SchemaFeature("FutureGroundworkFeature", "elsa-future")]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GroundworkReferenceDeploymentSchemaSelector.Select(context));

        Assert.Contains("FutureGroundworkFeature", exception.Message, StringComparison.Ordinal);
        Assert.Contains("elsa-future", exception.Message, StringComparison.Ordinal);
        Assert.Contains(GroundworkSchemaFeatureMetadata.Key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_shell_features_publish_the_well_known_schema_marker()
    {
        AssertSchemaMarker<AspNetCoreIdentityGroundworkFeature>(GroundworkSchemaFeatureMetadata.Identity);
        AssertSchemaMarker<IdentityGroundworkPersistenceFeature>(GroundworkSchemaFeatureMetadata.Identity);
    }

    [Fact]
    public void Diagnostics_shell_feature_publishes_the_well_known_schema_marker()
    {
        AssertSchemaMarker<DiagnosticsGroundworkPersistenceFeature>(GroundworkSchemaFeatureMetadata.Diagnostics);
    }

    [Fact]
    public void All_unified_provider_features_select_identity_from_the_same_shell_context()
    {
        var context = IdentityContext();
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

            Assert.IsType<GroundworkAllFeaturesWithIdentityDeploymentSchema>(
                provider.GetRequiredService<IPhysicalSchemaManifestSource>());
        }
    }

    [Fact]
    public void Bare_unified_provider_features_select_the_default_schema_and_do_not_register_identity()
    {
        var context = Context([], [SchemaFeature(IdentityFeatureId, GroundworkSchemaFeatureMetadata.Identity)]);
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
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IUserStore));
        }
    }

    [Fact]
    public async Task All_unified_provider_features_register_diagnostic_runtime_and_deployment_boundaries()
    {
        var context = Context(
            [DiagnosticsFeatureId],
            [SchemaFeature(DiagnosticsFeatureId, GroundworkSchemaFeatureMetadata.Diagnostics)]);
        var providers = new (string Provider, Action<IServiceCollection> Register)[]
        {
            ("sqlite", services => new SqliteGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "Data Source=:memory:"
            }.ConfigureServices(services)),
            ("postgresql", services => new PostgreSqlGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "Host=localhost;Database=elsa;Username=elsa;Password=secret"
            }.ConfigureServices(services)),
            ("sqlserver", services => new SqlServerGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "Server=localhost;Database=elsa;User Id=sa;Password=secret;TrustServerCertificate=True"
            }.ConfigureServices(services)),
            ("mongodb", services => new MongoDbGroundworkUnifiedPersistenceShellFeature(context)
            {
                ConnectionString = "mongodb://localhost:27017/?replicaSet=rs0",
                DatabaseName = "elsa"
            }.ConfigureServices(services))
        };

        foreach (var (expectedProvider, register) in providers)
        {
            var services = new ServiceCollection();
            register(services);
            await using var provider = services.BuildServiceProvider();

            Assert.IsType<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(
                provider.GetRequiredService<IPhysicalSchemaManifestSource>());
            Assert.NotNull(provider.GetRequiredService<IDiagnosticRecordStoreSessionFactory>());
            Assert.Equal(
                expectedProvider,
                provider.GetRequiredService<IDiagnosticRecordDeploymentApplier>().Provider);
            var initializer = provider.GetRequiredService<GroundworkDiagnosticRecordDeploymentInitializer>();
            Assert.Same(
                initializer,
                Assert.Single(provider.GetServices<IShellInitializer>()
                    .OfType<GroundworkDiagnosticRecordDeploymentInitializer>()));
            var lifecycle = Assert.Single(
                provider.GetServices<ShellInitializerRegistration>(),
                registration =>
                    registration.InitializerType == typeof(GroundworkDiagnosticRecordDeploymentInitializer));
            Assert.Equal(LifecyclePhase.Prepare, lifecycle.Phase);
            Assert.Equal(1, lifecycle.Order);
        }
    }

    [Fact]
    public void All_explicit_diagnostics_provider_overloads_register_the_deployment_manifest_source()
    {
        var providers = new Action<IServiceCollection>[]
        {
            services => services.AddGroundworkSqliteUnifiedPersistence<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(
                "Data Source=:memory:"),
            services => services.AddGroundworkPostgreSqlUnifiedPersistence<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(
                "Host=localhost;Database=elsa;Username=elsa;Password=secret"),
            services => services.AddGroundworkSqlServerUnifiedPersistence<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(
                "Server=localhost;Database=elsa;User Id=sa;Password=secret;TrustServerCertificate=True"),
            services => services.AddGroundworkMongoDbUnifiedPersistence<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(
                "mongodb://localhost:27017/?replicaSet=rs0",
                "elsa")
        };

        foreach (var register in providers)
        {
            var services = new ServiceCollection();
            register(services);
            using var provider = services.BuildServiceProvider();

            var selected = provider.GetRequiredService<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>();
            Assert.Same(
                selected,
                provider.GetRequiredService<global::Groundwork.DiagnosticRecords.IDiagnosticRecordDeploymentManifestSource>());
        }
    }

    private static ShellFeatureContext IdentityContext() =>
        Context(
            [IdentityFeatureId],
            [SchemaFeature(IdentityFeatureId, GroundworkSchemaFeatureMetadata.Identity)]);

    private static ShellFeatureContext Context(
        IReadOnlyList<string> enabled,
        IEnumerable<ShellFeatureDescriptor> descriptors) =>
        new(new ShellSettings(new ShellId("schema-selection"), enabled), descriptors);

    private static ShellFeatureDescriptor SchemaFeature(string id, object marker) =>
        new(id)
        {
            Metadata = new Dictionary<string, object>
            {
                [GroundworkSchemaFeatureMetadata.Key] = marker
            }
        };

    private static void AssertSchemaMarker<TFeature>(string expectedMarker)
    {
        var attribute = typeof(TFeature).GetCustomAttribute<ShellFeatureAttribute>();
        Assert.NotNull(attribute);

        var metadata = attribute.Metadata
            .Chunk(2)
            .ToDictionary(pair => Assert.IsType<string>(pair[0]), pair => pair[1]);
        Assert.Equal(expectedMarker, metadata[GroundworkSchemaFeatureMetadata.Key]);
    }
}
