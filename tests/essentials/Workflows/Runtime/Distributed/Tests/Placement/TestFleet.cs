using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.Primitives;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// A fleet whose every fact a test sets directly: which members exist, their status, liveness and lapse, what each
/// reports, what a cached read shows, and whether a fresh read fails. It stands in for a durable membership provider,
/// so the placement tests can drive the exact fleet views spec 184 reasons about.
/// </summary>
internal sealed class TestFleet(TimeProvider clock)
{
    private readonly List<Entry> _entries = [];
    private FleetView? _cached;

    /// <summary>When set, every fresh read fails, as it does while the membership store is unreachable.</summary>
    public bool FreshReadsFail { get; set; }

    /// <summary>Joins a new incarnation of <paramref name="hostId"/>, displacing its previous one, and makes it active.</summary>
    public TestMember Join(string hostId)
    {
        foreach (var previous in _entries.Where(entry => entry.Identity.HostId == hostId))
            previous.Displaced = true;
        var entry = new Entry(new ClusterMemberIdentity(hostId, MemberIncarnation.New()));
        _entries.Add(entry);
        return new TestMember(this, entry);
    }

    /// <summary>Removes every entry of <paramref name="hostId"/>, as a fleet that never saw it would show.</summary>
    public void Forget(string hostId) => _entries.RemoveAll(entry => entry.Identity.HostId == hostId);

    /// <summary>A cached read shows the fleet as it is now until the next <see cref="Snapshot"/>.</summary>
    public void Snapshot() => _cached = View(FleetReadMode.Cached);

    /// <summary>A cached read shows the fleet as it is now at every read.</summary>
    public void Uncache() => _cached = null;

    public FleetView Read(FleetReadMode mode)
    {
        if (mode == FleetReadMode.Cached)
            return _cached ?? View(FleetReadMode.Cached);
        if (FreshReadsFail)
            throw new ClusterMembershipReadException("The membership store is unreachable.");
        return View(FleetReadMode.Fresh);
    }

    private FleetView View(FleetReadMode mode) =>
        new(
            ClusterProviderKind.Durable,
            mode,
            clock.GetUtcNow(),
            _entries.Select(entry => new FleetMember(
                entry.Identity,
                entry.Status,
                clock.GetUtcNow(),
                TimeSpan.FromSeconds(30),
                IsLive: entry.IsLive && entry.Status != MemberStatus.Left,
                entry.Displaced,
                entry.Report,
                entry.Revision,
                Conditions: [])).ToArray());

    internal sealed class Entry(ClusterMemberIdentity identity)
    {
        public ClusterMemberIdentity Identity { get; } = identity;
        public MemberStatus Status { get; set; } = MemberStatus.Active;
        public bool IsLive { get; set; } = true;
        public bool Displaced { get; set; }
        public MemberLapse? Lapse { get; set; }
        public MemberReport Report { get; set; } = MemberReport.Empty;
        public long Revision { get; set; }
    }
}

/// <summary>One member of a <see cref="TestFleet"/>, as the membership its distributed runtime is composed on.</summary>
internal sealed class TestMember(TestFleet fleet, TestFleet.Entry entry) : IClusterMembership
{
    private IMemberReportSource<RunnabilitySection>? _runnability;

    public ClusterProviderKind ProviderKind => ClusterProviderKind.Durable;

    public ClusterMemberIdentity Identity => entry.Identity;

    /// <summary>Reads this member's runnability section from <paramref name="source"/> on every publish.</summary>
    public void ReportFrom(IMemberReportSource<RunnabilitySection> source) => _runnability = source;

    public void Become(MemberStatus status) => entry.Status = status;

    /// <summary>Its entry stops being live, as a crashed member's does once it expires.</summary>
    public void Expire() => entry.IsLive = false;

    /// <summary>Its entry is live again, as a member's is when a stalled heartbeat lands before a fresh read.</summary>
    public void Revive() => entry.IsLive = true;

    /// <summary>It concludes that it lapsed, as it does when it cannot reach the membership store (FR-007).</summary>
    public void Lapse(DateTimeOffset at) => entry.Lapse = new MemberLapse(MemberLapseReason.ExpiryPassed, at);

    public LocalMemberStanding GetLocalStanding() => new(entry.Identity, entry.Status, entry.Lapse);

    public ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(fleet.Read(mode));

    public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default)
    {
        var report = new MemberReport(runnability: _runnability is null ? null : await _runnability.ReadAsync(cancellationToken));
        if (!report.Equals(entry.Report))
        {
            entry.Report = report;
            entry.Revision++;
        }

        return new PublishedMemberReport(entry.Identity, entry.Report, entry.Revision);
    }

    public ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(query.Evaluate(fleet.Read(mode)));

    public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
}
