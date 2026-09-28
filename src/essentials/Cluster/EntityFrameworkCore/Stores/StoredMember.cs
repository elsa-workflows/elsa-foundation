using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.EntityFrameworkCore.Stores;

/// <summary>
/// One membership row as this build reads it: the envelope every provider version writes (host id, incarnation, whether
/// it is its host id's current incarnation, heartbeat time and expiry period), and what this build could interpret of
/// the rest.
/// </summary>
/// <remarks>
/// <para>
/// A row stamped with another schema version, a status this build does not know, or a report it cannot parse is
/// <em>uninterpretable</em> (spec 183, FR-012). Its report is <see cref="MemberReport.Unknown"/>, which counts as
/// reading nothing, and nothing but its envelope is trusted: it is judged <see cref="MemberStatus.Active"/> and live
/// until its own heartbeat and expiry pass, whatever status it claims. That is the conservative direction: such an entry
/// blocks every readability answer while it may still be alive, and never leaves early because of a word this build
/// cannot be sure it reads the same way.
/// </para>
/// <para>
/// A row whose envelope cannot even form an identity (a blank or over-long host id, a blank incarnation) fails the
/// whole read instead: nothing can be said about it, and leaving it out would be the partial view FR-012 forbids.
/// </para>
/// </remarks>
internal sealed record StoredMember(
    ClusterMemberIdentity Identity,
    bool IsCurrent,
    MemberStatus Status,
    DateTimeOffset HeartbeatAt,
    TimeSpan ExpiryPeriod,
    MemberReport Report,
    long ReportRevision,
    bool IsInterpretable)
{
    public static StoredMember From(ClusterMemberEntity row)
    {
        var identity = new ClusterMemberIdentity(row.HostId, new MemberIncarnation(row.Incarnation));
        var heartbeatAt = new DateTimeOffset(row.HeartbeatAtUtcTicks, TimeSpan.Zero);
        var expiryPeriod = TimeSpan.FromTicks(row.ExpiryPeriodTicks);
        var interpreted = Interpret(row);
        return new StoredMember(
            identity,
            row.CurrentHostId is not null,
            interpreted?.Status ?? MemberStatus.Active,
            heartbeatAt,
            expiryPeriod,
            interpreted?.Report ?? MemberReport.Unknown,
            row.ReportRevision,
            interpreted is not null);
    }

    /// <summary>Whether the row says, in terms this build reads, that the member left.</summary>
    public bool HasLeft => IsInterpretable && Status == MemberStatus.Left;

    /// <summary><see cref="HasLeft"/> for a row read for writing, which needs no identity formed to answer.</summary>
    public static bool SaysLeft(ClusterMemberEntity row) => Interpret(row)?.Status == MemberStatus.Left;

    /// <summary>Whether a reader whose clock shows <paramref name="now"/> judges this member live (FR-006, FR-028).</summary>
    public bool IsLive(DateTimeOffset now, TimeSpan skewAllowance) =>
        MemberLiveness.IsLive(Status, HeartbeatAt, ExpiryPeriod, skewAllowance, now);

    private static (MemberStatus Status, MemberReport Report)? Interpret(ClusterMemberEntity row)
    {
        if (!EfSchemaVersion.IsReadable(row.SchemaVersion, ClusterMembershipEfModule.SchemaVersion))
            return null;
        if (!Enum.TryParse<MemberStatus>(row.Status, ignoreCase: false, out var status) || !Enum.IsDefined(status) || int.TryParse(row.Status, out _))
            return null;

        return MemberReportJson.Read(row.ReportJson) is { } report ? (status, report) : null;
    }
}
