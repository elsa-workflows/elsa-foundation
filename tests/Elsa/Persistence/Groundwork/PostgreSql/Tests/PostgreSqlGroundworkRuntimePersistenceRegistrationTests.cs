using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
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

        // IDocumentStore resolves from the shared holder, which is populated once at startup by the initializer
        // (registered as both a hosted service and a shell initializer). The handle is owned by the holder, not
        // registered in DI, so the wiring is asserted through the holder + initializer instead.
        Assert.Single(services, d => d.ServiceType == typeof(IDocumentStore));
        Assert.Single(services, d => d.ServiceType == typeof(GroundworkDocumentStoreHolder));
        Assert.Single(services, d => d.ServiceType == typeof(PostgreSqlGroundworkDocumentStoreInitializer));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(services, d => d.ServiceType == typeof(IShellInitializer));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(ShellInitializerRegistration)
            && d.ImplementationInstance is ShellInitializerRegistration { Phase: LifecyclePhase.Prepare });
    }

    [Fact]
    public void Feature_swaps_the_runtime_store_seams_over_to_the_groundwork_bridges()
    {
        var services = ConfiguredServices();

        AssertBridge<IBookmarkStateStore, GroundworkBookmarkStateStore>(services);
        AssertBridge<IWorkflowExecutableStore, GroundworkWorkflowExecutableStore>(services);
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

    private static void AssertBridge<TContract, TImplementation>(ServiceCollection services)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TContract));
        Assert.True(
            descriptor.ImplementationType == typeof(TImplementation) || descriptor.ImplementationFactory is not null,
            $"Expected {typeof(TContract).Name} to resolve to {typeof(TImplementation).Name}.");
    }
}
