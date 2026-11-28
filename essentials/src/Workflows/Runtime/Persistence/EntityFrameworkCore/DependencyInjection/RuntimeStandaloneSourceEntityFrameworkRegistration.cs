using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class RuntimeStandaloneSourceEntityFrameworkRegistration
{
    private static readonly Type[] PreparedOwnedPorts =
    [
        typeof(EfRuntimePostCommitOutboxStorage), typeof(IRuntimePostCommitOutboxStore), typeof(IPostCommitOutboxLookupStore),
        typeof(IRuntimePostCommitOutboxProcessor), typeof(IRuntimePostCommitIntentDispatcher), typeof(IRuntimeAuthoredStandaloneEventIngress)
    ];

    internal static void UsePreparedEntityFramework(IServiceCollection services)
    {
        RuntimeEfContractBackendRegistration.EnsurePublicationContext(services, "Prepared standalone Runtime EF persistence");
        var marker = services.Select(x => x.ImplementationInstance).OfType<StandaloneTriggerProviderRegistration>().SingleOrDefault();
        if (marker is not null)
        {
            marker.EnsureOwned(services);
            if (marker.Name == StandaloneTriggerProviderRegistration.PreparedEntityFramework) return;
            throw new InvalidOperationException("Prepared standalone Runtime EF cannot replace an independently selected standalone provider.");
        }
        if (services.Any(descriptor => PreparedOwnedPorts.Contains(descriptor.ServiceType) ||
            typeof(IEfRuntimePostCommitOutboxOwner).IsAssignableFrom(descriptor.ServiceType) ||
            descriptor.ServiceType == typeof(IStandaloneTriggerStore) || descriptor.ServiceType == typeof(IRuntimeIngressDeliveryStore) ||
            descriptor.ServiceType == typeof(IRuntimeIngressReservationReader) || descriptor.ServiceType == typeof(ITriggerSourceResourceCommitStore)))
            throw new InvalidOperationException("Prepared standalone Runtime EF requires exclusive ownership of its source and outbox ports.");

        var snapshot = services.ToArray();
        try
        {
            StandaloneTriggerSourceRegistration.AddPreparedServices(services);
            services.AddScoped<EfRuntimePostCommitOutboxStorage>(provider =>
            {
                var context = provider.GetRequiredService<RuntimeDbContext>();
                var access = provider.GetRequiredService<IPersistenceAccessContextAccessor>();
                var time = provider.GetRequiredService<TimeProvider>();
                var references = provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>() as EfWorkflowExecutableSourceReferenceStore;
                return new(context, access, time, references is not null && ReferenceEquals(references.Context, context) ? references : null,
                    provider.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>(), preparedStandaloneOnly: true);
            });
            services.AddScoped<IRuntimePostCommitOutboxStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStorage>());
            services.AddScoped<IPostCommitOutboxLookupStore>(provider => provider.GetRequiredService<EfRuntimePostCommitOutboxStorage>());
            services.AddScoped<IRuntimeAuthoredStandaloneEventIngress, EfAuthoredStandaloneEventIngress>();
            var resource = ServiceDescriptor.Scoped<IStandaloneTriggerStore, EfStandaloneTriggerStore>();
            var ingress = ServiceDescriptor.Scoped<IRuntimeIngressDeliveryStore>(provider =>
            {
                var context = provider.GetRequiredService<RuntimeDbContext>();
                var access = provider.GetRequiredService<IPersistenceAccessContextAccessor>();
                var time = provider.GetRequiredService<TimeProvider>();
                return new EfRuntimeIngressDeliveryStore(context, provider.GetRequiredService<IStandaloneTriggerStore>(),
                    provider.GetRequiredService<IRuntimePostCommitOutboxStore>(), provider.GetRequiredService<IWorkflowActivationAuthority>(),
                    provider.GetRequiredService<IWorkflowExecutableStore>(), provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
                    provider.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>(), access, time,
                    schedules: provider.GetService<IRecurringTriggerScheduleStore>(),
                    preparedStarts: provider.GetService<IRuntimePreparedStartAttemptProof>(),
                    preparedTarget: provider.GetService<IRuntimePreparedStandaloneRecipient>());
            });
            RegisterResources(services, StandaloneTriggerProviderRegistration.PreparedEntityFramework, resource, ingress,
                services.Where(x => PreparedOwnedPorts.Contains(x.ServiceType)).ToArray());
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    internal static void RegisterResources(IServiceCollection services, string name, ServiceDescriptor resource, ServiceDescriptor ingress,
        IReadOnlyList<ServiceDescriptor>? additionalOwnedPorts = null)
    {
        services.Add(resource); services.Add(ingress);
        var reader = ServiceDescriptor.Scoped<IRuntimeIngressReservationReader, EfIngressReservationStore>();
        var sourceCommit = ServiceDescriptor.Scoped<ITriggerSourceResourceCommitStore, EfTriggerSourceResourceCommitStore>();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IRuntimeIngressReservationReader) || descriptor.ServiceType == typeof(ITriggerSourceResourceCommitStore)))
            throw new InvalidOperationException("Runtime EF cannot replace an independently selected source projection owner.");
        services.Add(reader); services.Add(sourceCommit);
        services.AddSingleton(new StandaloneTriggerProviderRegistration(name, resource, ingress)
        {
            Reservations = reader,
            SourceCommit = sourceCommit,
            AdditionalOwnedPorts = additionalOwnedPorts ?? []
        });
    }
}
