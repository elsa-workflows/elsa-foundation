using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// What <c>dotnet elsa persistence status</c> reads the cluster's members with: the membership table of the database the
/// command's connection reaches, read as any reader of the fleet reads it (spec 183, FR-012), so an entry this build cannot
/// interpret is listed and blocks every version rather than being dropped. The tool finds it beside the module, in this
/// assembly (<see cref="IEfToolingFleetSource"/>).
/// </summary>
/// <remarks>
/// Liveness is judged on this process's clock with the provider's default skew allowance, because the tool reads no host
/// configuration; a host configured with another allowance may count a member the tool lists as expired, or the reverse,
/// within that difference. Which members block a version is the counting query the finalization gate itself asks
/// (<see cref="ReadsSchemaVersion"/>), so the tool and the gate cannot disagree about who counts or what reading means.
/// </remarks>
public sealed class EfClusterMembershipToolingSource(TimeProvider clock) : IEfToolingFleetSource
{
    public EfClusterMembershipToolingSource() : this(TimeProvider.System)
    {
    }

    public string ModuleName => ClusterMembershipEfModule.Name;

    public async Task<EfToolingFleet> ReadAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is not ClusterMembershipDbContext membership)
            throw new ArgumentException($"Expected a {nameof(ClusterMembershipDbContext)}, not {context.GetType().Name}.", nameof(context));

        var skew = new ClusterMembershipOptions().SkewAllowance;
        var judgedAt = clock.GetUtcNow();
        var stored = (await membership.Members.AsNoTracking().ToListAsync(cancellationToken)).Select(StoredMember.From).ToArray();
        var view = new FleetView(
            ClusterProviderKind.Durable,
            FleetReadMode.Fresh,
            judgedAt,
            stored.Select(member => new FleetMember(
                member.Identity,
                member.Status,
                member.HeartbeatAt,
                member.ExpiryPeriod,
                member.IsLive(judgedAt, skew),
                IsDisplaced: !member.IsCurrent,
                member.Report,
                member.ReportRevision,
                [])).ToArray());

        return new EfToolingFleet(
            judgedAt,
            skew,
            [.. view.Members
                .OrderBy(member => member.HostId, StringComparer.Ordinal)
                .ThenByDescending(member => member.LastHeartbeatAt)
                .Select(Describe)],
            (family, databaseIdentity, version) => MemberQuery
                .Counting(new ReadsSchemaVersion(family, version, databaseIdentity))
                .Evaluate(view)
                .Failures
                .Select(failure => new EfToolingWaitingOn
                {
                    HostId = failure.Member.HostId,
                    Incarnation = failure.Member.Identity.Incarnation.Value,
                    ReportReadable = HasReadableReport(failure.Member),
                    Reads = [.. ReadsOf(failure.Member, family, databaseIdentity)]
                })
                .ToArray());
    }

    private static bool HasReadableReport(FleetMember member) => member.Report is { IsUnknown: false, Readability: not null };

    private static EfToolingClusterMember Describe(FleetMember member) => new()
    {
        HostId = member.HostId,
        Incarnation = member.Identity.Incarnation.Value,
        Status = member.Status.ToString(),
        Live = member.IsLive,
        Displaced = member.IsDisplaced,
        LastHeartbeatAt = member.LastHeartbeatAt,
        ReportReadable = HasReadableReport(member),
        Reads = member.Report.Readability?.Entries
            .Select(entry => new EfToolingClusterReads { Family = entry.Family, DatabaseIdentity = entry.DatabaseIdentity, Versions = entry.ReadableVersions })
            .OrderBy(reads => reads.Family, StringComparer.Ordinal)
            .ToArray() ?? []
    };

    /// <summary>The versions of <paramref name="family"/> the member reads for the database, across the entries that speak for it.</summary>
    private static IEnumerable<string> ReadsOf(FleetMember member, string family, string? databaseIdentity) =>
        member.Report.Readability?.Entries
            .Where(entry => string.Equals(entry.Family, family, StringComparison.Ordinal) && entry.AppliesTo(databaseIdentity))
            .SelectMany(entry => entry.ReadableVersions)
            .Distinct(StringComparer.Ordinal) ?? [];
}
