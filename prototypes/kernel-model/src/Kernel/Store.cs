namespace Kernel;

/// <summary><c>Runs</c> is the kernel's part of the row: the keys of the off-step work in flight, as a JSON array, or null.</summary>
public sealed record InstanceRow(string Id, string Model, string Definition, InstanceStatus Status, string State, long Revision, string? Parent, string? Fault = null, string? Runs = null);

/// <summary>A message row as a poll returns it.</summary>
public sealed record Stored(Message Message, long Seq, long Seen, int Attempts);

public sealed record SubscriptionRow(string Key, string Instance, string Wait);

public sealed record EventRow(string Key, string Id, string? Payload, long KeepUntil);

public sealed record JournalRow(int Partition, long Position, string Instance, long Revision, string Entries);

public sealed record Lease(long Epoch, long Position, long Now);

public sealed record Polled(IReadOnlyList<Stored> Messages, long Now);

/// <summary>
/// One instance's part of a commit. <c>Expected</c> is the revision the row must have; null means the row is new.
/// <c>Consumed</c> messages are deleted; <c>Done</c> ones (outside commands, which carry keep_until) are marked done.
/// </summary>
public sealed record StepCommit(
    string Instance, InstanceRow? Row, long? Expected, IReadOnlyList<string> Consumed, IReadOnlyList<string> Done, IReadOnlyList<Message> Sent,
    IReadOnlyList<SubscriptionRow> Subscribed, IReadOnlyList<SubscriptionRow> Unsubscribed, string? Journal);

/// <summary>What the owner of <c>Partition</c> at <c>Epoch</c>, believing the position is <c>Position</c>, writes in one transaction.</summary>
public sealed record CommitBatch(
    int Partition, long Epoch, long Position, TimeSpan Lease, bool HandOver, IReadOnlyList<StepCommit> Steps,
    IReadOnlyList<string> Seen, IReadOnlyList<string> Bumped)
{
    /// <summary>How far the position moves: one per journal row.</summary>
    public int Positions => Steps.Count(s => s.Journal is not null);
}

/// <summary><c>Landed</c> is false when the fence matched nothing. <c>Seqs</c> are those of the inserted messages, in order.</summary>
public sealed record Committed(bool Landed, long Now, IReadOnlyList<long> Seqs);

public enum Failure
{
    Unknown,    // the outcome is not known: reset
    Transient,  // it did not happen and may be tried again as it is: a deadlock victim, SQLITE_BUSY
    Definite,   // it did not happen and never will: a value too long, a constraint
}

public sealed class StoreException(Failure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public Failure Failure => failure;
}

public interface IKernelStore
{
    /// <summary>Take the partition if it is free, expired or already mine. Every take bumps the epoch.</summary>
    ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>The fenced commit. Throws <see cref="StoreException"/>; anything else thrown counts as an unknown outcome.</summary>
    ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default);

    /// <summary>A plain read: rows of the partition not yet seen at this epoch, not done, due within the horizon, in seq order.</summary>
    ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default);

    /// <summary>Add a message. False when the id exists already.</summary>
    ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default);

    /// <summary>Delete handled outside commands and events whose window has passed.</summary>
    ValueTask<int> SweepAsync(CancellationToken cancellationToken = default);
}

public static class Events
{
    /// <summary>The message that delivers an event to a wait. Both sides derive the same id, so the double is refused.</summary>
    public static Message Deliver(EventRow e, SubscriptionRow s) =>
        new($"ev:{e.Id}:{s.Instance}:{s.Wait}", new InstanceRef(s.Instance), Kinds.Event, e.Payload, KeepUntil: e.KeepUntil);

    /// <summary>The publisher's side: insert the event, and only then read who waits for it.</summary>
    public static async ValueTask PublishAsync(IKernelStore store, EventRow e, CancellationToken cancellationToken = default)
    {
        await store.PublishAsync(e, cancellationToken);
        foreach (var subscription in await store.FindSubscriptionsAsync(e.Key, cancellationToken))
            await store.AddAsync(Deliver(e, subscription), cancellationToken);
    }
}
