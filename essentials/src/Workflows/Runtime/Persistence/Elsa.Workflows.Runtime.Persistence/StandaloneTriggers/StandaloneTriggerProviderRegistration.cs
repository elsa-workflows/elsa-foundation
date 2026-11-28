using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed record StandaloneTriggerProviderRegistration(string Name, ServiceDescriptor Resource, ServiceDescriptor Ingress)
{
    internal const string PreparedEntityFramework = "EntityFrameworkCorePreparedStandalone";
    internal ServiceDescriptor? SourceCommit { get; init; }
    internal ServiceDescriptor? Reservations { get; init; }
    internal IReadOnlyList<ServiceDescriptor> AdditionalOwnedPorts { get; init; } = [];
    internal void EnsureOwned(IServiceCollection services)
    {
        if (!services.Where(x => x.ServiceType == typeof(IStandaloneTriggerStore)).SequenceEqual([Resource]) ||
            !services.Where(x => x.ServiceType == typeof(IRuntimeIngressDeliveryStore)).SequenceEqual([Ingress]) ||
            SourceCommit is not null && !services.Where(x => x.ServiceType == typeof(ITriggerSourceResourceCommitStore)).SequenceEqual([SourceCommit]) ||
            Reservations is not null && !services.Where(x => x.ServiceType == typeof(IRuntimeIngressReservationReader)).SequenceEqual([Reservations]) ||
            AdditionalOwnedPorts.Any(port => !services.Where(x => x.ServiceType == port.ServiceType).SequenceEqual([port])))
            throw new InvalidOperationException("Standalone trigger persistence no longer owns its selected resource and ingress ports.");
    }
}
