using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Triggers.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed record StandaloneTriggerProviderRegistration(string Name, ServiceDescriptor Resource, ServiceDescriptor Ingress)
{
    internal ServiceDescriptor? SourceCommit { get; init; }
    internal ServiceDescriptor? Reservations { get; init; }
    internal void EnsureOwned(IServiceCollection services)
    {
        if (!services.Where(x => x.ServiceType == typeof(IStandaloneTriggerStore)).SequenceEqual([Resource]) ||
            !services.Where(x => x.ServiceType == typeof(IRuntimeIngressDeliveryStore)).SequenceEqual([Ingress]) ||
            SourceCommit is not null && !services.Where(x => x.ServiceType == typeof(ITriggerSourceResourceCommitStore)).SequenceEqual([SourceCommit]) ||
            Reservations is not null && !services.Where(x => x.ServiceType == typeof(IRuntimeIngressReservationReader)).SequenceEqual([Reservations]))
            throw new InvalidOperationException("Standalone trigger persistence no longer owns its selected resource and ingress ports.");
    }
}

internal static class StandaloneTriggerRuntimeRegistration
{
    internal static void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<ITriggerInputMapper, TriggerInputMapper>();
        services.TryAddScoped<TriggerTargetResolver>();
        services.TryAddScoped<TriggerManagementTargets>();
        services.TryAddScoped<IStandaloneTriggerTargetDiscovery, StandaloneTriggerTargetDiscovery>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITriggerTargetModelSource, NativeTriggerTargetModelSource>());
        services.TryAddScoped<NativeIngressTargetEvidence>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandaloneTriggerRuntimeTarget, NativeStandaloneTriggerRuntimeTarget>());
        services.TryAddScoped<NativeIngressWaitTargetEvidence>();
        services.TryAddScoped<IRuntimeIngressWaitTarget, NativeIngressWaitTarget>();
        services.TryAddScoped<StandaloneTriggerAdmission>();
        services.TryAddScoped<WorkflowStimulusSubscriptionSelector>(provider => new(
            provider.GetRequiredService<IStandaloneTriggerStore>(), provider.GetRequiredService<TriggerTargetResolver>(), provider.GetRequiredService<ITriggerInputMapper>(),
            provider.GetServices<IStandaloneTriggerRuntimeTarget>(), provider.GetServices<IStimulusDeliveryIntentAdapter>(),
            Physical<IWorkflowExecutionStateStore>(provider, typeof(CoalescingWorkflowExecutionStateStore)), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutableStartPolicy>()));
        services.TryAddScoped<IStandaloneTriggerIngressFactory, StandaloneTriggerIngressFactory>();
        services.TryAddScoped<ITriggerManagementCommitter, ProtectedTriggerManagementCommitter>();
        services.TryAddScoped<IRuntimeIngressTransportObserver>(provider => provider.GetRequiredService<IRuntimeIngressDeliveryStore>() as IRuntimeIngressTransportObserver
            ?? new UnavailableIngressTransportObserver());
        services.AddRuntimeDeliverySourceHandler<StandaloneTriggerDeliveryHandler>(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)));
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
            provider.GetRequiredService<TriggerManagementTargets>()));
        services.Add(reader); services.Add(sourceCommit);
        services.AddSingleton(new StandaloneTriggerProviderRegistration("InMemory", resource, ingress) { Reservations = reader, SourceCommit = sourceCommit });
    }

    internal static T Physical<T>(IServiceProvider provider, Type knownDecorator) where T : class
    {
        var selected = provider.GetRequiredService<T>();
        return selected.GetType() == knownDecorator
            ? provider.GetRequiredService<CoalescingInner<T>>().Value : selected;
    }
}

internal sealed class UnavailableIngressTransportObserver : IRuntimeIngressTransportObserver;
