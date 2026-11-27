namespace Kernel.Foundation.Contracts;

/// <summary>Optional indexed reads of each activity's first committed structural observation.</summary>
public interface IActorPlaybackReads
{
    bool SupportsPlayback { get; }

    /// <summary>Reads one coherent retained window for the exact actor. Null denotes a missing actor.</summary>
    ValueTask<ActorPlaybackPage?> ReadPlaybackAsync(BoundScope scope, ActorRef actor, ActorPlaybackQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record ActorPlaybackQuery(int Limit, long? Position, string? Anchor, string? Activity,
    long? UpperSequence = null, long? RetainedAfterSequence = null);

public enum ActorPlaybackAvailability { Available, Unavailable, Invalidated, TargetUnavailable }

/// <summary>A bounded ranked row, with the committed observation identity and provider clock.</summary>
public sealed record ActorPlaybackEntry(long Position, long Sequence, long Revision, int CommitOrdinal,
    string Observation, long RecordedAt, string? PreviousActivity, string? NextActivity);

public sealed record ActorPlaybackPage(ActorPlaybackAvailability Availability, string? Reason,
    long LatestSequence, long UpperSequence, long RetainedAfterSequence, long? LastRecordedAt,
    long? Count, long? SelectedPosition, IReadOnlyList<ActorPlaybackEntry> Items, long AsOf);
