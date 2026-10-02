using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivitiesRuntimeFeatureTests
{
    [Fact]
    public void RegistersActivityInvocationSchedulerWorkHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesRuntimeFeature().ConfigureServices(services);

        // TS-1 (§2.23.1): single-implementation services are proven by resolvability, not implementation-type
        // pinning. Named participants in multi-implementation collection contracts (scheduler work handlers via the
        // resolved set below; the startup task) are preserved as composition contracts.
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask) && d.ImplementationType == typeof(RegisterActivityTypesStartupTask));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRuntimeActivityInputMaterializer>();
        provider.GetRequiredService<ActivityFaultIncidentRecorder>();
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowInvokeActivitySchedulerWorkHandler);
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowParentActivityCompletionSchedulerWorkHandler);
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowResumeBookmarkSchedulerWorkHandler);
    }

    [Fact]
    public async Task ComposesTheActivatorWithItsSecretInputResolver()
    {
        // The collaborator is a required constructor dependency, so the activator resolves only when the feature registers it.
        var services = new ServiceCollection().AddWorkflowRuntime();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<ActivityActivator>(scope.ServiceProvider.GetRequiredService<IActivityActivator>());
        scope.ServiceProvider.GetRequiredService<ActivitySecretInputResolver>();
    }

    [Fact]
    public void RegistersSnapshotHydrationAndTypeDiscoveryServices()
    {
        var services = new ServiceCollection();

        new ActivitiesRuntimeFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(ActivityInputHydrator));
        Assert.Contains(services, d => d.ServiceType == typeof(IRuntimeActivityInputMaterializer));
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask)
            && d.ImplementationType == typeof(RegisterActivityTypesStartupTask));
    }
}
