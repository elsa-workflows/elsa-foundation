using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Testing;

/// <summary>
/// The registrations the invariant tier composes beside the provider under test (FR-001, FR-056): a second opt-in
/// provider, a stand-in default, and a membership registered directly. None is ever asked for membership; only which
/// one a host selects matters.
/// </summary>
public static class ConformanceSentinelProvider
{
    public const string Name = "conformance-sentinel";

    public static void Compose(IServiceCollection services) =>
        services.AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(
            Name,
            ClusterProviderKind.Durable,
            ServiceDescriptor.Singleton<IClusterMembership, InertMembership>()));

    /// <summary>Registers a stand-in in-process default, the way a consumer registers the real one.</summary>
    public static void ComposeDefault(IServiceCollection services) =>
        services.TryAddClusterMembershipDefault(new ClusterMembershipProviderRegistration(
            "conformance-default",
            ClusterProviderKind.InProcess,
            ServiceDescriptor.Singleton<IClusterMembership, InertMembership>()));

    /// <summary>Registers <see cref="IClusterMembership"/> directly, bypassing every provider registration.</summary>
    public static void ComposeDirectly(IServiceCollection services) => services.AddSingleton<IClusterMembership, InertMembership>();

    /// <summary>A membership that is only ever composed, never asked.</summary>
    public sealed class InertMembership : IClusterMembership
    {
        public ClusterProviderKind ProviderKind => ClusterProviderKind.Durable;
        public LocalMemberStanding GetLocalStanding() => throw new NotSupportedException();
        public ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
