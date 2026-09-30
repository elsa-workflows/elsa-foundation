using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Cluster.InProcess;

public static class InProcessClusterMembershipServiceCollectionExtensions
{
    /// <summary>The in-process provider's name, as startup diagnostics report it.</summary>
    public const string ProviderName = "in-process";

    /// <summary>
    /// Registers the in-process membership default unless a provider is already registered. Every consumer of
    /// membership calls this, so a host that composes nothing about clustering is a cluster of one, and a host that
    /// composes a durable provider gets that provider instead, in either order.
    /// </summary>
    public static IServiceCollection TryAddInProcessClusterMembership(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        return services.TryAddClusterMembershipDefault(new ClusterMembershipProviderRegistration(
            ProviderName,
            ClusterProviderKind.InProcess,
            ServiceDescriptor.Singleton<IClusterMembership>(new HostMember().Get)));
    }

    /// <summary>
    /// The one member a host's registrations share, as the EF provider's is: created on first use, from the container that first
    /// asks, and every shell container built from copies of the host's registrations resolves the same instance, where a
    /// registration by type would give each its own member. It reads only what every container sees the same, the options and
    /// the report sources, so the container it is created from does not change what it reports.
    /// </summary>
    private sealed class HostMember
    {
        private readonly object _gate = new();
        private InProcessClusterMembership? _member;

        public IClusterMembership Get(IServiceProvider services)
        {
            lock (_gate)
                return _member ??= ActivatorUtilities.CreateInstance<InProcessClusterMembership>(services);
        }
    }
}
