using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.Readability;

/// <summary>
/// The finalization gate's view of the fleet (spec 181, "Requirements on membership"), answered by the host's one
/// membership provider. It adds no rule of its own: publishing is the provider's publish (MR-003, MR-004), and the
/// counting query is the contract's <see cref="ReadsSchemaVersion"/> requirement over a fresh read (MR-007; spec 183,
/// FR-023), which counts every live member whose report speaks for the database, a displaced-but-live one and one with
/// an unknown report included.
/// </summary>
/// <remarks>
/// Under the in-process provider the fleet is this host alone, so a version this host reads finalizes at once
/// (FR-021). Under a durable provider every live member that reports the family is counted.
/// </remarks>
public sealed class ClusterSchemaFleet(IClusterMembership membership) : IEfSchemaFleet
{
    public EfSchemaFleetStanding GetLocalStanding()
    {
        var standing = membership.GetLocalStanding();
        return new EfSchemaFleetStanding(
            new SchemaFinalizationMember(standing.Identity.HostId, standing.Identity.Incarnation.Value),
            standing.HasLapsed);
    }

    public async ValueTask PublishAsync(CancellationToken cancellationToken = default) =>
        await membership.PublishReportAsync(cancellationToken);

    public async ValueTask<EfSchemaFleetAnswer> CountAsync(
        string family,
        string version,
        string databaseIdentity,
        CancellationToken cancellationToken = default)
    {
        var answer = await membership.QueryAsync(
            MemberQuery.Counting(new ReadsSchemaVersion(family, version, databaseIdentity)),
            FleetReadMode.Fresh,
            cancellationToken);
        return new EfSchemaFleetAnswer(
            answer.EveryConsideredMemberMatches,
            answer.Failures.Select(failure => Describe(failure.Member, family, databaseIdentity)).ToArray());
    }

    public IChangeToken GetChangeToken() => membership.GetChangeToken();

    /// <summary>A counted member that cannot read the version, and what it reads instead, for the gate's status (FR-022).</summary>
    private static string Describe(FleetMember member, string family, string databaseIdentity)
    {
        if (member.Report.IsUnknown || member.Report.Readability is null)
            return $"{member.Identity} reports what it reads in a form this host cannot interpret";
        var readable = member.Report.Readability.Entries
            .Where(entry => string.Equals(entry.Family, family, StringComparison.Ordinal) && entry.AppliesTo(databaseIdentity))
            .SelectMany(entry => entry.ReadableVersions)
            .Distinct(StringComparer.Ordinal);
        return $"{member.Identity} reads [{string.Join(", ", readable)}]";
    }
}
