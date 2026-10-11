namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A request for the members that satisfy a list of requirements, for a stated purpose (FR-015, FR-016). Every provider
/// answers it the same way for the same fleet, through <see cref="Evaluate"/>.
/// </summary>
public sealed record MemberQuery
{
    public MemberQuery(MemberQueryPurpose purpose, IEnumerable<MemberRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown member query purpose.");

        var list = requirements.ToArray();
        if (list.Any(requirement => requirement is null))
            throw new ArgumentException("A member query requirement must not be null.", nameof(requirements));

        Purpose = purpose;
        Requirements = list;
    }

    /// <summary>A query that counts members, for decisions that must not be premature.</summary>
    public static MemberQuery Counting(params MemberRequirement[] requirements) => new(MemberQueryPurpose.Counting, requirements);

    /// <summary>A query that finds members able to take work.</summary>
    public static MemberQuery Placement(params MemberRequirement[] requirements) => new(MemberQueryPurpose.Placement, requirements);

    public MemberQueryPurpose Purpose { get; }

    public IReadOnlyList<MemberRequirement> Requirements { get; }

    /// <summary>
    /// Answers the query over <paramref name="fleet"/>: the members it considered that meet every requirement, and for
    /// each other member it considered, the first requirement that member failed.
    /// </summary>
    /// <remarks>
    /// Runnability requirements are met jointly, by one entry of the member's runnability section (spec 184, FR-009),
    /// so the requirement reported for a member that fails them is the first one no entry can meet together with the
    /// runnability requirements before it.
    /// </remarks>
    public MemberQueryAnswer Evaluate(FleetView fleet)
    {
        ArgumentNullException.ThrowIfNull(fleet);

        var considered = Purpose == MemberQueryPurpose.Counting
            ? fleet.Members.Where(member => member.IsLive).ToArray()
            : PlacementCandidates(fleet.Members);
        var outcomes = considered
            .Select(member => (Member: member, Failed: FirstFailed(member)))
            .ToArray();

        return new MemberQueryAnswer(
            this,
            fleet,
            outcomes.Where(outcome => outcome.Failed is null).Select(outcome => outcome.Member).ToArray(),
            outcomes.Where(outcome => outcome.Failed is not null).Select(outcome => new MemberQueryFailure(outcome.Member, outcome.Failed!)).ToArray());
    }

    private MemberRequirement? FirstFailed(FleetMember member)
    {
        var jointlyUnmet = RunnabilityRequirement.FirstUnmetJointly(member, Requirements.OfType<RunnabilityRequirement>());
        return Requirements.FirstOrDefault(requirement => requirement is RunnabilityRequirement
            ? ReferenceEquals(requirement, jointlyUnmet)
            : !requirement.IsMetBy(member, Purpose));
    }

    /// <summary>
    /// The current incarnation of each host id, while it is active and its report can be read. Two undisplaced live
    /// incarnations of one host id are doubtful, and a doubtful member is dropped from placement at once.
    /// </summary>
    private static FleetMember[] PlacementCandidates(IEnumerable<FleetMember> members) =>
        members
            .Where(member => member is { IsLive: true, IsDisplaced: false, Status: MemberStatus.Active })
            .Where(member => !member.Report.IsUnknown)
            .GroupBy(member => member.HostId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();

    public bool Equals(MemberQuery? other) =>
        other is not null && Purpose == other.Purpose && Requirements.SequenceEqual(other.Requirements);

    public override int GetHashCode() => HashCode.Combine(Purpose, Requirements.Count);
}
