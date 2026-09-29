using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The settle condition (spec 186, FR-012): every counted member reports observing the target or later, and the settle
/// margin has passed since this worker first saw that hold, which is no earlier than when the last of them began to report
/// it. A withdrawal starts the margin again, so verification after one waits a full margin (FR-012, FR-018).
/// </summary>
internal sealed class EfSchemaBackfillSettle(
    IEfSchemaFleet? fleet,
    EfSchemaBackfillOptions options,
    TimeProvider time,
    ILogger logger,
    EfSchemaBackfillStatusBoard status)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (string Target, DateTimeOffset Since)> _since = new(StringComparer.Ordinal);

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

    public async Task<Outcome> CheckAsync(EfSchemaChain chain, string target, string databaseIdentity, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        if (fleet is null)
        {
            status.Blocked(family, [new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoFleet, null, 0,
                "This host composes no fleet, so it cannot tell whether every counted member has observed the finalized version, " +
                "and it never records completion (spec 186, FR-012).")]);
            return Outcome.Blocked;
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
            return Outcome.Waiting;
        }

        if (!answer.EveryCountedMemberReads)
        {
            Reset(family);
            status.Update(family, current => current with { SettleWaitingFor = answer.Blockers, Detail = $"Waiting for every counted member to observe '{target}' as finalized." });
            return Outcome.Waiting;
        }

        var now = time.GetUtcNow();
        DateTimeOffset since;
        lock (_lock)
        {
            if (_since.TryGetValue(family, out var settled) && settled.Target == target)
                since = settled.Since;
            else
                _since[family] = (target, since = now);
        }

        var margin = options.SettleMargin ?? fleet.SettleMargin;
        if (now - since < margin)
        {
            status.Update(family, current => current with { SettleWaitingFor = [], Detail = $"Every counted member has observed '{target}'; verification starts at {since + margin:u}." });
            return Outcome.Waiting;
        }

        return Outcome.Settled;
    }

    /// <summary>Forgets when the condition was first seen to hold for <paramref name="family"/>, so the margin starts again the next time it does.</summary>
    public void Reset(string family)
    {
        lock (_lock)
            _since.Remove(family);
    }
}
