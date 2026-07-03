using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class WorkflowsRuntimeSchedulingFeatureTests
{
    [Fact]
    public void RegistersStoreSchedulerAndPumpTask()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);

        // TS-1 (§2.23.1): single-implementation services asserted by registration presence, not implementation-type
        // or lifetime pinning (resolvability is proven by ResolvesStoreAndScheduler). The pump task is a named
        // participant in the multi-implementation IRecurringTask collection, preserved as a composition contract.
        Assert.Contains(services, d => d.ServiceType == typeof(IDurableTimerStore));

        Assert.Contains(services, d => d.ServiceType == typeof(IDurableTimerScheduler));

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRecurringTask) &&
            d.ImplementationType == typeof(DurableTimerPumpTask));

        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeSchedulingFeature
        {
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            MaxTimersPerTick = 42,
            NotFoundGraceSeconds = 15
        }.ConfigureServices(services);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<DurableTimerPumpOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(3), options.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxBackoffInterval);
        Assert.Equal(42, options.MaxTimersPerTick);
        Assert.Equal(TimeSpan.FromSeconds(15), options.NotFoundGrace);
    }

    [Fact]
    public void ResolvesStoreAndScheduler()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // TS-1 (§2.23.1): resolvability, not resolved-implementation-type pinning.
        provider.GetRequiredService<IDurableTimerStore>();
        provider.GetRequiredService<IDurableTimerScheduler>();    }

    [Fact]
    public void DeclaresTasksDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeSchedulingFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeScheduling", attribute.Name);
        Assert.Contains("Tasks", attribute.DependsOn.Select(d => d?.ToString()));
    }
}
