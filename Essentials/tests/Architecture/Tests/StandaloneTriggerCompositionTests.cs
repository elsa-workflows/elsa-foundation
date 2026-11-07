using Elsa.Activities.Design.Api;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.Activities.Primitives.StandaloneTriggers;
using Elsa.Activities.Scheduling.StandaloneTriggers;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Events;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Actual module service compositions; host/browser acceptance is qualified separately.</summary>
public sealed class StandaloneTriggerCompositionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Bounded_management_composes_with_Design_Runtime_or_both_without_installing_unrequested_sources(bool design, bool runtime)
    {
        var services = new ServiceCollection(); services.AddLogging(); new EventsFeature().ConfigureServices(services);
        if (design)
        {
            new WorkflowsDesignApiFeature().ConfigureServices(services);
            new ActivitiesDesignApiFeature().ConfigureServices(services);
            new StandaloneHttpTriggerAuthoringFeature().ConfigureServices(services);
            new StandaloneEventTriggerAuthoringFeature().ConfigureServices(services);
            new StandaloneScheduleTriggerAuthoringFeature().ConfigureServices(services);
        }
        if (runtime) services.AddWorkflowRuntime();
        var management = new StandaloneTriggersApiFeature();
        management.ConfigureServices(services); management.PostConfigureServices(services);
        var initializer = Assert.Single(services, item => item.ServiceType == typeof(IStartupTask) && item.ImplementationType?.Name == "InitializeTriggerTypes");
        var capability = Assert.Single(services, item => item.ServiceType == typeof(IApiCapabilitySource) && item.ImplementationType?.Name == "StandaloneTriggerCapabilitySource");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var operation = provider.CreateScope(); var sp = operation.ServiceProvider;
        if (runtime) sp.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("composition")));
        // Resolve only this owning module's startup task; unrelated modules retain their own host activation gates.
        await ((IStartupTask)ActivatorUtilities.CreateInstance(sp, initializer.ImplementationType!)).ExecuteAsync(CancellationToken.None);
        var types = sp.GetRequiredService<IStandaloneTriggerTypeCatalog>().List();
        Assert.Equal(design ? ["cron", "event", "http", "timer"] : Array.Empty<string>(), types.Select(type => type.TypeKey).Order(StringComparer.Ordinal));
        Assert.Empty(sp.GetServices<StandaloneTriggerSourceDescriptor>());
        Assert.DoesNotContain(services, item => item.ServiceType.IsGenericType && item.ServiceType.GetGenericTypeDefinition() == typeof(IStandaloneTriggerIngress<>));
        var declarations = await ((IApiCapabilitySource)ActivatorUtilities.CreateInstance(sp, capability.ImplementationType!)).GetCapabilitiesAsync();
        if (!runtime)
        {
            Assert.Empty(declarations);
            foreach (var executionPort in new[] { typeof(IWorkflowStartDispatcher), typeof(IRuntimePostCommitOutboxProcessor),
                         typeof(IRuntimeIngressDeliveryStore), typeof(IRuntimeIngressWaitTarget), typeof(IRecurringTriggerScheduleStore) })
                Assert.Null(sp.GetService(executionPort));
            Assert.Null(sp.GetService<IStandaloneTriggerStore>());
        }
        else
        {
            Assert.True(sp.GetRequiredService<IRuntimeIngressDeliveryStore>().SupportsAdmission);
            Assert.NotNull(sp.GetRequiredService<IStandaloneTriggerTargetDiscovery>());
            Assert.NotNull(sp.GetRequiredService<ITriggerInputMapper>());
            var installed = Assert.Single(declarations);
            Assert.Equal("elsa.api.standalone-triggers", installed.CapabilityId);
            Assert.Contains(installed.Links, link => link.Rel == "triggers");
            Assert.Contains(installed.Links, link => link.Rel == "deliveries");
            // Metadata is available with Design; activation still requires the actual owning runtime source module.
            Assert.False(sp.GetRequiredService<ITriggerManagementCommitter>().CanActivateType("http", 1));
            Assert.False(sp.GetRequiredService<ITriggerManagementCommitter>().CanActivateType("event", 1));
            Assert.False(sp.GetRequiredService<ITriggerManagementCommitter>().CanActivateType("timer", 1));
        }
    }
}
