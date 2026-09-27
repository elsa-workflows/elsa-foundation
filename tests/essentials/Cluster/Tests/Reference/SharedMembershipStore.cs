using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing;

namespace Elsa.Cluster.Tests.Reference;

/// <summary>
/// The reference provider's authoritative store: every write is visible to the next read, as a committed row is on the
/// primary of a database.
/// </summary>
internal sealed class SharedMembershipStore
{
    private readonly object _gate = new();
    private readonly List<StoredMember> _entries = [];
    private long _sequence;

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>
    /// Inserts a new incarnation and displaces the earlier ones of its host id, unless the most recent one is still
    /// heartbeating: renewed within one heartbeat interval and the skew allowance (FR-004a, FR-004b).
    /// </summary>
    public void Join(ClusterMemberIdentity identity, MemberReport report, DateTimeOffset now, ConformanceTimings timings)
    {
        lock (_gate)
        {
            var sameHost = _entries.Where(entry => entry.Identity.HostId == identity.HostId).ToArray();
            var latest = sameHost.MaxBy(entry => entry.Sequence);
            if (latest is not null && latest.Status != MemberStatus.Left && now - latest.HeartbeatAt <= timings.HeartbeatInterval + timings.SkewAllowance)
                throw new ClusterMembershipJoinRefusedException(identity.HostId);

            foreach (var earlier in sameHost)
                Replace(earlier with { Displaced = true });
            _entries.Add(new StoredMember(identity, ++_sequence, MemberStatus.Joining, now, timings.ExpiryPeriod, Displaced: false, report, ReportRevision: 1, LeftAt: null));
        }
    }

    /// <summary>Renews an entry and returns it, or returns it unrenewed when it was displaced or left, or
    /// <see langword="null"/> when it is missing.</summary>
    public StoredMember? Renew(ClusterMemberIdentity identity, DateTimeOffset heartbeatAt)
    {
        lock (_gate)
        {
            var entry = Find(identity);
            return entry is null || entry.Displaced || entry.Status == MemberStatus.Left ? entry : Replace(entry with { HeartbeatAt = heartbeatAt });
        }
    }

    public void SetStatus(ClusterMemberIdentity identity, MemberStatus status, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (Find(identity) is { } entry)
                Replace(entry with { Status = status, LeftAt = status == MemberStatus.Left ? now : null });
        }
    }

    /// <summary>Stores a report and returns its revision, which rises only when the report changed.</summary>
    public long Publish(ClusterMemberIdentity identity, MemberReport report)
    {
        lock (_gate)
        {
            var entry = Find(identity) ?? throw new InvalidOperationException($"{identity} has no entry.");
            return Equals(entry.Report, report) ? entry.ReportRevision : Replace(entry with { Report = report, ReportRevision = entry.ReportRevision + 1 }).ReportRevision;
        }
    }

    public long RevisionOf(ClusterMemberIdentity identity)
    {
        lock (_gate)
            return Find(identity)?.ReportRevision ?? 0;
    }

    public IReadOnlyList<StoredMember> ReadAll()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    /// <summary>Writes an entry whose report no reader can interpret, as a newer provider version would.</summary>
    public void PlantUninterpretable(string hostId, DateTimeOffset now, TimeSpan expiryPeriod)
    {
        lock (_gate)
            _entries.Add(new StoredMember(new ClusterMemberIdentity(hostId, MemberIncarnation.New()), ++_sequence, MemberStatus.Active, now, expiryPeriod, Displaced: false, Report: null, ReportRevision: 1, LeftAt: null));
    }

    /// <summary>Deletes entries that have been left, or expired, for longer than the cleanup period (FR-031).</summary>
    public void Cleanup(DateTimeOffset now, TimeSpan cleanupPeriod, TimeSpan skewAllowance)
    {
        lock (_gate)
            _entries.RemoveAll(entry =>
                entry.LeftAt is { } leftAt ? now - leftAt > cleanupPeriod : now - MemberLiveness.ExpiresAfter(entry.HeartbeatAt, entry.ExpiryPeriod, skewAllowance) > cleanupPeriod);
    }

    private StoredMember? Find(ClusterMemberIdentity identity) => _entries.Find(entry => entry.Identity == identity);

    private StoredMember Replace(StoredMember entry)
    {
        _entries[_entries.FindIndex(existing => existing.Identity == entry.Identity)] = entry;
        return entry;
    }
}

/// <summary>One stored entry. A <see langword="null"/> report is one no reader can interpret.</summary>
internal sealed record StoredMember(
    ClusterMemberIdentity Identity,
    long Sequence,
    MemberStatus Status,
    DateTimeOffset HeartbeatAt,
    TimeSpan ExpiryPeriod,
    bool Displaced,
    MemberReport? Report,
    long ReportRevision,
    DateTimeOffset? LeftAt);
