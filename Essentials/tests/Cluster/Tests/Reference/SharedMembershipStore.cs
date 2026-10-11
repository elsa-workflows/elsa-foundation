using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.Tests.Reference;

/// <summary>
/// The reference provider's authoritative store: every write is visible to the next read, as a committed row is on the
/// primary of a database.
/// </summary>
internal sealed class SharedMembershipStore
{
    private readonly object _gate = new();
    private readonly List<StoredMember> _entries = [];
    private readonly Dictionary<string, RefusalWatch> _refusalWatches = new(StringComparer.Ordinal);
    private long _sequence;
    private CancellationTokenSource _changeSource = new();

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
    /// live by FR-006's own liveness rule (heartbeat, expiry and skew): only a non-live (expired or left) earlier
    /// incarnation may be displaced (FR-004a, FR-004b).
    /// </summary>
    /// <remarks>
    /// A refusal while the incumbent is live is retryable: <see cref="ClusterMembershipJoinRefusedException"/>, so the
    /// caller waits and tries again. But a joiner that has now been refused throughout one full liveness window since
    /// its first observation of this exact incumbent, with the incumbent renewing the whole time, is not looking at a
    /// crash to wait out: it is a live duplicate. That escalates to <see cref="ClusterMembershipDuplicateHostIdException"/>,
    /// which the caller MUST NOT retry (FR-004b, FR-039, #2097 review).
    /// </remarks>
    public void Join(ClusterMemberIdentity identity, MemberReport report, DateTimeOffset now, ConformanceTimings timings)
    {
        lock (_gate)
        {
            var sameHost = _entries.Where(entry => entry.Identity.HostId == identity.HostId).ToArray();
            var latest = sameHost.MaxBy(entry => entry.Sequence);
            if (latest is not null && MemberLiveness.IsLive(latest.Status, latest.HeartbeatAt, latest.ExpiryPeriod, timings.SkewAllowance, now))
            {
                if (!_refusalWatches.TryGetValue(identity.HostId, out var watch) || watch.Incumbent != latest.Identity)
                    _refusalWatches[identity.HostId] = new RefusalWatch(latest.Identity, now);
                else if (now - watch.FirstObservedAt >= latest.ExpiryPeriod + timings.SkewAllowance)
                    throw new ClusterMembershipDuplicateHostIdException(
                        identity.HostId,
                        new MemberCondition(
                            MemberConditionKind.DuplicateHostId,
                            [identity.HostId],
                            $"Host id '{identity.HostId}' has been claimed by a live process for a full liveness window; it kept renewing throughout it."));

                throw new ClusterMembershipJoinRefusedException(identity.HostId, latest.Identity.Incarnation, latest.HeartbeatAt);
            }

            _refusalWatches.Remove(identity.HostId);
            foreach (var earlier in sameHost)
                Replace(earlier with { Displaced = true });
            _entries.Add(new StoredMember(identity, ++_sequence, MemberStatus.Joining, now, timings.ExpiryPeriod, Displaced: false, report, ReportRevision: 1, LeftAt: null));
            SignalChanged();
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
            if (Find(identity) is not { } entry)
                return;

            Replace(entry with { Status = status, LeftAt = status == MemberStatus.Left ? now : null });
            SignalChanged();
        }
    }

    /// <summary>Stores a report and returns its revision, which rises only when the report changed.</summary>
    public long Publish(ClusterMemberIdentity identity, MemberReport report)
    {
        lock (_gate)
        {
            var entry = Find(identity) ?? throw new InvalidOperationException($"{identity} has no entry.");
            if (Equals(entry.Report, report))
                return entry.ReportRevision;

            var revision = Replace(entry with { Report = report, ReportRevision = entry.ReportRevision + 1 }).ReportRevision;
            SignalChanged();
            return revision;
        }
    }

    /// <summary>
    /// A change token that completes once, the next time this store observes a fleet change: a join, a status change
    /// (including a leave) or a report change (FR-013). Call this again after it completes to observe the next one, as
    /// with <see cref="IChangeToken"/> elsewhere in the framework (Elsa.Caching). It carries no dependency on the
    /// Events feature.
    /// </summary>
    public IChangeToken GetChangeToken()
    {
        lock (_gate)
            return new CancellationChangeToken(_changeSource.Token);
    }

    /// <summary>Fires the current change token and starts a new one. Called with <see cref="_gate"/> already held.</summary>
    private void SignalChanged()
    {
        var previous = _changeSource;
        _changeSource = new CancellationTokenSource();
        previous.Cancel();
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

/// <summary>When a joiner first observed <see cref="Incumbent"/> refusing it, so a later join for the same host id can
/// tell a live duplicate (refused throughout a full liveness window) from a fresh crash (FR-004b, FR-039).</summary>
internal sealed record RefusalWatch(ClusterMemberIdentity Incumbent, DateTimeOffset FirstObservedAt);
