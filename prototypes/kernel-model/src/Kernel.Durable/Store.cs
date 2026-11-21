using System.Text.Json.Serialization;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// An actor's row. The durable layer's part: <c>Revision</c>, <c>State</c> (opaque), <c>StateVersion</c>, <c>History</c> and
/// <c>Jobs</c>, the keys of the background jobs in flight. The workflow instance's part (layer 3): <c>Model</c>,
/// <c>Definition</c>, <c>Status</c>, <c>Parent</c>, <c>Fault</c>, <c>Suspended</c> and <c>Children</c>, the instances it started
/// that have not finished. One row, so one write per commit: a seam leak (FINDINGS section 24). JSON arrays or null.
/// <c>PendingStart</c> is the durable owner's initial delivery ID only while its first authored work remains held.
/// </summary>
public sealed record InstanceRow(string Id, string Model, string Definition, InstanceStatus Status, string State, long Revision, string? Parent, string? Fault = null, string? Jobs = null, History History = History.Full, bool Suspended = false, string? Children = null, int StateVersion = 1, string? CommitTimes = null,
    long Creation = 0, bool Retired = false, string? StartIntent = null, long ParentCreation = 0, InboxKey? PendingStart = null, string? CustomHandles = null, bool IdentityOnly = false, bool IdentityMetadataRequired = false);

/// <summary>A requested spelling, its stored actor identity (or first equivalent candidate if missing), and its snapshot.</summary>
public sealed record ActorLookup(string Requested, string Canonical, InstanceRow? Row)
{
    public static IReadOnlyList<ActorLookup> Ordinal(IReadOnlyCollection<string> ids, IReadOnlyList<InstanceRow> rows)
    {
        var found = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return ids.Select(id => new ActorLookup(id, id, found.GetValueOrDefault(id))).ToList();
    }
}

/// <summary>A message row as a poll returns it.</summary>
public sealed record Stored(Message Message, long Seq, long Seen, int Attempts, long Trial);

/// <summary>A wait of an instance; or, with <c>Start</c> and no instance, a definition that starts an instance for every event of the key.</summary>
public sealed record SubscriptionRow(
    [property: JsonConverter(typeof(DurableKeyJson))] string Key,
    [property: JsonConverter(typeof(DurableKeyJson))] string Instance,
    [property: JsonConverter(typeof(DurableKeyJson))] string Wait, StartSpec? Start = null, long Creation = 0);

public sealed record EventRow(
    [property: JsonConverter(typeof(DurableKeyJson))] string Key,
    [property: JsonConverter(typeof(DurableKeyJson))] string Id, string? Payload, long KeepUntil);

public sealed record JournalRow(int Partition, long Position, string Instance, long Revision, string Entries);

/// <summary>
/// A projector's position, and the partition's position read just before: every journal position up to <c>Written</c>
/// is committed, so one that is missing there was deleted, not written late.
/// </summary>
public sealed record Projection(long Position, long Written);

/// <summary>The viewer's row of an instance, as the projector has it: the last revision it saw and how many commits.</summary>
public sealed record ViewRow(string Instance, long Revision, int Commits);

/// <summary>The view change for one key in a projection page. Reset discards the view before the last delete.</summary>
public sealed record ViewChange(string Instance, long Revision, int Commits, bool Reset)
{
    public static IReadOnlyCollection<ViewChange> From(IReadOnlyList<JournalRow> rows)
    {
        var changes = new Dictionary<string, ViewChange>(StringComparer.Ordinal);
        foreach (var row in rows.OrderBy(r => r.Position))
        {
            if (row.Revision < 0) changes[row.Instance] = new(row.Instance, 0, 0, true);
            else if (row.Revision > 0)
            {
                changes.TryGetValue(row.Instance, out var previous);
                changes[row.Instance] = new(row.Instance, row.Revision, (previous?.Commits ?? 0) + 1, previous?.Reset ?? false);
            }
        }
        return changes.Values;
    }
}

public sealed record Lease(long Epoch, long Position, long Now)
{
    /// <summary>This lease as one entry of an owned set.</summary>
    public PartitionFence For(int partition) => new(partition, Epoch, Position);
}

/// <summary>
/// One virtual partition as its owner holds it (K1, ownership by a set of virtual partitions): the epoch of the take
/// and the position the owner believes the partition is at. Each virtual partition has its own fence row, epoch,
/// position and journal, keyed (virtual partition, position).
/// </summary>
public sealed record PartitionFence(int Partition, long Epoch, long Position);

