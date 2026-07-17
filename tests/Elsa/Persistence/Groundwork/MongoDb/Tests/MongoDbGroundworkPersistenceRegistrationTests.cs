using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.MongoDb.Unified;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Documents.Scoping;
using Microsoft.Extensions.Logging;
using Groundwork.MongoDb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using static Elsa.Persistence.Groundwork.RegistrationTests.GroundworkProviderRegistrationAssertions;

namespace Elsa.Persistence.Groundwork.MongoDb.Tests;

/// <summary>
/// Direct red tests for the MongoDB provider leaves introduced by Spec 094 T029. The configured URI names a replica
/// set because the selected families include atomic multi-document behavior; store exposure remains gated behind the
/// startup initializer, where T029 must perform topology admission before serving work.
/// </summary>
public sealed class MongoDbGroundworkPersistenceRegistrationTests
{
    private const string RegistrationSecret = "mongodb-registration-secret";
    private const string ConnectionString =
        $"mongodb://elsa:{RegistrationSecret}@localhost:27017/?replicaSet=rs0";
    private const string DatabaseName = "elsa_registration";

    [Fact]
    public void Runtime_feature_registers_a_replica_set_gated_startup_leaf_without_exposing_credentials()
    {
        var services = new ServiceCollection();
        new MongoDbGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName
        }.ConfigureServices(services);

