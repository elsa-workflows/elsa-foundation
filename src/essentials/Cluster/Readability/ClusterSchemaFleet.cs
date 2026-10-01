using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.Readability;

/// <summary>
/// The finalization gate's view of the fleet (spec 181, "Requirements on membership"), answered by the host's one
/// membership provider. It adds no rule of its own: publishing is the provider's publish (MR-003, MR-004), and the
/// counting query is the contract's <see cref="ReadsSchemaVersion"/> requirement over a fresh read (MR-007; spec 183,
/// FR-023), which counts every live member whose report speaks for the database, a displaced-but-live one and one with
/// an unknown report included. The backfill's settle condition counts fewer: only a member whose module is active for the
/// family (<see cref="ReadabilityEntry.ModuleActive"/>; spec 186, FR-012).
/// </summary>
/// <remarks>
/// Under the in-process provider the fleet is this host alone, so a version this host reads finalizes at once
/// (FR-021). Under a durable provider every live member that reports the family is counted.
/// </remarks>
public sealed class ClusterSchemaFleet(IClusterMembership membership, IOptions<ClusterMembershipOptions>? options = null) : IEfSchemaFleet
{
    /// <summary>
    /// The membership expiry period plus the skew allowance (spec 186, FR-012; spec 183, FR-006), read when the backfill
    /// asks rather than when the fleet is built: the finalization gate resolves the fleet at module activation, and reading
    /// the membership settings there would run the provider's own validation of them a second time, ahead of the provider.
    /// </summary>
    public TimeSpan SettleMargin
    {
        get
        {
            var settings = options?.Value ?? new ClusterMembershipOptions();
            return settings.ExpiryPeriod + settings.SkewAllowance;
        }
    }

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

    public async ValueTask<EfSchemaFleetAnswer> CountObservingAsync(
        string family,
        IReadOnlyList<string> versions,
        string databaseIdentity,
        CancellationToken cancellationToken = default)
    {
        var answer = await membership.QueryAsync(
            MemberQuery.Counting(new ObservesFinalizedSchemaVersion(family, versions, databaseIdentity)),
            FleetReadMode.Fresh,
            cancellationToken);
        return new EfSchemaFleetAnswer(
            answer.EveryConsideredMemberMatches,
            answer.Failures.Select(failure => DescribeObserved(failure.Member, family, databaseIdentity)).ToArray());
    }

    public IChangeToken GetChangeToken() => membership.GetChangeToken();

    /// <summary>
    /// A counted member that has not observed the version yet, and what it has observed instead, for the backfill's status
    /// (spec 186, FR-021): from the entries whose module is active, the only ones the settle condition counts.
    /// </summary>
    private static string DescribeObserved(FleetMember member, string family, string databaseIdentity)
    {
        if (member.Report.IsUnknown || member.Report.Readability is null)
            return $"{member.Identity} reports what it writes in a form this host cannot interpret";
        var observed = member.Report.Readability.Entries
            .Where(entry => entry.ModuleActive && string.Equals(entry.Family, family, StringComparison.Ordinal) && entry.AppliesTo(databaseIdentity))
            .Select(entry => entry.ObservedFinalizedVersion ?? "nothing")
            .Distinct(StringComparer.Ordinal);
        return $"{member.Identity} has observed [{string.Join(", ", observed)}]";
    }

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