/// <summary>A take of a set: what it got, in ascending partition id, each at its own epoch and position.</summary>
public sealed record OwnedLease(IReadOnlyList<PartitionFence> Taken, long Now);


public sealed record Polled(IReadOnlyList<Stored> Messages, long Now);

/// <summary>A partition the node heartbeat found owned by its node, at the epoch the store has for it.</summary>
public sealed record OwnedEpoch(int Partition, long Epoch);

/// <summary>
/// The node heartbeat's answer (K1b): the partitions this node owns, in ascending id; the database time of the renewal,
/// so the node lease runs until <c>Now</c> plus the lease; and the membership, read after the renewal outside its transaction.
/// </summary>
public sealed record NodeBeat(IReadOnlyList<OwnedEpoch> Owned, long Now, Cluster Cluster);

/// <summary>
/// One instance's part of a commit. <c>Expected</c> is the revision the row must have; null means the row is new.
/// <c>Consumed</c> messages are deleted; <c>Done</c> ones (outside commands, which carry keep_until) are marked done.
/// <c>Held</c> messages stay, and are no longer on trial: their turn ran and did not kill the process.
/// <c>Delete</c> requires a present <c>Expected</c> matching an existing row. With <c>Row</c> null it is a trusted
/// raw deletion; otherwise <c>Row</c> must be the canonical retirement marker with a non-null deletion journal. Both remove positive journal history;
/// the delete's own journal item records removal. Missing actors never acquire a default revision.
/// <c>Unsubscribe</c> deletes every subscription of the instance: it was ended by force and the kernel holds no list of them.
/// <c>Subscribed</c> and <c>Unsubscribed</c> are idempotent (FINDINGS 25.2): a wait that is there already stays one wait,
/// and removing one that is not there changes nothing; neither is a count that does not match. Within a turn its
/// subscribes apply before its unsubscribes.
/// </summary>
public sealed record TurnCommit(
    string Instance, InstanceRow? Row, long? Expected, IReadOnlyList<InboxKey> Consumed, IReadOnlyList<InboxKey> Done, IReadOnlyList<Message> Sent,
    IReadOnlyList<SubscriptionRow> Subscribed, IReadOnlyList<SubscriptionRow> Unsubscribed, string? Journal, bool Unsubscribe = false, IReadOnlyList<InboxKey>? Held = null, bool Delete = false);

/// <summary>What the owner of <c>Partition</c> at <c>Epoch</c>, believing the position is <c>Position</c>, writes in one transaction.</summary>
public sealed record CommitBatch(
    int Partition, long Epoch, long Position, TimeSpan Lease, bool HandOver, IReadOnlyList<TurnCommit> Turns,
    IReadOnlyList<InboxKey> Seen, IReadOnlyList<InboxKey> Trial)
{
    /// <summary>
    /// How far the position moves: one per journal row. It counts the whole batch, while <c>Position</c> is the lowest
    /// fenced partition's; in the owned-set form each partition moves by <see cref="PositionsIn"/>.
    /// </summary>
    public int Positions => Turns.Count(s => s.Journal is not null);

    /// <summary>Optional closed-open authority interval required by this transaction's guarded turns.</summary>
    public CommitWindow? Window { get; init; }

    /// <summary>
    /// The owned-set form (<see cref="Owned"/>): every virtual partition the batch touches, each at its own epoch and
    /// position. The batch lands only if every one of these fences matches; one stale fence rolls the whole batch back.
    /// Null in the one-partition form, which fences <c>(Partition, Epoch, Position)</c>; in the owned-set form those three
    /// name the lowest fenced partition, so a store that fences one partition per commit reads a one-element set as today.
    /// </summary>
    public IReadOnlyList<PartitionFence>? Fences { get; init; }

    /// <summary>The fences this batch checks, in ascending partition id: the order a store locks their rows in.</summary>
    public IReadOnlyList<PartitionFence> Fenced => Fences is { } fences ? [.. fences.OrderBy(f => f.Partition)] : [new(Partition, Epoch, Position)];

    /// <summary>
    /// The partition whose journal and position a turn moves: in the one-partition form the batch's partition (as today),
    /// and in the owned-set form, one fence or several, the virtual partition the turn's address embeds; -1 when it embeds none.
    /// </summary>
    public int PartitionOf(TurnCommit turn) => Fences is null ? Partition : StoreBatchRules.AddressPartition(turn.Instance) ?? -1;

    /// <summary>How far this batch moves one partition's position.</summary>
    public int PositionsIn(int partition) => Turns.Count(t => t.Journal is not null && PartitionOf(t) == partition);

    /// <summary>A batch fenced by an owned set. A one-element set is the one-partition batch of today, with <see cref="Fences"/> set.</summary>
    public static CommitBatch Owned(IReadOnlyList<PartitionFence> fences, TimeSpan lease, bool handOver, IReadOnlyList<TurnCommit> turns,
        IReadOnlyList<InboxKey> seen, IReadOnlyList<InboxKey> trial)
    {
        var lowest = fences.MinBy(f => f.Partition) ?? throw new ArgumentException("An owned batch fences at least one partition.", nameof(fences));
        return new(lowest.Partition, lowest.Epoch, lowest.Position, lease, handOver, turns, seen, trial) { Fences = fences };
    }
}