        Assert.Contains("replicaSet=rs0", ConnectionString, StringComparison.Ordinal);
        AssertStartupLeafRegistration<MongoDbGroundworkDocumentStoreInitializer>(services, RegistrationSecret);
        AssertRepresentativeFamilyContracts(services, typeof(IBookmarkStateStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkWorkflowExecutionStatePageQuery));
        AssertRegistrationDiagnosticsAreSanitized(services, RegistrationSecret, ConnectionString);
    }

    [Fact]
    public void Unified_feature_registers_builds_and_resolves_the_selected_seven_family_startup_leaf()
    {
        var services = new ServiceCollection();
        new MongoDbGroundworkUnifiedPersistenceShellFeature
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName
        }.ConfigureServices(services);

        AssertStartupLeafRegistration<MongoDbGroundworkDocumentStoreInitializer>(services, RegistrationSecret);
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
    }

    [Fact]
    public void Repeated_runtime_provider_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        var feature = new MongoDbGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName
        };

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        AssertStartupLeafRegistration<MongoDbGroundworkDocumentStoreInitializer>(services, RegistrationSecret);
        AssertRegistrationDiagnosticsAreSanitized(services, RegistrationSecret, ConnectionString);
    }

    [Fact]
    public async Task Startup_publishes_the_selected_store_once_only_after_topology_and_schema_admission()
    {
        var services = new ServiceCollection();
        new MongoDbGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName
        }.ConfigureServices(services);
        var admission = new RecordingRuntimeAdmission();
        services.Replace(ServiceDescriptor.Singleton<IMongoDbGroundworkRuntimeAdmission>(admission));

        var provider = services.BuildServiceProvider();
        try
        {
            Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
            var initializer = provider.GetRequiredService<MongoDbGroundworkDocumentStoreInitializer>();

            await initializer.InitializeAsync();

            var resources = await provider.GetRequiredService<GroundworkStoreSessionSource>()
                .OpenAsync(GroundworkTestAccess.DefaultScoped);
            var globalResources = await provider.GetRequiredService<GroundworkStoreSessionSource>()
                .OpenAsync(DocumentStoreAccess.Global);
            Assert.NotSame(resources.DocumentStore, globalResources.DocumentStore);
            Assert.Equal(GroundworkTestAccess.DefaultScoped, resources.DocumentStore.Access);
            Assert.Equal(DocumentStoreAccess.Global, globalResources.DocumentStore.Access);
            Assert.Null(resources.Lease);
            Assert.Null(globalResources.Lease);
            Assert.Equal(1, admission.TopologyInspections);
            Assert.Equal(1, admission.OpenAttempts);
            Assert.Equal(
                [GroundworkTestAccess.DefaultScoped, DocumentStoreAccess.Global],
                admission.Accesses);
            Assert.Equal(MongoDbGroundworkCapabilities.Provider, admission.Source!.PhysicalTarget.Provider);
            Assert.Equal(["elsa-workflows-runtime"], admission.Source.Snapshot.SelectedFeatures);
            Assert.Contains(WellKnownCapabilities.AtomicCommit, admission.Source.Snapshot.RequiredCapabilities);
            AssertExactMongoTarget(admission.Source);

            await initializer.StartAsync(CancellationToken.None);
            Assert.Equal(1, admission.TopologyInspections);
            Assert.Equal(1, admission.OpenAttempts);
        }
        finally
        {
            await provider.DisposeAsync();
        }

        Assert.True(admission.Handle.IsDisposed);
    }

    [Fact]
    public async Task Unified_shell_standalone_composes_all_seven_families_into_one_exact_MongoDB_target()
    {
        var services = new ServiceCollection();
        new MongoDbGroundworkUnifiedPersistenceShellFeature
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName
        }.ConfigureServices(services);
        var admission = new RecordingRuntimeAdmission();
        services.Replace(ServiceDescriptor.Singleton<IMongoDbGroundworkRuntimeAdmission>(admission));

        var provider = services.BuildServiceProvider();
        try
        {
            await provider.GetRequiredService<MongoDbGroundworkDocumentStoreInitializer>()
                .InitializeAsync();

            Assert.Equal(
            [
                "elsa-activities-design",
                "elsa-identity",
                "elsa-secrets",
                "elsa-workflows-design",
                "elsa-workflows-publishing",
                "elsa-workflows-runtime",
                "elsa-workflows-runtime-distributed"
            ], admission.Source!.Snapshot.SelectedFeatures);
            AssertExactMongoTarget(admission.Source);
            var resources = await provider.GetRequiredService<GroundworkStoreSessionSource>()
                .OpenAsync(GroundworkTestAccess.DefaultScoped);
            Assert.Same(Assert.Single(admission.Stores), resources.DocumentStore);
        }
        finally
        {
            await provider.DisposeAsync();
        }

        Assert.True(admission.Handle.IsDisposed);
    }

    [Fact]
    public async Task Topology_admission_rejects_a_connection_without_replica_set_intent_and_suppresses_credentials()
    {
        const string secret = "standalone-topology-secret";
        var connectionString = $"mongodb://elsa:{secret}@localhost:27017";
        var admission = new MongoDbGroundworkRuntimeAdmission();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admission.InspectReplicaSetAsync(connectionString, DatabaseName, CancellationToken.None).AsTask());

        Assert.Contains("writable replica set", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class RecordingRuntimeAdmission : IMongoDbGroundworkRuntimeAdmission
    {
        public int TopologyInspections { get; private set; }

        public int OpenAttempts { get; private set; }

        public GroundworkPhysicalSchemaManifestSource? Source { get; private set; }

        public List<DocumentStoreAccess> Accesses { get; } = [];

        public List<IDocumentStore> Stores { get; } = [];

        public RecordingHandle Handle { get; } = new();

        public ValueTask<GroundworkProviderTopologySnapshot> InspectReplicaSetAsync(
            string connectionString,
            string databaseName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TopologyInspections++;
            return ValueTask.FromResult(new GroundworkProviderTopologySnapshot(
                "groundwork-mongodb",
                "transaction-capable-replica-set",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "multi-document-transactions",
                    "transaction-capable-mongodb",
                    "transaction-capable-replica-set"
                }));
        }

        public ValueTask OpenAndPublishAsync(
            string connectionString,
            string databaseName,
            GroundworkPhysicalSchemaManifestSource source,
            GroundworkStoreSessionSource sessionSource,
            bool autoApplyOnStartup,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenAttempts++;
            Source = source;
            sessionSource.TrySet(
                (access, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var store = new InMemoryDocumentStore(source.CreateManifest(), access);
                    Accesses.Add(access);
                    Stores.Add(store);
                    return ValueTask.FromResult(new GroundworkStoreSessionResources(store, store));
                },
                Handle);
            return ValueTask.CompletedTask;
        }
    }

    private static void AssertExactMongoTarget(GroundworkPhysicalSchemaManifestSource source)
    {
        var compiled = MongoDbPhysicalStorageModel.Compile(
            source.CreateManifest(),
            source.PhysicalTarget.Provider,
            source.CreateNamePolicy());

        Assert.Equal(source.TargetFingerprint, compiled.Target.Fingerprint);
        Assert.Equal(source.PhysicalTarget.ManifestIdentity, compiled.Target.ManifestIdentity);
        Assert.Equal(source.PhysicalTarget.ManifestVersion, compiled.Target.ManifestVersion);
        Assert.Equal(source.PhysicalTarget.Provider, compiled.Target.Provider);
    }

    private sealed class RecordingHandle : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
