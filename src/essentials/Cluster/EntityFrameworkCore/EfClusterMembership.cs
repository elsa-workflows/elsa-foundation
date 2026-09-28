using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// The durable membership provider: members register, heartbeat and expire through one shared EF table, and the fleet
/// view is read from it (spec 183, B2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant.</b> A fresh read never omits a member that may still read or write, and never credits one with
/// more than it reported. Here that rests on four mechanisms. A publish commits before it returns and a fresh read is
/// one query over the whole table, so a published member is in every later fresh read, and a read that cannot return
/// every row fails instead of returning part (FR-011, FR-012). A member stamps its heartbeat from its own clock at the
/// start of the heartbeat it measures its lapse from, and readers add the skew allowance, so a member knows it lapsed no
/// later than anyone counts it expired (FR-007, FR-008). A join never displaces a live incarnation: the displacement is
/// a compare-and-set on the revision the liveness judgement read, and a unique index allows one current incarnation per
/// host id (FR-004a, FR-004b). And an entry this build cannot interpret is returned with an unknown report, never
/// skipped (FR-012).
/// </para>
/// <para>
/// A host shares one instance between its own container and every shell container built from copies of its
/// registrations, so every shell sees the same member (FR-017). That is why this type is not disposable: a shell
/// container disposing it would stop the host's member. <see cref="EfClusterMembershipLifecycle"/> drives it through the
/// host's lifecycle: joining at start, heartbeating on a timer, draining as the host stops and leaving once it has.
/// </para>
/// </remarks>
public sealed class EfClusterMembership : IClusterMembership
{
    private readonly EfClusterMembershipStore _store;
    private readonly string _hostId;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _expiryPeriod;
    private readonly TimeSpan _skewAllowance;
    private readonly TimeSpan _cleanupPeriod;
    private readonly IMemberReportSource<ReadabilitySection>? _readability;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    // Serializes this member's own writes (join, heartbeat, status and publish), so its row only ever moves forward.
    private readonly SemaphoreSlim _ownWrites = new(1, 1);
    private readonly object _gate = new();

    private ClusterMemberIdentity _identity;
    private MemberStatus _status = MemberStatus.Joining;
    private bool _joined;
    private MemberLapse? _lapse;
    private DateTimeOffset _lastSuccessWall;
    private long _lastSuccessMonotonic;
    private JoinWatch? _watch;
    private ClusterMemberIdentity? _rejoining;
    private FleetView? _cached;
    private bool _hadFailedFreshRead;
    private string? _observedFleet;
    private HashSet<string> _reportedConditions = new(StringComparer.Ordinal);
    private CancellationTokenSource _changes = new();

    public EfClusterMembership(
        IServiceScopeFactory scopes,
        IOptions<ClusterMembershipOptions> options,
        EfClusterMembershipOptions settings,
        IEnumerable<IMemberReportSource<ReadabilitySection>> readabilitySources,
        TimeProvider clock,
        ILogger<EfClusterMembership> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        var membership = options.Value;
        _hostId = membership.HostId is { } hostId
            ? ClusterHostIdConstraints.Validate(hostId, nameof(membership.HostId))
            : throw new ClusterMembershipConfigurationException(
                $"The EF cluster membership provider requires an explicit host id: set {ClusterMembershipOptions.SectionName}:{nameof(membership.HostId)}.");
        _heartbeatInterval = membership.HeartbeatInterval;
        _expiryPeriod = membership.ExpiryPeriod;
        _skewAllowance = membership.SkewAllowance;
        _cleanupPeriod = settings.CleanupPeriod;
        _readability = MemberReportComposition.SingleSource(readabilitySources);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = new EfClusterMembershipStore(scopes);
        _identity = new ClusterMemberIdentity(_hostId, MemberIncarnation.New());
    }

    public ClusterProviderKind ProviderKind => ClusterProviderKind.Durable;

    public LocalMemberStanding GetLocalStanding()
    {
        lock (_gate)
        {
            ConcludeExpiryLapse();
            return new LocalMemberStanding(_identity, _status, _lapse);
        }
    }

