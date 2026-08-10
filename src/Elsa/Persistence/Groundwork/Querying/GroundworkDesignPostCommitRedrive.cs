namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Finishes the work a committed design mutation ordered but could not perform itself.
/// <para>
/// A cross-target publication becomes done when the design commit lands; whatever it derives on another target
/// is written afterwards, by the same caller. Until now that was the only driver there was, so a caller that
/// died in between left the derived write undone forever. This sweep is the other driver: it claims the
/// intents that commit made durable, delivers them, and deletes each one that lands.
/// </para>
/// <para>
/// One sweep reads one bounded page. It is not a loop and does not drain: a caller decides how often to sweep,
/// which is what keeps the cost of an idle deployment one indexed query per interval.
/// </para>
/// </summary>
public sealed class GroundworkDesignPostCommitRedrive
{
    /// <summary>
    /// How long a claim holds an intent. Long enough that an ordinary delivery finishes inside it, short enough
    /// that an owner which dies mid-delivery does not strand the intent for long.
    /// </summary>
    public static readonly TimeSpan DefaultVisibilityTimeout = TimeSpan.FromMinutes(1);

    private const int MaximumFailureMessageLength = 512;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

    private readonly GroundworkDesignPostCommitOutbox _outbox;
    private readonly IReadOnlyDictionary<string, IDesignPostCommitIntentDeliverer> _deliverers;
    private readonly TimeProvider _timeProvider;
    private readonly string _ownerId = $"design-redrive-{Guid.NewGuid():N}";

    public GroundworkDesignPostCommitRedrive(
        GroundworkDesignPostCommitOutbox outbox,
        IEnumerable<IDesignPostCommitIntentDeliverer> deliverers,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(deliverers);
        _outbox = outbox;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _deliverers = deliverers
            .GroupBy(deliverer => deliverer.IntentKind, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException(
                        $"Two features claim delivery of design post-commit intent kind '{group.Key}'."),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Claims and delivers up to <paramref name="limit"/> due intents in the store session's tenant scope.
    /// </summary>
    public async Task<DesignPostCommitRedriveResult> SweepAsync(
        int limit = 16,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var claims = await _outbox.ClaimAsync(
            new(_ownerId, now, DefaultVisibilityTimeout, limit),
            cancellationToken);

        var results = new List<DesignPostCommitIntentResult>(claims.Count);
        foreach (var claim in claims)
            results.Add(await DeliverAsync(claim, cancellationToken));
        return new(results);
    }

    private async Task<DesignPostCommitIntentResult> DeliverAsync(
        DesignPostCommitIntentClaim claim,
        CancellationToken cancellationToken)
    {
        var intent = claim.Intent;
        if (!_deliverers.TryGetValue(intent.Kind, out var deliverer))
        {
            // Not an error to be swallowed, and not a reason to drop the intent: a host that has not composed
            // the owning feature yet must not lose the obligation, so it goes back and waits for the feature.
            return await ReleaseAsync(
                claim,
                $"No deliverer is registered for design post-commit intent kind '{intent.Kind}'.",
                cancellationToken);
        }

        try
        {
            await deliverer.DeliverAsync(intent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await ReleaseAsync(claim, Summarize(exception), cancellationToken);
        }

        // Delivered. Losing the fence here means the lease expired and another owner now holds the intent; it
        // will redeliver — idempotently — and delete it. Reporting the lost claim rather than a delivery keeps
        // that visible instead of double-counting one intent as two deliveries.
        return await _outbox.TryCompleteAsync(claim, cancellationToken)
            ? new(intent.IntentId, intent.Kind, DesignPostCommitIntentOutcome.Delivered)
            : new(intent.IntentId, intent.Kind, DesignPostCommitIntentOutcome.ClaimLost);
    }

    private async Task<DesignPostCommitIntentResult> ReleaseAsync(
        DesignPostCommitIntentClaim claim,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        // No dead letter, deliberately. The design commit already happened, so the obligation is real and
        // cannot be abandoned; an intent that keeps failing stays in the outbox at the backoff ceiling, which
        // is the operator's signal that something downstream needs attention.
        var released = await _outbox.TryReleaseAsync(
            claim,
            _timeProvider.GetUtcNow().Add(RetryDelay(claim.AttemptCount)),
            failureMessage,
            cancellationToken);
        return new(
            claim.Intent.IntentId,
            claim.Intent.Kind,
            released ? DesignPostCommitIntentOutcome.Deferred : DesignPostCommitIntentOutcome.ClaimLost,
            failureMessage);
    }

    /// <summary>
    /// The failure text kept on the intent. Bounded, because this lands in a durable document that nothing
    /// else truncates and a provider message can be arbitrarily long.
    /// </summary>
    private static string Summarize(Exception exception)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        return summary.Length <= MaximumFailureMessageLength
            ? summary
            : summary[..MaximumFailureMessageLength];
    }

    /// <summary>Exponential backoff from <see cref="FirstRetryDelay"/>, capped at <see cref="MaximumRetryDelay"/>.</summary>
    public static TimeSpan RetryDelay(int attemptCount)
    {
        if (attemptCount >= 32)
            return MaximumRetryDelay;
        var scaled = FirstRetryDelay * Math.Pow(2, Math.Max(0, attemptCount));
        return scaled >= MaximumRetryDelay ? MaximumRetryDelay : scaled;
    }
}