/// <summary><c>Landed</c> is false when the fence matched nothing. <c>Seqs</c> are those of the inserted messages, in order.</summary>
public sealed record Committed(bool Landed, long Now, IReadOnlyList<long> Seqs);

public sealed record CommitWindow(long FromInclusive, long UntilExclusive)
{
    public CommitWindow Intersect(CommitWindow next) =>
        new(Math.Max(FromInclusive, next.FromInclusive), Math.Min(UntilExclusive, next.UntilExclusive));
}

public enum Failure
{
    Unknown,    // the outcome is not known: reset
    Transient,  // it did not happen and may be tried again as it is: a deadlock victim, SQLITE_BUSY
    Definite,   // it did not happen and never will: a value too long, a constraint
    WindowExpired, // the entire transaction rolled back and its time-dependent decisions must be reconsidered
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
    /// <summary>A supported store validates a guarded transaction against its clock before making its writes visible.</summary>
    bool SupportsCommitWindow => false;
    /// <summary>Qualified creation headers, retired high-water marks and exact target envelopes; default unavailable.</summary>
    bool CreationIdentityAvailable => false;
    /// <summary>Irreversible empty markers for generated physical addresses, independent of positive generations.</summary>
    bool PhysicalRetirementAvailable => false;
    bool HasActorAliases => false;

    /// <summary>
    /// Qualified immutable clock facts in the same atomic commit; unavailable by default. A qualified store returns
    /// the exact authoritative stamp time in Committed.Now; the generic permission to omit that time does not apply.
    /// </summary>
    bool CommitClockAvailable => false;

