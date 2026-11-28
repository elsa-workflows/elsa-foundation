namespace Kernel.Foundation.Contracts;

/// <summary>Retention windows for terminal actors. A null window keeps that status indefinitely.</summary>
public sealed record ActorRetentionWindows(TimeSpan? Finished = null, TimeSpan? Cancelled = null, TimeSpan? Faulted = null);

/// <summary>The number of newly eligible actors and the Unix-millisecond clock used to evaluate their ages.</summary>
public sealed record ActorRetentionQuote(long NewlyEligible, long AsOf);

/// <summary>Optional retention planning over committed terminal metadata, before owner deletion checks.</summary>
public interface IActorRetentionReads
{
    /// <summary>
    /// Counts non-parked terminal actors in the bound tenant that qualify under the proposed windows but not the previous ones.
    /// An optional exact label narrows the count. A supplied clock must be at or before the store clock and evaluates the
    /// current committed projection at that age cutoff.
    /// </summary>
    ValueTask<ActorRetentionQuote> ReadRetentionQuoteAsync(BoundScope scope, string actorType, ActorRetentionWindows previous,
        ActorRetentionWindows proposed, KeyValuePair<string, string>? definitionLabel = null, long? asOf = null,
        CancellationToken cancellationToken = default);
}
