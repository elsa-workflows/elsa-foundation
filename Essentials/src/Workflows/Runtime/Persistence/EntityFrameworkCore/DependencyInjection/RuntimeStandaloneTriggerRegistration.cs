using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class RuntimeStandaloneTriggerRegistration
{
    internal static void UseEntityFramework(IServiceCollection services)
    {
        var markerDescriptors = services.Where(x => x.ImplementationInstance is StandaloneTriggerProviderRegistration).ToArray();
        var marker = markerDescriptors.Select(x => x.ImplementationInstance).OfType<StandaloneTriggerProviderRegistration>().SingleOrDefault();
        if (marker is not null)
        {
            marker.EnsureOwned(services);
            if (marker.Name == "EntityFrameworkCore") return;
            if (marker.Name != "InMemory") throw new InvalidOperationException("The selected standalone trigger provider cannot be replaced implicitly.");
            services.Remove(marker.Resource); services.Remove(marker.Ingress);
            if (marker.Reservations is not null) services.Remove(marker.Reservations);
            if (marker.SourceCommit is not null) services.Remove(marker.SourceCommit);
            foreach (var descriptor in markerDescriptors) services.Remove(descriptor);
        }
        else if (services.Any(x => x.ServiceType == typeof(IStandaloneTriggerStore) || x.ServiceType == typeof(IRuntimeIngressDeliveryStore)))
            throw new InvalidOperationException("Runtime EF cannot replace an independently selected standalone trigger resource or ingress port.");
        var resource = ServiceDescriptor.Scoped<IStandaloneTriggerStore, EfStandaloneTriggerStore>();
        var ingress = ServiceDescriptor.Scoped<IRuntimeIngressDeliveryStore>(provider => new EfRuntimeIngressDeliveryStore(
            provider.GetRequiredService<RuntimeDbContext>(), provider.GetRequiredService<IStandaloneTriggerStore>(),
            StandaloneTriggerRuntimeRegistration.Physical<IRuntimePostCommitOutboxStore>(provider, typeof(CoalescingRuntimePostCommitOutboxStore)),
            provider.GetRequiredService<IWorkflowActivationAuthority>(), provider.GetRequiredService<IWorkflowExecutableStore>(),
            provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            StandaloneTriggerRuntimeRegistration.Physical<IWorkflowExecutionStateStore>(provider, typeof(CoalescingWorkflowExecutionStateStore)),
            provider.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<NativeIngressTargetEvidence>(), provider.GetService<IRuntimeModelExecutionReader>(), provider.GetService<IRecurringTriggerScheduleStore>(),
            provider.GetServices<IStimulusDeliveryIntentAdapter>(), provider.GetRequiredService<NativeIngressWaitTargetEvidence>(),
            provider.GetRequiredService<IBookmarkResumeResolver>(),
            StandaloneTriggerRuntimeRegistration.Physical<IRuntimeCheckpointCommitStore>(provider, typeof(CoalescingRuntimeCheckpointCommitStore))));
        services.Add(resource); services.Add(ingress);
        var reader = ServiceDescriptor.Scoped<IRuntimeIngressReservationReader, EfIngressReservationStore>();
        var sourceCommit = ServiceDescriptor.Scoped<ITriggerSourceResourceCommitStore, EfTriggerSourceResourceCommitStore>();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IRuntimeIngressReservationReader) || descriptor.ServiceType == typeof(ITriggerSourceResourceCommitStore)))
            throw new InvalidOperationException("Runtime EF cannot replace an independently selected source projection owner.");
        services.Add(reader); services.Add(sourceCommit);
        services.AddSingleton(new StandaloneTriggerProviderRegistration("EntityFrameworkCore", resource, ingress) { Reservations = reader, SourceCommit = sourceCommit });
        var queue = services.Where(x => x.ServiceType == typeof(IAuthoredModelQueue)).ToArray();
        if (queue.Length > 1 || queue.Length == 1 && queue[0].ImplementationType != typeof(EfAuthoredModelQueue))
            throw new InvalidOperationException("Runtime EF cannot replace an independently selected authored model queue participant.");
        services.TryAddScoped<IAuthoredModelQueue, EfAuthoredModelQueue>();
    }
}
