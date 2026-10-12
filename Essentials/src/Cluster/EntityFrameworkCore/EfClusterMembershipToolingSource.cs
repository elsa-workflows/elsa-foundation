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
/// Liveness is judged on this process's clock with the skew allowance the tool names, or the provider's default when it
/// names none. A host configured with another allowance may count a member the tool lists as expired, or the reverse,
/// within that difference. Each member is judged by <see cref="StoredMember.ToFleetMember"/>, the judgement the provider's
/// own fleet view uses, and which members block a version is the counting query the finalization gate itself asks
/// (<see cref="ReadsSchemaVersion"/>), so the tool and the gate cannot disagree about who counts or what reading means.
/// </remarks>
public sealed class EfClusterMembershipToolingSource(TimeProvider clock) : IEfToolingFleetSource
{
    public EfClusterMembershipToolingSource() : this(TimeProvider.System)
    {
    }

    public string ModuleName => ClusterMembershipEfModule.Name;

    public async Task<EfToolingFleet> ReadAsync(DbContext context, TimeSpan? skewAllowance = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is not ClusterMembershipDbContext membership)
            throw new ArgumentException($"Expected a {nameof(ClusterMembershipDbContext)}, not {context.GetType().Name}.", nameof(context));

        var skew = skewAllowance ?? new ClusterMembershipOptions().SkewAllowance;
        var judgedAt = clock.GetUtcNow();
        var judged = (await membership.Members.AsNoTracking().ToListAsync(cancellationToken))
            .Select(StoredMember.From)
            .Select(stored => (Stored: stored, Member: stored.ToFleetMember(judgedAt, skew)))
            .ToArray();
        var view = new FleetView(ClusterProviderKind.Durable, FleetReadMode.Fresh, judgedAt, [.. judged.Select(judgement => judgement.Member)]);

        return new EfToolingFleet(
            judgedAt,
            skew,
            families => [.. judged
                .OrderBy(judgement => judgement.Member.HostId, StringComparer.Ordinal)
                .ThenByDescending(judgement => judgement.Member.LastHeartbeatAt)
                .Select(judgement => Describe(judgement.Stored, judgement.Member, families))],
            (family, databaseIdentity, version) => MemberQuery
                .Counting(new ReadsSchemaVersion(family, version, databaseIdentity))
                .Evaluate(view)
                .Failures
                .Select(failure => new EfToolingWaitingOn
                {
                    HostId = failure.Member.HostId,
                    Incarnation = failure.Member.Identity.Incarnation.Value,
                    ReportReadable = HasReadableReport(failure.Member),
                    Reads = [.. ReadsOf(failure.Member, family, databaseIdentity) ?? []]
                })
                .ToArray());
    }

    private static bool HasReadableReport(FleetMember member) => member.Report is { IsUnknown: false, Readability: not null };

    /// <summary>
    /// The member as the tool lists it. A row this build cannot interpret says its status is unknown: the status it defaults
    /// to for judging is not one the row stated.
    /// </summary>
    private static EfToolingClusterMember Describe(StoredMember stored, FleetMember member, IReadOnlyList<EfToolingFamilyDatabase> families) => new()
    {
        HostId = member.HostId,
        Incarnation = member.Identity.Incarnation.Value,
        Status = stored.IsInterpretable ? member.Status.ToString() : EfToolingClusterMember.UnknownStatus,
        Live = member.IsLive,
        Displaced = member.IsDisplaced,
        LastHeartbeatAt = member.LastHeartbeatAt,
        ReportReadable = HasReadableReport(member),
        Reads =
        [
            .. families.Select(family => (family.Family, Versions: ReadsOf(member, family.Family, family.DatabaseIdentity)))
                .Where(reads => reads.Versions is not null)
                .Select(reads => new EfToolingClusterReads { Family = reads.Family, Versions = [.. reads.Versions!] })
        ]
    };

    /// <summary>
    /// The versions of <paramref name="family"/> the member reads for the database, across the entries that speak for it, or
    /// <see langword="null"/> when it reports no entry that does, or nothing this build can interpret.
    /// </summary>
    private static IEnumerable<string>? ReadsOf(FleetMember member, string family, string? databaseIdentity)
    {
        var entries = member.Report.Readability?.Entries
            .Where(entry => string.Equals(entry.Family, family, StringComparison.Ordinal) && entry.AppliesTo(databaseIdentity))
            .ToArray();
        return entries is { Length: > 0 } ? entries.SelectMany(entry => entry.ReadableVersions).Distinct(StringComparer.Ordinal) : null;
    }
}
