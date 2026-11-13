using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// An actor's row. The durable layer's part: <c>Revision</c>, <c>State</c> (opaque), <c>StateVersion</c>, <c>History</c> and
/// <c>Jobs</c>, the keys of the background jobs in flight. The workflow instance's part (layer 3): <c>Model</c>,
/// <c>Definition</c>, <c>Status</c>, <c>Parent</c>, <c>Fault</c>, <c>Suspended</c> and <c>Children</c>, the instances it started
/// that have not finished. One row, so one write per commit: a seam leak (FINDINGS section 24). JSON arrays or null.
/// </summary>
public sealed record InstanceRow(string Id, string Model, string Definition, InstanceStatus Status, string State, long Revision, string? Parent, string? Fault = null, string? Jobs = null, History History = History.Full, bool Suspended = false, string? Children = null, int StateVersion = 1);

/// <summary>A message row as a poll returns it.</summary>
public sealed record Stored(Message Message, long Seq, long Seen, int Attempts, long Trial);

/// <summary>A wait of an instance; or, with <c>Start</c> and no instance, a definition that starts an instance for every event of the key.</summary>
public sealed record SubscriptionRow(string Key, string Instance, string Wait, StartSpec? Start = null);

public sealed record EventRow(string Key, string Id, string? Payload, long KeepUntil);

public sealed record JournalRow(int Partition, long Position, string Instance, long Revision, string Entries);

/// <summary>
/// A projector's position, and the partition's position read just before: every journal position up to <c>Written</c>
/// is committed, so one that is missing there was deleted, not written late.
/// </summary>
public sealed record Projection(long Position, long Written);

/// <summary>The viewer's row of an instance, as the projector has it: the last revision it saw and how many commits.</summary>
public sealed record ViewRow(string Instance, long Revision, int Commits);

public sealed record Lease(long Epoch, long Position, long Now);

public sealed record Polled(IReadOnlyList<Stored> Messages, long Now);

/// <summary>
/// One instance's part of a commit. <c>Expected</c> is the revision the row must have; null means the row is new.
/// <c>Consumed</c> messages are deleted; <c>Done</c> ones (outside commands, which carry keep_until) are marked done.
/// <c>Held</c> messages stay, and are no longer on trial: their turn ran and did not kill the process.
/// <c>Delete</c> removes the instance's row (at revision <c>Expected</c>) and its journal rows; its own journal row says so.
/// <c>Unsubscribe</c> deletes every subscription of the instance: it was ended by force and the kernel holds no list of them.
/// </summary>
public sealed record TurnCommit(
    string Instance, InstanceRow? Row, long? Expected, IReadOnlyList<string> Consumed, IReadOnlyList<string> Done, IReadOnlyList<Message> Sent,
    IReadOnlyList<SubscriptionRow> Subscribed, IReadOnlyList<SubscriptionRow> Unsubscribed, string? Journal, bool Unsubscribe = false, IReadOnlyList<string>? Held = null, bool Delete = false);

