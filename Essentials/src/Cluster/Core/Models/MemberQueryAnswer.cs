namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The answer to a member query (FR-016): the members that match, and for every other member the query considered,
/// the requirement it failed. For a counting query the failures are the blockers: the answer is "yes" only when there
/// are none (MR-007).
/// </summary>
public sealed record MemberQueryAnswer(
    MemberQuery Query,
    FleetView Fleet,
    IReadOnlyList<FleetMember> Matches,
    IReadOnlyList<MemberQueryFailure> Failures)
{
    /// <summary>Whether every member the query considered meets every requirement.</summary>
    public bool EveryConsideredMemberMatches => Failures.Count == 0;
}

/// <summary>A member the query considered, and the first requirement it failed.</summary>
public sealed record MemberQueryFailure(FleetMember Member, MemberRequirement Requirement);
