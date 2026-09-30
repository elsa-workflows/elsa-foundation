using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The settle condition (spec 186, FR-012): every counted member reports observing the target or later, and the settle
/// margin has passed since this worker first saw that hold, which is no earlier than when the last of them began to report
/// it. A withdrawal starts the margin again on every worker, whichever of them withdrew (FR-012, FR-018): a worker notes
/// where the family's finish history stood when its margin began, and a withdrawal entry past that position restarts it.
/// The check is by position in the history, not by time, since two hosts' clocks cannot order a withdrawal against a
/// margin.
/// </summary>
internal sealed class EfSchemaBackfillSettle(
    IEfSchemaFleet? fleet,
    EfSchemaBackfillFinish finish,
    EfSchemaBackfillOptions options,
    TimeProvider time,
    ILogger logger,
    EfSchemaBackfillStatusBoard status)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Margin> _margins = new(StringComparer.Ordinal);
    private bool _raisedMarginLogged;

    /// <summary>What the condition says of a family now.</summary>
    public enum Outcome
    {
        /// <summary>It holds: verification may start.</summary>
        Settled,

        /// <summary>Not yet: a member has not observed the target, the margin has not passed, or the fleet could not be read.</summary>
        Waiting,

        /// <summary>It can never be shown to hold, since this host composes no fleet.</summary>
        Blocked
    }

    /// <summary>
    /// Whether a completion was withdrawn in <paramref name="history"/> after position <paramref name="mark"/>: a row below a
    /// version turned up since, so a margin that began at the mark proves nothing.
    /// </summary>
    public static bool WithdrawnSince(IReadOnlyList<SchemaFinishHistoryEntry> history, int mark) =>
        history.Skip(mark).Any(entry => entry.Transition == SchemaFinishTransition.Withdrawn);

    /// <summary>
    /// What the condition says of <paramref name="chain"/>'s family at <paramref name="target"/> now, and where the family's
    /// finish history stood when the margin began: the mark a verification pass after it holds its completion to, since a
    /// withdrawal past it, before the pass or during it, means a row below a version turned up after the margin began,
    /// which only a pass after a new margin can answer.
    /// </summary>
    public async Task<(Outcome Outcome, int HistoryMark)> CheckAsync(
        EfSchemaBackfillScopeRunner scopes,
        EfSchemaChain chain,
        string target,
        string databaseIdentity,
        CancellationToken cancellationToken)
    {
        var family = chain.Family;
        if (fleet is null)
        {
            status.Blocked(family, [new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoFleet, null, 0,
                "This host composes no fleet, so it cannot tell whether every counted member has observed the finalized version, " +
                "and it never records completion (spec 186, FR-012).")]);
            return (Outcome.Blocked, 0);
        }

        status.Update(family, current => current with { State = EfSchemaBackfillState.Settling, Detail = null });
        var versions = chain.ReadableVersions.Skip(SchemaVersionChain.PositionOf(chain.ReadableVersions, target)).ToArray();
        EfSchemaFleetAnswer answer;
        try
        {
            answer = await fleet.CountObservingAsync(family, versions, databaseIdentity, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No answer is no settle condition: verification waits for a round in which the fleet can be read.
            logger.LogWarning(exception, "The backfill of schema family {Family} could not read the fleet for its settle condition; it tries again next round.", family);
            status.Update(family, current => current with { Detail = $"The fleet could not be read: {exception.GetType().Name}." });
            return (Outcome.Waiting, 0);
        }

        if (!answer.EveryCountedMemberReads)
        {
            lock (_lock)
                _margins.Remove(family);
            status.Update(family, current => current with { SettleWaitingFor = answer.Blockers, Detail = $"Waiting for every counted member to observe '{target}' as finalized." });
            return (Outcome.Waiting, 0);
        }

        var history = (await finish.ReadAsync(scopes, family, cancellationToken))?.FinishHistory ?? [];
        var now = time.GetUtcNow();
        Margin margin;
        lock (_lock)
        {
            if (!_margins.TryGetValue(family, out margin) || margin.Target != target || WithdrawnSince(history, margin.HistoryMark))
                _margins[family] = margin = new Margin(target, now, history.Count);
        }

        var length = EffectiveMargin(fleet);
        if (now - margin.Since < length)
        {
            status.Update(family, current => current with { SettleWaitingFor = [], Detail = $"Every counted member has observed '{target}'; verification starts at {margin.Since + length:u}." });
            return (Outcome.Waiting, margin.HistoryMark);
        }

        return (Outcome.Settled, margin.HistoryMark);
    }

    /// <summary>
    /// The margin the condition waits: the configured one, never less than the fleet's own (<see cref="IEfSchemaFleet.SettleMargin"/>).
    /// A member reports its module's deactivation at its next publish, one heartbeat later, and a shell's last writes can
    /// outlast its drain (30 seconds unless CShells is configured otherwise), so a margin below what the fleet needs to
    /// see a member leave, which is at least both under the defaults, would let verification start while a write from a
    /// member the condition no longer counts is still in flight. The setting can lengthen the margin, for a longer drain; a
    /// shorter one is raised to the fleet's and said once, not refused, so a host does not fail to start over a timing.
    /// </summary>
    private TimeSpan EffectiveMargin(IEfSchemaFleet fleet)
    {
        var floor = fleet.SettleMargin;
        if (options.SettleMargin is not { } configured)
            return floor;
        if (configured >= floor)
            return configured;

        lock (_lock)
        {
            if (!_raisedMarginLogged)
            {
                _raisedMarginLogged = true;
                logger.LogWarning(
                    "The backfill's settle margin {Configured} is shorter than the {Floor} this fleet needs to see a member stop writing (its membership expiry plus the skew allowance), so the fleet's is used (spec 186, FR-012).",
                    configured, floor);
            }
        }

        return floor;
    }

    /// <summary>When this worker first saw the condition hold for a target, and where the finish history stood then.</summary>
    private readonly record struct Margin(string Target, DateTimeOffset Since, int HistoryMark);
}
