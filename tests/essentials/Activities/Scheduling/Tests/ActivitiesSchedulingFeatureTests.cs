using CShells.Features;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesSchedulingFeature"/> (constitution §2.23.1). The
/// activity types themselves are transiently activated from compiled CLR contracts, so the feature registers
/// no per-type activity instances; it does register the Timer/Cron publish-time providers (trigger stimulus +
/// recurring schedule), and pins the shell metadata that brings the durable timer store (Delay) and the
/// recurring-trigger pump (Timer/Cron) into the composition.
/// </summary>
public sealed class ActivitiesSchedulingFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersTimerAndCronTriggerStimulusProviders()
    {
        var services = new ServiceCollection();

        new ActivitiesSchedulingFeature().ConfigureServices(services);

        var providers = services
            .Where(d => d.ServiceType == typeof(IActivityTriggerStimulusProvider))
            .Select(d => d.ImplementationType)
            .ToArray();

        Assert.Contains(typeof(TimerTriggerStimulusProvider), providers);
        Assert.Contains(typeof(CronTriggerStimulusProvider), providers);
    }

    [Fact]
    public void ConfigureServices_RegistersTimerAndCronScheduleProviders()
    {
        var services = new ServiceCollection();

        new ActivitiesSchedulingFeature().ConfigureServices(services);

        var providers = services
            .Where(d => d.ServiceType == typeof(IRecurringTriggerScheduleProvider))
            .Select(d => d.ImplementationType)
            .ToArray();

        Assert.Contains(typeof(TimerRecurringScheduleProvider), providers);
        Assert.Contains(typeof(CronRecurringScheduleProvider), providers);
    }

    [Fact]
    public void DeclaresSchedulingAndRecurringTriggerDependencies()
    {
        var attribute = Assert.Single(
            typeof(ActivitiesSchedulingFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("ActivitiesScheduling", attribute.Name);
        var dependencies = attribute.DependsOn.Select(d => d?.ToString()).ToArray();
        Assert.Contains("WorkflowsRuntimeScheduling", dependencies);
        Assert.Contains("WorkflowsRuntimeRecurringTriggers", dependencies);
    }
}