    /// <summary>
    /// Take the partition if it is free, expired or already mine. Every take bumps the epoch. A take from another
    /// owner can only succeed because that owner's lease expired: that is a death, and in the same transaction every
    /// message the dead owner had on trial gets one more attempt. <paramref name="node"/> must be unique per process
    /// start, or a restarted process would look like its predecessor taking again. Under the node lease, "expired" is
    /// the owner's node record, and the take renews its taker (<see cref="NodeLeaseAvailable"/>).
    /// </summary>
    ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Give the partition up, if this is still its epoch. A release is not a death.</summary>
    ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default);

    /// <summary>
    /// The fenced commit. Throws <see cref="StoreException"/>; anything else thrown counts as an unknown outcome. A batch
    /// over several virtual partitions (<see cref="CommitBatch.Fences"/>) needs <see cref="OwnedSetsAvailable"/>: it
    /// locks the fence rows in ascending partition id, and lands only if every fence matches. Its Seen and Trial marks
    /// record the epoch of each message's own virtual partition, which must be fenced. A store that fences one partition
    /// (it calls <see cref="StoreBatchRules.Validate"/> without <c>ownedSets</c>) refuses such a batch as definite.
    /// </summary>
    ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default);

    /// <summary>A plain read: rows of the partition not yet seen at this epoch, not done, due within the horizon, in seq order.</summary>
    ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default);

    /// <summary>The store commits a batch over several virtual partitions all or nothing under their k fence rows. Default unavailable.</summary>
    bool OwnedSetsAvailable => false;

    /// <summary>
    /// K1b, the node lease ("The partition record" and "The node's heartbeat" in ownership-and-fencing.md). The lease is
    /// the node record, <c>Nodes (Id, SeenAt, LeaseUntil)</c> in database time, and covers every partition its process
    /// owns. A take accepts a free partition, one whose owner's node record is missing or has <c>LeaseUntil</c> before
    /// database time, or a retake by the recorded owner. Its transaction reads the owners' node records with a plain read
    /// that holds nothing, then writes the taker's own record (<c>SeenAt</c> and <c>LeaseUntil</c>, whatever it takes),
    /// then locks the partition rows in ascending id, each decided by that read; deaths count as before, a missing record
    /// as expired. A retake writes the taker's record the same way, also when it is refused. A caller does not bound its
    /// node lease by a take's or retake's database time (an empty take returns none). A Worker whose guard expired keeps
    /// the partitions it stood down pending and retakes them counting deaths (#4837; the counting retake below). The
    /// fence renews no lease: <see cref="CommitBatch.Lease"/>
    /// renews nothing (slice 4 makes it the commit-step deadline), and the fence still checks and locks every fenced
    /// row, a batch with no journal rows included. Membership's free partitions are those unowned or whose owner's node
    /// lease has expired. Under the node lease the host's membership call is <see cref="NodeHeartbeatAsync"/>: the
    /// runtime's balancing round reads the cluster its last renewing heartbeat saw (K1b slice 5), and
    /// <see cref="IMembership.HeartbeatAsync"/> stays for the per-partition lease only. Off by default: until K1b slice 6
    /// deletes the switch, the per-partition lease stands.
    /// </summary>
    bool NodeLeaseAvailable => false;

    /// <summary>
    /// The node's heartbeat under <see cref="NodeLeaseAvailable"/>. One statement or transaction writes this node's
    /// record, <c>SeenAt</c> at database time and <c>LeaseUntil</c> at database time plus <paramref name="lease"/>, then
    /// reads the partitions whose owner is <paramref name="node"/> with their epochs (a read that writes nothing). The
    /// membership reads (the live-node count for <paramref name="window"/> and the free list) run after it, outside any
    /// transaction that holds a node record, as <see cref="IMembership.HeartbeatAsync"/> runs them: a heartbeat never
    /// reads another node record while holding its own. The caller takes its monotonic time just before sending it.
    /// </summary>
    ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This store keeps no node lease.");

    /// <summary>The partitions the database was created with (K1 slice 7), or null when the store does not say.</summary>
    ValueTask<int?> PartitionCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<int?>(null);

    /// <summary>
    /// Take a set of virtual partitions. Each is taken, or left out, exactly as the one-partition take decides for it,
    /// with its own epoch and its own death count; the set need not be contiguous. <c>Now</c> is the store's time when a row was taken; when nothing was, a caller must not read it (every store returns 0).
    /// Under the node lease a caller does not bound its node lease by <c>Now</c> either (<see cref="NodeLeaseAvailable"/>).
    /// The default takes one at a time, k round trips: if one of them fails, the partitions before it are taken already,
    /// so a failure is an unknown outcome and the caller treats the whole set as it treats a failed take.
    /// </summary>
    async ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        // The earliest take's time, so that a lease reckoned from it never outlasts any of the rows.
        var (taken, now) = (new List<PartitionFence>(), long.MaxValue);
        foreach (var partition in partitions.Distinct().Order())
        {
            if (await TakeAsync(partition, node, lease, cancellationToken) is not { } one) continue;
            taken.Add(one.For(partition));
            now = Math.Min(now, one.Now);
        }
        return new(taken, taken.Count > 0 ? now : 0);
    }

    /// <summary>Give up each of these partitions that is still at its epoch; positions are not compared. The rest of the set is untouched.</summary>
    async ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default)
    {
        foreach (var fence in owned.OrderBy(f => f.Partition)) await ReleaseAsync(fence.Partition, fence.Epoch, cancellationToken);
    }

    /// <summary>
    /// Take one partition again after a reset, only if <paramref name="node"/> still holds it at <paramref name="epoch"/>:
    /// the take bumps the epoch, keeps the position and is no death. A row that was released (no owner) or taken since,
    /// by another node or by this node elsewhere (another epoch), is left alone: null. A store with owned sets implements
    /// it; the default refuses, so a worker drops that partition and its host takes it as usual.
    /// </summary>
    ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This store does not retake a partition at an epoch.");

    /// <summary>
    /// The retake, deciding as the one above. With <paramref name="countDeaths"/> (K1b, #4837: a pending partition retaken
    /// after the guard expired) a successful retake is a death: in the same transaction every message on trial at the
    /// previous epoch and not done gets one more attempt, as a take from an expired owner counts it. Without it, it is the
    /// retake above. There is no set form: a pending row is still this node's at the epoch it held, so a set take would
    /// read it as a retake by the recorded owner and count nothing; the Worker retakes pending rows one at a time with
    /// this call, as it retakes after a reset. "A gain or set take that includes a pending partition is its retake"
    /// (ownership-and-fencing.md) is the Worker's routing: it splits the pending partitions out of the take and retakes
    /// each with <paramref name="countDeaths"/> set. The default refuses a counting retake.
    /// </summary>
    ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default) =>
        countDeaths ? throw new NotSupportedException("This store does not count deaths at a retake.") : RetakeAsync(partition, node, epoch, lease, cancellationToken);

    /// <summary>
    /// One poll of an owned set: each partition's rows not yet seen at that partition's epoch, merged in seq order, at most
    /// <paramref name="limit"/>. Rows of different partitions are ordered only to the store's tick: two added in one tick
    /// may come in either order (Mongo's sequence is each partition's clock, in milliseconds).
    /// </summary>
    async ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.RequireDistinct(owned);
        var (rows, now) = (new List<Stored>(), long.MaxValue);
        foreach (var fence in owned.OrderBy(f => f.Partition))
        {
            var one = await PollAsync(fence.Partition, fence.Epoch, horizon, limit, cancellationToken);
            now = Math.Min(now, one.Now);
            rows.AddRange(one.Messages);
        }
        return new([.. rows.OrderBy(m => m.Seq).Take(limit)], owned.Count == 0 ? 0 : now);
    }

    /// <summary>Add a message. False when the id exists already.</summary>
    ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>Insert the delivery derived from an event and subscription. Repeating the match is idempotent.</summary>
    ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

    /// <summary>One load under the provider's actor-key equality. Ordinal providers use their existing indexed read.</summary>
    async ValueTask<IReadOnlyList<ActorLookup>> LoadActorsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        ActorLookup.Ordinal(ids, await LoadAsync(ids, cancellationToken));

    ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Register a subscription of kind start. Waits of instances are written by their commits, not here. Idempotent.</summary>
    ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    /// <summary>Read canonical persisted rows using this store's correlation and identity equality.</summary>
    ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default);

    /// <summary>The position of the partition's projector, its own row apart from the fence, and then the partition's position.</summary>
    ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: move the projector's position from <paramref name="from"/> to <paramref name="to"/>, if it is
    /// still at <paramref name="from"/>, and write the view rows of <paramref name="rows"/>; a row of a deleted instance
    /// resets its view at that journal position. Positive revisions after that delete start a fresh count. False when another projector moved it first.
    /// </summary>
    ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default);

    /// <summary>The viewer's read: the view rows of these instances, as the projectors have written them. Missing ids are skipped.</summary>
    ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decision 11, the viewer's tail: one instance's journal rows past position <paramref name="after"/>, in position order.
    /// Return one committed snapshot, including any delete marker visible in that snapshot. Concurrent commits must
    /// not extend the read indefinitely or combine rows from different snapshots. Wrappers forward this operation.
    /// </summary>
    ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default);

    /// <summary>Delete handled outside commands and events whose window has passed.</summary>
    ValueTask<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <summary>A read-only event delivery. Only the event router constructs this envelope.</summary>
