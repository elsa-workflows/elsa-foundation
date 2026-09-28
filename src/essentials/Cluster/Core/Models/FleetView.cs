namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The fleet as one provider sees it, judged at one instant (FR-009). A fresh view holds every member the provider's
/// store holds; a provider that cannot return all of them fails the read instead (FR-012).
/// </summary>
public sealed record FleetView(
    ClusterProviderKind ProviderKind,
    FleetReadMode ReadMode,
    DateTimeOffset JudgedAt,
    IReadOnlyList<FleetMember> Members)
{
    /// <summary>The member with <paramref name="identity"/>, or <see langword="null"/> when the view does not hold it.</summary>
    public FleetMember? Find(ClusterMemberIdentity identity) => Members.FirstOrDefault(member => member.Identity == identity);
}
