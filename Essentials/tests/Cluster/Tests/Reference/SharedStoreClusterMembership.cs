using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.Tests.Reference;

/// <summary>
/// The deliberately broken variants of the reference provider (FR-060). Each must fail a named conformance test.
/// </summary>
public enum SharedStoreFault
{
    None,

    /// <summary>Judges a member expired when its expiry period has passed, ignoring the skew allowance.</summary>
    EarlyExpiry,

    /// <summary>Drops the last member from every fresh read of more than one.</summary>
    Truncate,

    /// <summary>Returns from a publish before the report is stored; the next heartbeat stores it.</summary>
    DeferPublish,

    /// <summary>Replaces whatever provider is already composed instead of refusing it.</summary>
    LastWriteWins
}

/// <summary>
/// A reference durable provider over <see cref="SharedMembershipStore"/>, driven by the conformance fixture's clocks. It
/// is how the conformance suite's multi-member tests are shown to pass for a correct provider and to fail for each
/// broken one, before any real durable provider exists.
/// </summary>
internal sealed class SharedStoreClusterMembership(
    SharedMembershipStore store,
    string hostId,
    ConformanceTimings timings,
    TimeProvider clock,
    IMemberReportSource<ReadabilitySection>? readability,
    IMemberReportSource<RunnabilitySection>? runnability,
    SharedStoreFault fault) : IClusterMembership
{
    private DateTimeOffset _lastSuccessWall;
    private long _lastSuccessMonotonic;
    private MemberLapse? _lapse;
    private FleetView? _cached;
    private MemberReport? _deferred;
    private bool _hadFailedFreshRead;

    public ClusterMemberIdentity Identity { get; } = new(hostId, MemberIncarnation.New());

    public MemberStatus Status { get; private set; } = MemberStatus.Joining;

    public bool IsKilled { get; private set; }

    public bool IsIsolated { get; private set; }

    public ClusterProviderKind ProviderKind => ClusterProviderKind.Durable;

    public async ValueTask JoinAsync(CancellationToken cancellationToken = default)
    {
        var report = await MemberReportComposition.ComposeAsync(readability, runnability, cancellationToken);
        var now = clock.GetUtcNow();
        store.Join(Identity, report, now, timings);
        _lastSuccessWall = now;
        _lastSuccessMonotonic = clock.GetTimestamp();
    }

    public void SetStatus(MemberStatus status)
    {
        Status = status;
        _cached = null;
        store.SetStatus(Identity, status, clock.GetUtcNow());
    }

    public void Kill() => IsKilled = true;

    public void Isolate() => IsIsolated = true;

    /// <summary>
    /// Renews the entry from the start of the heartbeat, republishes the report, and refreshes the cached view. An
    /// isolated member's attempt fails; a killed or departed one makes none.
    /// </summary>
    public async ValueTask HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        if (IsKilled || IsIsolated || Status == MemberStatus.Left)
            return;

        var start = clock.GetUtcNow();
        var startMonotonic = clock.GetTimestamp();
        var entry = store.Renew(Identity, start);
        if (entry is null || entry.Displaced)
        {
            _lapse ??= new MemberLapse(entry is null ? MemberLapseReason.EntryMissing : MemberLapseReason.DuplicateHostId, start);
            return;
        }

        _lastSuccessWall = start;
        _lastSuccessMonotonic = startMonotonic;
        store.Publish(Identity, _deferred ?? await MemberReportComposition.ComposeAsync(readability, runnability, cancellationToken));
        _deferred = null;
        _cached = View(FleetReadMode.Cached);
    }

    public LocalMemberStanding GetLocalStanding()
    {
        var wallElapsed = clock.GetUtcNow() - _lastSuccessWall;
        var monotonicElapsed = clock.GetElapsedTime(_lastSuccessMonotonic);
        if (_lapse is null && Status != MemberStatus.Left && MemberLiveness.HasLapsed(wallElapsed, monotonicElapsed, timings.ExpiryPeriod))
            _lapse = new MemberLapse(MemberLapseReason.ExpiryPassed, clock.GetUtcNow());
        return new LocalMemberStanding(Identity, Status, _lapse);
    }

    public ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default)
    {
        if (mode == FleetReadMode.Cached && _cached is { } cached)
            return ValueTask.FromResult(_hadFailedFreshRead ? WithFailedFreshRead(cached) : cached);
        if (IsIsolated)
        {
            _hadFailedFreshRead = true;
            throw new ClusterMembershipReadException($"Member {Identity} cannot reach the membership store.");
        }

        var view = View(mode);
        _hadFailedFreshRead = false;
        return ValueTask.FromResult(view);
    }

    public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default)
    {
        if (IsIsolated)
            throw new InvalidOperationException($"Member {Identity} cannot reach the membership store.");

        var report = await MemberReportComposition.ComposeAsync(readability, runnability, cancellationToken);
        if (fault != SharedStoreFault.DeferPublish)
            return new PublishedMemberReport(Identity, report, store.Publish(Identity, report));

        _deferred = report;
        return new PublishedMemberReport(Identity, report, store.RevisionOf(Identity) + 1);
    }

    public async ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default) =>
        query.Evaluate(await ReadFleetAsync(mode, cancellationToken));

    /// <summary>
    /// A change token that completes once, the next time the shared store observes a fleet change (FR-013). Call this
    /// again after it completes to observe the next one.
    /// </summary>
    public IChangeToken GetChangeToken() => store.GetChangeToken();

    private FleetView View(FleetReadMode mode)
    {
        var now = clock.GetUtcNow();
        var skewAllowance = fault == SharedStoreFault.EarlyExpiry ? TimeSpan.Zero : timings.SkewAllowance;
        var members = store.ReadAll()
            .Select(entry => new FleetMember(
                entry.Identity,
                entry.Status,
                entry.HeartbeatAt,
                entry.ExpiryPeriod,
                MemberLiveness.IsLive(entry.Status, entry.HeartbeatAt, entry.ExpiryPeriod, skewAllowance, now),
                entry.Displaced,
                entry.Report ?? MemberReport.Unknown,
                entry.ReportRevision,
                Conditions(entry, now, skewAllowance)))
            .ToList();
        if (fault == SharedStoreFault.Truncate && members.Count > 1)
            members.RemoveAt(members.Count - 1);
        return new FleetView(ClusterProviderKind.Durable, mode, now, members);
    }

    /// <summary>
    /// The FR-037 to FR-042 conditions this reader observes about <paramref name="entry"/> from data already in the
    /// store, without a separate write for each: a lapse and a displacement are visible because they change what is
    /// stored (FR-004a, FR-007); an uninterpretable entry, because its report is missing; a clock skew, by comparing
    /// the entry's own heartbeat time to this reader's clock. FR-039 (a duplicate host id: a displacement while the
    /// member's own heartbeats were still succeeding) cannot occur for this store: <see cref="SharedMembershipStore.Join"/>
    /// holds one lock across the liveness check and the displacement, so a live incumbent is never displaced.
    /// </summary>
    private IReadOnlyList<MemberCondition> Conditions(StoredMember entry, DateTimeOffset now, TimeSpan skewAllowance)
    {
        var isLive = MemberLiveness.IsLive(entry.Status, entry.HeartbeatAt, entry.ExpiryPeriod, skewAllowance, now);
        List<MemberCondition>? conditions = null;
        void Add(MemberConditionKind kind, IReadOnlyList<string> hostIds, string message) => (conditions ??= []).Add(new MemberCondition(kind, hostIds, message));

        if (entry.Displaced)
            Add(MemberConditionKind.Displaced, [entry.Identity.HostId], $"A later incarnation of host id '{entry.Identity.HostId}' has joined; this incarnation is displaced.");

        if (!isLive && entry.Status != MemberStatus.Left)
            Add(MemberConditionKind.Lapsed, [entry.Identity.HostId], $"Host id '{entry.Identity.HostId}' has not renewed within its expiry period and the skew allowance; it has lapsed.");

        if (entry.Report is null)
            Add(MemberConditionKind.UninterpretableEntry, [entry.Identity.HostId], $"This reader cannot interpret the entry for host id '{entry.Identity.HostId}'.");

        if (entry.Identity.HostId != Identity.HostId && entry.HeartbeatAt > now + skewAllowance)
            Add(MemberConditionKind.ClockSkew, [Identity.HostId, entry.Identity.HostId], $"Host id '{entry.Identity.HostId}' heartbeated ahead of host id '{Identity.HostId}''s clock by more than the skew allowance.");

        if (entry.Identity == Identity && _hadFailedFreshRead)
            Add(MemberConditionKind.FailedFreshRead, [Identity.HostId], $"The previous fresh read by host id '{Identity.HostId}' failed.");

        return conditions ?? [];
    }

    /// <summary>Attaches a <see cref="MemberConditionKind.FailedFreshRead"/> condition to this reader's own entry in
    /// <paramref name="cached"/>, and clears the flag so it is reported once (FR-042).</summary>
    private FleetView WithFailedFreshRead(FleetView cached)
    {
        _hadFailedFreshRead = false;
        var members = cached.Members
            .Select(member => member.Identity == Identity
                ? member with { Conditions = [.. member.Conditions, new MemberCondition(MemberConditionKind.FailedFreshRead, [Identity.HostId], $"The previous fresh read by host id '{Identity.HostId}' failed.")] }
                : member)
            .ToList();
        return cached with { Members = members };
    }
}
