using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Providers;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workbench;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Guards the Workbench's replacement for the provider-specific preset. The dashboard feature is tested
/// here because its v2 source registration was previously hidden inside Unified provider registration and would
/// otherwise silently fall back to the dashboard's unavailable/in-memory defaults.
/// </summary>
public sealed class WorkbenchGroundworkCompositionTests
{
    [Fact]
    public void Explicit_workbench_lanes_keep_dashboard_projections_on_the_shared_default_target()
    {
        var services = new ServiceCollection();

        new GroundworkSqliteProviderFeature().ConfigureServices(services);
        new GroundworkWorkflowRuntimeFeature().ConfigureServices(services);
        new ActivitiesDesignGroundworkPersistenceFeature().ConfigureServices(services);
        new WorkflowsDesignGroundworkPersistenceFeature().ConfigureServices(services);
        new PublishingGroundworkFeature().ConfigureServices(services);
        new WorkflowsRuntimeDistributedGroundworkPersistenceFeature().ConfigureServices(services);
        new WorkbenchGroundworkDashboardFeature().ConfigureServices(services);

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var registrations = registry.Registrations;

        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == "workflowRunHealthState" && registration.TargetName == "default");
        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == "workflowDefinition" && registration.TargetName == "default");
        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == "workflowDefinitionDraft" && registration.TargetName == "default");
        Assert.Contains(registrations, registration =>
            registration.Unit.Id.Value == "workflowExecutableSourceReference" && registration.TargetName == "default");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowRunHealthDataSource) && descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowPortfolioDataSource) && descriptor.ImplementationFactory is not null);

        services.AddSingleton<IGroundworkStorageSessionSource, ThrowingSessionSource>();
        services.AddSingleton<IPersistenceAccessContextAccessor, GlobalAccessContextAccessor>();
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkV2WorkflowRunHealthDataSource>(
            provider.GetRequiredService<IWorkflowRunHealthDataSource>());
        Assert.IsType<GroundworkV2WorkflowPortfolioDataSource>(
            provider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    private sealed class ThrowingSessionSource : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            throw new NotSupportedException();

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            throw new NotSupportedException();
    }

    private sealed class GlobalAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Global;
    }

}
