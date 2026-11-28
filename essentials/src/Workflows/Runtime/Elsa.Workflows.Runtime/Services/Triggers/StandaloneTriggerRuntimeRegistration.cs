using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class StandaloneTriggerRuntimeRegistration
{
    internal static void AddServices(IServiceCollection services)
    {
        if (services.Select(x => x.ImplementationInstance).OfType<StandaloneTriggerProviderRegistration>()
            .Any(marker => marker.Name == StandaloneTriggerProviderRegistration.PreparedEntityFramework))
            throw new InvalidOperationException("Legacy standalone services cannot be combined with the prepared Actor Foundation provider.");
        StandaloneTriggerSourceRegistration.AddServices(services);
        services.TryAddScoped<NativeIngressTargetEvidence>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandaloneTriggerRuntimeTarget, NativeStandaloneTriggerRuntimeTarget>());
        services.TryAddScoped<NativeIngressWaitTargetEvidence>();
        services.TryAddScoped<IRuntimeIngressWaitTarget, NativeIngressWaitTarget>();
        services.TryAddScoped<WorkflowStimulusSubscriptionSelector>(provider => new(
            provider.GetRequiredService<IStandaloneTriggerStore>(), provider.GetRequiredService<TriggerTargetResolver>(), provider.GetRequiredService<ITriggerInputMapper>(),
            provider.GetServices<IStandaloneTriggerRuntimeTarget>(), provider.GetServices<IStimulusDeliveryIntentAdapter>(),
            Physical<IWorkflowExecutionStateStore>(provider, typeof(CoalescingWorkflowExecutionStateStore)), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutableStartPolicy>()));
        var marker = services.Select(x => x.ImplementationInstance).OfType<StandaloneTriggerProviderRegistration>().SingleOrDefault();
        if (marker is not null) { marker.EnsureOwned(services); return; }
        if (services.Any(x => x.ServiceType == typeof(IStandaloneTriggerStore) || x.ServiceType == typeof(IRuntimeIngressDeliveryStore))) return;
        var resource = ServiceDescriptor.Scoped<IStandaloneTriggerStore, InMemoryStandaloneTriggerStore>();
        var ingress = ServiceDescriptor.Scoped<IRuntimeIngressDeliveryStore>(provider => new InMemoryRuntimeIngressDeliveryStore(
            provider.GetRequiredService<IStandaloneTriggerStore>(), Physical<IRuntimePostCommitOutboxStore>(provider, typeof(CoalescingRuntimePostCommitOutboxStore)),
            provider.GetRequiredService<IWorkflowActivationAuthority>(), provider.GetRequiredService<IWorkflowExecutableStore>(),
            provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), Physical<IWorkflowExecutionStateStore>(provider, typeof(CoalescingWorkflowExecutionStateStore)),
            provider.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<NativeIngressTargetEvidence>(), provider.GetService<IRecurringTriggerScheduleStore>(), provider.GetServices<IStimulusDeliveryIntentAdapter>(),
            provider.GetRequiredService<NativeIngressWaitTargetEvidence>(), provider.GetRequiredService<IBookmarkResumeResolver>(),
            Physical<IRuntimeCheckpointCommitStore>(provider, typeof(CoalescingRuntimeCheckpointCommitStore))));
        services.Add(resource); services.Add(ingress);
        var reader = ServiceDescriptor.Scoped<IRuntimeIngressReservationReader, InMemoryIngressReservationReader>();
        var sourceCommit = ServiceDescriptor.Scoped<ITriggerSourceResourceCommitStore>(provider => new InMemoryTriggerSourceResourceCommitStore(
            provider.GetRequiredService<IStandaloneTriggerStore>(), provider.GetRequiredService<IRuntimeIngressDeliveryStore>(),
            provider.GetRequiredService<IWorkflowActivationAuthority>(), provider.GetRequiredService<IWorkflowActivationSwitch>(), provider.GetService<IWorkflowTriggerBindingStore>(),
            provider.GetRequiredService<Elsa.Workflows.Runtime.Services.Checkpoints.InMemoryRuntimeCheckpointStoreState>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetServices<IStandaloneSchedulePlanner>(), provider.GetService<IRecurringTriggerScheduleStore>(),
            provider.GetRequiredService<TriggerManagementTargets>(), provider.GetServices<IIngressReservationPolicy>()));
        services.Add(reader); services.Add(sourceCommit);
        services.AddSingleton(new StandaloneTriggerProviderRegistration("InMemory", resource, ingress) { Reservations = reader, SourceCommit = sourceCommit });
    }

    internal static void AddPreparedServices(IServiceCollection services) =>
        StandaloneTriggerSourceRegistration.AddPreparedServices(services, PreparedStandaloneOutboxFactory.Create);

    internal static T Physical<T>(IServiceProvider provider, Type knownDecorator) where T : class
    {
        var selected = provider.GetRequiredService<T>();
        return selected.GetType() == knownDecorator
            ? provider.GetRequiredService<CoalescingInner<T>>().Value : selected;
    }
}
