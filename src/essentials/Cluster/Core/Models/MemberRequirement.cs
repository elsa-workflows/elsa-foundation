namespace Elsa.Cluster.Core.Models;

/// <summary>
/// One requirement of a member query, from a closed vocabulary this contract defines so that every provider can
/// translate it into its own terms (FR-015; ADR 0078, invariant 3). Adding a kind, with the report section that
/// carries it, is a contract change: no other assembly can derive a requirement.
/// </summary>
public abstract record MemberRequirement
{
    private protected MemberRequirement()
    {
    }

    /// <summary>Whether <paramref name="member"/>, already considered for <paramref name="purpose"/>, meets this
    /// requirement.</summary>
    internal abstract bool IsMetBy(FleetMember member, MemberQueryPurpose purpose);
}
