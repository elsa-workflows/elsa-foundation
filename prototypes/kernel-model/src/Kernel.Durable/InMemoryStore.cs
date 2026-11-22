using System.Collections.Immutable;
using System.Text;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// The store in memory, with the semantics the SQL is meant to have: every operation is atomic, the conditional
/// updates are honoured, keys are create-only. It also keeps <see cref="Ghost"/>: things a correct kernel never does,
/// seen from the one place that sees every write.
/// </summary>
public sealed partial class InMemoryStore(int partitions, Func<long> clock, Flaw flaws = Flaw.None) : IKernelStore
{
    private readonly StringComparer _foundationEventEquality = StringComparer.Ordinal;

    internal InMemoryStore(int partitions, Func<long> clock, Flaw flaws, StringComparer foundationEventEquality,
        StringComparer? foundationCallerEquality = null)
        : this(partitions, clock, flaws)
    {
        _foundationEventEquality = foundationEventEquality;
        if (foundationCallerEquality is not null) _foundationCallerBindings = new(foundationCallerEquality);
        var pairs = EqualityComparer<(string Key, string Id)>.Create(
            (a, b) => foundationEventEquality.Equals(a.Key, b.Key) && foundationEventEquality.Equals(a.Id, b.Id),
            pair => HashCode.Combine(foundationEventEquality.GetHashCode(pair.Key), foundationEventEquality.GetHashCode(pair.Id)));
        _foundationEvents = new(pairs);
        _foundationEventReceipts = new(pairs);
    }

    public Func<long> FoundationClock => clock;

    public bool CommitClockAvailable { get; set; } = true;
    public bool PhysicalRetirementAvailable => true;
    public bool CreationIdentityAvailable { get; set; } = true;

    public sealed class PartitionRow
    {
        public string? Owner;
        public long Epoch, Position;
    }

    public sealed class MessageRow(Message message, long seq, long seen)
    {
        internal ParkKeepReason? KeptBy;
        internal readonly int PayloadLength = checked(
            PayloadBytes(message.StoreStartInputOnce ? null : message.Payload) + PayloadBytes(message.Start?.Input));
        // Cache the bounded deadline linkage at insertion. A recovery page never deserializes a ticket's arguments.
        internal readonly (string? JobKey, string? Timer, long? At) JobTiming = ReadJobTiming(message);
        internal ParkRelease? ReleasedBy;
        internal bool ParkDeadLetter;
        public Message Message = message.StoreStartInputOnce ? message with { Payload = null, StoredEnvelope = null } : message;
        public long Seq = seq, Seen = seen, Trial;
        public int Attempts;
        public bool Done, Held;

        private static int PayloadBytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

        private static (string?, string?, long?) ReadJobTiming(Message envelope)
        {
            if (envelope.Payload is null || !envelope.HasIssuedTicket) return default;
            try
            {
                if (envelope.Kind == Kinds.Job && envelope.Read<JobTicket>() is { Timer: not null } ticket)
                    return (ticket.Key, ticket.Timer, ticket.Deadline);
                if (envelope.Kind == Kinds.JobDeadline && envelope.Read<JobDeadline>() is { } deadline)
                    return (deadline.Key, null, null);
            }
            catch (System.Text.Json.JsonException) { }
            return default;
        }
    }

    private readonly Lock _gate = new();
    private long _seq;

    public PartitionRow[] Partitions { get; } = Enumerable.Range(0, partitions).Select(_ => new PartitionRow()).ToArray();
    public Dictionary<string, InstanceRow> Instances { get; } = new(StringComparer.Ordinal);
    public Dictionary<InboxKey, MessageRow> Messages { get; } = [];
    private readonly HashSet<SubscriptionRow> _subscriptions = new(EqualityComparer<SubscriptionRow>.Create(
        (a, b) => a is null || b is null ? a == b : (a.Key, a.Instance, a.Wait) == (b.Key, b.Instance, b.Wait),
        row => HashCode.Combine(row.Key, row.Instance, row.Wait)));
    public IReadOnlySet<SubscriptionRow> Subscriptions => _subscriptions;
    public Dictionary<(string Key, string Id), EventRow> EventRows { get; } = [];
    private readonly Dictionary<(string Key, string Id), EventRow> _foundationEvents = [];
    internal IReadOnlyDictionary<(string Key, string Id), EventRow> FoundationEvents => _foundationEvents;

    private Dictionary<(string Key, string Id), EventRow> EventsFor(EventRow @event) =>
        @event.FoundationTenant is null ? EventRows : _foundationEvents;

    private StringComparer EventEquality(EventRow publication) =>
        publication.FoundationTenant is null ? StringComparer.Ordinal : _foundationEventEquality;

    private EventRoute MatchRoute(EventRow publication, SubscriptionRow wait) =>
        new(publication, wait) { FoundationKeyEquality = EventEquality(publication) };

    private SubscriptionRow? RegisteredSubscription(SubscriptionRow requested)
    {
        if (_subscriptions.TryGetValue(requested, out var stored) && stored.FoundationTenant == requested.FoundationTenant)
            return stored;
        if (requested.FoundationTenant is null || _foundationEventEquality == StringComparer.Ordinal) return null;

        return Subscriptions.FirstOrDefault(wait => wait.FoundationTenant == requested.FoundationTenant &&
            wait.Instance == requested.Instance && _foundationEventEquality.Equals(wait.Key, requested.Key) &&
            _foundationEventEquality.Equals(wait.Wait, requested.Wait));
    }

    public Dictionary<(int Partition, long Position), JournalRow> Journal { get; } = [];
    public List<string> Ghost { get; } = [];
    public Dictionary<string, NodeRow> Nodes { get; } = new(StringComparer.Ordinal);

    /// <summary>A node record: membership's <c>SeenAt</c> and, under the node lease, <c>LeaseUntil</c>; both database time.</summary>
    public sealed class NodeRow
    {
        public long SeenAt, LeaseUntil;
    }


    /// <summary>
    /// For tests: called with the taker's name once a node-lease take has made its plain read of the owners' node
    /// records, before it writes its own record and locks partition rows. A store call made here lands between the two.
    /// </summary>
    public Action<string>? TakeRead { get; set; }
    public long[] Projection { get; } = new long[partitions];
    public long[] Deleted { get; } = new long[partitions];     // journal rows removed by deletes, per partition: for the tests
    public Dictionary<string, ViewRow> Views { get; } = new(StringComparer.Ordinal);

    /// <summary>A row the database refuses, every time: a value too long.</summary>
    public Func<InstanceRow, bool>? Refuses { get; set; }

    public sealed record DeletedCheckpoint(InstanceRow Row, ImmutableArray<JournalRow> Journal)
    {
        public JournalRow? Receipt { get; init; }
    }
    private Dictionary<CreationAddress, TaskCompletionSource<DeletedCheckpoint>>? _watchedDeletes;

    /// <summary>For tests: preserve the actual checkpoint and journal at the next fenced deletion of this occurrence.</summary>
    public Task<DeletedCheckpoint> WatchDeletion(CreationAddress address)
    {
        lock (_gate)
        {
            var watches = _watchedDeletes ??= [];
            if (!watches.TryGetValue(address, out var watch)) watches[address] = watch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return watch.Task;
        }
    }

