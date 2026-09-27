using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing;

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
    SharedStoreFault fault) : IClusterMembership
{
    private DateTimeOffset _lastSuccessWall;
    private long _lastSuccessMonotonic;
    private MemberLapse? _lapse;
    private FleetView? _cached;
    private MemberReport? _deferred;

    public ClusterMemberIdentity Identity { get; } = new(hostId, MemberIncarnation.New());

    public MemberStatus Status { get; private set; } = MemberStatus.Joining;

    public bool IsKilled { get; private set; }

    public bool IsIsolated { get; private set; }

    public ClusterProviderKind ProviderKind => ClusterProviderKind.Durable;

    public async ValueTask JoinAsync(CancellationToken cancellationToken = default)
    {
        var report = await MemberReportComposition.ComposeAsync(readability, cancellationToken);
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
        store.Publish(Identity, _deferred ?? await MemberReportComposition.ComposeAsync(readability, cancellationToken));
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
            return ValueTask.FromResult(cached);
        if (IsIsolated)
            throw new ClusterMembershipReadException($"Member {Identity} cannot reach the membership store.");
        return ValueTask.FromResult(View(mode));
    }

    public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default)
    {
        if (IsIsolated)
            throw new InvalidOperationException($"Member {Identity} cannot reach the membership store.");

        var report = await MemberReportComposition.ComposeAsync(readability, cancellationToken);
        if (fault != SharedStoreFault.DeferPublish)
            return new PublishedMemberReport(Identity, report, store.Publish(Identity, report));

        _deferred = report;
        return new PublishedMemberReport(Identity, report, store.RevisionOf(Identity) + 1);
    }

    public async ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default) =>
        query.Evaluate(await ReadFleetAsync(mode, cancellationToken));

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
                Conditions: []))
            .ToList();
        if (fault == SharedStoreFault.Truncate && members.Count > 1)
            members.RemoveAt(members.Count - 1);
        return new FleetView(ClusterProviderKind.Durable, mode, now, members);
    }
}
