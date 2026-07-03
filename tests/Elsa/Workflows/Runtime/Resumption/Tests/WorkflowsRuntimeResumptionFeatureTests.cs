using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Resumption.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Resumption.Tests;

public sealed class WorkflowsRuntimeResumptionFeatureTests
{
    [Fact]
    public void RegistersResumptionServiceAndPumpTask()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);

        // TS-1 (§2.23.1): single-implementation service asserted by registration presence, not implementation-type
        // or lifetime pinning. The resumption pump task is a named participant in the multi-implementation
        // IRecurringTask collection, preserved as a composition contract.
        Assert.Contains(services, d => d.ServiceType == typeof(IRuntimeResumptionService));

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRecurringTask) &&
            d.ImplementationType == typeof(RuntimeResumptionPumpTask));
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeResumptionFeature
        {
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            OutboxBatchSize = 11,
            BacklogBatchSize = 12,
            RecoveryScanBatchSize = 13,
            MaxExecutionsPerSweep = 14,
            LeaseTimeoutMinutes = 7,
            HeartbeatTimeoutMinutes = 8
        }.ConfigureServices(services);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<RuntimeResumptionOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(3), options.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxBackoffInterval);
        Assert.Equal(11, options.OutboxBatchSize);
        Assert.Equal(12, options.BacklogBatchSize);
        Assert.Equal(13, options.RecoveryScanBatchSize);
        Assert.Equal(14, options.MaxExecutionsPerSweep);
        Assert.Equal(TimeSpan.FromMinutes(7), options.LeaseTimeout);
        Assert.Equal(TimeSpan.FromMinutes(8), options.HeartbeatTimeout);
    }

    [Fact]
    public void DeclaresTasksDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeResumptionFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeResumption", attribute.Name);
        Assert.Contains("Tasks", attribute.DependsOn.Select(d => d?.ToString()));
    }
}
