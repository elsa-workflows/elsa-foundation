using CShells.Features;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesSchedulingFeature"/>. The
/// activity types themselves are transiently activated from compiled CLR contracts, so the feature registers
/// no per-type activity instances; it does register the Timer/Cron publish-time providers (trigger stimulus +
/// recurring schedule), and pins the shell metadata that brings the durable timer store (Delay) and the
/// recurring-trigger pump (Timer/Cron) into the composition.
/// </summary>
public sealed class ActivitiesSchedulingFeatureTests
{
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
