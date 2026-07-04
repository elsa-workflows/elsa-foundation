using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class WorkflowsRuntimeDistributedFeatureTests
{
    [Fact]
    public void RegistersPlacementTransportPumpAndReplacesActorProvider()
    {
        var services = BuildBaselineServices();

        // Simulate the core registration this feature must replace (RuntimeCoreServiceCollectionExtensions).
        services.TryAddSingleton<IWorkflowExecutionActorProvider, InProcessWorkflowExecutionActorProvider>();

        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // S=2.6 single active provider: after the feature, the one IWorkflowExecutionActorProvider resolves to the
        // distributed provider, not the in-process one it replaced.
        Assert.IsType<DistributedWorkflowExecutionActorProvider>(provider.GetRequiredService<IWorkflowExecutionActorProvider>());
        Assert.Single(services, d => d.ServiceType == typeof(IWorkflowExecutionActorProvider));

        // Leaf contracts resolve, and the pump is a named participant in the IRecurringTask collection.
        Assert.NotNull(provider.GetRequiredService<IExecutionPlacementStore>());
        Assert.NotNull(provider.GetRequiredService<IExecutionPlacementService>());
        Assert.NotNull(provider.GetRequiredService<IExecutionCommandTransport>());
        Assert.Contains(provider.GetServices<IRecurringTask>(), task => task is ExecutionPlacementPumpTask);
    }

    [Fact]
    public void ResolvesEveryRegisteredService()
    {
        var services = BuildBaselineServices();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // TS-1 (§2.23.1): every service the feature registers must resolve.
        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionActorProvider>());
        Assert.NotNull(provider.GetRequiredService<IExecutionPlacementStore>());
        Assert.NotNull(provider.GetRequiredService<IExecutionPlacementService>());
        Assert.NotNull(provider.GetRequiredService<IExecutionCommandTransport>());
        Assert.NotNull(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>().Single());
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = BuildBaselineServices();

        new WorkflowsRuntimeDistributedFeature
        {
            NodeId = "node-x",
            LeaseDurationSeconds = 45,
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            MaxExecutionsPerSweep = 14,
            TransportLeaseBatchSize = 7
        }.ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
        var placement = serviceProvider.GetRequiredService<IOptions<ExecutionPlacementOptions>>().Value;
        var pump = serviceProvider.GetRequiredService<IOptions<ExecutionPlacementPumpOptions>>().Value;

        Assert.Equal("node-x", placement.NodeId);
        Assert.Equal(TimeSpan.FromSeconds(45), placement.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), pump.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), pump.MaxBackoffInterval);
        Assert.Equal(14, pump.MaxExecutionsPerSweep);
        Assert.Equal(7, pump.TransportLeaseBatchSize);
    }

    [Fact]
    public void DeclaresTasksDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeDistributedFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeDistributed", attribute.Name);
        Assert.Contains("Tasks", attribute.DependsOn.Select(d => d?.ToString()));
    }

    private static ServiceCollection BuildBaselineServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IWorkflowExecutionCommandExecutor>(NoopWorkflowExecutionCommandExecutor.Instance);
        return services;
    }
}