public sealed class EventDelivery
{
    internal EventDelivery(Message message, EventMatch match) => Message = message.WithInboxScope(-2, match.Encode());
    public Message Message { get; }
    public Message At(long serial) => Message.Start is null ? Message : Message with { Target = InstanceRef.ForEvent(Message.Target.Partition, serial) };
}

public sealed record EventRoute(EventRow Event, SubscriptionRow Wait)
{
    public EventDelivery Delivery(int partitions = 1, string? replyTo = null) =>
        new(Events.Deliver(Event, Wait, partitions) with { ReplyTo = replyTo }, new(Wait.Key, Event.Id, Wait.Instance, Wait.Wait, Wait.Creation));
}

public sealed record EventAcceptance(bool Added, InstanceRef Target)
{
    public static implicit operator bool(EventAcceptance result) => result.Added;
}

public sealed record EventMatch(
    [property: JsonConverter(typeof(DurableKeyJson))] string Key,
    [property: JsonConverter(typeof(DurableKeyJson))] string Event,
    [property: JsonConverter(typeof(DurableKeyJson))] string Instance,
    [property: JsonConverter(typeof(DurableKeyJson))] string Wait,
    long Creation = 0)
{
    public string Encode() => Json.Write(this);
    public static EventMatch Decode(string text) => Json.Read<EventMatch>(text);
}

