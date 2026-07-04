using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class WorkflowsRuntimeRecurringTriggersFeatureTests
{
    [Fact]
    public void RegistersStoreCalculatorPumpAndTimeProvider()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IRecurringTriggerScheduleStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IRecurringScheduleCalculator));
        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRecurringTask) &&
            d.ImplementationType == typeof(RecurringTriggerPumpTask));
        // The decorator lands as an additional IWorkflowTriggerIndexer registration (factory-built).
        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowTriggerIndexer));
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeRecurringTriggersFeature
        {
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            MaxSchedulesPerTick = 42
        }.ConfigureServices(services);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<RecurringTriggerPumpOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(3), options.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxBackoffInterval);
        Assert.Equal(42, options.MaxSchedulesPerTick);
    }

    [Fact]
    public void DecoratesTriggerIndexer_WhenTriggerCoreComposed()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        // Stand in for the WorkflowsRuntimeTriggers dependency: the inner indexer and its collaborators.
        services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(
            new WorkflowTriggerBindingExtractor(Array.Empty<IActivityTriggerStimulusProvider>()));
        services.AddSingleton<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();

        new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // The resolved indexer is the scheduling decorator, wrapping the trigger core's indexer.
        Assert.IsType<RecurringTriggerScheduleIndexer>(provider.GetRequiredService<IWorkflowTriggerIndexer>());
        provider.GetRequiredService<IRecurringTriggerScheduleStore>();
        provider.GetRequiredService<IRecurringScheduleCalculator>();
    }

    [Fact]
    public void DeclaresTriggersAndTasksDependencies()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeRecurringTriggersFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeRecurringTriggers", attribute.Name);
        var dependencies = attribute.DependsOn.Select(d => d?.ToString()).ToArray();
        Assert.Contains("WorkflowsRuntimeTriggers", dependencies);
        Assert.Contains("Tasks", dependencies);
    }
}
