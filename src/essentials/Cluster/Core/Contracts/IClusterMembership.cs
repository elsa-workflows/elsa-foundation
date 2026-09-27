using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>
/// Declares <see cref="IClusterMembership"/> as a replacement contract (framework constitution §2.6.2): one provider
/// is active per host process.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ClusterMembershipReplacementContractAttribute : Attribute;

/// <summary>
/// Cluster membership: the record of which hosts are alive and what each of them reports about itself (ADR 0078,
/// spec 183).
/// </summary>
/// <remarks>
/// <para>
/// Exactly one provider is active per host process, selected once on the host container. The in-process provider is
/// the default, a cluster of one; a durable provider replaces it when a host composes one, and two durable providers
/// in one host fail at startup (ADR 0078, invariant 1). Providers register through
/// <see cref="Extensions.ClusterMembershipServiceCollectionExtensions"/>, never directly.
/// </para>
/// <para>
/// A fresh read never omits a member that may still read or write, and never credits a member with more than it
/// reported. Any decision that must not be premature uses <see cref="FleetReadMode.Fresh"/>. Membership is never
/// consulted at a checkpoint commit; the fence there is independent of it (ADR 0078, invariant 2).
/// </para>
/// </remarks>
[ClusterMembershipReplacementContract]
public interface IClusterMembership
{
    /// <summary>Whether this provider is the in-process cluster of one or a durable fleet (MR-006).</summary>
    ClusterProviderKind ProviderKind { get; }

    /// <summary>
    /// This process's member: its host id, its current incarnation, its status, and whether it has concluded that it
    /// lapsed (FR-003, FR-007, MR-005). A durable provider's incarnation changes when it rejoins after a lapse.
    /// </summary>
    LocalMemberStanding GetLocalStanding();

    /// <summary>
    /// Reads the fleet view. A fresh read reads the provider's authoritative store and either returns every member or
    /// throws <see cref="Exceptions.ClusterMembershipReadException"/>; it never returns part of the fleet (FR-012). A
    /// cached read may lag by at most one heartbeat interval (FR-010).
    /// </summary>
    ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes this member's report from its report sources and publishes it. Returns only once the report is
    /// visible to every later fresh read by any member (FR-011, MR-003).
    /// </summary>
    ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a member query over the fleet view read in <paramref name="mode"/> (FR-015, FR-016). Consumers ask
    /// here, never a provider's store or a framework's placement API (ADR 0078, invariant 3).
    /// </summary>
    ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default);
}