    /// <summary>
    /// Makes one attempt to join the fleet as this member's incarnation. A refusal because another incarnation of the host
    /// id is still live throws <see cref="ClusterMembershipJoinRefusedException"/>, and the caller retries it. Once this
    /// member has watched that incarnation renew, it throws <see cref="ClusterMembershipDuplicateHostIdException"/>
    /// instead, and the caller must stop (FR-004b, FR-039).
    /// </summary>
    public async Task JoinAsync(CancellationToken cancellationToken = default)
    {
        await _ownWrites.WaitAsync(cancellationToken);
        try
        {
            ClusterMemberIdentity identity;
            lock (_gate)
            {
                if (_joined)
                    return;
                identity = _identity;
            }

            await JoinAsAsync(identity, previous: null, cancellationToken);
        }
        finally
        {
            _ownWrites.Release();
        }
    }

    /// <summary>
    /// Renews this member's entry from its own clock at the start of the heartbeat, republishes its report if it changed,
    /// and refreshes the cached view (FR-027). A heartbeat the store refuses or cannot take is logged and not retried
    /// here: the next one comes one heartbeat interval later, never widened. A member that concluded it lapsed rejoins
    /// as a new incarnation instead, unless it was displaced, which it never recovers from (FR-007).
    /// </summary>
    public async Task HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        await _ownWrites.WaitAsync(cancellationToken);
        try
        {
            ClusterMemberIdentity identity;
            MemberStatus status;
            MemberLapse? lapse;
            lock (_gate)
            {
                if (!_joined || _status == MemberStatus.Left)
                    return;
                ConcludeExpiryLapse();
                (identity, status, lapse) = (_identity, _status, _lapse);
            }

            if (lapse is not null)
            {
                await RejoinAsync(lapse, status, cancellationToken);
                return;
            }

            var start = _clock.GetUtcNow();
            var startMonotonic = _clock.GetTimestamp();
            var reportJson = MemberReportJson.Write(await ComposeReportAsync(cancellationToken));
            var written = await _store.RenewAsync(identity, start, status, reportJson, cancellationToken);
            if (written.Result == OwnWriteResult.Written)
            {
                lock (_gate)
                    (_lastSuccessWall, _lastSuccessMonotonic) = (start, startMonotonic);
                await RefreshCacheAsync(cancellationToken);
                return;
            }

            LapseFromOwnWrite(identity, written.Result);
        }
        catch (ClusterMembershipStoreException exception)
        {
            _logger.LogWarning(exception, "Cluster member {Member} could not heartbeat; it tries again in {Interval}.", CurrentIdentity(), _heartbeatInterval);
        }
        finally
        {
            _ownWrites.Release();
        }
    }

    /// <summary>The host finished starting: joining becomes active (FR-005).</summary>
    public Task ActivateAsync(CancellationToken cancellationToken = default) => MoveForwardAsync(MemberStatus.Active, cancellationToken);

    /// <summary>The host began to stop: the member drains, and is still counted while it does (FR-030).</summary>
    public Task DrainAsync(CancellationToken cancellationToken = default) => MoveForwardAsync(MemberStatus.Draining, cancellationToken);

    /// <summary>The host stopped: the member leaves, which a crash never writes (FR-030).</summary>
    public Task LeaveAsync(CancellationToken cancellationToken = default) => MoveForwardAsync(MemberStatus.Left, cancellationToken);

    public async ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default)
    {
        if (mode == FleetReadMode.Cached)
        {
            lock (_gate)
            {
                // A cached view lags by at most one heartbeat interval (FR-010); an older one is read afresh.
                if (_cached is { } cached && _clock.GetUtcNow() - cached.JudgedAt <= _heartbeatInterval)
                    return WithFailedFreshReadOnce(cached);
            }
        }

        var view = await ReadFreshAsync(cancellationToken);
        return mode == FleetReadMode.Fresh ? view : view with { ReadMode = FleetReadMode.Cached };
    }

    public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default)
    {
        await _ownWrites.WaitAsync(cancellationToken);
        try
        {
            ClusterMemberIdentity identity;
            lock (_gate)
            {
                if (!_joined)
                    throw new ClusterMembershipStoreException($"Cluster member {_identity} has not joined, so it has nothing to publish to.");
                identity = _identity;
            }

            var report = await ComposeReportAsync(cancellationToken);
            var written = await _store.PublishAsync(identity, MemberReportJson.Write(report), cancellationToken);
            if (written.Result != OwnWriteResult.Written)
            {
                LapseFromOwnWrite(identity, written.Result);
                throw new ClusterMembershipStoreException($"Cluster member {identity} cannot publish: its entry is {written.Result.ToString().ToLowerInvariant()}.");
            }

            if (written.ReportChanged)
            {
                // The write committed, so every later fresh read shows it; the cached view no longer does.
                lock (_gate)
                {
                    _cached = null;
                    SignalChanged();
                }
            }

            return new PublishedMemberReport(identity, report, written.ReportRevision);
        }
        finally
        {
            _ownWrites.Release();
        }
    }

    public async ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Evaluate(await ReadFleetAsync(mode, cancellationToken));
    }

    public IChangeToken GetChangeToken()
    {
        lock (_gate)
            return new CancellationChangeToken(_changes.Token);
    }

    /// <summary>
    /// Deletes one batch of entries that have been left or expired for longer than the cleanup period, as this member's
    /// clock judges it (FR-031). Any member may run it; a renewed entry is never deleted.
    /// </summary>
    public Task<int> CleanupAsync(CancellationToken cancellationToken = default) =>
        _store.CleanupAsync(_clock.GetUtcNow(), _cleanupPeriod, _skewAllowance, cancellationToken);

    /// <summary>
    /// Joins as <paramref name="identity"/>, or throws the refusal. Returns <see langword="false"/>, having written
    /// nothing, when <paramref name="previous"/> (the incarnation a lapsed member rejoins after) has been displaced.
    /// </summary>
    private async Task<bool> JoinAsAsync(ClusterMemberIdentity identity, ClusterMemberIdentity? previous, CancellationToken cancellationToken)
    {
        var start = _clock.GetUtcNow();
        var startMonotonic = _clock.GetTimestamp();
        var reportJson = MemberReportJson.Write(await ComposeReportAsync(cancellationToken));
        var outcome = await _store.JoinAsync(identity, previous, reportJson, start, _expiryPeriod, _skewAllowance, cancellationToken);
        if (outcome.IsPreviousDisplaced)
            return false;
        if (outcome.Incumbent is { } incumbent)
            throw Refuse(identity, incumbent, start);

        lock (_gate)
        {
            (_identity, _joined, _lapse, _watch) = (identity, true, null, null);
            (_lastSuccessWall, _lastSuccessMonotonic) = (start, startMonotonic);
            _cached = null;
            SignalChanged();
        }

        _logger.LogInformation("Cluster member {Member} joined the fleet.", identity);
        await RefreshCacheAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// A join refused because <paramref name="incumbent"/> is still live. The first refusal starts a watch on it; a later
    /// one that finds it renewed since is a live duplicate, not a crash to wait out (FR-004b, FR-039).
    /// </summary>
    private ClusterMembershipException Refuse(ClusterMemberIdentity joiner, StoredMember incumbent, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_watch is null || _watch.Incumbent != incumbent.Identity)
                _watch = new JoinWatch(incumbent.Identity, now, incumbent.HeartbeatAt);
            else if (incumbent.HeartbeatAt > _watch.HeartbeatAt)
            {
                var message =
                    $"Host id '{joiner.HostId}' is held by live incarnation '{incumbent.Identity.Incarnation}', which renewed at " +
                    $"{incumbent.HeartbeatAt:O} while incarnation '{joiner.Incarnation}' (process {Environment.ProcessId} on " +
                    $"{Environment.MachineName}) waited to join it since {_watch.FirstObservedAt:O}. Two live processes are " +
                    "configured with one host id; configure a distinct host id for one of them.";
                _watch = null;
                _logger.LogError("Refusing to join as a live duplicate: {Diagnostic}", message);
                return new ClusterMembershipDuplicateHostIdException(
                    joiner.HostId,
                    new MemberCondition(MemberConditionKind.DuplicateHostId, [joiner.HostId], message));
            }
        }

        _logger.LogWarning(
            "Cluster member {Member} cannot join yet: host id {HostId} is held by incarnation {Incumbent}, last renewed at {HeartbeatAt:O}. " +
            "It waits for that incarnation to expire, and refuses to start if it renews.",
            joiner, joiner.HostId, incumbent.Identity.Incarnation, incumbent.HeartbeatAt);
        return new ClusterMembershipJoinRefusedException(joiner.HostId, incumbent.Identity.Incarnation, incumbent.HeartbeatAt);
    }

    /// <summary>
    /// A lapsed member rejoins as a new incarnation, waiting out its own earlier entry like any other (FR-004, FR-007). A
    /// member whose earlier incarnation was displaced meanwhile never rejoins, because another process now holds its host
    /// id: it learns so here, and stays lapsed. Nor does a member that is already stopping rejoin.
    /// </summary>
    private async Task RejoinAsync(MemberLapse lapse, MemberStatus status, CancellationToken cancellationToken)
    {
        if (lapse.Reason is MemberLapseReason.Displaced or MemberLapseReason.DuplicateHostId || status >= MemberStatus.Draining)
            return;

        ClusterMemberIdentity rejoining, previous;
        lock (_gate)
        {
            previous = _identity;
            rejoining = _rejoining ??= new ClusterMemberIdentity(_hostId, MemberIncarnation.New());
        }

        try
        {
            var joined = await JoinAsAsync(rejoining, previous, cancellationToken);
            lock (_gate)
            {
                _rejoining = null;
                if (!joined)
                    ConcludeLapse(MemberLapseReason.Displaced);
            }

            if (!joined)
                return;
            if (status == MemberStatus.Active)
                await WriteStatusAsync(rejoining, MemberStatus.Active, cancellationToken);
        }
        catch (ClusterMembershipJoinRefusedException)
        {
            // Its own earlier entry, or another's, is still live; Refuse reported it, and the next heartbeat tries again.
        }
        catch (ClusterMembershipDuplicateHostIdException)
        {
            lock (_gate)
            {
                _rejoining = null;
                ConcludeLapse(MemberLapseReason.DuplicateHostId);
            }
        }
    }

    private async Task MoveForwardAsync(MemberStatus target, CancellationToken cancellationToken)
    {
        await _ownWrites.WaitAsync(cancellationToken);
        try
        {
            ClusterMemberIdentity identity;
            bool write;
            lock (_gate)
            {
                if (target <= _status)
                    return;
                _status = target;
                _cached = null;
                SignalChanged();
                identity = _identity;
                write = _joined && _lapse is not { Reason: MemberLapseReason.Displaced or MemberLapseReason.DuplicateHostId };
            }

            if (write)
                await WriteStatusAsync(identity, target, cancellationToken);
        }
        finally
        {
            _ownWrites.Release();
        }
    }

    /// <summary>
    /// A status write the store cannot take is logged, not thrown: the next heartbeat carries the status forward, and a
    /// member that never gets to write <see cref="MemberStatus.Left"/> simply expires, as a crashed one does.
    /// </summary>
    private async Task WriteStatusAsync(ClusterMemberIdentity identity, MemberStatus status, CancellationToken cancellationToken)
    {
        try
        {
            var written = await _store.SetStatusAsync(identity, status, _clock.GetUtcNow(), cancellationToken);
            if (written.Result != OwnWriteResult.Written)
                LapseFromOwnWrite(identity, written.Result);
        }
        catch (ClusterMembershipStoreException exception)
        {
            _logger.LogWarning(exception, "Cluster member {Member} could not record its status {Status}.", identity, status);
        }
    }

    /// <summary>A write to this member's own row found it missing, displaced or left: the member lapsed (FR-007).</summary>
    private void LapseFromOwnWrite(ClusterMemberIdentity identity, OwnWriteResult result)
    {
        lock (_gate)
        {
            if (_identity != identity || result == OwnWriteResult.Left)
                return;
            ConcludeLapse(result == OwnWriteResult.Displaced ? MemberLapseReason.Displaced : MemberLapseReason.EntryMissing);
        }
    }

    /// <summary>Concludes a lapse once the expiry period has passed since the start of the last successful heartbeat, by
    /// the larger of wall-clock and monotonic elapsed time (FR-007). Called with <see cref="_gate"/> held.</summary>
    private void ConcludeExpiryLapse()
    {
        if (!_joined || _lapse is not null || _status == MemberStatus.Left)
            return;
        if (MemberLiveness.HasLapsed(_clock.GetUtcNow() - _lastSuccessWall, _clock.GetElapsedTime(_lastSuccessMonotonic), _expiryPeriod))
            ConcludeLapse(MemberLapseReason.ExpiryPassed);
    }

    /// <summary>Records and reports a lapse (FR-037, FR-038). Called with <see cref="_gate"/> held.</summary>
    private void ConcludeLapse(MemberLapseReason reason)
    {
        if (_lapse is not null && (_lapse.Reason == reason || _lapse.Reason is MemberLapseReason.Displaced or MemberLapseReason.DuplicateHostId))
            return;

        _lapse = new MemberLapse(reason, _clock.GetUtcNow());
        _cached = null;
        SignalChanged();
        if (reason == MemberLapseReason.Displaced)
            _logger.LogWarning("Cluster member {Member} was displaced by a later incarnation of host id {HostId}; it has lapsed and never rejoins.", _identity, _hostId);
        else
            _logger.LogWarning("Cluster member {Member} has lapsed ({Reason}): others may count it as expired.", _identity, reason);
    }

    /// <summary>
    /// Reads every entry and judges each one on this member's clock. A store failure, or an entry whose envelope cannot
    /// even be judged, fails the whole read rather than returning the rest (FR-012, FR-042).
    /// </summary>
    private async Task<FleetView> ReadFreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _store.ReadAllAsync(cancellationToken);
            var now = _clock.GetUtcNow();
            lock (_gate)
            {
                var view = new FleetView(ClusterProviderKind.Durable, FleetReadMode.Fresh, now, stored.Select(member => Judge(member, now)).ToArray());
                _hadFailedFreshRead = false;
                _cached = view with { ReadMode = FleetReadMode.Cached };
                Observe(view);
                return view;
            }
        }
        catch (Exception exception) when (exception is ClusterMembershipStoreException or ArgumentException)
        {
            lock (_gate)
                _hadFailedFreshRead = true;
            _logger.LogWarning(exception, "Cluster member {Member} could not read the fleet afresh.", CurrentIdentity());
            throw new ClusterMembershipReadException(
                $"Cluster member {CurrentIdentity()} could not read every member of the fleet, so it returned none.", exception);
        }
    }

    private async Task RefreshCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReadFreshAsync(cancellationToken);
        }
        catch (ClusterMembershipReadException)
        {
            // Reported by the read itself; the cached view simply ages until a later read succeeds.
        }
    }

    /// <summary>One entry as this reader judges it at <paramref name="now"/>, with the conditions it observes about it
    /// (FR-037 to FR-042). Called with <see cref="_gate"/> held.</summary>
    private FleetMember Judge(StoredMember stored, DateTimeOffset now)
    {
        var isLive = stored.IsLive(now, _skewAllowance);
        var hostId = stored.Identity.HostId;
        List<MemberCondition>? conditions = null;
        void Add(MemberConditionKind kind, IReadOnlyList<string> hostIds, string message) => (conditions ??= []).Add(new MemberCondition(kind, hostIds, message));

        if (!stored.IsCurrent)
            Add(MemberConditionKind.Displaced, [hostId], $"A later incarnation of host id '{hostId}' has joined; incarnation '{stored.Identity.Incarnation}' is displaced.");
        if (!isLive && !stored.HasLeft)
            Add(MemberConditionKind.Lapsed, [hostId], $"Host id '{hostId}' has not renewed within its expiry period and the skew allowance; it has lapsed.");
        if (!stored.IsInterpretable)
            Add(MemberConditionKind.UninterpretableEntry, [hostId], $"This reader cannot interpret the entry of host id '{hostId}', incarnation '{stored.Identity.Incarnation}'; it counts as reading nothing.");
        if (!string.Equals(hostId, _hostId, StringComparison.Ordinal) && stored.HeartbeatAt > now + _skewAllowance)
            Add(MemberConditionKind.ClockSkew, [_hostId, hostId], $"Host id '{hostId}' heartbeated at {stored.HeartbeatAt:O}, ahead of host id '{_hostId}''s clock ({now:O}) by more than the skew allowance.");
        if (stored.Identity == _identity && _hadFailedFreshRead)
            Add(MemberConditionKind.FailedFreshRead, [_hostId], $"The previous fresh read by host id '{_hostId}' failed.");

        return new FleetMember(
            stored.Identity,
            stored.Status,
            stored.HeartbeatAt,
            stored.ExpiryPeriod,
            isLive,
            IsDisplaced: !stored.IsCurrent,
            stored.Report,
            stored.ReportRevision,
            conditions ?? []);
    }

    /// <summary>
    /// Compares a fresh view with the previous one, firing the change signal on any join, status change, new report,
    /// lapse, displacement, departure or expiry (FR-013), and logs each condition the first time it is observed
    /// (FR-040, FR-041). Called with <see cref="_gate"/> held.
    /// </summary>
    private void Observe(FleetView view)
    {
        var fleet = string.Join('\n', view.Members
            .Select(member => $"{member.Identity}|{member.Status}|{member.IsLive}|{member.IsDisplaced}|{member.ReportRevision}|{member.Report.IsUnknown}")
            .Order(StringComparer.Ordinal));
        if (!string.Equals(fleet, _observedFleet, StringComparison.Ordinal))
        {
            _observedFleet = fleet;
            SignalChanged();
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (member, condition) in view.Members.SelectMany(member => member.Conditions.Select(condition => (member, condition))))
        {
            var key = $"{condition.Kind}|{member.Identity}";
            if (reported.Add(key) && !_reportedConditions.Contains(key) && condition.Kind is MemberConditionKind.ClockSkew or MemberConditionKind.UninterpretableEntry)
                _logger.LogWarning("Cluster member {Member} observed {Condition}: {Message}", _identity, condition.Kind, condition.Message);
        }

        _reportedConditions = reported;
    }

    /// <summary>A failed fresh read is reported once, on this member's own entry in the next view it returns (FR-042).
    /// Called with <see cref="_gate"/> held.</summary>
    private FleetView WithFailedFreshReadOnce(FleetView cached)
    {
        if (!_hadFailedFreshRead)
            return cached;

        _hadFailedFreshRead = false;
        var failed = new MemberCondition(MemberConditionKind.FailedFreshRead, [_hostId], $"The previous fresh read by host id '{_hostId}' failed.");
        return cached with
        {
            Members = cached.Members
                .Select(member => member.Identity == _identity ? member with { Conditions = [.. member.Conditions, failed] } : member)
                .ToArray()
        };
    }

    private async Task<MemberReport> ComposeReportAsync(CancellationToken cancellationToken) =>
        await MemberReportComposition.ComposeAsync(_readability, cancellationToken);

    private ClusterMemberIdentity CurrentIdentity()
    {
        lock (_gate)
            return _identity;
    }

    /// <summary>Fires the current change token and starts a new one. Called with <see cref="_gate"/> held.</summary>
    private void SignalChanged()
    {
        var previous = _changes;
        _changes = new CancellationTokenSource();
        previous.Cancel();
    }

    /// <summary>When this member first saw <see cref="Incumbent"/> refuse its join, and the heartbeat it saw then.</summary>
    private sealed record JoinWatch(ClusterMemberIdentity Incumbent, DateTimeOffset FirstObservedAt, DateTimeOffset HeartbeatAt);
}
