using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>
/// Declares <see cref="IClusterMembership"/> as a replacement contract: one provider
/// is active per host process.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ClusterMembershipReplacementContractAttribute : Attribute;

/// <summary>
/// Cluster membership: the record of which hosts are alive and what each of them reports about itself.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one provider is active per host process, selected once on the host container. The in-process provider is
/// the default, a cluster of one; a durable provider replaces it when a host composes one, and two durable providers
/// in one host fail at startup. Providers register through
/// <see cref="Extensions.ClusterMembershipServiceCollectionExtensions"/>, never directly.
/// </para>
/// <para>
/// A fresh read never omits a member that may still read or write, and never credits a member with more than it
/// reported. Any decision that must not be premature uses <see cref="FleetReadMode.Fresh"/>. Membership is never
/// consulted at a checkpoint commit; the fence there is independent of it.
/// </para>
/// </remarks>
[ClusterMembershipReplacementContract]
public interface IClusterMembership
{
    /// <summary>Whether this provider is the in-process cluster of one or a durable fleet.</summary>
    ClusterProviderKind ProviderKind { get; }

    /// <summary>
    /// This process's member: its host id, its current incarnation, its status, and whether it has concluded that it
    /// lapsed. A durable provider's incarnation changes when it rejoins after a lapse.
    /// </summary>
    LocalMemberStanding GetLocalStanding();

    /// <summary>
    /// Reads the fleet view. A fresh read reads the provider's authoritative store and either returns every member or
    /// throws <see cref="Exceptions.ClusterMembershipReadException"/>; it never returns part of the fleet. A
    /// cached read may lag by at most one heartbeat interval.
    /// </summary>
    ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes this member's report from its report sources and publishes it. Returns only once the report is
    /// visible to every later fresh read by any member.
    /// </summary>
    ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a member query over the fleet view read in <paramref name="mode"/>. Consumers ask
    /// here, never a provider's store or a framework's placement API.
    /// </summary>
    ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// A change token that completes once, the next time the provider observes a fleet change: a join, a status
    /// change, a new report, a lapse, a displacement or an expiry (#2097, owner decision). It carries no
    /// dependency on the Events feature: <see cref="IChangeToken"/> (Microsoft.Extensions.Primitives) is the pattern
    /// this framework already uses for a signal a consumer polls or re-registers against (Elsa.Caching's
    /// <c>IChangeTokenSignaler</c>), so membership uses it too rather than introducing an <c>IObservable</c>
    /// dependency the rest of the codebase does not otherwise take. Call this again after the token completes to
    /// observe the next change. A consumer MUST NOT depend on it as its only way to learn of a change; it MUST also
    /// compare successive reads.
    /// </summary>
    IChangeToken GetChangeToken();
}
