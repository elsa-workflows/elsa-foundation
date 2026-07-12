using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeReferenceGarbageCollectionFeatureTests
{
    [Fact]
    public void RegistersCollectorAndRecurringPump()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeReferenceGarbageCollectionFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableReferenceGarbageCollector));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRecurringTask) &&
            descriptor.ImplementationType == typeof(WorkflowExecutableReferenceGarbageCollectionPumpTask));
    }

    [Fact]
    public void MapsCadenceAndSafetySettingsOntoTheirOwningOptions()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeReferenceGarbageCollectionFeature
        {
            SweepIntervalMinutes = 2,
            MaxBackoffIntervalMinutes = 7,
            ArtifactCreationGracePeriodMinutes = 11
        }.ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var pump = provider.GetRequiredService<IOptions<WorkflowExecutableReferenceGarbageCollectionOptions>>().Value;
        var collector = provider.GetRequiredService<IOptions<WorkflowExecutableGarbageCollectionOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(2), pump.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(7), pump.MaxBackoffInterval);
        Assert.Equal(TimeSpan.FromMinutes(11), pump.ArtifactCreationGracePeriod);
        Assert.Equal(TimeSpan.FromMinutes(11), collector.ArtifactCreationGracePeriod);
    }

    [Fact]
    public void DeclaresTasksDependency()
    {
        var feature = Assert.Single(
            typeof(WorkflowsRuntimeReferenceGarbageCollectionFeature)
                .GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeReferenceGarbageCollection", feature.Name);
        Assert.Contains("Tasks", feature.DependsOn.Select(dependency => dependency?.ToString()));
    }
}