public static class Events
{
    /// <summary>
    /// The logical wire envelope for a match. A start carries its placement partition until the store
    /// allocates the physical target and returns it with the acceptance receipt.
    /// </summary>
    internal static Message Deliver(EventRow e, SubscriptionRow s, int partitions = 1) => s.Start is null
        ? new(s.Creation > 0 ? FormattableString.Invariant($"evq:{e.Id.Length}:{e.Id}:{CreationProtocol.Origin(s.Instance, s.Creation)}:{s.Wait}")
            : $"ev:{e.Id}:{s.Instance}:{s.Wait}", new InstanceRef(s.Instance), Kinds.Event, e.Payload, KeepUntil: e.KeepUntil,
            TargetCreation: s.Creation > 0 ? s.Creation : null)
        : new($"ev:{e.Id}::{s.Wait}", InstanceRef.In(InstanceRef.Spread($"{e.Id}:{s.Wait}", partitions), ""), Kinds.Event, e.Payload, Start: s.Start, KeepUntil: e.KeepUntil);

    /// <summary>
    /// What a delivery is for: the event's id and the wait it was delivered to, read back from the derived id (merge,
    /// friction F-N3: a model need not know the id's format). Null for a message that is no delivery.
    /// </summary>
    public static (string Event, string Wait)? Delivered(Message m)
    {
        if (m.Kind == Kinds.Event && m.Start is null && m.TargetCreation is > 0 && m.Id.StartsWith("evq:", StringComparison.Ordinal))
        {
            var split = m.Id.IndexOf(':', 4);
            if (split < 0 || !int.TryParse(m.Id.AsSpan(4, split - 4), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var length) || length > m.Id.Length - split - 1) return null;
            var start = split + 1;
            var qualifiedMarker = $":{CreationProtocol.Origin(m.Target.Id, m.TargetCreation.Value)}:";
            if (!m.Id.AsSpan(start + length).StartsWith(qualifiedMarker, StringComparison.Ordinal)) return null;
            return (m.Id.Substring(start, length), m.Id[(start + length + qualifiedMarker.Length)..]);
        }
        if (m.Kind != Kinds.Event || !m.Id.StartsWith("ev:", StringComparison.Ordinal)) return null;
        if (m.EventMatch is { } encoded)
        {
            var match = EventMatch.Decode(encoded);
            return (match.Event, match.Wait);
        }
        var marker = m.Start is null ? $":{m.Target.Id}:" : "::";
        var at = m.Id.LastIndexOf(marker, StringComparison.Ordinal);
        return at < 3 ? null : (m.Id[3..at], m.Id[(at + marker.Length)..]);
    }

    /// <summary>Both sides of the match insert the same kernel-derived envelope. An HTTP waiter may attach its response handle.</summary>
    public static EventDelivery Route(EventRow e, SubscriptionRow s, int partitions = 1, string? replyTo = null)
    {
        if (e.Key != s.Key) throw new ArgumentException("The event and subscription must name the same key.", nameof(s));
        return new EventRoute(e, s).Delivery(partitions, replyTo);
    }

    /// <summary>The publisher's side: insert the event, and only then read who waits for it.</summary>
    public static async ValueTask PublishAsync(IKernelStore store, EventRow e, int partitions = 1, CancellationToken cancellationToken = default, Action<EventRoute, EventAcceptance>? matched = null)
    {
        await store.PublishAsync(e, cancellationToken);
        foreach (var match in await store.PublishedMatchesAsync(e, cancellationToken))
        {
            var accepted = await store.AddEventAsync(match.Delivery(partitions), cancellationToken);
            matched?.Invoke(match, accepted);
        }
    }

    /// <summary>
    /// The registrar's side of a start subscription: insert it, and only then read the events of its key that are still
    /// kept. Like a publisher, the registrar retries until both are done.
    /// </summary>
    public static async ValueTask SubscribeAsync(IKernelStore store, SubscriptionRow start, int partitions, CancellationToken cancellationToken = default, Action<EventRoute, EventAcceptance>? matched = null)
    {
        await store.SubscribeAsync(start, cancellationToken);
        await CatchUpAsync(store, start, partitions, cancellationToken, matched);
    }

    public static async ValueTask CatchUpAsync(IKernelStore store, SubscriptionRow start, int partitions, CancellationToken cancellationToken = default, Action<EventRoute, EventAcceptance>? matched = null)
    {
        foreach (var match in await store.KeptMatchesAsync([start], cancellationToken))
        {
            var accepted = await store.AddEventAsync(match.Delivery(partitions), cancellationToken);
            matched?.Invoke(match, accepted);
        }
    }
}
