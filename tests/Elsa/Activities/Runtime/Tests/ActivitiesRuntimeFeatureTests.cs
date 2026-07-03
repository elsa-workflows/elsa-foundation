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

        // TS-1 (§2.23.1): single-implementation services are proven by resolvability, not implementation-type
        // pinning. Named participants in multi-implementation collection contracts (scheduler work handlers via the
        // resolved set below; the startup task) are preserved as composition contracts.
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask) && d.ImplementationType == typeof(RegisterActivityTypesStartupTask));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IActivityFactory>();
        provider.GetRequiredService<IRuntimeActivityInputMaterializer>();
        provider.GetRequiredService<ActivityFaultIncidentRecorder>();
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
        // TS-1 (§2.23.1): implementation-type/lifetime pinning of the single-implementation registry + factory is
        // downgraded to resolvability; the genuine singleton contract is still proven behaviourally via Assert.Same.
        // The named event-handler and startup-task participants in their multi-implementation collections are preserved
        // as composition contracts.
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler<OnActivityConstructorsInitializing>) && d.ImplementationType == typeof(RegisterActivityConstructors));
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask) && d.ImplementationType == typeof(ActivityConstructorsStartupTask));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IActivityConstructorRegistry>();
        provider.GetRequiredService<IActivityFactory>();
        // Same instance twice → genuinely singleton.
        Assert.Same(
            provider.GetRequiredService<IActivityConstructorRegistry>(),
            provider.GetRequiredService<IActivityConstructorRegistry>());
    }
}
