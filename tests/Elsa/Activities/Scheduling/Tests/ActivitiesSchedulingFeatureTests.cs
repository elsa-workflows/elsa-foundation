using CShells.Features;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesSchedulingFeature"/> (constitution §2.23.1). Like the
/// other activity features, scheduling activities are constructed by the runtime's CLR activity constructor, so
/// the feature registers no per-type services; the test pins the shell metadata (name + dependency) that makes
/// the durable timer store, scheduler, and pump present when <c>Delay</c> runs.
/// </summary>
public sealed class ActivitiesSchedulingFeatureTests
{
    [Fact]
    public void ConfigureServices_DoesNotThrow_AndRegistersNoExtraActivityServices()
    {
        var services = new ServiceCollection();

        new ActivitiesSchedulingFeature().ConfigureServices(services);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IActivityConstructor));
    }

    [Fact]
    public void DeclaresSchedulingRuntimeDependency()
    {
        var attribute = Assert.Single(
            typeof(ActivitiesSchedulingFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("ActivitiesScheduling", attribute.Name);
        Assert.Contains("WorkflowsRuntimeScheduling", attribute.DependsOn.Select(d => d?.ToString()));
    }
}