    private bool Has(Flaw flaw) => (flaws & flaw) != 0;

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Take([partition], node, lease, clock()).Select(t => t.Lease).SingleOrDefault());
    }

    /// <summary>A take of a set: one transaction, the rows in ascending id, each decided as the one-partition take decides.</summary>
    public ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            var taken = Take(partitions, node, lease, now);
            return new(new OwnedLease([.. taken.Select(t => t.Lease.For(t.Partition))], taken.Count > 0 ? now : 0));   // 0 when nothing was taken, as on EF and MongoDB
        }
    }

    // One take's transaction. Under the node lease it reads the owners' node records first, a plain read holding nothing,
    // then writes the taker's own record, then decides the partition rows in ascending id by that read.
    private List<(int Partition, Lease Lease)> Take(IEnumerable<int> partitions, string node, TimeSpan lease, long now)
    {
        var ids = partitions.Distinct().Order().ToList();
        // MUTATION TakeIgnoresNodeLease: every owner reads as expired.
        var expired = ids.Select(p => Partitions[p].Owner).OfType<string>().Where(o => o != node && (Has(Flaw.TakeIgnoresNodeLease) || NodeExpired(o, now))).ToHashSet(StringComparer.Ordinal);
        TakeRead?.Invoke(node);
        Renew(node, lease, now);
        var taken = new List<(int, Lease)>();
        foreach (var partition in ids)
        {
            var p = Partitions[partition];
            var died = p.Owner is not null && p.Owner != node;      // taken from another: only possible once its lease has expired
            if (died && !expired.Contains(p.Owner!)) continue;
            if (died || (Has(Flaw.EveryTakeCounts) && p.Owner is not null))
                foreach (var row in Messages.Values)
                    if (row.Trial == p.Epoch && !row.Done && row.Message.Target.Partition == partition && !row.Message.HasFencedEventStep)
                        row.Attempts++;
            CountAbandonedParkCatchUpTrials(partition, p.Epoch);
            (p.Owner, p.Epoch) = (node, p.Epoch + 1);
            taken.Add((partition, new Lease(p.Epoch, p.Position, now)));
        }
        return taken;
    }

    // The node lease: the owner's node record is missing, or its lease is before database time.
    private bool NodeExpired(string owner, long now) => !Nodes.TryGetValue(owner, out var n) || n.LeaseUntil < now;

    private bool Free(PartitionRow p, long now) => p.Owner is null || NodeExpired(p.Owner, now);

    // The node record's write, by a heartbeat and by every take under the node lease.
    private void Renew(string node, TimeSpan lease, long now)
    {
        if (!Nodes.TryGetValue(node, out var row)) Nodes[node] = row = new NodeRow();
        (row.SeenAt, row.LeaseUntil) = (now, now + (long)lease.TotalMilliseconds);
    }

    public bool OwnedSetsAvailable => true;

    public ValueTask<int?> PartitionCountAsync(CancellationToken cancellationToken = default) => new(Partitions.Length);

    /// <summary>For tests: called with each fence row's partition as a commit locks it, in the order it locks them.</summary>
    public Action<int>? Locking { get; set; }

    public bool SupportsCommitWindow => true;
    bool IKernelStore.SupportsCascadePages => true;
    public bool SupportsJobTrialClear => true;

    // FenceSubset: the batch cut to the partitions whose fence matches, or null when none does or every one does.
    private CommitBatch? Subset(CommitBatch batch, int stale)
    {
        var current = batch.Fenced.Where(f => Partitions[f.Partition].Epoch == f.Epoch && Partitions[f.Partition].Position == f.Position).ToList();
        if (current.Count == 0 || stale == 0) return null;
        var kept = current.Select(f => f.Partition).ToHashSet();
        bool Kept(InboxKey id) => Messages.GetValueOrDefault(id) is { } row && kept.Contains(row.Message.Target.Partition);
        var turns = batch.Turns.Where(t => batch.PartitionOf(t) is var p && kept.Contains(p)).ToList();
        return CommitBatch.Owned(current, batch.Lease, batch.HandOver, turns, [.. batch.Seen.Where(Kept)], [.. batch.Trial.Where(Kept)])
            with { Window = batch.Window, ClearJobTrials = [.. batch.ClearJobTrials.Where(Kept)] };
    }

    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.Validate(batch, ownedSets: true, supportsJobTrialClear: SupportsJobTrialClear,
            supportsFoundationParkResolution: true);
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(batch, physicalRetirement: true);
        if (!CommitClockAvailable) CommitClockFacts.RejectUnsupported(batch);
        lock (_gate)
        {
            // The k-row fence: every fence row the batch touches, locked in ascending partition id, all or nothing.
            var (fences, now) = (batch.Fenced, clock());
            var (epoch, position, fenced, stale) = (true, true, true, new List<string>());
            // MUTATION PlainReadEmptyFence: a batch with no journal rows reads its fence rows without locking them, so a
            // take may land on them after the check.
            var plain = Has(Flaw.PlainReadEmptyFence) && batch.Positions == 0;
            foreach (var fence in fences)
            {
                if (!plain) Locking?.Invoke(fence.Partition);
                var row = Partitions[fence.Partition];
                bool e = row.Epoch == fence.Epoch, q = row.Position == fence.Position;
                (epoch, position) = (epoch && e, position && q);
                fenced &= (e || Has(Flaw.FenceWithoutEpoch)) && (q || Has(Flaw.FenceWithoutPosition));
                if (!e) stale.Add($"I1 a commit prepared under epoch {fence.Epoch} landed in partition {fence.Partition} at epoch {row.Epoch}");
                else if (!q) stale.Add($"I1 a commit prepared at position {fence.Position} landed in partition {fence.Partition} at position {row.Position}");
            }
            if (plain) foreach (var fence in fences) Locking?.Invoke(fence.Partition);
            if (!fenced && Has(Flaw.FenceSubset) && batch.Fences is not null && Subset(batch, stale.Count) is { } part)
                return CommitAsync(part, cancellationToken);    // MUTATION: the part whose fences still match lands
            if (!fenced && !Has(Flaw.CommitDespiteFence)) return new(new Committed(false, now, []));
            Ghost.AddRange(stale);
            var epochs = fences.ToDictionary(f => f.Partition, f => f.Epoch);
            // MUTATION SeenAtNodeEpoch: the set's highest epoch, as one epoch for the whole node would be.
            long MarkEpoch(MessageRow row) => Has(Flaw.SeenAtNodeEpoch) && fences.Count > 0 ? fences.Max(f => f.Epoch) : epochs.GetValueOrDefault(row.Message.Target.Partition, batch.Epoch);

            foreach (var id in batch.ClearJobTrials)
                if (Messages.GetValueOrDefault(id) is not { Done: false, Trial: > 0, Message.Kind: Kinds.Job } pending ||
                    pending.Message.InboxScope <= 0 ||
                    !epochs.TryGetValue(pending.Message.Target.Partition, out var fenceEpoch) || fenceEpoch != pending.Trial)
                    Diverged($"job ticket {id} has no pending trial at the fenced epoch");

            Dictionary<InboxKey, ReferenceBinding>? childBindings = null;
            Dictionary<string, ReferenceBinding>? inlineRoots = null;
            Dictionary<string, Message>? initialExpiries = null;
            Dictionary<TurnCommit, FoundationSubscriptionInsert>? subscriptionInserts = null;
            HashSet<string>? subscriptionWriters = null;
            Dictionary<InboxKey, CatchUpStep>? catchUpSteps = null;
            PublicationPage? publicationPage = null;
            Dictionary<TurnCommit, CascadeInputs>? cascades = null;
            if (fenced)
            {
                // Everything is checked before anything is written: a statement that fails aborts the transaction.
                ValidateParkCatchUpMarks(batch, epochs);
                var at = fences.ToDictionary(f => f.Partition, f => f.Position);
                HashSet<CreationAddress>? childAddresses = null;
                HashSet<string>? childReferences = null;
                long? expiryFrom = null, expiryUntil = null;
                var revoked = CatchUpRevocations(batch);
                var hasSubscriptionWrites = batch.Turns.Any(turn => turn.Subscribed.Count != 0);
                var trackSubscriptionRemovals = batch.Turns.Count > 1 && hasSubscriptionWrites;
                var mixedSubscriptionWrites = hasSubscriptionWrites &&
                    batch.Turns.Any(turn => turn.Subscribed.Any(wait => wait.FoundationTenant is null)) &&
                    batch.Turns.Any(turn => turn.Subscribed.Any(wait => wait.FoundationTenant is not null));
                var removedWaits = trackSubscriptionRemovals ? new HashSet<SubscriptionRow>(ReferenceEqualityComparer.Instance) : null;
                var clearedWaits = trackSubscriptionRemovals ? new HashSet<string>(StringComparer.Ordinal) : null;
                Dictionary<(string Key, string Instance, string Wait), SubscriptionRow>? plannedSubscriptionSlots = null;
                var earlier = batch.Turns.Any(turn => turn.FoundationCascade is not null) ? new HashSet<InboxKey>() : null;
                var competing = earlier is not null
                    ? batch.Turns.SelectMany(turn => turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? [])
                        .Concat(turn.Sent.Select(message => message.InboxKey)))
                        .Concat(batch.Seen).Concat(batch.Trial).Concat(batch.ClearJobTrials).ToHashSet()
                    : [];
                foreach (var turn in batch.Turns)
                {
                    if (turn.Delete && Instances.GetValueOrDefault(turn.Instance)?.Revision != turn.Expected) Diverged($"instance {turn.Instance} to delete is not at revision {turn.Expected}");
                    if (turn.Row is { } row)
                    {
                        if (Refuses?.Invoke(row) == true) throw new StoreException(Failure.Definite, $"the row of {row.Id} was refused");
                        var revision = Instances.GetValueOrDefault(row.Id)?.Revision;
                        if (revision != turn.Expected) Diverged($"instance {row.Id} is at revision {revision?.ToString() ?? "none"}, the commit expected {turn.Expected?.ToString() ?? "none"}");
                        if (!row.Retired) CommitClockFacts.Validate(row, Instances.GetValueOrDefault(row.Id));
                    }
                    var previous = Instances.GetValueOrDefault(turn.Instance);
                    var thenDelete = IsThenDeleteRetirement(turn, previous, now);
                    CreationProtocol.Validate(turn, previous, thenDelete);
                    if ((PrepareCascade(turn, previous, epochs, earlier, competing, thenDelete) ??
                        PrepareTerminalJobDeadlines(turn, previous, epochs, batch)) is { } cascade)
                    {
                        if (cascade.Kept.Count != 0) _ = checked(now + (long)IngressDeduplicationWindow.TotalMilliseconds);
                        (cascades ??= new(ReferenceEqualityComparer.Instance)).Add(turn, cascade);
                        earlier?.UnionWith(cascade.Pending);
                        earlier?.UnionWith(cascade.Kept);
                    }
                    earlier?.UnionWith(turn.Consumed);
                    earlier?.UnionWith(turn.Done);
                    if (turn.Row is { Retired: false } owner && previous?.Creation == owner.Creation &&
                        previous.NextJobGeneration is { } oldGeneration &&
                        (owner.NextJobGeneration is null || owner.NextJobGeneration.Value < oldGeneration))
                        throw new StoreException(Failure.Definite, "A job generation counter cannot move backwards within its creation.");
                    StoreBatchRules.ValidateActorSends(turn, previous);
                    ValidateFoundationHeader(turn);
                    if (ValidateParkDrain(turn, previous, epochs, now) is { } expiryWindow)
                    {
                        if (expiryWindow.Due) expiryFrom = Math.Max(expiryFrom ?? long.MinValue, expiryWindow.At);
                        else expiryUntil = Math.Min(expiryUntil ?? long.MaxValue, expiryWindow.At);
                    }
                    if (turn.FoundationPublicationSequence is not null)
                    {
                        if (batch.Turns.Count != 1) throw InvalidPublicationPage();
                        publicationPage = PreparePublicationPage(turn, epochs);
                    }
                    else if (PrepareParkCatchUp(turn, epochs, revoked, batch.Turns.Count == 1) is { } catchUp &&
                        !(catchUpSteps ??= []).TryAdd(turn.FoundationCatchUpStep!.Value, catchUp))
                        throw new StoreException(Failure.Definite, "A catch-up ticket can advance only once per batch.");
                    var inlineRoot = turn.Row is { FoundationInlineBirth: not null } local ? BindInlineRoot(local) : null;
                    if (inlineRoot is not null && !(inlineRoots ??= new(StringComparer.Ordinal)).TryAdd(turn.Instance, inlineRoot))
                        Diverged($"two inline births for {turn.Instance}");
                    foreach (var subscription in turn.Subscribed)
                        RequireSameSubscriptionScope(subscription, inlineRoot, removedWaits, clearedWaits);
                    var subscriptions = PrepareFoundationSubscriptionInsert(turn, epochs, now, removedWaits, clearedWaits);
                    if (subscriptions is not null)
                    {
                        if (!(subscriptionWriters ??= new(StringComparer.Ordinal)).Add(turn.Instance))
                            Diverged($"two subscription writes for {turn.Instance}");
                        (subscriptionInserts ??= new(ReferenceEqualityComparer.Instance)).Add(turn, subscriptions);
                    }
                    if (mixedSubscriptionWrites)
                    {
                        foreach (var addition in (subscriptions?.Rows ?? []).Concat(turn.Subscribed.Where(wait => wait.FoundationTenant is null)))
                        {
                            var key = (addition.Key, addition.Instance, addition.Wait);
                            plannedSubscriptionSlots ??= [];
                            if (!plannedSubscriptionSlots.TryAdd(key, addition) &&
                                plannedSubscriptionSlots[key].FoundationTenant != addition.FoundationTenant)
                                throw ConflictingSubscriptionScope();
                        }
                        if (plannedSubscriptionSlots is not null && (turn.Unsubscribe || turn.Unsubscribed.Count != 0))
                            foreach (var (key, planned) in plannedSubscriptionSlots.ToArray())
                                if (turn.Unsubscribe && planned.Instance == turn.Instance || turn.Unsubscribed.Any(removal =>
                                    removal.FoundationTenant == planned.FoundationTenant && removal.Instance == planned.Instance &&
                                    (removal.FoundationTenant is null
                                        ? removal.Key == planned.Key && removal.Wait == planned.Wait
                                        : removal.Creation == planned.Creation && _foundationEventEquality.Equals(removal.Key, planned.Key) &&
                                            _foundationEventEquality.Equals(removal.Wait, planned.Wait))))
                                    plannedSubscriptionSlots.Remove(key);
                    }
                    if (turn.Unsubscribe) clearedWaits?.Add(turn.Instance);
                    if (removedWaits is not null)
                        foreach (var removal in turn.Unsubscribed)
                            if (RegisteredSubscription(removal) is { } wait && (removal.FoundationTenant is null || wait.Creation == removal.Creation))
                                removedWaits.Add(wait);
                    foreach (var id in turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []))
                        if (!Has(Flaw.DeleteWithoutCount) && Messages.GetValueOrDefault(id) is not { Done: false }) Diverged($"consumed message {id} is not there");
                    foreach (var id in turn.FoundationReleasedHolds ?? [])
                        if (turn.Row is not { } releasing || Messages.GetValueOrDefault(id) is not { Done: false, Held: true } released ||
                            released.Message.Target.Id != releasing.Id || released.Message.TargetCreation != releasing.Creation)
                            Diverged($"released message {id} is not there for {turn.Instance}");
                    if (turn.FoundationKept is { } kept)
                    {
                        if (turn.Row is not { Retired: false, Status: InstanceStatus.Faulted } park ||
                            !turn.OpensPark && !_foundationParks.ContainsKey(new(park.Id, park.Creation)) ||
                            kept.Values.Any(reason => reason is not (ParkKeepReason.Arrived or ParkKeepReason.Consumed)) ||
                            !kept.Keys.All(id => turn.Done.Contains(id) && !turn.Consumed.Contains(id) &&
                                Messages.GetValueOrDefault(id) is { Done: false } selected &&
                                selected.Message.Target.Id == park.Id && selected.Message.TargetCreation == park.Creation))
                            throw new StoreException(Failure.Definite, "A kept delivery must be marked done by its exact Park.");
                    }
                    if (ChangedFoundationParkInput(turn, previous, epochs) is { } changed)
                        Diverged($"kept input {changed.Cursor.Key} changed before its Park page cleared");
                    var source = turn.Row ?? previous;
                    var foundation = inlineRoot is not null ||
                        source is { } bound && _referenceBirths.ContainsKey(new(bound.Id, bound.Creation));
                    if (foundation && PrepareInitialParkExpiry(turn, previous, now) is { } expiry)
                    {
                        if (Messages.ContainsKey(expiry.InboxKey)) Diverged($"expiry {expiry.Id} exists already");
                        (initialExpiries ??= new(StringComparer.Ordinal)).Add(turn.Instance, expiry);
                    }
                    foreach (var message in turn.Sent)
                    {
                        if (message.FoundationPublish is not null)
                            ValidateAuthoredPublication(turn, message, inlineRoot?.Tenant ??
                                (source is null ? null : _referenceBirths.GetValueOrDefault(new(source.Id, source.Creation))?.Tenant), now);
                        if ((foundation || message.BindFoundationChild) && !FoundationMetadataLimits.FitsComposed(message))
                            throw new StoreException(Failure.Definite, "The composed message exceeds the foundation row budget.");
                        if (Messages.ContainsKey(message.InboxKey)) Diverged($"sent message {message.Id} exists already");
                        if (!message.BindFoundationChild) continue;
                        var binding = BindChild(turn, previous, message, inlineRoot);
                        childBindings ??= [];
                        childAddresses ??= [];
                        childReferences ??= new(StringComparer.Ordinal);
                        if (_referenceBindings.ContainsKey(binding.Reference) || _referenceBirths.ContainsKey(binding.Address) ||
                            !childAddresses.Add(binding.Address) || !childReferences.Add(binding.Reference) ||
                            !childBindings.TryAdd(message.InboxKey, binding))
                            Diverged($"child {message.Target.Id} already has a creator");
                    }
                    if (turn.Journal is not null && batch.PartitionOf(turn) is var journal && Journal.ContainsKey((journal, ++at[journal])))
                        Diverged($"journal position {at[journal]} of partition {journal} is written already");
                }
                if (subscriptionInserts is not null || catchUpSteps is not null)
                    ValidateCatchUpWrites(batch, subscriptionInserts?.Values.Select(insert => insert.Ticket).OfType<Message>() ?? [],
                        catchUpSteps?.Values ?? (IEnumerable<CatchUpStep>)[], initialExpiries?.Values ?? (IEnumerable<Message>)[]);
                if (batch.Turns.Any(turn => turn.FoundationCatchUpBefore is { } deadline && clock() >= deadline))
                    throw new StoreException(Failure.Definite, "The catch-up step reached its deadline before committing.");
                // A mark records its message's own partition's epoch, so a mark outside the fenced set would be unbound, and a
                // delete outside it would be a write no fence covers.
                if (batch.Fences is not null)
                    foreach (var id in batch.Seen.Concat(batch.Trial).Concat(batch.Turns.SelectMany(t => t.Consumed.Concat(t.Done))))
                        if (Messages.GetValueOrDefault(id) is { } marked && !epochs.ContainsKey(marked.Message.Target.Partition))
                            throw new StoreException(Failure.Definite, $"message {id} is outside the batch's fenced partitions");
                if (batch.Window is not null || expiryFrom is not null || expiryUntil is not null)
                {
                    var atCommit = clock();
                    if (batch.Window is { } window && (atCommit < now || atCommit < window.FromInclusive || atCommit >= window.UntilExclusive) ||
                        atCommit < expiryFrom || atCommit >= expiryUntil)
                        throw new StoreException(Failure.WindowExpired, "The database clock left the decision's authority window.");
                }
                foreach (var fence in fences)
                {
                    Partitions[fence.Partition].Position = fence.Position + batch.PositionsIn(fence.Partition);
                }
            }

            List<LifecycleObservation.Change>? changes = null;
            List<Stored>? armedExpiries = null;
            var (next, seqs) = (fences.ToDictionary(f => f.Partition, f => f.Position), new List<long>());
            List<(CreationAddress Address, DeletedCheckpoint Checkpoint, (int, long)? Position)>? deletes = null;
            foreach (var turn in batch.Turns)
            {
                var journal = batch.PartitionOf(turn);
                var opening = epoch && position ? Lifecycle?.BeforeTurn(turn, this) : null;
                if (epoch && position && turn.Delete && _watchedDeletes is { Count: > 0 } watches &&
                    Instances.GetValueOrDefault(turn.Instance) is { Retired: false } prior && watches.ContainsKey(new(prior.Id, prior.Creation)))
                {
                    var entries = prior.History == History.Full ? Journal.Values.Where(j => j.Instance == prior.Id).ToImmutableArray() : [];
                    (deletes ??= []).Add((new(prior.Id, prior.Creation), new(prior, entries), turn.Journal is null ? null : (journal, next[journal] + 1)));
                }
                if (turn.Row is { } stored)
                {
                    if (inlineRoots?.GetValueOrDefault(stored.Id) is { } root) InsertReference(root);
                    var previous = Instances.GetValueOrDefault(stored.Id);
                    Instances[stored.Id] = CommitClockFacts.Stamp(stored.WithoutInlineBirth(), now);
                    var expiry = initialExpiries?.GetValueOrDefault(turn.Instance);
                    RecordFoundationMetadata(turn, stored, previous, now, expiry);
                    if (previous?.ParkRecovery is not null && stored.ParkRecovery is null &&
                        stored.Status == InstanceStatus.Faulted && turn.FoundationKept is { Count: > 0 })
                        RevertFoundationRetryMarks(stored);
                    if (expiry is not null)
                    {
                        var seen = batch.HandOver ? epochs.GetValueOrDefault(expiry.Target.Partition) : 0;
                        Messages[expiry.InboxKey] = new(expiry, ++_seq, seen);
                        (armedExpiries ??= []).Add(new(expiry, _seq, seen, 0, 0));
                    }
                }
                if (turn.Delete)
                {
                    if (turn.Row is not { Retired: true } && Instances.Remove(turn.Instance, out var removed) && _referenceBirths.Count > 0)
                        RemoveFoundationMetadata(new(removed.Id, removed.Creation));
                    foreach (var key in Journal.Where(j => j.Value.Instance == turn.Instance && j.Value.Revision > 0 && !Has(Flaw.DeleteLeavesJournal)).Select(j => j.Key).ToList())
                    {
                        Journal.Remove(key);
                        Deleted[key.Partition]++;
                    }
                }
                if (turn.FoundationPublicationSequence is null)
                {
                    ApplyParkCatchUpFailure(turn);
                    if (turn.FoundationCatchUpStep is { } catchUpSource) ClearParkCatchUpTrial(catchUpSource, Messages[catchUpSource]);
                }
                foreach (var id in turn.Consumed) Messages.Remove(id);
                if (cascades?.GetValueOrDefault(turn) is { } cascade)
                    ApplyCascade(cascade, now);
                foreach (var id in turn.FoundationReleasedHolds ?? []) Messages[id].Held = false;
                ClearFoundationParkPage(turn.FoundationParkClear, now);
                ApplyFoundationRecoveryPage(turn, now);
                ApplyFoundationRetryOpen(turn.FoundationRetry);
                ApplyFoundationTimerOpen(turn.FoundationRecoveryTimers);
                ApplyFoundationJobOpen(turn.FoundationRecoveryJobs);
                foreach (var id in turn.Done)
                {
                    if (turn.FoundationKept?.TryGetValue(id, out var reason) == true)
                    {
                        var retained = Messages[id];
                        (retained.Done, retained.Held, retained.Trial, retained.KeptBy) = (true, false, 0, reason);
                        continue;
                    }
                    // An outside command stays as a marker until its window has passed, so a late retry meets a duplicate key.
                    if (Has(Flaw.DeleteOutsideCommands)) Messages.Remove(id);
                    else if (Messages.GetValueOrDefault(id) is { } done) done.Done = true;
                }
                foreach (var message in turn.Sent)
                {
                    // Messages the owner hands over in memory to its own partition are written as seen already.
                    Messages[message.InboxKey] = new MessageRow(StampAuthoredPublication(message, now), ++_seq,
                        batch.HandOver && epochs.TryGetValue(message.Target.Partition, out var mine) ? mine : 0)
                    {
                        Held = turn.FoundationHeldPromotions?.Contains(message.InboxKey) == true
                    };
                    seqs.Add(_seq);
                    if (childBindings?.GetValueOrDefault(message.InboxKey) is { } binding) InsertReference(binding);
                }
                if (subscriptionInserts?.GetValueOrDefault(turn) is { } subscriptions)
                    InsertFoundationSubscriptions(subscriptions);
                foreach (var subscription in turn.Subscribed)
                    if (subscription.FoundationTenant is null && RegisteredSubscription(subscription) is null) AddRegisteredSubscription(subscription);
                foreach (var subscription in turn.Unsubscribed)
                    if (RegisteredSubscription(subscription) is { } found &&
                        (subscription.FoundationTenant is null || found.Creation == subscription.Creation))
                        RemoveRegisteredSubscription(found);
                if (turn.Unsubscribe)
                    foreach (var subscription in Subscriptions.Where(subscription => subscription.Instance == turn.Instance).ToArray())
                        RemoveRegisteredSubscription(subscription);
                if (turn.FoundationCatchUpStep is { } step && catchUpSteps?.GetValueOrDefault(step) is { } prepared)
                    ApplyParkCatchUp(prepared);
                if (publicationPage is not null) ApplyPublicationPage(publicationPage);
                if (turn.Journal is not null)
                {
                    var creation = turn.Row?.Creation ?? Instances.GetValueOrDefault(turn.Instance)?.Creation ?? 0;
                    Journal[(journal, ++next[journal])] = new JournalRow(journal, next[journal], turn.Instance,
                        turn.Delete ? -1 : turn.Row?.Revision ?? 0, turn.Journal)
                    {
                        FoundationCreation = _referenceBirths.ContainsKey(new(turn.Instance, creation)) ? creation : 0
                    };
                }
                if (opening is not null)
                    (changes ??= []).Add(opening with { After = Instances.GetValueOrDefault(turn.Instance),
                        Receipt = turn.Delete && turn.Journal is not null ? Journal[(journal, next[journal])] : null });
            }
            foreach (var id in batch.Seen)
                if (Messages.GetValueOrDefault(id) is { } seen) seen.Seen = MarkEpoch(seen);
            foreach (var id in batch.Trial)
                if (Messages.GetValueOrDefault(id) is { } tried)
                {
                    (tried.Seen, tried.Trial) = (MarkEpoch(tried), MarkEpoch(tried));
                    if (tried.Message.HasFencedEventStep) MarkParkCatchUpTrial(id, tried);
                }
            foreach (var id in batch.ClearJobTrials) Messages[id].Trial = 0;
            foreach (var id in batch.Turns.SelectMany(s => s.Held ?? []))
                if (Messages.GetValueOrDefault(id) is { } held)
                {
                    held.Held = true;
                    if (!Has(Flaw.HoldKeepsTrial)) held.Trial = 0;
                }
            if (changes is not null) Lifecycle!.Publish(changes);
            if (deletes is not null)
                foreach (var (address, checkpoint, receiptPosition) in deletes)
                    if (_watchedDeletes!.Remove(address, out var watch))
                        watch.SetResult(checkpoint with { Receipt = receiptPosition is { } slot ? Journal.GetValueOrDefault(slot) : null });
            return new(new Committed(fenced, now, seqs)
            {
                ParkExpiries = armedExpiries ?? [],
                CascadeRemovals = cascades?.GroupBy(page => page.Key.Instance, StringComparer.Ordinal).ToDictionary(
                    pages => pages.Key, pages => (IReadOnlyList<InboxKey>)pages.SelectMany(page => page.Value.All).Distinct().ToArray(), StringComparer.Ordinal)
            });

            // A count that does not match: memory and rows have parted. At the current epoch and position that is a kernel bug.
            void Diverged(string what)
            {
                if (epoch && position) Ghost.Add("I9 " + what);
                throw new StoreException(Failure.Unknown, what);
            }
        }
    }

    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            if (!Nodes.TryGetValue(node, out var row)) Nodes[node] = row = new NodeRow();
            row.SeenAt = now;
            return new(Membership(node, window, now));
        }
    }

    private Cluster Membership(string node, TimeSpan window, long now)
    {
        var free = Enumerable.Range(0, Partitions.Length).Where(p => Free(Partitions[p], now)).ToList();
        var heard = Nodes.Where(n => n.Value.SeenAt >= now - (long)window.TotalMilliseconds).Select(n => n.Key).Order(StringComparer.Ordinal).ToList();
        return new Cluster(heard.Count, free, now, heard.IndexOf(node));
    }

    /// <summary>The node heartbeat: the node record's write first, then the read of this node's partitions and membership.</summary>
    public ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            Renew(node, lease, now);
            var owned = Enumerable.Range(0, Partitions.Length).Where(p => Partitions[p].Owner == node).Select(p => new OwnedEpoch(p, Partitions[p].Epoch)).ToList();
            return new(new NodeBeat(owned, now, Membership(node, window, now)));
        }
    }

    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => ReleaseAsync([new PartitionFence(partition, epoch, 0)], cancellationToken);

    /// <summary>A release of a set, in one transaction: each row still at its epoch is freed; kept rows are not written.</summary>
    public ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            foreach (var fence in owned.OrderBy(f => f.Partition))
                if (Partitions[fence.Partition].Epoch == fence.Epoch) Partitions[fence.Partition].Owner = null;
        return default;
    }

    /// <summary>The retake after a reset: only a row this node still holds at that epoch; one more epoch, no death.</summary>
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) =>
        RetakeAsync(partition, node, epoch, lease, countDeaths: false, cancellationToken);

    /// <summary>The retake, counting a death on the previous epoch's unfinished trial messages when asked (a pending partition).</summary>
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var (row, now) = (Partitions[partition], clock());
            Renew(node, lease, now);    // a retake is a take: it writes the taker's node record first
            if (row.Owner != node || row.Epoch != epoch) return new((Lease?)null);
            if (countDeaths)
                foreach (var message in Messages.Values)
                    if (message.Trial == row.Epoch && !message.Done && message.Message.Target.Partition == partition && !message.Message.HasFencedEventStep)
                        message.Attempts++;
            CountAbandonedParkCatchUpTrials(partition, row.Epoch);
            row.Epoch++;
            return new(new Lease(row.Epoch, row.Position, now));
        }
    }

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        PollAsync([new PartitionFence(partition, epoch, 0)], horizon, limit, cancellationToken);

    /// <summary>One query over an owned set: each row is compared with the epoch of its own partition.</summary>
    public ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.RequireDistinct(owned);
        lock (_gate)
        {
            var now = clock();
            var until = now + (long)horizon.TotalMilliseconds;
            var epochs = owned.ToDictionary(f => f.Partition, f => f.Epoch);
            var rows = Messages.Values
                .Where(m => epochs.TryGetValue(m.Message.Target.Partition, out var epoch) && m.Seen < epoch && !m.Done && (m.Message.Due ?? 0) <= until)
                .OrderBy(m => m.Message.DeliveryOrder(m.Seq)).Take(limit)
                .Select(m => new Stored(Materialize(m.Message), m.Seq, m.Seen, m.Attempts, m.Trial)
                {
                    ReleasedBy = m.ReleasedBy,
                    PayloadLength = m.PayloadLength,
                    PersistedHold = m.Held && m.Trial == 0 && m.Message.TargetCreation is > 0 &&
                        Instances.GetValueOrDefault(m.Message.Target.Id) is { Status: InstanceStatus.Running, Retired: false } row &&
                        row.Creation == m.Message.TargetCreation && _referenceBirths.ContainsKey(new(row.Id, row.Creation))
                }).ToList();
            return new(new Polled(rows, now));
        }
    }

    private static Message Materialize(Message stored)
    {
        if (stored is { StoreStartInputOnce: true, Start: { } start })
            return stored with { Payload = start.Input, StoredEnvelope = stored };
        if (stored.Kind != Kinds.Event || !FoundationEventDelivery.HasDigest(stored)) return stored;

        var match = FoundationEventDelivery.ReadHeader(stored, out var payload);
        return stored with { Payload = FoundationEventFrame.ReadPayload(payload), ProjectedEvent = match, StoredEnvelope = stored };
    }

    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(message);
        Kinds.RequireApplicationSend(message.Kind);
        return InsertAsync(message.AsOrdinary(), cancellationToken);
    }

    private long _eventSerial;
    public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(InsertEventDelivery(delivery));
    }

    private EventAcceptance InsertEventDelivery(EventDelivery delivery)
    {
        ValidateEventIdentity(delivery);
        if (Messages.TryGetValue(delivery.Message.InboxKey, out var existing))
            return new(false, existing.Message.Target);
        var serial = _eventSerial;
        var write = PrepareEventWrite(delivery, ref serial);
        ApplyEventWrite(write);
        _eventSerial = serial;
        return new(true, write.Delivery.Target);
    }

    private sealed record EventWrite(Message Delivery, Message? Creator = null, ReferenceBinding? Birth = null);

    private EventWrite PrepareEventWrite(EventDelivery delivery, ref long serial)
    {
        var message = delivery.Message.Start is null ? delivery.Message : delivery.At(checked(++serial));
        if (delivery.FoundationTenant is { } tenant && message.Start is { } start)
        {
            var address = new CreationAddress(message.Target.Id, 1);
            start = start with { Creation = new CreationStart(1, 0, address.Id) };
            // The subscription creates an actor from its spec. The event is a separate delivery to that same life.
            // Both rows and the exact reference become visible together, in start-then-event delivery order.
            var creator = (message with
            {
                Id = "event-start:" + address.Id,
                Kind = Kinds.Start,
                Payload = start.Input,
                Start = start,
                StoreStartInputOnce = true,
                EventStartAmbientKey = FoundationEventDelivery.HasDigest(message) ? message.Id : null,
                TargetCreation = 1,
                KeepUntil = null
            }).WithInboxScope(InboxKey.EventCreatorScope, message.EventMatch);
            message = message with { Start = null, TargetCreation = 1 };
            var reference = ReferenceFor(tenant, address);
            var binding = new ReferenceBinding(tenant, reference, address, creator.InboxKey,
                CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload)!);
            if (Messages.ContainsKey(creator.InboxKey) || Instances.ContainsKey(address.Id) ||
                _referenceBindings.ContainsKey(reference) || _referenceBirths.ContainsKey(address))
                throw new StoreException(Failure.Definite, "The event start address already has a creator.");
            return new(message, creator, binding);
        }
        return new(message);
    }

    private void ApplyEventWrite(EventWrite write)
    {
        if (write.Creator is { } creator)
        {
            Messages.Add(creator.InboxKey, new MessageRow(creator, ++_seq, 0));
            InsertReference(write.Birth!);
        }
        Messages.Add(write.Delivery.InboxKey, new MessageRow(write.Delivery, ++_seq, 0));
    }

    /// <summary>
    /// Accept an event request and route it to its matching waits in one in-memory transaction.
    /// A null receipt publishes nothing for a later registration to catch up.
    /// </summary>
    internal IReadOnlyList<EventAcceptance>? PublishRequest(EventRow request, string replyTo, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return PublishOutside(request, replyTo, requiresWait: true, reuseUntil: null, clock(), cancellationToken)?.Deliveries;
    }

    private sealed record OutsideEventReceipt(bool Duplicate, IReadOnlyList<EventAcceptance> Deliveries);
    private readonly Dictionary<(string Key, string Id), long> _foundationEventReceipts = [];
    private long _foundationEventOccurrence;

    internal bool? PublishFoundationEvent(string tenant, string correlation, string id, string? payload, TimeSpan? keepFor,
        string? replyTo, CancellationToken cancellationToken)
    {
        FoundationMetadataLimits.RequireContent(payload, nameof(payload), FoundationMetadataLimits.EventContentBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepFor ?? TimeSpan.Zero, TimeSpan.Zero, nameof(keepFor));
        lock (_gate)
        {
            var now = clock();
            var until = keepFor is { } keep ? checked(now + (long)keep.TotalMilliseconds) : now - 1;
            var reuseUntil = checked(now + (long)(keepFor ?? IngressDeduplicationWindow).TotalMilliseconds);
            var publication = new EventRow(tenant + "_" + correlation, id, payload, until) { FoundationTenant = tenant };
            var receipt = PublishOutside(publication, replyTo, replyTo is not null, reuseUntil, now, cancellationToken);
            return receipt is null ? null : !receipt.Duplicate;
        }
    }

    private OutsideEventReceipt? PublishOutside(EventRow request, string? replyTo, bool requiresWait, long? reuseUntil, long now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (request.Key, request.Id);
        var events = EventsFor(request);
        if (events.ContainsKey(key) ||
            reuseUntil is not null && _foundationEventReceipts.TryGetValue(key, out var prior) && now <= prior)
            return new(true, []);

        if (reuseUntil is not null)
            request = request with { FoundationOccurrence = checked(_foundationEventOccurrence + 1) };
        request = IdentifyPublication(request);
        var waits = request.FoundationTenant is { } tenant ? FoundationWaits(tenant, request.Key) :
            Subscriptions.Where(wait => wait.FoundationTenant is null && wait.Key == request.Key)
                .OrderBy(wait => wait.Instance, StringComparer.Ordinal).ThenBy(wait => wait.Wait, StringComparer.Ordinal);
        var deliveries = waits
            .Select(wait => MatchRoute(request, wait).Delivery(partitions, replyTo)).ToArray();
        if (deliveries.Length == 0 && requiresWait) return null;

        foreach (var delivery in deliveries) ValidateEventIdentity(delivery);
        var starts = deliveries.Count(delivery => delivery.Message.Start is not null && !Messages.ContainsKey(delivery.Message.InboxKey));
        _ = checked(_eventSerial + starts);
        if (reuseUntil is { } expires)
        {
            _foundationEventOccurrence = request.FoundationOccurrence;
            _foundationEventReceipts[key] = expires;
        }
        if (request.FoundationTenant is null) events[key] = request;
        else RecordFoundationPublication(request);
        return new(false, Array.ConvertAll(deliveries, InsertEventDelivery));
    }

    private void ValidateEventIdentity(EventDelivery delivery)
    {
        var message = delivery.Message;
        if (!CreationIdentityAvailable)
        {
            if (delivery.FoundationTenant is not null)
                throw new StoreException(Failure.Definite, "A bound event delivery requires qualified creation identity.");
            CreationProtocol.RejectUnsupported(message);
        }
        if (message.Start is null)
        {
            var tenant = message.TargetCreation is > 0
                ? _referenceBirths.GetValueOrDefault(new(message.Target.Id, message.TargetCreation.Value))?.Tenant : null;
            if (tenant != delivery.FoundationTenant)
                throw new StoreException(Failure.Definite, "The event's scope does not own the target creation.");
        }
    }

    internal void SetFoundationEventStart(string tenant, string correlationKey, string wait, StartSpec? start)
    {
        if (start is not null && (start.Parent is not null || start.ParentCreation != 0 || start.Creation is not null))
            throw new ArgumentException("A start subscription supplies no creator identity or parent.", nameof(start));
        FoundationMetadataLimits.RequireContent(start?.Input, nameof(start), FoundationMetadataLimits.EventStartInputBytes);
        var key = tenant + "_" + correlationKey;
        lock (_gate)
        {
            if (start is not null && !CreationIdentityAvailable)
                throw new StoreException(Failure.Definite, "A bound event starter requires qualified creation identity.");

            var row = new SubscriptionRow(key, "", wait, start) { FoundationTenant = tenant };
            if (start is not null) RequireSameSubscriptionScope(row);
            var previous = RegisteredSubscription(row);
            if (previous is not null)
            {
                if (start is null)
                {
                    RemoveRegisteredSubscription(previous);
                    return;
                }
                if (previous.FoundationIncarnation is not null && previous.Start == start) return;
                row = row with { Key = previous.Key, Wait = previous.Wait };
            }
            if (start is null) return;

            var now = clock();
            var incarnation = Guid.NewGuid();
            row = row with { FoundationKeepsEvents = true, FoundationIncarnation = incarnation, FoundationRegisteredAt = now };
            var id = FoundationEventIdentity.StartCatchUpId(row, incarnation);
            var address = InstanceRef.In(InstanceRef.Spread(id, partitions), "start-registrar");
            var work = new ParkCatchUpTicket(FoundationEventIdentity.CatchUpId(InboxKey.Issued(id)), 0,
                CatchUpEntitlement(tenant, now), Start: new(tenant, row.Key, row.Wait, incarnation));
            var ticket = CatchUpMessage(id, address.Id, 0, work);
            if (Messages.ContainsKey(ticket.InboxKey) || _seq == long.MaxValue)
                throw new StoreException(Failure.Definite, "The event start catch-up ticket cannot be inserted.");
            if (previous is not null) RemoveRegisteredSubscription(previous);
            InsertFoundationSubscriptions(new([row], ticket));
        }
    }

    private ValueTask<bool> InsertAsync(Message message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Messages.ContainsKey(message.InboxKey)) return new(false);
            Messages[message.InboxKey] = new MessageRow(message, ++_seq, 0);
            return new(true);
        }
    }

    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(ids.Select(id => Instances.GetValueOrDefault(id)).OfType<InstanceRow>().ToList());
    }

    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var events = EventsFor(@event);
            var key = (@event.Key, @event.Id);
            if (!events.TryGetValue(key, out var stored))
            {
                if (@event.FoundationTenant is null) events.Add(key, @event);
                else RecordFoundationPublication(IdentifyPublication(@event));
            }
            else if (stored.FoundationTenant is not null && stored.FoundationPublishTicket is null &&
                @event.FoundationPublishTicket is { } ticket)
                RecordFoundationPublication(stored with { FoundationPublishTicket = ticket });
        }
        return default;
    }

    private static EventRow IdentifyPublication(EventRow publication) =>
        publication is { FoundationTenant: not null, FoundationIncarnation: null }
            ? publication with { FoundationIncarnation = Guid.NewGuid() }
            : publication;

    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default)
    {
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(subscription);
        lock (_gate)
        {
            RequireSameSubscriptionScope(subscription);
            if (RegisteredSubscription(subscription) is null) AddRegisteredSubscription(subscription);
        }
        return default;
    }

    private void RequireSameSubscriptionScope(SubscriptionRow subscription, ReferenceBinding? inlineRoot = null,
        IReadOnlySet<SubscriptionRow>? removed = null, IReadOnlySet<string>? cleared = null)
    {
        var target = new CreationAddress(subscription.Instance, subscription.Creation);
        var targetTenant = inlineRoot?.Address == target ? inlineRoot.Tenant
            : subscription.Creation > 0 ? _referenceBirths.GetValueOrDefault(target)?.Tenant : null;
        if (subscription.Start is null && targetTenant != subscription.FoundationTenant)
            throw new StoreException(Failure.Definite, "The subscription's scope does not own the target creation.");
        if (_subscriptions.TryGetValue(subscription, out var previous) && previous.FoundationTenant != subscription.FoundationTenant &&
            !SubscriptionWasRemoved(previous, removed, cleared) && RegisteredSubscription(subscription) is null)
            throw ConflictingSubscriptionScope();
    }

    private static bool SubscriptionWasRemoved(SubscriptionRow subscription, IReadOnlySet<SubscriptionRow>? removed,
        IReadOnlySet<string>? cleared) =>
        removed?.Contains(subscription) == true || cleared?.Contains(subscription.Instance) == true;

    private static StoreException ConflictingSubscriptionScope() =>
        new(Failure.Definite, "The subscription key is already registered in another scope.");

    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Subscriptions.Where(s => s.Key == key && s.FoundationTenant is null).OrderBy(s => s.Instance, StringComparer.Ordinal).ToList());
    }

    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!EventsFor(published).TryGetValue((published.Key, published.Id), out var stored)) return new([]);
            var waits = stored.FoundationTenant is { } tenant ? FoundationWaits(tenant, stored.Key) :
                Subscriptions.Where(wait => wait.FoundationTenant is null && wait.Key == stored.Key)
                    .OrderBy(wait => wait.Instance, StringComparer.Ordinal);
            return new(waits.Select(wait => MatchRoute(stored, wait)).ToList());
        }
    }

    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            var selected = waits.Select(w => RegisteredSubscription(w) is { } stored &&
                    stored.Creation == w.Creation ? stored : null)
                .OfType<SubscriptionRow>().ToHashSet();
            var registered = Subscriptions.Where(selected.Contains).ToArray();
            var raw = registered.Where(s => s.FoundationTenant is null).ToLookup(s => s.Key, StringComparer.Ordinal);
            var bound = registered.Where(s => s.FoundationTenant is not null).ToLookup(s => s.Key, _foundationEventEquality);
            return new((from e in EventRows.Values.Concat(FoundationEvents.Values) orderby e.Id
                where e.KeepUntil >= now
                from s in (e.FoundationTenant is null ? raw[e.Key] : bound[e.Key])
                where e.FoundationTenant == s.FoundationTenant
                select MatchRoute(e, s)).ToList());
        }
    }

    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            return new(EventRows.Values.Where(e => keys.Contains(e.Key) && e.KeepUntil >= now).OrderBy(e => e.Id, StringComparer.Ordinal).ToList());
        }
    }

    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Journal.Values.Where(j => j.Partition == partition && j.Position > after).OrderBy(j => j.Position).Take(take).ToList());
    }

    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(new Projection(Projection[partition], Partitions[partition].Position));
    }

    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (Projection[partition] != from && !Has(Flaw.ProjectorBlindWrite)) return new(false);
            Projection[partition] = to;
            foreach (var change in ViewChange.From(rows))
            {
                if (change.Commits == 0) Views.Remove(change.Instance);
                else
                {
                    int prior = change.Reset ? 0 : Views.GetValueOrDefault(change.Instance)?.Commits ?? 0;
                    Views[change.Instance] = new(change.Instance, change.Revision, prior + change.Commits);
                }
            }
            return new(true);
        }
    }

    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Journal.Values.Where(j => j.Partition == partition && j.Instance == instance && j.Position > after).OrderBy(j => j.Position).ToList());
    }

    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(instances.Select(i => Views.GetValueOrDefault(i)).OfType<ViewRow>().ToList());
    }

    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            HashSet<string>? liveMatches = null;
            HashSet<(string Key, string Id)>? unlinkedTickets = null;
            var messages = Messages.Values.Where(m => m.Done && m.KeptBy is null && !m.ParkDeadLetter &&
                    m.Message.KeepUntil < now && !HasLivePublication(m.Message))
                .Select(m => m.Message.InboxKey).ToList();
            var events = EventRows.Values.Where(e => e.KeepUntil < now).Select(e => (e.Key, e.Id)).ToList();
            messages.ForEach(id => Messages.Remove(id));
            events.ForEach(key => EventRows.Remove(key));
            var foundationEvents = FoundationEvents.Where(pair => pair.Value.KeepUntil < now &&
                    !UnsettledPublication(pair.Value) && !OwedToCatchUp(pair.Value))
                .GroupBy(pair => pair.Value.FoundationTenant, StringComparer.Ordinal)
                .SelectMany(tenant => tenant.OrderBy(pair => pair.Value.KeepUntil).ThenBy(pair => pair.Value.Id, StringComparer.Ordinal).Take(ParkPageLimit)).ToArray();
            foreach (var (key, publication) in foundationEvents) SweepFoundationPublication(key, publication);
            var eventReceipts = _foundationEventReceipts.Where(pair => pair.Value < now).Select(pair => pair.Key).ToList();
            eventReceipts.ForEach(key => _foundationEventReceipts.Remove(key));
            return new(messages.Count + events.Count + foundationEvents.Length + eventReceipts.Count + SweepFoundationIngressReceipts(now));

            bool HasLivePublication(Message message)
            {
                if (message.Kind != Kinds.Event || message.EventMatch is null || FoundationEvents.Count == 0) return false;
                if (!FoundationEventDelivery.HasDigest(message))
                    return Events.MatchFor(message) is { FoundationTenant: not null } legacy &&
                        FoundationEvents.GetValueOrDefault((legacy.Key, legacy.Event)) is { FoundationIncarnation: null } original &&
                        original.FoundationTenant == legacy.FoundationTenant && original.FoundationOccurrence == legacy.FoundationOccurrence;

                liveMatches ??= FoundationEvents.Values.Where(e => e.FoundationIncarnation.HasValue)
                    .Select(e => FoundationEventIdentity.Match(e, e.FoundationIncarnation!.Value)).ToHashSet(StringComparer.Ordinal);
                return liveMatches.Contains(message.EventMatch);
            }

            bool UnsettledPublication(EventRow publication)
            {
                if (publication.FoundationPublishTicket is { } ticket)
                    return Messages.GetValueOrDefault(ticket) is { } work &&
                        (work.Message.FoundationPublish is { Authored: null } || Active(work));
                if (publication.FoundationIncarnation is not null || !publication.Id.StartsWith('~')) return false;

                // A pre-upgrade turn publication may still be paired with a live legacy ticket.
                // The next publish retry records that link and keeps the existing event identity.
                unlinkedTickets ??= Messages.Values.Where(row => row.Message.Kind == Kinds.Publish && row.Message.FoundationPublish is null && Active(row))
                    .Select(row => FoundationEventTicket.ReadScopedIdentity(row.Message)).OfType<(string Key, string Id)>()
                    .ToHashSet(_foundationEvents.Comparer);
                return unlinkedTickets.Contains((publication.Key, publication.Id));
            }

            static bool Active(MessageRow row) => !row.Done || row.KeptBy is not null;
        }
    }
}
