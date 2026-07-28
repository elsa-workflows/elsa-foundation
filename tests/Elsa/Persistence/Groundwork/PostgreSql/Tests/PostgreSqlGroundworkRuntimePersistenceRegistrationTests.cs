using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using global::Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.Tests;

/// <summary>
/// Docker-free registration tests for <see cref="PostgreSqlGroundworkRuntimePersistenceShellFeature"/>. These
/// assert the feature's DI surface by inspecting the <see cref="IServiceCollection"/> descriptors <b>without</b>
/// resolving them, so no PostgreSQL connection is opened. The live end-to-end behaviour is covered by the
/// Testcontainers integration tests.
/// </summary>
public sealed class PostgreSqlGroundworkRuntimePersistenceRegistrationTests
{
    private const string CacheEnabledProperty = "CacheWorkflowExecutables";
    private const string CacheCapacityProperty = "WorkflowExecutableCacheCapacity";
    private const string OptionsTypeName = "Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableCacheOptions";

    private static ServiceCollection ConfiguredServices()
    {
        var services = new ServiceCollection();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres"
        }.ConfigureServices(services);
        return services;
    }

    [Fact]
    public void Feature_registers_a_single_document_store_and_its_startup_initializer()
    {
        var services = ConfiguredServices();

        // IDocumentStore is scoped and opens immutable access-bound provider sessions from the static source
        // published at startup by the initializer (registered as both hosted and shell initializer).
        Assert.Single(services, d => d.ServiceType == typeof(IDocumentStore));
        Assert.Single(services, d => d.ServiceType == typeof(IBoundedDocumentStore));
        Assert.Single(services, d => d.ServiceType == typeof(GroundworkStoreSessionSource));
        Assert.Single(services, d => d.ServiceType == typeof(PostgreSqlGroundworkDocumentStoreInitializer));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(services, d => d.ServiceType == typeof(IShellInitializer));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(ShellInitializerRegistration)
            && d.ImplementationInstance is ShellInitializerRegistration
            {
                InitializerType: var initializerType,
                Phase: LifecyclePhase.Prepare,
                RegistrationIndex: -1
            }
            && initializerType == typeof(PostgreSqlGroundworkDocumentStoreInitializer));
    }

    [Fact]
    public void Feature_swaps_the_runtime_store_seams_over_to_the_groundwork_bridges()
    {
        var services = ConfiguredServices();

        AssertBridge<IBookmarkStateStore, GroundworkBookmarkStateStore>(services);
        var selectedExecutableDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) && !descriptor.IsKeyedService);
        Assert.NotNull(selectedExecutableDescriptor.ImplementationFactory);
        var cacheOptions = Assert.IsType<WorkflowExecutableCacheOptions>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableCacheOptions)).ImplementationInstance);
        Assert.False(cacheOptions.Enabled);
        var providerDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore)
            && descriptor.ServiceKey?.Equals(GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey) == true);
        Assert.Equal(typeof(GroundworkWorkflowExecutableStore), providerDescriptor.KeyedImplementationType);
        AssertBridge<IActivityExecutionStateStore, GroundworkActivityExecutionStateStore>(services);
        AssertBridge<IWorkflowExecutionStateStore, GroundworkWorkflowExecutionStateStore>(services);
        AssertBridge<IDurableValueStateStore, GroundworkDurableValueStateStore>(services);
        AssertBridge<ISchedulerStateStore, GroundworkSchedulerStateStore>(services);
        AssertBridge<IExecutionLivenessStateStore, GroundworkExecutionLivenessStateStore>(services);
        AssertBridge<IWorkflowHoldStateStore, GroundworkWorkflowHoldStateStore>(services);
        AssertBridge<IIncidentStateStore, GroundworkIncidentStateStore>(services);
        AssertBridge<IRuntimeCheckpointCommitStore, GroundworkRuntimeCheckpointWriter>(services);
        AssertBridge<IWorkflowSchedulerWorkQueue, GroundworkWorkflowSchedulerWorkQueue>(services);
        AssertBridge<IDurableTimerStore, GroundworkDurableTimerStore>(services);
        AssertBridge<IWorkflowTriggerBindingStore, GroundworkWorkflowTriggerBindingStore>(services);
    }

    [Fact]
    public void Blank_connection_string_falls_back_to_the_feature_default()
    {
        var services = new ServiceCollection();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = "   " }.ConfigureServices(services);

        // The feature still wires a document store; the default connection string is used internally.
        Assert.Single(services, d => d.ServiceType == typeof(IDocumentStore));
        Assert.False(string.IsNullOrWhiteSpace(PostgreSqlGroundworkRuntimePersistenceShellFeature.DefaultConnectionString));
    }

    [Fact]
    public void Executable_cache_settings_are_public_manifest_settings_with_durable_defaults()
    {
        var feature = new PostgreSqlGroundworkRuntimePersistenceShellFeature();

        AssertSetting(feature, CacheEnabledProperty, expectedDefault: false);
        AssertSetting(feature, CacheCapacityProperty, expectedDefault: 256);
    }

    [Fact]
    public void Configured_executable_cache_capacity_is_threaded_to_runtime_registration()
    {
        var feature = new PostgreSqlGroundworkRuntimePersistenceShellFeature();
        SetSetting(feature, CacheEnabledProperty, true);
        SetSetting(feature, CacheCapacityProperty, 19);
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var optionsType = typeof(IWorkflowExecutableStore).Assembly.GetType(OptionsTypeName);
        Assert.NotNull(optionsType);
        var options = provider.GetService(optionsType!);
        Assert.NotNull(options);
        var capacity = options!.GetType().GetProperty("Capacity");
        Assert.NotNull(capacity);
        Assert.Equal(19, capacity!.GetValue(options));
    }

    [Fact]
    public void Provider_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        var feature = new PostgreSqlGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = "Host=localhost;Database=elsa;Username=postgres;Password=secret"
        };

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStoreSessionSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBoundedDocumentStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(PostgreSqlGroundworkDocumentStoreInitializer));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        // Two distinct shell initializers are expected: the document-store initializer (Prepare)
        // and the schema readiness guard (Start). Idempotency means re-registration adds no third.
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkSchemaReadinessTask));
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IShellInitializer)));
    }

    [Fact]
    public async Task Runtime_initialization_suppresses_connection_details()
    {
        const string secret = "postgresql-admission-secret";
        var connectionString = $"Host=localhost;Port={secret};Database=elsa;Username=postgres;Password={secret}";
        var services = new ServiceCollection();
        services.AddLogging();
        new PostgreSqlGroundworkRuntimePersistenceShellFeature { ConnectionString = connectionString }
            .ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.InitializeGroundworkStoreAsync());

        Assert.Contains("connection details were suppressed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, exception.ToString(), StringComparison.Ordinal);
        Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
    }

    private static void AssertBridge<TContract, TImplementation>(ServiceCollection services)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TContract));
        Assert.True(
            descriptor.ImplementationType == typeof(TImplementation) || descriptor.ImplementationFactory is not null,
            $"Expected {typeof(TContract).Name} to resolve to {typeof(TImplementation).Name}.");
    }

    private static void AssertSetting(object feature, string propertyName, object expectedDefault)
    {
        var property = feature.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedDefault, property!.GetValue(feature));
        Assert.Contains(property.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }

    private static void SetSetting(object feature, string propertyName, object value)
    {
        var property = feature.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        property!.SetValue(feature, value);
    }
}
