namespace Kernel.Foundation.Contracts;

/// <summary>An opaque continuation for a read.</summary>
public readonly record struct PageToken(string Value);

public sealed record Page<T>(IReadOnlyList<T> Items, PageToken? Next, long AsOf);

public sealed record ActorView(ActorHeader Header, string? Selection, ParkView? Park, long AsOf);

public sealed record ActorQuery(
    string? Type = null,
    ActorStatus? Status = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    long? EndedBefore = null,
    bool? Parked = null);

public sealed record ParkView(long At, string? OpenResolution);

public sealed record JournalEntry(long Revision, string Entry);

public enum DeliveryState { Pending, Held, Consumed, Arrived }

public sealed record DeliveryView(
    string Handle,
    string Id,
    string Kind,
    ActorRef? Sender,
    long? Due,
    DeliveryState State,
    int Attempts,
    long Size);

public interface IActorReads
{
    ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default);

    ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default);

    /// <summary>A nonempty page returns a bookmark after its last entry, including when the reader catches up.</summary>
    ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default);

    ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default);
}
