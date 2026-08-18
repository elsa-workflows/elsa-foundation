using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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
            d.ImplementationFactory is not null &&
            d.Lifetime == ServiceLifetime.Singleton);
        // The recurring projection's own preparer, registered against its own contract (T044b) — never as an
        // IWorkflowTriggerIndexer, whose replacement must not be able to disarm this projection.
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRecurringTriggerScheduleProjectionPreparer) &&
            d.Lifetime == ServiceLifetime.Scoped);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IWorkflowTriggerIndexer));
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
    public void LeavesTheTriggerIndexerAlone_WhenTriggerCoreComposed()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        // Stand in for the WorkflowsRuntimeTriggers dependency: the indexer and its collaborators.
        services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(
            new WorkflowTriggerBindingExtractor(Array.Empty<IActivityTriggerStimulusProvider>()));
        services.AddSingleton<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();

        new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // T044b: composing recurring triggers no longer rewrites what IWorkflowTriggerIndexer resolves to — the
        // recurring projection arrives as its own collaborator beside it.
        Assert.IsType<WorkflowTriggerIndexer>(provider.GetRequiredService<IWorkflowTriggerIndexer>());
        Assert.IsType<RecurringTriggerScheduleProjectionPreparer>(
            provider.GetRequiredService<IRecurringTriggerScheduleProjectionPreparer>());
        provider.GetRequiredService<IRecurringTriggerScheduleStore>();
        provider.GetRequiredService<IRecurringScheduleCalculator>();
    }

    [Fact]
    public void DefersToAHostRegisteredPreparer()
    {
        // §2.6.2 first-wins: the preparer is a replacement contract, so a host that registered its own before
        // this feature keeps it rather than being silently overwritten.
        var services = new ServiceCollection();
        services.AddScoped<IRecurringTriggerScheduleProjectionPreparer, HostPreparer>();

        new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRecurringTriggerScheduleProjectionPreparer));
        Assert.Equal(typeof(HostPreparer), descriptor.ImplementationType);
    }

    private sealed class HostPreparer : IRecurringTriggerScheduleProjectionPreparer
    {
        public ValueTask PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
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