/// <summary>What the owner of <c>Partition</c> at <c>Epoch</c>, believing the position is <c>Position</c>, writes in one transaction.</summary>
public sealed record CommitBatch(
    int Partition, long Epoch, long Position, TimeSpan Lease, bool HandOver, IReadOnlyList<TurnCommit> Turns,
    IReadOnlyList<string> Seen, IReadOnlyList<string> Trial)
{
    /// <summary>How far the position moves: one per journal row.</summary>
    public int Positions => Turns.Count(s => s.Journal is not null);
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

/// <summary>
/// The durable layer's storage contract, stated by what each operation guarantees. It also keeps the built-in runtime's
/// membership (<see cref="IMembership"/>): the node rows and which partitions nobody holds.
/// </summary>
public interface IKernelStore : IMembership
{
    /// <summary>
    /// Take the partition if it is free, expired or already mine. Every take bumps the epoch. A take from another
    /// owner can only succeed because that owner's lease expired: that is a death, and in the same transaction every
    /// message the dead owner had on trial gets one more attempt. <paramref name="node"/> must be unique per process
    /// start, or a restarted process would look like its predecessor taking again.
    /// </summary>
    ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Give the partition up, if this is still its epoch. A release is not a death.</summary>
    ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default);

    /// <summary>The fenced commit. Throws <see cref="StoreException"/>; anything else thrown counts as an unknown outcome.</summary>
    ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default);

    /// <summary>A plain read: rows of the partition not yet seen at this epoch, not done, due within the horizon, in seq order.</summary>
    ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default);

    /// <summary>Add a message. False when the id exists already.</summary>
    ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Register a subscription of kind start. Waits of instances are written by their commits, not here. Idempotent.</summary>
    ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default);

    /// <summary>The position of the partition's projector, its own row apart from the fence, and then the partition's position.</summary>
    ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: move the projector's position from <paramref name="from"/> to <paramref name="to"/>, if it is
    /// still at <paramref name="from"/>, and write the view rows of <paramref name="rows"/>; a row of a deleted instance
    /// removes its view. False when another projector moved it first.
    /// </summary>
    ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default);

    /// <summary>The viewer's read: the view rows of these instances, as the projectors have written them. Missing ids are skipped.</summary>
    ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decision 11, the viewer's tail: one instance's journal rows past position <paramref name="after"/>, in position order.
    /// A provider should answer it from an index on (partition, instance); this default reads the partition's journal.
    /// </summary>
    async ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default)
    {
        var rows = new List<JournalRow>();
        for (var from = after; ;)
        {
            var page = await ReadJournalAsync(partition, from, 1000, cancellationToken);
            rows.AddRange(page.Where(r => r.Instance == instance));
            if (page.Count < 1000) return rows;
            from = page[^1].Position;
        }
    }

    /// <summary>Delete handled outside commands and events whose window has passed.</summary>
    ValueTask<int> SweepAsync(CancellationToken cancellationToken = default);
}

public static class Events
{
    /// <summary>
    /// The message that delivers an event to a wait, or that starts an instance for it. Both sides derive the same id, so
    /// the double is refused; a start's target is derived from event and subscription too, so it is created once.
    /// </summary>
    public static Message Deliver(EventRow e, SubscriptionRow s, int partitions = 1) => s.Start is null
        ? new($"ev:{e.Id}:{s.Instance}:{s.Wait}", new InstanceRef(s.Instance), Kinds.Event, e.Payload, KeepUntil: e.KeepUntil)
        : new($"ev:{e.Id}::{s.Wait}", InstanceRef.In(InstanceRef.Spread($"{e.Id}:{s.Wait}", partitions), $"on:{e.Id}:{s.Wait}"), Kinds.Event, e.Payload, Start: s.Start, KeepUntil: e.KeepUntil);

    /// <summary>
    /// What a delivery is for: the event's id and the wait it was delivered to, read back from the derived id (merge,
    /// friction F-N3: a model need not know the id's format). Null for a message that is no delivery.
    /// </summary>
    public static (string Event, string Wait)? Delivered(Message m)
    {
        if (m.Kind != Kinds.Event || !m.Id.StartsWith("ev:", StringComparison.Ordinal)) return null;
        var marker = m.Start is null ? $":{m.Target.Id}:" : "::";
        var at = m.Id.LastIndexOf(marker, StringComparison.Ordinal);
        return at < 3 ? null : (m.Id[3..at], m.Id[(at + marker.Length)..]);
    }

    /// <summary>The publisher's side: insert the event, and only then read who waits for it.</summary>
    public static async ValueTask PublishAsync(IKernelStore store, EventRow e, int partitions = 1, CancellationToken cancellationToken = default)
    {
        await store.PublishAsync(e, cancellationToken);
        foreach (var subscription in await store.FindSubscriptionsAsync(e.Key, cancellationToken))
            await store.AddAsync(Deliver(e, subscription, partitions), cancellationToken);
    }

    /// <summary>
    /// The registrar's side of a start subscription: insert it, and only then read the events of its key that are still
    /// kept. Like a publisher, the registrar retries until both are done.
    /// </summary>
    public static async ValueTask SubscribeAsync(IKernelStore store, SubscriptionRow start, int partitions, CancellationToken cancellationToken = default)
    {
        await store.SubscribeAsync(start, cancellationToken);
        await CatchUpAsync(store, start, partitions, cancellationToken);
    }

    public static async ValueTask CatchUpAsync(IKernelStore store, SubscriptionRow start, int partitions, CancellationToken cancellationToken = default)
    {
        foreach (var e in await store.FindEventsAsync([start.Key], cancellationToken))
            await store.AddAsync(Deliver(e, start, partitions), cancellationToken);
    }
}
