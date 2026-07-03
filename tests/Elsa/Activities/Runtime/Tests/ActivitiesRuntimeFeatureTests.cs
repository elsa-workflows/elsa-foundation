using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Activities.Runtime.Handlers;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
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

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowInvokeActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowParentActivityCompletionSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowResumeBookmarkSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeActivityInputMaterializer) &&
            descriptor.ImplementationType == typeof(RuntimeActivityInputMaterializer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStartupTask) &&
            descriptor.ImplementationType == typeof(RegisterActivityTypesStartupTask));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ActivityFaultIncidentRecorder) &&
            descriptor.ImplementationType == typeof(ActivityFaultIncidentRecorder));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ActivityFactory>(provider.GetRequiredService<IActivityFactory>());
        Assert.IsType<RuntimeActivityInputMaterializer>(provider.GetRequiredService<IRuntimeActivityInputMaterializer>());
        Assert.IsType<ActivityFaultIncidentRecorder>(provider.GetRequiredService<ActivityFaultIncidentRecorder>());
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowInvokeActivitySchedulerWorkHandler);
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowParentActivityCompletionSchedulerWorkHandler);
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowResumeBookmarkSchedulerWorkHandler);
    }

    [Fact] // T018 (G27) — the descriptor-type-driven construction seam: registry + factory + the
           // Registry + StartUp Task + Domain Event wiring that populates it from contributed constructors.
    public void RegistersConstructionSeamServices()
    {
        var services = new ServiceCollection();

        new ActivitiesRuntimeFeature().ConfigureServices(services);

        // The registry is a singleton (one per host; populated at startup, read afterward).
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IActivityConstructorRegistry) &&
            descriptor.ImplementationType == typeof(ActivityConstructorRegistry) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IActivityFactory) &&
            descriptor.ImplementationType == typeof(ActivityFactory));
        // The single aggregating handler is registered against the closed generic dispatch interface.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IEventHandler<OnActivityConstructorsInitializing>) &&
            descriptor.ImplementationType == typeof(RegisterActivityConstructors));
        // The startup task that publishes the initialization event and flushes the registry.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStartupTask) &&
            descriptor.ImplementationType == typeof(ActivityConstructorsStartupTask));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ActivityConstructorRegistry>(provider.GetRequiredService<IActivityConstructorRegistry>());
        Assert.IsType<ActivityFactory>(provider.GetRequiredService<IActivityFactory>());
        // Same instance twice → genuinely singleton.
        Assert.Same(
            provider.GetRequiredService<IActivityConstructorRegistry>(),
            provider.GetRequiredService<IActivityConstructorRegistry>());
    }
}
