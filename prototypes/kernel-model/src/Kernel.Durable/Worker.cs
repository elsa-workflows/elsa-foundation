using System.Collections.Concurrent;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>Where the worker released an emitted item relative to its own store operation. For test observation.</summary>
public enum ReplyRelease { BeforeCommit, AfterCommit, WriteFree, EphemeralWriteFree }

public sealed record WorkerOptions
{
    /// <summary>The owner's default of 12 November: 10 s. A takeover waits for it to run out, so failover takes about one lease.</summary>
    public TimeSpan Lease { get; init; } = Defaults.Lease;
    public TimeSpan Horizon { get; init; } = TimeSpan.FromSeconds(10);
    internal Action? BeforeTimedJobCutoff { get; init; }
    internal Action? AfterTimedJobCutoff { get; init; }
    public int PollLimit { get; init; } = 500;
    public int MaxBatch { get; init; } = 500;
    public int PassTurnBudget { get; init; } = 500;
    public IReadOnlyDictionary<string, int>? TenantTurnWeights { get; init; }
    internal BacklogOptions? Backlog { get; init; }

    /// <summary>How many idle instances are kept in memory. Above it the least recently used are dropped, and loaded by key again when a message comes.</summary>
    public int MaxInstances { get; init; } = int.MaxValue;

    /// <summary>
    /// Decision 4: how long an Ephemeral instance may wait in memory, unwritten, for its own short timers and jobs, when
    /// its start spec does not say (an HTTP route says: its timeout). Past it the instance is written.
    /// </summary>
    public TimeSpan ShortWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How many partitions the database has: needed to place a child in another one.</summary>
    public int Partitions { get; init; } = 1;
    /// <summary>How many owners may be lost with a message on trial before it is parked.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// How many suspects are tried together at first; the group halves with every attempt. 1 tries them one at a time,
    /// which is exact and slow. A larger group recovers a backlog faster and needs MaxAttempts above its base-2 logarithm.
    /// </summary>
    public int TrialGroup { get; init; } = 1;

    /// <summary>Shared by the workers of one process: a death kills all of them, so a trial must be alone in the process.</summary>
    public TrialGate? Gate { get; init; }

    /// <summary>Where live output goes: to callers on this process, or over the runtime's transport to other nodes.</summary>
    public IReplyTransport? Transport { get; init; }

    /// <summary>
    /// The host's monotonic clock in milliseconds. Only differences are used: the database tells the time. The default
    /// is the one clock the kernel names, for hosts that pass none; the simulation always passes its own (FINDINGS
    /// section 24).
    /// </summary>
    public Func<long> Monotonic { get; init; } = static () => Environment.TickCount64;

    public Func<long>? WallClock { get; init; }     // read only under Flaw.WallClockTimers
    public Flaw Flaws { get; init; }

    /// <summary>Called when an instance is kept in memory, unwritten (decision 4), with its revision there. For tests.</summary>
    public Action<string, long>? OnKeptInMemory { get; init; }

    /// <summary>Called when an instance held in memory leaves it without a row, with the status it left with. For tests.</summary>
    public Action<string, InstanceStatus?>? OnLeftMemory { get; init; }

    /// <summary>
    /// Where a background job's body runs. Null (the default): it starts inline, in the pass that started it, as the
    /// simulation needs. A host that runs a worker's passes on a scheduler of its own (RuntimeNode)
    /// sets another so that a long or blocking job does not hold up the node's passes. Completed drivers hand their
    /// outcomes back to the owner, which alone publishes them into its inbox.
    /// </summary>
    public TaskScheduler? JobScheduler { get; init; }
    internal TimeSpan? ParkCatchUpTimeout { get; init; }

    /// <summary>For tests: the destination, actual emitted carrier, turn revision and its release disposition.</summary>
    public Action<string, Reply, long, ReplyRelease>? OnEmittedReply { get; init; }

    /// <summary>For tests: associate a proposed physical checkpoint with the model state used by this activation.</summary>
    public Action<TurnCommit, object?, Message?>? OnPreparedCheckpoint { get; init; }

    /// <summary>For tests: the actual host refusal carrier sent to its request.</summary>
    public Action<string, Reply, Message>? OnCreationRefused { get; init; }
    public InMemoryStore.LifecycleObservation? Lifecycle { get; init; }

    /// <summary>Called when a turn starts, with the worker's idea of database time. For tests.</summary>
    public Action<InstanceRef, IReadOnlyList<Message>, long>? OnTurn { get; init; }
    /// <summary>K1: the partitions whose journal a landed commit moved, or that a take or gain just took (<see cref="NodeProjection.Nudge"/>).</summary>
    public Action<IReadOnlyCollection<int>>? OnJournal { get; init; }
    /// <summary>Original stored metadata when a newly polled delivery is held; for the simulation's history oracle.</summary>
    public Action<Stored>? OnObservedDelivery { get; init; }
    /// <summary>A job dispatch or result observed an expired local window; for deterministic witnesses.</summary>
    public Action? OnExpiredJob { get; init; }
    /// <summary>Called inside the scheduler just before the job's generation/deadline check; for deterministic witnesses.</summary>
    public Action? BeforeScheduledJobCheck { get; init; }
}

/// <summary>
/// Layer 2's side of one partition on one process: the durable actors of the partition while this process holds its
/// fence. It holds their messages in memory, runs their turns through the actor type (layer 3) and hands what each
/// turn did to one committer. Its <see cref="DurableHost"/> activates it for the runtime, which takes the fence; it is
/// then the scope's <see cref="IScopeActivation"/>, and gives the scope up when disposed.
/// It is a single-threaded state machine. A pass (<see cref="RunAsync"/>) is <see cref="PollAsync"/> when woken,
/// <see cref="LoadAsync"/>, <see cref="MeetAsync"/>, <see cref="RunTurn"/> for every instance that is ready, and
/// <see cref="FlushAsync(CancellationToken)"/>. The simulation calls those pieces one at a time, in any order, and
/// decides when. Nothing here reads a clock but the one in <see cref="WorkerOptions.Monotonic"/>, starts a thread or
/// picks at random.
/// </summary>
public sealed class Worker : IScopeActivation, ITurnServices
{
    private sealed record JobPublication(Delivery Ticket, Slot Slot, CancellationToken Attempt, Message? Result, long At, long Order, bool SchedulerRejected = false);
    // Each ownership generation has its own handoff. A callback that outlives reset can only post to its old queue.
    private ConcurrentQueue<JobPublication> _jobPublications = new();
    private readonly HashSet<Delivery> _schedulerRetries = [];
    private readonly ConcurrentQueue<(long Generation, Message Creator)> _inlineStarts = new();
    private int _jobsDeferredByCancellation;
    private long _lastReceivedJob; // owner-side observation order of the latest result moved into an inbox

    private readonly InMemoryStore.LifecycleObservation? _lifecycle;
    private enum Outcome { Landed, Refused, Lost }

    private readonly IKernelStore _store;
    private readonly Func<long>? _foundationClock;
    private readonly IActorType _type;
    private readonly WorkerOptions _options;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly PassTurnSchedule _passTurns;
    private readonly BacklogSampling? _backlog;
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<InboxKey, Delivery> _items = [];    // every message held, by id
    private readonly HashSet<Delivery> _timedJobs = [];
    private readonly List<Delivery> _meets = [];
    private readonly HashSet<Delivery> _exhaustedCatchUps = [];
    private readonly HashSet<InboxKey> _cancelling = [];        // tickets of jobs cancelled by a commit in flight
    private readonly HashSet<InboxKey> _consumed = [];          // while a poll is in flight
    private readonly Dictionary<InboxKey, int> _behind = [];    // own rows written while catching up, not yet polled, with their partition
    private readonly List<TurnOutcome> _queue = [];
    private long _generation, _anchor = long.MinValue, _local, _observed;
    private long _safeUntil = long.MinValue;    // local monotonic deadline, measured from the send time of a successful take or heartbeat; none before the first
    private long _expiredGeneration = -1; // monotonically latched by an off-turn callback; only the owner resets state
    private bool _aliasesChangedDuringLoad, _retryAliases;
    private long _aliasRead, _aliasBirths = 1, _birthComparison, _previousBirths;
    private bool _taking, _polling, _loading, _flushing, _meeting, _caughtUp, _woken, _releasing;
    private readonly object _turnSync = new(); // serialize asynchronous turn completion with stand-down fencing
    private string _refusal = "";
    private IReadOnlyList<Delivery> TrialItems => _gate.Selected(this);
    private bool _jobsWaitingForTrial;
    private bool _jobsWaitingForTerminate;
    private bool _deferredJobEligibility;
    private bool CanWithdrawJobTrials => _store.SupportsJobTrialClear && _type.SupportsTrialWithdrawal;
    private long _trialObserved; // last first-observation ordinal when the trial mark was sent
    // The (partition, epoch) pairs this process has held: what it marked then did not kill it. Epochs count per partition,
    // so a row is compared only with its own partition's epochs (D4).
    private readonly HashSet<(int Partition, long Epoch)> _mine = [];
    // One child id issuer per held partition, at the epoch it is held. A take or a retake bumps the epoch and starts a new
    // one; a turn still running from before keeps the old issuer, and its result goes with its generation.
    private readonly Dictionary<int, ChildIssuer> _issuers = [];

    /// <summary>One virtual partition while this worker holds its fence: its own epoch, position and journal.</summary>
    private sealed class Held(long epoch, long position)
    {
        public readonly long Epoch = epoch;
        public long Position = position;
    }
    private readonly SortedDictionary<int, Held> _held = [];
    // K1b, the node lease ("Liveness follows the Worker's passes"): the partitions a guard expiry stood down, at the epoch
    // they were held. They count as held for the host's pass gate, no answer releases them, and they move only through a
    // retake that counts a death. Reset leaves them.
    private readonly SortedDictionary<int, long> _pending = [];
    private long _lastPass, _takesStarted, _takeAt, _retakeAt;     // when the take, or the reset's retake, in flight started: for the gate
    private Answered? _answered;     // K1b: the last answer's stand-down side, for the next pass
    private long _judgedAt = long.MinValue;     // CR-4: the send time of the last answer judged; anything older is refused
    private sealed record Answered(HeartbeatSent Sent, NodeBeat Answer, Func<int, bool>? Sibling);
    private int[] _gaining = [];
    private bool _coldGain;     // an initial take gets its lease window for the pass gate
    private bool _retaking, _retakeStoodDown;      // a stand-down during a retake (RetakeAsync) cancels it
    private readonly HashSet<int> _retakeLost = [];     // partitions given up during a retake: it does not take them back
    private readonly HashSet<int> _retakeSet = [];      // what a retake after a reset takes back: the Worker's still, for ClaimedPartitions
    private readonly HashSet<int> _handingOver = [];    // K1b: what a LoseAsync under way gives up: never kept pending
    private readonly HashSet<int> _broughtBack = [];    // K1b: pending partitions an answer's retake took back, for the host's runtime to adopt
    private readonly HashSet<int> _givenUp = [];        // K1b: partitions the host gave up while their gain or counting retake was in flight

    public Worker(string node, int partition, IKernelStore store, IActorType type, WorkerOptions? options = null)
        : this(node, [partition], store, type, options) { }

    /// <summary>
    /// A worker for a set of virtual partitions (K1): it takes, polls, flushes and releases them together, each under its
    /// own fence row, epoch and position. A batch over several of them needs <see cref="IKernelStore.OwnedSetsAvailable"/>.
    /// </summary>
    public Worker(string node, IReadOnlyCollection<int> partitions, IKernelStore store, IActorType type, WorkerOptions? options = null)
        : this(node, partitions, store, type, options, nodeSet: false) { }

    /// <summary>
    /// The one worker of a node (D1): it starts with no partition, and its host (<see cref="DurableHost.ForNode"/>) adds
    /// and removes them while it runs (<see cref="GainAsync"/>, <see cref="LoseAsync"/>). It needs owned sets.
    /// </summary>
    internal static Worker ForNode(string node, IKernelStore store, IActorType type, WorkerOptions? options = null) =>
        store.OwnedSetsAvailable ? new(node, [], store, type, options, nodeSet: true)
            : throw new NotSupportedException("This store fences one partition per commit: a node runs one worker per partition on it.");

    private Worker(string node, IReadOnlyCollection<int> partitions, IKernelStore store, IActorType type, WorkerOptions? options, bool nodeSet)
    {
        Node = node;
        Assigned = [.. partitions.Distinct().Order()];
        if (Assigned.Count == 0 && !nodeSet) throw new ArgumentException("A worker serves at least one partition.", nameof(partitions));
        Partition = nodeSet ? -1 : Assigned[0];
        _store = store;
        _foundationClock = store.FoundationClock;
        _lifecycle = options?.Lifecycle ?? (store as InMemoryStore)?.Lifecycle;
        _type = type;
        _options = options ?? new WorkerOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.PassTurnBudget, nameof(WorkerOptions.PassTurnBudget));
        var weights = _options.TenantTurnWeights is { } configured
            ? new Dictionary<string, int>(configured, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        if (weights.Values.Any(weight => weight < 1))
            throw new ArgumentOutOfRangeException(nameof(WorkerOptions.TenantTurnWeights), "Turn weights must be positive.");
        _passTurns = new(weights);
        if (_options.Backlog is { } backlog)
        {
            if (backlog.LowWater < 1 || backlog.HighWater < backlog.LowWater || backlog.HighWater == int.MaxValue ||
                backlog.SampleInterval.TotalMilliseconds < 1 || backlog.HeartbeatInterval.TotalMilliseconds < 1)
                throw new ArgumentOutOfRangeException(nameof(WorkerOptions.Backlog));
            if (store is not IDueBacklogStore { MeasuresDueBacklog: true } depths)
                throw new NotSupportedException("This store cannot measure due backlog.");
            _backlog = new(depths, backlog);
        }
        _gate = Has(Flaw.TrialPerPartition) ? new TrialGate() : _options.Gate ?? new TrialGate();
    }

    private readonly TrialGate _gate;

    // Finding F11: another worker of this process has a trial in flight, so this one starts nothing.
    private bool Blocked => _gate.Blocks(this);

    public string Node { get; }
    /// <summary>The lowest partition of <see cref="Assigned"/>: the scope layer 1 activates. A node's worker has none (-1).</summary>
    public int Partition { get; }
    /// <summary>The virtual partitions this worker takes, in ascending id.</summary>
    public IReadOnlyList<int> Assigned { get; }
    /// <summary>The epoch and position of <see cref="Partition"/>, or 0 while it is not held.</summary>
    public long Epoch => EpochOf(Partition);
    public long Position => PositionOf(Partition);
    // _held is written only under _turnSync (Reset, Install, a landed commit), and a stand-down on another thread
    // (DeactivateAsync, DisposeAsync) clears it there. Every read takes the lock or works on a detached copy; Owns reads
    // one count, which cannot tear.
    public long EpochOf(int partition) { lock (_turnSync) return _held.TryGetValue(partition, out var held) ? held.Epoch : 0; }
    public long PositionOf(int partition) { lock (_turnSync) return _held.TryGetValue(partition, out var held) ? held.Position : 0; }
    /// <summary>The partitions held now, in ascending id, each at its epoch and the position this worker believes: a detached copy.</summary>
    public IReadOnlyList<PartitionFence> Fences { get { lock (_turnSync) return [.. _held.Select(h => new PartitionFence(h.Key, h.Value.Epoch, h.Value.Position))]; } }
    public bool Holds(int partition) { lock (_turnSync) return _held.ContainsKey(partition); }
    /// <summary>
    /// The child id issuer of the partition an instance is in, at the epoch this worker holds it: the parent's commit is
    /// fenced at that (partition, epoch), so only this issuer's ids are ever published under it.
    /// </summary>
    internal ChildIssuer ChildIdsFor(string instance)
    {
        lock (_turnSync)
        {
            var partition = Has(Flaw.ChildIssuerPerWorker) ? _held.Keys.Min()      // MUTATION: one issuer for the whole set
                : StoreBatchRules.AddressPartition(instance) is { } p && _held.ContainsKey(p) ? p : _held.Keys.First();
            var epoch = _held[partition].Epoch;
            if (!_issuers.TryGetValue(partition, out var issuer) || issuer.Epoch != epoch) _issuers[partition] = issuer = new(partition, epoch);
            return issuer;
        }
    }
    public bool Owns => _held.Count != 0;
    public long Evicted { get; private set; }
    public int Idle => _slots.Values.Count(s => s.Loaded && !s.Busy && s.Inbox.Count == 0 && s.Replies.Count == 0);    // for tests

    /// <summary>The instances held in memory only, never written (decision 4). For tests.</summary>
    public IEnumerable<string> Unwritten => _slots.Where(s => s.Value.Unwritten).Select(s => s.Key);

    /// <summary>The reply addresses of the callers this owner holds, for tests of decision 6.</summary>
    public IEnumerable<string> Callers => _slots.Values.SelectMany(slot => slot.Replies).Select(route => route.Address);
    private long _used;

    public bool CreationIdentityAvailable => _store.CreationIdentityAvailable;
    public bool CommitClockAvailable => _store.CommitClockAvailable;
    bool ITurnServices.SupportsCascadePages => _store.SupportsCascadePages;
    /// <summary>The shared foundation reading when available, or the worker's elapsed-time estimate for a remote store.</summary>
    public long Now => Has(Flaw.WallClockTimers) ? _options.WallClock!() : _foundationClock?.Invoke() ?? _anchor + _options.Monotonic();

    public bool Taking => _taking;
    public bool Polling => _polling;
    public bool Loading => _loading;
    public bool Flushing => _flushing;
    public bool Meeting => _meeting;
    public int Queued => _queue.Count;
    // A provider lookup compares every requested name. Births from that same comparison
    // cannot invalidate a distinct canonical slot. Any intervening other birth still can.
    private bool Loaded(Slot slot) => slot.Loaded && (slot.AbsentAt == 0 || slot.AbsentAt >=
        (slot.Comparison != 0 && slot.Comparison == _birthComparison ? _previousBirths : _aliasBirths));
    private int LookupPriority(Slot slot) => _store.HasActorAliases && (slot.Busy || slot.Unwritten) ? 2 : Loaded(slot) ? 1 : 0;

    public bool NeedsLoad => _retryAliases || _slots.Values.Any(s => !s.Loaded || s.PendingReferences > 0 ||
        !Loaded(s) && s.Inbox.Exists(i => !i.Taken && (i.Message.Due is not { } due || due <= Now)));
    public bool NeedsFlush => _queue.Count > 0 || _items.Values.Any(Unmarked) || NextTrial().Count > 0 || Outgrown().Any();
    public IEnumerable<string> Trial => TrialItems.Select(i => i.Message.Id);
    public bool NeedsMeet => _meets.Exists(i => !i.Taken && (i.Message.HasFencedEventStep || !Awaits(i)));
    public IEnumerable<string> Ready => ReadyForTurns(default).Select(turn => turn.Id);

    private IEnumerable<(string Id, InstanceRow? Row, List<Delivery> Inbox)> ReadyForTurns(CancellationToken cancellationToken) =>
        ReadyForTurnsAt(ReceiveJobPublications(cancellationToken));

    private IEnumerable<(string Id, InstanceRow? Row, List<Delivery> Inbox)> ReadyForTurnsAt(long now)
    {
        foreach (var (id, slot) in _slots)
        {
            if (!Loaded(slot) || slot.Busy || slot.DetachedAuthoredTurn || Blocked ||
                TrialItems.Count > 0 && !OnTrial(id) && !Spared(id)) continue;
            var deliverable = Deliverable(id, slot, now);
            if (deliverable.Count > 0)
                yield return (id, slot.Row, deliverable);
        }
    }

    private int RefreshPassTurns(CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        var ready = new List<PassTurnSchedule.ReadyTurn>();
        foreach (var (id, row, inbox) in ReadyForTurns(cancellationToken))
        {
            var oldest = inbox.MinBy(item => item.ObservedOrder)!;
            ready.Add(new(id, TurnTenant(id, row, oldest.Message), oldest.ObservedOrder));
        }
        lock (_turnSync)
            return generation == _generation ? _passTurns.Refresh(ready, _options.PassTurnBudget) : 0;
    }

    private string TurnTenant(string id, InstanceRow? row, Message input) =>
        row?.FoundationInlineBirth?.Tenant ?? input.Start?.FoundationInlineTenant ??
        (_store as IFoundationSchedulingStore)?.FoundationScheduleTenant(new(id, row?.Creation ?? input.TargetCreation ?? 0)) ?? string.Empty;

    private string? RotatePassTurn()
    {
        lock (_turnSync) return _passTurns.Next();
    }

    // What is in memory, for tests of invariant 9.
    public IEnumerable<(string Id, InstanceRow? Row, string? State, bool Busy)> Instances =>
        _slots.Where(s => Loaded(s.Value)).Select(s => (s.Key, s.Value.Row, s.Value.State is null ? null : Json.Write(s.Value.State), s.Value.Busy));
    public IEnumerable<(InboxKey Id, bool Durable, bool Taken, InstanceRef Target)> Messages => _items.Values.Select(i => (i.Message.InboxKey, i.Durable, i.Taken, i.Message.Target));

    /// <summary>Test observation of a completed job result, after it has been placed in the instance inbox.</summary>
    public bool JobResultReady(string instance, string key, long creation = 0)
    {
        ReceiveJobPublications();
        return _slots.TryGetValue(instance, out var slot)
            && slot.Inbox.Exists(i => !i.Taken && i.CompletedJob && i.Message.Kind == Kinds.JobFinished &&
                i.Message.InboxKey == InboxKey.Ticket(instance, key, creation, DiagnosticJobGeneration(slot.CurrentRow, key, creation)));
    }

    /// <summary>
    /// Diagnostic: capture before a ticket is removed. The task ends after its driver queues or discards the result;
    /// it does not establish that a model consumed the result or committed a logical job outcome.
    /// </summary>
    public Task? JobCompletion(string instance, string key, long creation = 0) =>
        _items.GetValueOrDefault(InboxKey.Ticket(instance, key, creation,
            DiagnosticJobGeneration(_slots.GetValueOrDefault(instance)?.CurrentRow, key, creation))) is { } ticket
            && ticket.Message.Target.Id == instance && ticket.Key == key ? ticket.JobDriver : null;

    private static long DiagnosticJobGeneration(InstanceRow? row, string key, long creation) =>
        row?.Creation == creation ? JobRegistry.Generation(row, key) ?? 0 : 0;

    public bool Has(Flaw flaw) => (_options.Flaws & flaw) != 0;
    public int Partitions => _options.Partitions;
    public TimeSpan ShortWait => _options.ShortWait;
    internal int MaxAttempts => _options.MaxAttempts;

    /// <summary>A turn cancelled a job. Its ticket may be in memory, or still on its way in with a poll.</summary>
    internal void Cancelling(InboxKey ticket)
    {
        _cancelling.Add(ticket);
        if (_items.TryGetValue(ticket, out var held))
        {
            // Serialize this exact cancellation with admission on the queued driver's executing thread.
            if (held.Key is not null && _slots.TryGetValue(Canonical(held.Message.Target.Id), out var slot))
                lock (slot) held.Taken = true;
            else held.Taken = true;
        }
    }
    void ITurnServices.Cancelling(InboxKey ticket) => Cancelling(ticket);

    internal ITurnServices ServicesFor(Slot slot, long admittedAt)
    {
        var services = new TurnServices(this, slot, _generation, slot.TurnFence, _foundationClock is not null ? admittedAt : null);
        slot.UndoTurn = () => services.Invalidate(restartJobs: true);
        slot.RollbackTurn = () => services.Invalidate(restartJobs: false);
        return services;
    }

    private sealed class TurnServices(Worker worker, Slot slot, long generation, long fence, long? admittedAt) : ITurnServices
    {
        private readonly HashSet<InboxKey> _reservations = [];
        public long Now => admittedAt ?? worker.Now;
        public bool CreationIdentityAvailable => worker.CreationIdentityAvailable;
        public bool CommitClockAvailable => worker.CommitClockAvailable;
        bool ITurnServices.SupportsCascadePages => worker._store.SupportsCascadePages;
        public int Partitions => worker.Partitions;
        public TimeSpan ShortWait => worker.ShortWait;
        public bool Has(Flaw flaw) => worker.Has(flaw);
        public void Cancelling(InboxKey ticket)
        {
            lock (worker._turnSync)
                if (generation == worker._generation && fence == slot.TurnFence)
                {
                    if (!worker._cancelling.Contains(ticket)) _reservations.Add(ticket);
                    worker.Cancelling(ticket);
                }
        }
        public void Invalidate(bool restartJobs)
        {
            lock (slot)
            {
                foreach (var id in _reservations)
                {
                    worker._cancelling.Remove(id);
                    if (!worker._items.TryGetValue(id, out var item)) continue;
                    if (restartJobs && item.Key is not null && item.Message.Kind == Kinds.Job)
                    {
                        // Deadline/stand-down invalidation retains the foundation's fresh-driver retry contract.
                        item.Taken = true;
                        CancelJobDriver(item);
                        var replacement = new Delivery(item.Message, item.Seq) { Key = item.Key, Ticket = true,
                            Durable = item.Durable, Marked = item.Marked, ObservedOrder = item.ObservedOrder, ObservedAt = item.ObservedAt,
                            DeferredJobTrial = item.DeferredJobTrial, DeferredTrialObserved = item.DeferredTrialObserved };
                        worker._items[id] = replacement;
                        slot.Inbox.Remove(item);
                        worker.Insert(slot, replacement);
                    }
                    else item.Taken = false; // failed-turn rollback keeps the original driver or its retained result
                }
                _reservations.Clear();
                worker.RefreshPoisonBoundary(slot);
            }
        }
    }

    /// <summary>A job's ticket: derived from instance and key, so a later turn can delete it without holding it.</summary>
    public static string TicketId(string instance, string key, long creation = 0) => creation > 0
        ? FormattableString.Invariant($"{CreationProtocol.Origin(instance, creation)}!{key.Length}_{key}") : $"{instance}!{key}";

    // ---- the seam to layer 1 --------------------------------------------------------------------------------------

    public int Scope => Partition;
    public bool Active => Owns;

    /// <summary>Gives the scope up: what <see cref="ReleaseAsync"/> does.</summary>
    public ValueTask DisposeAsync() => new(ReleaseAsync());

    /// <summary>A hint, or the poll interval: the next pass reads the partition's inbox.</summary>
    public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default)
    {
        _woken = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// One pass, as the hosts drive a worker: read the inbox if woken, load, read kept events, run a budget of
    /// ready turns in rotating order, and flush. Zero when the pass found work or received a job result. Otherwise,
    /// return the next delay, capped at a third of the lease so the host can observe completed passes. Null means the scope is
    /// no longer held. A cancelled token stops before the next turn or flush and leaves pending work for a later pass.
    /// </summary>
    public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_releasing) return null;        // a release in flight: no pass runs, and none counts for the host's gate
        var next = await PassAsync(cancellationToken);
        if (!cancellationToken.IsCancellationRequested) Volatile.Write(ref _lastPass, _options.Monotonic());     // a completed pass, for the host's gate
        return next;
    }

    public async ValueTask<ScopePass> RunWithAgeAsync(CancellationToken cancellationToken = default)
    {
        var delay = await RunAsync(cancellationToken);
        if (delay != TimeSpan.Zero || cancellationToken.IsCancellationRequested) return new(delay);
        return new(delay, AgeOfReadyWork().TryGetValue(Partition, out var age) ? age : null);
    }

    internal Dictionary<int, TimeSpan> AgeOfReadyWork()
    {
        var now = Now;
        var oldest = new Dictionary<int, TimeSpan>();
        lock (_turnSync)
        {
            foreach (var (_, _, inbox) in ReadyForTurnsAt(now))
            {
                var partition = inbox[0].Message.Target.Partition;
                var readyAt = inbox.Min(item => Math.Max(item.ObservedAt, item.Message.Due ?? item.ObservedAt));
                var age = TimeSpan.FromMilliseconds(Math.Max(0, now - readyAt));
                if (!oldest.TryGetValue(partition, out var previous) || age > previous)
                    oldest[partition] = age;
            }
        }
        return oldest;
    }

    private async ValueTask<TimeSpan?> PassAsync(CancellationToken cancellationToken)
    {
        if (_releasing) return null;
        if (Owns && !LeaseUsable()) return null;
        // K1b: the last heartbeat answer's stand-down side, under the host's turn, before the pass reads or writes.
        if (Volatile.Read(ref _answered) is not null && !cancellationToken.IsCancellationRequested) await AnsweredAsync(cancellationToken);
        if (!LeaseUsable()) return null;
        if (cancellationToken.IsCancellationRequested) return TimeSpan.Zero;
        if (_woken)
        {
            _woken = false;
            await PollAsync(cancellationToken);
        }
        if (!LeaseUsable()) return null;
        if (cancellationToken.IsCancellationRequested) return TimeSpan.Zero;
        if (_backlog is not null)
        {
            await _backlog.ReadAsync(Fences, _options.Monotonic(), cancellationToken);
            if (!LeaseUsable()) return null;
            if (cancellationToken.IsCancellationRequested) return TimeSpan.Zero;
        }
        while (_inlineStarts.TryDequeue(out var inline))
        {
            if (inline.Generation != _generation || !Post(inline.Creator, fresh: true))
                LoseInlineStart(inline.Creator);
        }
        // Retry once per pass. A scheduler that keeps refusing cannot make the publication drain spin.
        if (_schedulerRetries.Count > 0)
            foreach (var ticket in _schedulerRetries.ToArray())
            {
                if (cancellationToken.IsCancellationRequested) return TimeSpan.Zero;
                _schedulerRetries.Remove(ticket);
                StartJob(ticket, cancellationToken);
            }
        // A cancelled store or model continuation can finish off the owner thread. It only signals a rescan;
        // the next healthy owner pass decides which tickets are still present and eligible.
        if (Volatile.Read(ref _jobsDeferredByCancellation) != 0 && Interlocked.Exchange(ref _jobsDeferredByCancellation, 0) != 0)
            foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToArray()) StartJob(ticket, cancellationToken);
        var receivedBeforePass = _lastReceivedJob;
        var busy = NeedsLoad || NeedsMeet || NeedsFlush || ReadyForTurns(cancellationToken).Any();
        await LoadAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested) return Owns ? TimeSpan.Zero : null;
        await MeetAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested) return Owns ? TimeSpan.Zero : null;
        var turns = RefreshPassTurns(cancellationToken);
        for (var i = 0; i < turns && !cancellationToken.IsCancellationRequested; i++)
        {
            if (RotatePassTurn() is not { } id) break;
            RunTurn(id, cancellationToken);
        }
        if (!cancellationToken.IsCancellationRequested)
        {
            await FlushAsync(cancellationToken);
            if (_backlog is not null && LeaseUsable())
                await PublishBacklogAsync(cancellationToken);
        }
        if (!Owns) return null;
        Evict();
        if (busy || turns > 0 || cancellationToken.IsCancellationRequested) return TimeSpan.Zero;
        var now = Now;
        var next = _items.Values.Where(i => !i.Taken && i.Message.Due is { } due && due > now).Select(i => i.Message.Due!.Value - now)
            .Concat(_slots.Values.Where(s => s.Unwritten && s.Until > now).Select(s => s.Until - now)).DefaultIfEmpty(long.MaxValue).Min();
        if (_backlog is not null) next = Math.Min(next, _backlog.NextDelay(Fences, _options.Monotonic()));
        var caller = Callers.Select(to => ReplyTo.Parse(to).Deadline - now).DefaultIfEmpty(long.MaxValue).Min();
        var delay = TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(caller, Math.Min(next, (long)(_options.Lease.TotalMilliseconds / 3)))));
        // Flush can receive a result after the last Ready scan. Give that inbox another pass before advising sleep.
        return _lastReceivedJob != receivedBeforePass || !_jobPublications.IsEmpty || Volatile.Read(ref _jobsDeferredByCancellation) != 0 || _jobsWaitingForTrial && _gate.Idle ? TimeSpan.Zero : delay;
    }

    private async ValueTask PublishBacklogAsync(CancellationToken cancellationToken)
    {
        if (_flushing || cancellationToken.IsCancellationRequested || !Owns) return;
        var generation = _generation;
        var pressure = _backlog!.PressureFences(Fences, _options.Monotonic());
        if (pressure.Count == 0) return;

        _flushing = true;
        var outcome = await CommitAsync([], [], [], generation, cancellationToken, pressure: pressure);
        if (outcome == Outcome.Refused && generation == _generation) Reset();
        if (generation == _generation) _flushing = false;
    }

    // A successful response can arrive arbitrarily late. The lease was renewed no earlier than when its request began;
    // stop at that conservative local deadline, even if the actual database lease has more time. No read is needed.
    private bool CanStartWork => CanStartGeneration(Volatile.Read(ref _generation));

    private bool CanStartGeneration(long generation)
    {
        if ((!Has(Flaw.IgnoreJobGeneration) && generation != Volatile.Read(ref _generation)) || !Owns) return false;
        if (Has(Flaw.IgnoreLeaseDeadline)) return true;
        if (Volatile.Read(ref _expiredGeneration) == generation) return false;
        var now = _options.Monotonic();
        var deadline = Volatile.Read(ref _safeUntil);
        // Clock callbacks and scheduler delays can overlap a reset and retake of this same Worker. Never combine
        // an old generation with a deadline read from the replacement lease.
        if ((!Has(Flaw.IgnoreJobGeneration) && generation != Volatile.Read(ref _generation)) || !Owns ||
            Volatile.Read(ref _expiredGeneration) == generation) return false;
        if (now < deadline) return true;
        // A delayed renewal acknowledgement must not revive a generation whose job was already discarded. Off-turn
        // callbacks only latch a number; the worker's own loop performs Reset. Never overwrite a newer generation.
        long seen;
        do
        {
            seen = Volatile.Read(ref _expiredGeneration);
            if (seen >= generation) break;
        } while (Interlocked.CompareExchange(ref _expiredGeneration, generation, seen) != seen);
        return false;
    }

    internal IKernelStore Store => _store;      // K1b: the host's heartbeat goes to the Workers' store
    internal TimeSpan Lease => _options.Lease;

    /// <summary>
    /// The node lease's guard (decisions, "The local lease guard"): one deadline, the latest send time plus the lease over
    /// the successful heartbeats and takes whose reply arrived before their own send time plus the lease. A late reply
    /// restarts nothing and never moves the deadline earlier. One sent after the deadline had passed extends nothing it
    /// held: the guard expired, so the Worker stands down first and starts again (<paramref name="fresh"/>: a take into
    /// empty memory, which starts new ownership).
    /// </summary>
    private void RenewGuard(long sent, bool fresh = false)
    {
        var until = sent + (long)_options.Lease.TotalMilliseconds;
        if (_options.Monotonic() >= until) return;
        if (!fresh && Owns && sent >= Volatile.Read(ref _safeUntil)) return;
        long seen;
        do
        {
            seen = Volatile.Read(ref _safeUntil);
            if (seen >= until) return;
        } while (Interlocked.CompareExchange(ref _safeUntil, until, seen) != seen);
    }

    /// <summary>What the host records just before it sends the node's heartbeat, to judge its answer by (K1b).</summary>
    public sealed record HeartbeatSent(long At, long Generation, IReadOnlyList<PartitionFence> Held, long Takes = 0, bool Taking = false)
    {
        internal IReadOnlyDictionary<int, long>? PendingAtSend { get; init; }
    }

    /// <summary>Taken by the host just before it sends the heartbeat: the monotonic time, this generation, what is held and the takes so far.</summary>
    public HeartbeatSent BeforeHeartbeat()
    {
        lock (_turnSync) return new(_options.Monotonic(), _generation, Fences, Interlocked.Read(ref _takesStarted), _taking)
        {
            PendingAtSend = _pending.Count == 0 ? null : new Dictionary<int, long>(_pending)
        };
    }

    /// <summary>
    /// The heartbeat's answer, as the host hands it over (ownership-and-fencing, "The node's heartbeat"). Judged only
    /// within the generation it was sent in: a Worker that reset meanwhile discards it.
    /// <para>
    /// The calling contract: the host calls this when the answer arrives, from any thread, off the host's turn and
    /// beside a pass. It runs only the renewal side, at once, so a long pass never makes a timely reply look late: the
    /// guard moves to the send time plus the lease if the reply came within it, and the store's clock is observed. A
    /// heartbeat sent after the guard had expired renews nothing; the next pass stands everything down, pending. The rest
    /// is recorded (the latest answer only, since a newer one judges the same rows again) and the next pass runs it at its
    /// start, under the host's turn (<see cref="AnsweredAsync"/>): a stand-down never runs beside a pass, whose flush
    /// state sits outside the turn's lock. While one waits (<see cref="AnswerWaiting"/>), the host gives the Worker a pass.
    /// </para>
    /// </summary>
    public void HeartbeatAnswered(HeartbeatSent sent, NodeBeat answer, Func<int, bool>? sibling = null)
    {
        lock (_turnSync)
        {
            if (sent.Generation != _generation && !Has(Flaw.ReleaseDuringTake)) return;
            if (Owns && sent.At >= Volatile.Read(ref _safeUntil)) return;     // sent after the guard expired: the next pass stands down
            RenewGuard(sent.At);
            Observe(answer.Now);
            // Recorded only when it may ask something of the pass: a partition pending, or held and listed apart. An answer
            // that matches what is held leaves nothing to do, and replaces an older record, whose rows it judged again.
            var listed = answer.Owned.ToDictionary(o => o.Partition, o => o.Epoch);
            var waits = _pending.Count > 0 || listed.Count != _held.Count || sent.Held.Count != _held.Count
                || listed.Any(l => !(_held.TryGetValue(l.Key, out var held) && held.Epoch == l.Value))
                || sent.Held.Any(f => listed.GetValueOrDefault(f.Partition, -1) != f.Epoch);
            // CR-4: an older answer that arrives last never replaces a newer one's record.
            if (sent.At >= _judgedAt && (_answered is not { } recorded || sent.At >= recorded.Sent.At)) _answered = waits ? new(sent, answer, sibling) : null;
        }
    }

    /// <summary>
    /// K1b: a heartbeat answer waits for the stand-down side, which runs at the start of the next pass. The host gives the
    /// Worker a pass, whether or not it holds a partition: an answer can release rows of a Worker that holds none.
    /// </summary>
    public bool AnswerWaiting => Volatile.Read(ref _answered) is not null;

    /// <summary>
    /// K1b: the stand-down side of the last answer, at the start of a pass. Every partition held at the send that the
    /// answer omits or lists at another epoch, and still held at that epoch, is stood down through <see cref="LoseAsync"/>:
    /// when nothing of those partitions is in flight their memory goes and the rest keep their epochs, otherwise the Worker
    /// resets and takes the rest back. While a release or retake is in flight the answer stays recorded, for a later pass.
    /// The other way: a partition the answer lists as this process's, at an epoch the Worker does not hold, is released at
    /// that epoch, unless a take of this Worker ran since the send, it is pending, or <c>sibling</c> (another Worker of the
    /// host) claims it; a release that fails is retried at the next answer. A pending partition listed at its epoch is
    /// retaken, counting the death. An omitted or different epoch retires only a claim already pending at the send.
    /// A later take may have established a new pending epoch while the heartbeat or its pass was waiting.
    /// </summary>
    private async Task AnsweredAsync(CancellationToken cancellationToken)
    {
        HashSet<int> lost;
        List<PartitionFence> release = [];
        List<int> retake = [];
        HeartbeatSent sent;
        NodeBeat answer;
        Func<int, bool>? sibling;
        lock (_turnSync)
        {
            if (_answered is not { } answered || _releasing || _retaking) return;
            (sent, answer, sibling) = answered;
            _answered = null;
            _judgedAt = Math.Max(_judgedAt, sent.At);
            if (sent.Generation != _generation && !Has(Flaw.ReleaseDuringTake)) return;     // MUTATION ReleaseDuringTake: also below
            var listed = answer.Owned.ToDictionary(o => o.Partition, o => o.Epoch);
            foreach (var (partition, epoch) in _pending.ToList())
                if (listed.GetValueOrDefault(partition, -1) == epoch) retake.Add(partition);
                else if (sent.PendingAtSend?.GetValueOrDefault(partition, -1) == epoch) _pending.Remove(partition);
            if (!Has(Flaw.KeepUnheldRows) && (Has(Flaw.ReleaseDuringTake) || !sent.Taking && !_taking && sent.Takes == Interlocked.Read(ref _takesStarted)))
                release = [.. listed.Where(l => !(_held.TryGetValue(l.Key, out var held) && held.Epoch == l.Value) && !_pending.ContainsKey(l.Key))
                    .Select(l => new PartitionFence(l.Key, l.Value, 0))];
            lost = Has(Flaw.HeartbeatIgnoresAnswer) ? [] : sent.Held.Where(f => (!listed.TryGetValue(f.Partition, out var epoch) || epoch != f.Epoch)
                && _held.TryGetValue(f.Partition, out var held) && held.Epoch == f.Epoch).Select(f => f.Partition).ToHashSet();
        }
        // As a cooperative hand-over goes: drain, then drop when quiet, or reset and take the rest back. Its release of the
        // lost rows at their old epochs is a no-op, by the store's epoch guard: they moved to another epoch.
        if (lost.Count > 0) await LoseAsync(lost, cancellationToken);
        release = [.. release.Where(f => !(sibling?.Invoke(f.Partition) ?? false))];     // outside this Worker's lock: a sibling's pass may ask this one
        if (release.Count > 0)
            try { await _store.ReleaseAsync(release, cancellationToken); }
            catch (Exception) { /* retried at the next answer, which lists them again */ }
        if (retake.Count > 0) await RetakePendingAsync(retake, cancellationToken, byItself: true);
    }

    /// <summary>
    /// K1b: the partitions an answer's retake brought back since the last call, by the Worker itself and no activation of
    /// its runtime's. The host hands them to its runtime, which activates the ones it does not know.
    /// </summary>
    internal IReadOnlyCollection<int> TakeBroughtBack()
    {
        lock (_turnSync)
        {
            int[] back = [.. _broughtBack.Where(_held.ContainsKey)];
            _broughtBack.Clear();
            return back;
        }
    }

    /// <summary>
    /// The retake of pending partitions (K1b), one at a time at the epoch each was held, counting a death for its
    /// previous-epoch trial messages, as a take from an expired owner does. A partition retaken is added to the set
    /// without a reset; one refused is no longer pending; one whose answer is lost stays pending, for the next answer.
    /// </summary>
    private async Task<bool> RetakePendingAsync(IReadOnlyList<int> partitions, CancellationToken cancellationToken, bool byItself = false)
    {
        var any = false;
        foreach (var partition in partitions)
        {
            long epoch;
            lock (_turnSync)
                if (!_pending.TryGetValue(partition, out epoch) || _held.ContainsKey(partition)) continue;
            if (_taking || _releasing || _retaking || TakesClosed) break;
            var (started, generation) = (_options.Monotonic(), _generation);
            Lease? lease;
            lock (_turnSync) (_taking, _gaining) = (true, [partition]);
            Interlocked.Increment(ref _takesStarted);
            try { lease = await _store.RetakeAsync(partition, Node, epoch, _options.Lease, countDeaths: true, cancellationToken); }
            catch (Exception)
            {
                lock (_turnSync)
                {
                    (_taking, _gaining) = (false, []);
                    // Not known to have landed: it stays pending (and given up, if it was), for the next answer to judge.
                }
                continue;       // not known: the next answer judges it
            }
            var installed = false;
            lock (_turnSync)
            {
                _pending.Remove(partition);
                var given = _givenUp.Remove(partition);     // CR-5: the host gave it up meanwhile: released, not installed
                if (lease is not null && generation == _generation && !_releasing && !_retaking && !given)
                {
                    var fresh = !Owns;
                    var received = _options.Monotonic();
                    if (!TakeReplyInTime(started, received))
                    {
                        _pending[partition] = lease.Epoch;
                        (_taking, _gaining) = (false, []);
                        continue;       // a later answer must confirm this new epoch before the partition can come back
                    }
                    if (fresh) Volatile.Write(ref _lastPass, received);
                    if (!fresh) RenewGuard(started);       // before the partition joins: a retake sent past the deadline extends nothing
                    _held[partition] = new(lease.Epoch, lease.Position);
                    _mine.Add((partition, lease.Epoch));
                    Observe(lease.Now);
                    if (fresh) RenewGuard(started, fresh: true);
                    _caughtUp = false;      // F1: until a poll has read the partition's inbox, nothing is handed over in memory
                    (_woken, installed) = (true, true);
                    if (byItself) _broughtBack.Add(partition);
                }
                (_taking, _gaining) = (false, []);      // after the install: ClaimedPartitions never sees neither
            }
            if (installed)
            {
                _options.OnJournal?.Invoke([partition]);
                if (!LeaseUsable()) continue;       // an answer delayed past the safe deadline cannot authorize work, as in GainAsync
                any = true;
            }
            else if (lease is not null)
                try { await _store.ReleaseAsync(partition, lease.Epoch, cancellationToken); }      // the set changed meanwhile
                catch (Exception) { /* the next answer lists it, and releases it */ }
        }
        return any;
    }

    private bool LeaseUsable()
    {
        if (!Owns) return false;
        if (CanStartWork) return true;
        // Stood down by the guard: pending. What the host is giving up (a release, or a hand-over under way) is given up too:
        // its counting retake at the next answer counts the death and releases instead of installing.
        lock (_turnSync)
            foreach (var fence in Fences)
            {
                _pending[fence.Partition] = fence.Epoch;
                if (_releasing || _handingOver.Contains(fence.Partition)) _givenUp.Add(fence.Partition);
            }
        Reset();
        return false;
    }

    /// <summary>K1b: the partitions a guard expiry stood down, at the epochs they were held. A detached copy.</summary>
    public IReadOnlyDictionary<int, long> Pending { get { lock (_turnSync) return new Dictionary<int, long>(_pending); } }

    /// <summary>
    /// For the host's pass gate (K1b): a Worker that holds partitions, pending ones included, is live while it has completed
    /// a pass within the last lease; one that holds none is live.
    /// </summary>
    public bool Live
    {
        get
        {
            lock (_turnSync)
            {
                // A Worker that holds nothing counts only when nothing is on its way back either: no gain in flight, and no
                // reset's retake or take in flight for a lease or more. The take and retake terms are a backstop for a Worker
                // that passes nothing; a recent pass still opens the gate.
                var (now, lease) = (_options.Monotonic(), (long)_options.Lease.TotalMilliseconds);
                var retakeHangs = _retaking && _retakeSet.Count > 0 && now - Volatile.Read(ref _retakeAt) >= lease;
                var takeHangs = _taking && now - Volatile.Read(ref _takeAt) >= lease;
                return (!Owns && _pending.Count == 0 && !retakeHangs && (_gaining.Length == 0 || _coldGain) && !takeHangs) || now - Volatile.Read(ref _lastPass) < lease
                    || Has(Flaw.RenewRegardlessOfPasses);       // MUTATION: renewal regardless of passes
            }
        }
    }

    /// <summary>K1b: the host's pass gate, set by its heartbeat (<see cref="NodeHeartbeat"/>). While it is closed the Worker takes nothing.</summary>
    public Func<bool>? TakesOpen { get; set; }
    private bool TakesClosed => TakesOpen is { } open && !open() && !Has(Flaw.TakeRenewsWedgedHost);     // MUTATION: a wedged host takes

    /// <summary>Test-only (N1, N6): how many times <see cref="ClaimedPartitions"/> ran. One answer asks each Worker once.</summary>
    internal long ClaimedLookups { get; private set; }

    /// <summary>
    /// The partitions this Worker claims: held, pending, or being taken. A host with several Workers divides an answer by
    /// these, under the Worker's lock, building one map from them per answer.
    /// </summary>
    internal IReadOnlyList<int> ClaimedPartitions()
    {
        lock (_turnSync)
        {
            ClaimedLookups++;
            HashSet<int> claimed = [.. _held.Keys, .. _pending.Keys];
            claimed.UnionWith(_gaining);
            if (_taking) claimed.UnionWith(Assigned);
            if (_retaking) claimed.UnionWith(_retakeSet);
            return [.. claimed];
        }
    }

    // ---- ownership ---------------------------------------------------------------------------------------------

    internal Task TakeAsync(CancellationToken cancellationToken = default) => TakeAsync(Assigned, cancellationToken);

    /// <summary>
    /// Take these partitions, keeping what the take gets: the store decides each row on its own, so a partition that has
    /// moved is left out and never fails the rest. One partition takes as today.
    /// </summary>
    private async Task TakeAsync(IReadOnlyList<int> partitions, CancellationToken cancellationToken)
    {
        if (_taking || TakesClosed) return;
        List<int> pending;
        lock (_turnSync)
        {
            _givenUp.ExceptWith(partitions);        // the host asks for them again
            pending = [.. partitions.Where(_pending.ContainsKey)];
        }
        if (pending.Count > 0)
        {
            // A take that includes a pending partition is its retake, which counts the death.
            var rest = partitions.Where(p => !pending.Contains(p)).ToList();
            if (rest.Count > 0) await TakeAsync(rest, cancellationToken);
            await RetakePendingAsync(pending, cancellationToken);
            return;
        }
        Volatile.Write(ref _takeAt, _options.Monotonic());
        _taking = true;
        Interlocked.Increment(ref _takesStarted);
        if (partitions.Count > 1 && !_store.OwnedSetsAvailable)
        {
            _taking = false;
            throw new NotSupportedException("This store fences one partition per commit; a worker of several partitions needs owned sets.");
        }
        var started = _options.Monotonic();
        OwnedLease? lease = null;
        try { lease = await TakeFencesAsync(partitions, cancellationToken); }
        catch (Exception) { /* not taken, or taken without knowing: the next take bumps the epoch again */ }
        lock (_turnSync)
        {
            if (lease is { Taken.Count: > 0 } && !Install(lease, started)) lease = null;
            _taking = false;        // after the install: ClaimedPartitions never sees neither
        }
        if (lease is { Taken.Count: > 0 }) Installed(lease);
    }

    // A heartbeat may have renewed the node while another owner took these rows. Each take's reply has its own deadline.
    private bool TakeReplyInTime(long started, long received) => received - started < (long)_options.Lease.TotalMilliseconds;

    private async ValueTask<OwnedLease?> TakeFencesAsync(IReadOnlyList<int> partitions, CancellationToken cancellationToken)
    {
        if (partitions.Count > 1) return await _store.TakeAsync(partitions, Node, _options.Lease, cancellationToken);
        var lease = await _store.TakeAsync(partitions[0], Node, _options.Lease, cancellationToken);
        return lease is null ? null : new([lease.For(partitions[0])], lease.Now);
    }

    /// <summary>
    /// After a refused fence or a lost answer over several partitions: take back, one at a time, each partition this
    /// node still holds at the epoch it held, at a new epoch. One that was released or taken since, or whose take fails,
    /// is dropped; the takes that succeed are kept.
    /// </summary>
    private async Task RetakeAsync(IReadOnlyList<PartitionFence> held, CancellationToken cancellationToken)
    {
        if (_taking || TakesClosed)
        {
            lock (_turnSync)
            {
                _retaking = false;      // a take in flight replaces the retake; a closed gate takes nothing
                _retakeLost.Clear();
                _retakeSet.Clear();
            }
            return;
        }
        _taking = true;
        Interlocked.Increment(ref _takesStarted);
        var (started, taken, now, reached) = (_options.Monotonic(), new List<PartitionFence>(), long.MaxValue, 0);
        List<PartitionFence> skipped = [];
        try
        {
            foreach (var fence in held)
                try
                {
                    if (Volatile.Read(ref _retakeStoodDown)) break;
                    reached++;
                    bool lost;
                    lock (_turnSync) lost = _retakeLost.Contains(fence.Partition);
                    if (lost)
                    {
                        skipped.Add(fence);     // given up meanwhile: not taken back, and released at the epoch it was held
                        continue;
                    }
                    if (await _store.RetakeAsync(fence.Partition, Node, fence.Epoch, _options.Lease, cancellationToken) is not { } one) continue;
                    taken.Add(one.For(fence.Partition));
                    now = Math.Min(now, one.Now);
                }
                catch (Exception) { /* not taken, or taken without knowing: dropped, and its host takes it as usual */ }
        }
        finally { _taking = false; }
        bool stoodDown;
        List<PartitionFence> given = skipped;
        // One critical section, for the same stand-down: it sees _retaking, or the installed set, never neither.
        lock (_turnSync)
        {
            (stoodDown, _retaking) = (_retakeStoodDown, false);
            if (!stoodDown)
            {
                given.AddRange(taken.Where(t => _retakeLost.Contains(t.Partition)));      // given up after their retake was sent
                taken.RemoveAll(t => _retakeLost.Contains(t.Partition));
            }
            _retakeLost.Clear();
            _retakeSet.Clear();     // in the same critical section as the install: ClaimedPartitions sees the retake's set, or the held one
            if (!stoodDown && taken.Count > 0)
            {
                if (!Install(new(taken, now), started)) taken.Clear();
                _woken = true;      // memory went with the reset: the next pass reads the inbox at the new epochs
            }
        }
        if (!stoodDown && taken.Count > 0) Installed(new(taken, now));
        try
        {
            if (given.Count == 1) await _store.ReleaseAsync(given[0].Partition, given[0].Epoch, cancellationToken);
            else if (given.Count > 1) await _store.ReleaseAsync(given, cancellationToken);
        }
        catch (Exception) { /* then the leases run out instead */ }
        if (stoodDown)
        {
            // A stand-down came while the retake was in flight: install nothing, and give up what it took at the new epochs
            // and what it had not reached yet at the captured ones (the epoch guard leaves a moved partition alone).
            List<PartitionFence> release = [.. taken, .. held.Skip(reached)];
            try
            {
                if (release.Count == 1) await _store.ReleaseAsync(release[0].Partition, release[0].Epoch, cancellationToken);
                else if (release.Count > 1) await _store.ReleaseAsync(release, cancellationToken);
            }
            catch (Exception) { /* then the leases run out instead */ }
        }
    }

    private bool Install(OwnedLease lease, long started)
    {
        lock (_turnSync)
        {
            Reset();    // a take starts from empty memory
            var received = _options.Monotonic();
            if (!TakeReplyInTime(started, received))
            {
                foreach (var fence in lease.Taken) _pending[fence.Partition] = fence.Epoch;
                return false;
            }
            Volatile.Write(ref _lastPass, received);     // the first pass of a newly installed owner is due within a lease
            Observe(lease.Now);
            foreach (var taken in lease.Taken)
            {
                _held[taken.Partition] = new(taken.Epoch, taken.Position);
                _mine.Add((taken.Partition, taken.Epoch));
            }
            RenewGuard(started, fresh: true);
            return true;
        }
    }

    // After an install, outside _turnSync: the journal nudge (a take may find a journal not projected yet) and the guard check
    // (an answer delayed past the safe deadline cannot authorize work).
    private void Installed(OwnedLease lease)
    {
        _options.OnJournal?.Invoke([.. lease.Taken.Select(t => t.Partition)]);
        LeaseUsable();
    }

    /// <summary>Stand down: drop what is in memory and give the partition up. Whoever takes it next finds no death.</summary>
    internal async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        await ReleaseHeldAsync(cancellationToken);
        await ReleasePendingAsync(cancellationToken);
    }

    // K1b, the coordinator's rule for a host's give-up of a pending partition (19-20 November): a pending partition moves
    // only through its counting retake. A release runs that retake now and releases at the epoch it returns; a refused
    // retake leaves nothing to release. One the release cannot retake now (the gate closed, a take or retake in flight, a
    // failed or unknown call) stays pending and given up: its retake at the next answer counts the death and releases.
    private async Task ReleasePendingAsync(CancellationToken cancellationToken)
    {
        List<int> pending;
        lock (_turnSync) pending = [.. _pending.Keys.Where(_givenUp.Contains)];
        if (pending.Count > 0) await RetakePendingAsync(pending, cancellationToken);
    }

    private async Task ReleaseHeldAsync(CancellationToken cancellationToken)
    {
        long generation;
        IReadOnlyList<PartitionFence> held;     // every change of the set resets, so the generation guards it
        lock (_turnSync)
        {
            // K1b: the host gives everything up. A pending partition is given up through its counting retake (above); a
            // gain or counting retake in flight releases what its reply returns instead of installing it.
            _givenUp.UnionWith(_gaining);
            _givenUp.UnionWith(_pending.Keys);
            if (_retaking) { _retakeStoodDown = true; return; }     // the retake in flight gives up what it gets
            if (!Owns || _releasing) return;
            generation = _generation;
            held = Fences;
            _releasing = true;      // in the first critical section, with no await before it (CodeRabbit 4636228077)
        }
        // Collect genuine results for promotion after closing admission to retries uncovered by the same drain.
        ReceiveJobPublications();
        // Decision 4: a stand-down hands its partition over, so what it holds in memory only is written first, unless a
        // commit is in flight (then those instances are lost with this memory, as in a crash, and their callers told).
        if (!_flushing && !Has(Flaw.ForgetShortWaits))
        {
            lock (_turnSync)
            {
                if (generation != _generation) return;
                WriteUnwritten(_ => true);
            }
            while (generation == _generation && _queue.Count > 0 && LeaseUsable())
                await FlushAsync(cancellationToken);
            if (generation != _generation || !Owns)
            {
                // The drain's own flush was refused or lost, and that reset this worker without a retake. Over several
                // partitions the ones still leased to it are given up at the captured epochs. The store's epoch guard
                // makes this a no-op for a partition that moved or was taken again since, and nothing here resets.
                // K1b: a guard expiry during the drain made them pending: they move only through their counting retake.
                lock (_turnSync) held = [.. held.Where(f => !_pending.ContainsKey(f.Partition))];
                if (held.Count > 1)
                    try { await _store.ReleaseAsync(held, cancellationToken); }
                    catch (Exception) { /* then the leases run out instead */ }
                return;
            }
        }
        lock (_turnSync)
        {
            if (generation != _generation) return;
            Reset();
        }
        try
        {
            if (held.Count == 1) await _store.ReleaseAsync(held[0].Partition, held[0].Epoch, cancellationToken);
            else await _store.ReleaseAsync(held, cancellationToken);
        }
        catch (Exception) { /* then the lease runs out instead */ }
    }

    /// <summary>Decision 4: queue a write of every instance held in memory only, of the partitions handed over.</summary>
    private void WriteUnwritten(Func<string, bool> handedOver)
    {
        foreach (var (_, slot) in _slots.Where(s => handedOver(s.Key) && s.Value.Unwritten && (!s.Value.Busy || s.Value.Driving)).ToList())
        {
            // A suspended turn may be mutating its live object. Fence its eventual result and write only the
            // previous sealed row; its taken volatile inputs are still in _items and Promote writes them too.
            if (slot.Busy) { slot.TurnFence++; slot.UndoTurn?.Invoke(); slot.Driving = false; slot.EnteredTurn = null; }
            slot.Busy = true;
            var unit = Bound(_type.Write(slot.Row!, null, slot.Pending), slot);
            Promote(unit);
            _queue.Add(unit);
        }
    }

    // ---- the node's owned set (D1) -------------------------------------------------------------------------------

    /// <summary>The worker's monotonic clock (<see cref="WorkerOptions.Monotonic"/>), in milliseconds: the host times a shared pass answer by it.</summary>
    internal long Monotonic => _options.Monotonic();

    /// <summary>A wake, a job result or a deferred job came since the last pass: the next call must run a pass.</summary>
    internal bool PassWanted => _woken || AnswerWaiting || !_inlineStarts.IsEmpty || !_jobPublications.IsEmpty || Volatile.Read(ref _jobsDeferredByCancellation) != 0 || _schedulerRetries.Count > 0;

    internal async Task<bool> GainAsync(int partition, CancellationToken cancellationToken = default)
    {
        await GainSetAsync([partition], cancellationToken);
        return Holds(partition);
    }

    /// <summary>Add the fresh partitions in one take. Pending partitions still require their own counting retake.</summary>
    internal async Task GainSetAsync(IReadOnlyList<int> requested, CancellationToken cancellationToken)
    {
        int[] fresh, pending;
        long started, generation;
        lock (_turnSync)
        {
            if (Owns) LeaseUsable();
            if (_taking || _releasing || _retaking || TakesClosed) return;
            _givenUp.ExceptWith(requested);
            pending = [.. requested.Where(_pending.ContainsKey)];
            fresh = [.. requested.Where(p => !_held.ContainsKey(p) && !_pending.ContainsKey(p))];
            started = _options.Monotonic();
            generation = _generation;
            if (fresh.Length > 0)
            {
                _taking = true;
                _gaining = fresh;
                _coldGain = !Owns && _pending.Count == 0;
                Interlocked.Increment(ref _takesStarted);
                Volatile.Write(ref _takeAt, started);
            }
        }
        if (fresh.Length > 0)
        {
            OwnedLease? lease = null;
            try { lease = await TakeFencesAsync(fresh, cancellationToken); }
            catch (Exception) { /* An unknown answer installs nothing. The next heartbeat reconciles what the store owns. */ }
            List<PartitionFence> installed = [], returning = [];
            lock (_turnSync)
            {
                if (lease is not null)
                {
                    var current = generation == _generation && !_releasing && !_retaking;
                    var timely = TakeReplyInTime(started, _options.Monotonic());
                    foreach (var fence in lease.Taken)
                    {
                        if (!current || _givenUp.Contains(fence.Partition)) returning.Add(fence);
                        else if (timely) installed.Add(fence);
                        else _pending[fence.Partition] = fence.Epoch;     // a later heartbeat can confirm or discard it
                    }
                    if (installed.Count > 0)
                    {
                        if (!Owns)
                        {
                            if (!Install(new(installed, lease.Now), started)) installed.Clear();
                        }
                        else
                        {
                            RenewGuard(started);
                            Observe(lease.Now);
                            foreach (var fence in installed)
                            {
                                _held[fence.Partition] = new(fence.Epoch, fence.Position);
                                _mine.Add((fence.Partition, fence.Epoch));
                            }
                            _caughtUp = false;
                            _woken = true;
                        }
                    }
                }
                _coldGain = false;
                _givenUp.ExceptWith(fresh);
                (_taking, _gaining) = (false, []);
            }
            if (installed.Count > 0) Installed(new(installed, lease!.Now));
            try
            {
                if (returning.Count == 1) await _store.ReleaseAsync(returning[0].Partition, returning[0].Epoch, cancellationToken);
                else if (returning.Count > 1) await _store.ReleaseAsync(returning, cancellationToken);
            }
            catch (Exception) { /* the heartbeat reconciles the epoch or the lease expires */ }
        }
        if (pending.Length > 0) await RetakePendingAsync(pending, cancellationToken);
        if (Owns) LeaseUsable();
    }

    /// <summary>
    /// Hand part of the set over, cooperatively, between passes. What those partitions hold in memory only is written
    /// first, as a stand-down does (decision 4). Then, if nothing of theirs is in flight, their memory goes, their fence
    /// rows are released at their epochs and nothing else changes: the partitions kept keep their epochs, positions and
    /// memory. If something of theirs is still in flight (a turn, a running job, a trial), the worker writes what every
    /// partition holds in memory only, resets, releases them, and takes the rest back at new epochs, as after a refused
    /// fence. Handing over every partition is a stand-down. During a retake after a reset the Worker holds nothing: the
    /// retake leaves these partitions out and releases them, at the epoch it held them or the one it got back.
    /// </summary>
    internal async Task LoseAsync(IReadOnlyCollection<int> partitions, CancellationToken cancellationToken = default)
    {
        // K1b: a guard expiry during the hand-over keeps nothing of these partitions pending: the host gave them up.
        lock (_turnSync) _handingOver.UnionWith(partitions);
        try
        {
            LeaseUsable();      // an idle worker may have expired since the host planned its hand-over
            await HandOverAsync(partitions, cancellationToken);
        }
        finally
        {
            lock (_turnSync) _handingOver.ExceptWith(partitions);
        }
    }

    private async Task HandOverAsync(IReadOnlyCollection<int> partitions, CancellationToken cancellationToken)
    {
        bool HandedOver(string id) => StoreBatchRules.AddressPartition(id) is { } p && partitions.Contains(p);
        lock (_turnSync)
        {
            if (_retaking && !_releasing)
            {
                // A retake after a reset is in flight: the Worker holds nothing to drain, and the retake leaves these
                // partitions out, or gives them up at the new epoch if it already has them back.
                _retakeLost.UnionWith(partitions);
                return;
            }
            // K1b: a pending partition, or one whose gain or counting retake is in flight, is given up too. It stays pending;
            // its counting retake (at the next answer, or the one in flight) counts the death and releases at the epoch it
            // returns, instead of installing. The coordinator's rule for a host's give-up of a pending partition.
            foreach (var partition in partitions)
                if (_gaining.Contains(partition) || _pending.ContainsKey(partition)) _givenUp.Add(partition);
        }
        if (_releasing || _retaking || !partitions.Any(Holds)) return;
        if (Fences.All(f => partitions.Contains(f.Partition)))
        {
            await ReleaseAsync(cancellationToken);
            return;
        }
        var captured = Fences.Where(f => partitions.Contains(f.Partition)).ToList();
        ReceiveJobPublications(cancellationToken);
        await DrainAsync(HandedOver, cancellationToken);
        LeaseUsable();
        List<PartitionFence> leaving, kept = [];
        var quiet = false;
        lock (_turnSync)
        {
            var held = Fences;
            leaving = [.. held.Where(f => partitions.Contains(f.Partition))];
            if (leaving.Count > 0 && leaving.Count < held.Count && Quiet(HandedOver))
            {
                Drop(HandedOver, leaving);
                quiet = true;
            }
        }
        if (leaving.Count > 0 && !quiet)
        {
            // The fallback resets everything, so what the kept partitions hold in memory only is written first too, as a
            // stand-down writes it.
            await DrainAsync(_ => true, cancellationToken);
            LeaseUsable();
            lock (_turnSync)
            {
                var held = Fences;
                leaving = [.. held.Where(f => partitions.Contains(f.Partition))];
                if (leaving.Count > 0)      // empty: the drain's refusal reset already, or the partitions went; no second reset
                {
                    kept = [.. held.Where(f => !partitions.Contains(f.Partition))];
                    Reset();
                    if (kept.Count > 0) (_retaking, _retakeStoodDown, _retakeAt) = (true, false, _options.Monotonic());
                    _retakeSet.UnionWith(kept.Select(f => f.Partition));
                }
            }
        }
        // A drain refused down to a reset with no retake holds nothing: give up what was captured, as ReleaseAsync does.
        // The store's epoch guard makes it a no-op for a row that moved or was taken again since.
        if (leaving.Count == 0) leaving = captured;
        lock (_turnSync) leaving = [.. leaving.Where(f => !_pending.ContainsKey(f.Partition))];      // K1b: a guard expiry made them pending
        try
        {
            if (leaving.Count == 1) await _store.ReleaseAsync(leaving[0].Partition, leaving[0].Epoch, cancellationToken);
            else if (leaving.Count > 1) await _store.ReleaseAsync(leaving, cancellationToken);
        }
        catch (Exception) { /* then the leases run out instead */ }
        if (kept.Count > 0) await RetakeAsync(kept, cancellationToken);
    }

    // Decision 4: write what these instances hold in memory only, and flush until the queue is empty. A refusal resets
    // and retakes, as any flush's does.
    private async Task DrainAsync(Func<string, bool> handedOver, CancellationToken cancellationToken)
    {
        if (!_flushing && !Has(Flaw.ForgetShortWaits))
            lock (_turnSync) WriteUnwritten(handedOver);
        while (_queue.Count > 0 && !_flushing && LeaseUsable()) await FlushAsync(cancellationToken);
    }

    // Nothing of the partitions handed over is in flight: no read, commit or turn, no running job, no trial. A job result
    // published for a ticket that goes is dropped with it (ReceiveJobPublications checks the ticket), as at a reset.
    private bool Quiet(Func<string, bool> handedOver) =>
        !_flushing && !_polling && !_loading && !_meeting && _queue.Count == 0
        && !_slots.Any(s => handedOver(s.Key) && (s.Value.Busy || s.Value.Unwritten || s.Value.PendingReferences > 0))
        && !_items.Values.Any(i => handedOver(i.Message.Target.Id) && (i.Taken || i.JobDriver is { IsCompleted: false }))
        && !TrialItems.Any(i => handedOver(i.Message.Target.Id));

    // The memory of the partitions handed over goes, as Reset drops all of it: their callers are told, their messages
    // and instances forgotten. Under _turnSync.
    private void Drop(Func<string, bool> handedOver, List<PartitionFence> leaving)
    {
        var items = _items.Values.Where(i => handedOver(i.Message.Target.Id)).ToList();
        var slots = _slots.Where(s => handedOver(s.Key)).ToList();
        LoseCallers(slots.Select(entry => entry.Value), items);
        foreach (var item in items)
        {
            Forget(item);
            _cancelling.Remove(item.Message.InboxKey);
        }
        // Rows sent to these partitions that the poll would have read back: it no longer reads them, so they must not
        // keep the worker from catching up (F1).
        var gone = leaving.Select(f => f.Partition).ToHashSet();
        foreach (var key in _behind.Where(b => gone.Contains(b.Value)).Select(b => b.Key).ToList()) _behind.Remove(key);
        foreach (var (id, slot) in slots)
        {
            slot.TurnFence++;       // a second guard: a turn of a dropped slot that still completes is fenced out
            RemoveSlot(id);
        }
        foreach (var alias in _aliases.Where(a => handedOver(a.Value)).Select(a => a.Key).ToList()) _aliases.Remove(alias);
        foreach (var fence in leaving)
        {
            _held.Remove(fence.Partition);
            _issuers.Remove(fence.Partition);
        }
    }

    private void LoseCallers(IEnumerable<Slot> slots, IEnumerable<Delivery> items)
    {
        var pending = items.Where(item => !item.Message.SuppliedCreator ||
                _slots.TryGetValue(Canonical(item.Message.Target.Id), out var slot) && slot.CurrentRow is not null)
            .Select(item => item.ReplyRoute).OfType<LiveReplyRoute>();
        foreach (var route in slots.SelectMany(slot => slot.Replies).Concat(pending).Distinct().ToList())
            Send(route, "lost", null, null, error: "lost");
    }

    /// <summary>Recovery after any doubt is the same path as start-up: drop everything; the host takes the partition again.</summary>
    private void Reset()
    {
        lock (_turnSync)
        {
            Interlocked.Increment(ref _generation);
            _jobPublications = new();
            _schedulerRetries.Clear();
            while (_inlineStarts.TryDequeue(out var inline)) LoseInlineStart(inline.Creator);
            Volatile.Write(ref _jobsDeferredByCancellation, 0);
            _held.Clear();
            _polling = _loading = _flushing = _meeting = _releasing = _aliasesChangedDuringLoad = _retryAliases = false;
            foreach (var item in _items.Values.ToList()) CancelJobDriver(item);
            // Every caller this memory knows of is told at once; a new owner answers only what is still in the rows.
            LoseCallers(_slots.Values, _items.Values);
            _slots.Clear();
            _passTurns.Clear();
            _aliases.Clear();
            _items.Clear();
            _timedJobs.Clear();
            _meets.Clear();
            _exhaustedCatchUps.Clear();
            _queue.Clear();
            _consumed.Clear();
            _behind.Clear();
            _cancelling.Clear();
            _caughtUp = false;
            _jobsWaitingForTrial = false;
            _jobsWaitingForTerminate = false;
            _deferredJobEligibility = false;
            KeepPhysicalTrialGuards();
        }
    }

    private void Observe(long now) => _anchor = Math.Max(_anchor, now - _options.Monotonic());

    // ---- messages into memory ------------------------------------------------------------------------------------

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        if (_polling || !LeaseUsable()) return;
        var generation = _generation;
        _polling = true;
        _consumed.Clear();
        Polled polled;
        var fences = Fences;
        try
        {
            polled = fences.Count == 1 ? await _store.PollAsync(fences[0].Partition, fences[0].Epoch, _options.Horizon, _options.PollLimit, cancellationToken)
                : await _store.PollAsync(fences, _options.Horizon, _options.PollLimit, cancellationToken);
        }
        catch (Exception)
        {
            if (generation == _generation)
            {
                _polling = false;
                if (cancellationToken.IsCancellationRequested) _woken = true; // the interrupted read consumed no rows
            }
            return;
        }
        if (generation != _generation) return;
        _polling = false;
        Observe(polled.Now);    // anchored when the answer arrives, so the estimate never runs ahead of the database
        if (!LeaseUsable()) return;
        var read = 0;
        foreach (var row in polled.Messages)
        {
            if (!_items.ContainsKey(row.Message.InboxKey) && !_consumed.Contains(row.Message.InboxKey))
            {
                read++;
                _options.OnObservedDelivery?.Invoke(row);
            }
            Hold(row.Message, row.Seq, durable: true, marked: false, attempts: row.Attempts,
                // A released hold is local to the owner that made it. A cold owner requalifies it by trial.
                suspect: (!row.PersistedHold || row.ReleasedBy is not null) && Suspect(row, polled.Now), again: row.Seen > 0 || row.Trial > 0,
                cancellationToken: cancellationToken, persistedHold: row.PersistedHold && row.ReleasedBy is null, releasedBy: row.ReleasedBy, replyTenant: row.ReplyTenant);
            if (!LeaseUsable()) return;
        }
        if (ParkDrainMetrics.Enabled)
        {
            HashSet<InboxKey> heldHere;
            IReadOnlyList<PartitionFence> sampledFences;
            lock (_turnSync)
            {
                sampledFences = Fences;
                heldHere = _items.Where(entry => entry.Value.HeldHere).Select(entry => entry.Key).ToHashSet();
            }
            if ((_store as IFoundationSchedulingStore)?.SampleParkDrains(Node, sampledFences, heldHere.Contains) is { } sample && LeaseUsable() && generation == _generation)
                ParkDrainMetrics.Record(sample);
        }

        // Finding F1: caught up when a poll comes back short and every row this owner wrote meanwhile has come back with it.
        // A poll proves it only for the set it read: a partition gained while it was in flight has not been read yet.
        if (polled.Messages.Count < _options.PollLimit && _behind.Count == 0 && Fences.Select(f => f.Partition).SequenceEqual(fences.Select(f => f.Partition))) _caughtUp = true;
        // K1 D1: a full poll that read something new is read on at the next pass, without waiting for a hint or the poll
        // interval. One poll serves the node's whole set, so a backlog in one partition must not slow the others' reads.
        if (polled.Messages.Count >= _options.PollLimit && read > 0) _woken = true;
    }

    // Ingress can stage a local creator without mutating the owner's slots or taking its turn lock.
    internal bool TryQueueInlineStart(Message creator)
    {
        var generation = Volatile.Read(ref _generation);
        if (_releasing || !CanStartGeneration(generation) ||
            creator.Start is not { } start || !_type.SupportsFoundationInlineStart(start.Model)) return false;
        _inlineStarts.Enqueue((generation, creator));
        return true;
    }

    private void LoseInlineStart(Message creator)
    {
        if (creator.ReplyTo is { } route) Send(new(route, creator.Start?.FoundationInlineTenant), "lost", null, null, error: "lost");
    }

    /// <summary>
    /// A message handed to the owner in memory and never written: a start without a caller's key. <paramref name="fresh"/>
    /// says the target's id was just minted, so no read is needed to know that it does not exist.
    /// </summary>
    public bool Post(Message message, bool fresh = false, object? ambient = null)
    {
        Kinds.RequireApplicationSend(message.Kind);
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(message);
        message = message.AsOrdinary();
        if (_releasing || !LeaseUsable() || !Holds(message.Target.Partition)) return false;
        Hold(message, long.MaxValue / 2 + ++_local, durable: false, marked: true, fresh: fresh && !message.Target.GeneratedAddress && message.Kind != Kinds.JobFinished,
            starterAmbient: message.Start is not null && !message.SuppliedCreator && message.Kind != Kinds.JobFinished ? ambient : null,
            replyTenant: message.Start?.FoundationInlineTenant);
        return LeaseUsable();
    }

    /// <summary>
    /// A retryable RPC to an existing actor. Its caller resends a lost request or answer. Each wire attempt has
    /// a fresh message id and reply address; the actor records the stable operation id with its decision.
    /// </summary>
    public bool PostRequest(Message message, string replyAddress)
    {
        Kinds.RequireApplicationSend(message.Kind);
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(message);
        message = message.AsOrdinary();
        var (node, request, _, _) = ReplyTo.Parse(replyAddress);
        if (message.Start is not null) throw new ArgumentException("A retryable RPC must address an existing actor.", nameof(message));
        if (message.ReplyTo is not null || string.IsNullOrEmpty(node) || string.IsNullOrEmpty(request))
            throw new ArgumentException("An RPC needs a single reply address and cannot subscribe to live output.");
        if (_releasing || !LeaseUsable() || !Holds(message.Target.Partition)) return false;
        Hold(message, long.MaxValue / 2 + ++_local, durable: false, marked: true, requestAddress: replyAddress);
        return LeaseUsable();
    }

    /// <summary>
    /// A message that another owner had marked, or that is on trial already, may be what killed that owner. What this
    /// same process marked in an epoch it held earlier is not: the process is alive.
    /// </summary>
    private bool Suspect(Stored row, long now) => row.Message.Kind is not (Kinds.Meet or Kinds.Publish) && !row.Message.IsControl && row.Attempts < _options.MaxAttempts
        && (row.Trial > 0 || (row.Seen > 0 && !Mine(row.Message.Target.Partition, row.Seen) && (row.Message.Due is not { } due || due <= now)));

    private bool Mine(int partition, long epoch)
    {
        // MUTATION SuspectAcrossPartitions: epochs count per partition, so a bare epoch names another partition's take too.
        lock (_turnSync) return Has(Flaw.SuspectAcrossPartitions) ? _mine.Any(m => m.Epoch == epoch) : _mine.Contains((partition, epoch));
    }

    private bool OnTrial(string instance) => TrialItems.Any(t => !t.AbandonedTrial && Canonical(t.Message.Target.Id) == instance);

    private void KeepPhysicalTrialGuards()
    {
        foreach (var ticket in TrialItems)
        {
            // Reset can follow any mark or commit, even before a terminate has requested withdrawal.
            if (CanWithdrawJobTrials && ticket.JobDriver is { IsCompleted: false } driver)
            {
                ticket.AbandonedTrial = true;
                _ = driver.ContinueWith(_ => _gate.Finished(this, ticket), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else _gate.Finished(this, ticket);
        }
    }

    // MUTATION TrialSparesOtherPartitions: a trial holds back only its own partition, as if a death killed only that one.
    private bool Spared(string instance) => Has(Flaw.TrialSparesOtherPartitions) && StoreBatchRules.AddressPartition(instance) is { } p
        && !TrialItems.Any(t => t.Message.Target.Partition == p);

    // A fenced trial that cannot physically enter must not hold the process hostage. Its durable trial
    // and loss accounting stay intact; resuming the job reacquires isolation without another store mark.
    private void DeferPoisonBlockedJobTrials()
    {
        foreach (var ticket in TrialItems.ToArray())
        {
            if (ticket.AbandonedTrial || ticket.RetainsTrialGate || !ticket.Ticket || ticket.Key is null || ticket.Message.Kind != Kinds.Job ||
                !_slots.TryGetValue(Canonical(ticket.Message.Target.Id), out var slot)) continue;
            lock (slot)
            {
                if (ticket.JobEntered || !PoisonPending(slot)) continue;
                ticket.DeferredJobTrial = true;
                ticket.DeferredTrialObserved = _trialObserved;
                _gate.Finished(this, ticket);
            }
        }
    }


    private void Hold(Message message, long seq, bool durable, bool marked, bool suspect = false, int attempts = 0, bool fresh = false, bool again = false, string? requestAddress = null, CancellationToken cancellationToken = default, object? starterAmbient = null, bool persistedHold = false, ParkRelease? releasedBy = null, string? replyTenant = null)
    {
        if (message.ReplyTo is not null && (message.Kind == Kinds.JobFinished ||
            message.InboxScope == 0 && message.Kind is Kinds.Job or Kinds.Meet or Kinds.Publish or Kinds.JobDeadline))
            message = message with { ReplyTo = null }; // an outside copy cannot acquire an observer, even if this activation resets
        if (durable && _items.TryGetValue(message.InboxKey, out var promoted) && promoted.AwaitPoll)
        {
            if (promoted.HeldHere && promoted.ReleasedBy is { } expected)
            {
                // A poll already in flight can return the row from before the page retagged it.
                if (releasedBy != expected) return;
                promoted.Message = message;
            }
            _behind.Remove(message.InboxKey);
            void Observe(Slot? waiting)
            {
                promoted.AwaitPoll = false;
                promoted.Marked = marked;
                promoted.Suspect = suspect;
                promoted.Attempts = attempts;
                if (promoted.Key is not null) promoted.AwaitingJobMark = Awaits(promoted);
                promoted.Again = again;
                promoted.RecoveryOrdering |= again;
                promoted.Seq = seq;
                if (message.ReplyTo is not null) promoted.ReplyTenant = replyTenant;
                if (waiting is null) return;
                if ((promoted.CompletedOrder == 0 || promoted.Message.FoundationHeldJobResult) && waiting.Inbox.Remove(promoted)) Insert(waiting, promoted);
                RefreshPoisonBoundary(waiting);
            }
            if (_slots.TryGetValue(Canonical(message.Target.Id), out var waiting)) { lock (waiting) Observe(waiting); }
            else Observe(null);
            return;
        }
        // The owner skips ids it holds, and (finding F5) ids it consumed while the poll that returned them was in flight.
        if (!Has(Flaw.PollWithoutSkip) && (_items.ContainsKey(message.InboxKey) || _consumed.Contains(message.InboxKey))) return;
        _behind.Remove(message.InboxKey);
        // A recalled message (Recall) whose row a poll brings in before the commit that deletes it lands: taken, never given.
        // A previously marked row is durable evidence of recovery, even when it was marked by this
        // process. The process can rule out a death for trial accounting, not reconstruct its lost inbox.
        var item = new Delivery(message, seq)
        {
            StarterAmbient = starterAmbient, ReplyTenant = replyTenant, RequestAddress = requestAddress, Durable = durable, Marked = marked, Suspect = suspect, Attempts = attempts,
            Again = again, RecoveryOrdering = again, Held = persistedHold, ReleasedBy = releasedBy, Taken = _cancelling.Contains(message.InboxKey),
            ObservedOrder = Interlocked.Increment(ref _observed), ObservedAt = Now
        };
        _items[message.InboxKey] = item;
        if (!item.SuppliedTicket && message.Kind is Kinds.Meet or Kinds.Publish)     // NATIVE N1: a publish ticket is handled like a meet ticket
        {
            item.Ticket = true;
            _meets.Add(item);
            if (CanParkOnEventFailure(message) && attempts >= _options.MaxAttempts)
                _exhaustedCatchUps.Add(item);
            return;
        }
        if (!item.SuppliedTicket && message.Kind == Kinds.Job && attempts >= _options.MaxAttempts && Has(Flaw.JobPoisonAsError))
        {
            // Mutation: restore the old rule which reported an error result instead of parking.
            var error = $"the job was on trial when {attempts} owners were lost";
            item.CompletedJob = true;
            item.Original = message;
            item.Message = message with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(message.Read<JobTicket>().Key, null, error)) };
        }
        else if (!item.SuppliedTicket && message.Kind == Kinds.Job && attempts < _options.MaxAttempts)
        {
            item.Ticket = true;
            var job = message.Read<JobTicket>();
            (item.Key, item.JobDeadline) = (job.Key, job.Deadline);
            if (job.Deadline is not null) _timedJobs.Add(item);
            item.Taken = _cancelling.Contains(message.InboxKey);
        }
        if (!_slots.TryGetValue(Canonical(message.Target.Id), out var slot))
        {
            // Give the invalidated lookup a turn before newly admitted spellings can create rows.
            _slots[Canonical(message.Target.Id)] = slot = new Slot
            {
                Loaded = fresh && !_retryAliases, DeferredFresh = fresh && _retryAliases,
                AbsentAt = fresh && _store.HasActorAliases ? _aliasBirths : 0
            };
            if (_loading && _store.HasActorAliases && !_retryAliases) _aliasesChangedDuringLoad = true;
        }
        if (_store.HasActorAliases && _type.SelfReference(item) is { } reference && reference != message.Target.Id &&
            reference != slot.Row?.Id && slot.Aliases?.Contains(reference) != true && slot.SelfReferences?.ContainsKey(reference) != true)
        {
            item.UncheckedReference = reference;
            slot.PendingReferences++;
        }
        Insert(slot, item);
        if (item.Key is not null) StartJob(item, cancellationToken);
    }

    // Effective inbox insertion and its physical-entry boundary publish together, including alias merges.
    private void Insert(Slot slot, Delivery item)
    {
        lock (slot)
        {
            var at = slot.Inbox.FindIndex(i => i.InboxOrder > item.InboxOrder);
            slot.Inbox.Insert(at < 0 ? slot.Inbox.Count : at, item);    // a target's messages are handled in seq order
            RefreshPoisonBoundary(slot);
        }
    }

    private bool Unmarked(Delivery item) => !item.Message.HasFencedEventStep && item.Durable && !item.AwaitPoll &&
        !item.Marked && !item.Taken && (!item.Suspect || (!item.Shelved && Shelf(item)));

    /// <summary>
    /// Finding F12: an authored suspect whose instance is suspended cannot be tried, and while its row says unseen it fills a place
    /// in every poll; a poll limit of them hides the resume behind them. So it is shelved: its row is marked seen at this
    /// epoch, which moves the polls on, but it is neither on trial nor cleared to run. The next owner still suspects it.
    /// </summary>
    private bool Shelf(Delivery suspect) => !Has(Flaw.NoShelf) && _slots.TryGetValue(Canonical(suspect.Message.Target.Id), out var slot) &&
        (WithheldByDrain(suspect, slot) || WaitsForCohortHead(suspect, slot) || Withheld(slot) && !CanSettleWhileWithheld(slot, suspect)) &&
        CreationError(slot, suspect.Message) is null;

    private bool WaitsForCohortHead(Delivery item, Slot slot)
    {
        var cohort = CohortInput(item, slot);
        return (cohort || slot.CurrentRow?.ParkCohort is not null) && FirstUnheldRecoveryInput(slot) is { } head &&
            (!cohort || RecoveryOrder(item) != head);
    }

    // Exhausted input blocks new physical jobs and job trials. Ordinary predecessor suspects may
    // still be fenced so an earlier due timer cannot strand the pending Park.
    private bool PoisonPending(Slot slot) => Volatile.Read(ref slot.PoisonAt) <= Now;

    private static ParkRelease? RecoveryRelease(InstanceRow? row) => row is { ParkRecovery: not null }
        ? new ParkRelease(row.Creation, row.ParkResolutionGeneration) : row?.ParkCohort;

    private static bool CohortInput(Delivery item, Slot slot) => item.ReleasedBy is { } tag &&
        tag == RecoveryRelease(slot.CurrentRow) && !ParkRecoveryCredit.IsTicket(item.Message.Kind);

    private static bool WithheldByDrain(Delivery item, Slot slot)
    {
        if (item.Message.IsControl || slot.CurrentRow is not { } row) return false;
        if (row.ParkRecovery is not null)
            return row.Status != InstanceStatus.Running || item.ReleasedBy != RecoveryRelease(row);
        // Clearing pages own the kept rows. A pending release from an earlier Park settles after Complete.
        return row.ParkDiscard is not null && EarlierParkRelease(item, row);
    }

    private static bool EarlierParkRelease(Delivery item, InstanceRow row) => item.ReleasedBy is { } prior &&
        prior.Creation == row.Creation && prior.Generation < row.ParkResolutionGeneration;

    private ParkPageCursor? FirstUnheldRecoveryInput(Slot slot) =>
        slot.CurrentRow is { } row && RecoveryRelease(row) is { } release && !Withheld(slot)
            ? _type.FirstUnheldRecoveryInput(row, release, key => _items.TryGetValue(key, out var input) && input.Held)
            : null;

    private static ParkPageCursor RecoveryOrder(Delivery item) => new(item.Seq, item.Message.InboxKey);

    internal bool RecoveryHandedOff(InboxKey key) => _items.TryGetValue(key, out var item) && item.HeldHere;

    private bool PoisonInput(Delivery item, Slot slot) => !item.Taken && !DiscardedAt(item, slot) && !WithheldByDrain(item, slot) &&
        item.UncheckedReference is null && item.Attempts >= _options.MaxAttempts &&
        item.Message.Kind != Kinds.JobFinished && !item.Message.IsControl && CreationError(slot, item.Message) is null;

    private void RefreshPoisonBoundary(Slot slot) => Volatile.Write(ref slot.PoisonAt,
        slot.Inbox.Where(i => PoisonInput(i, slot)).Select(i => i.Message.Due ?? long.MinValue)
            .DefaultIfEmpty(long.MaxValue).Min());

    // Called only by the owner: unlike physical admission this may inspect the ordered inbox.
    private bool PrecedesPoison(Delivery suspect, Slot slot, Delivery? control) => slot.Inbox
        .Where(i => PoisonInput(i, slot) && !(i.Message.Due > Now) && BeforeRecoveryControl(i, control))
        .All(i => CompareRecoveryInputs(suspect, i, slot) < 0);

    private static int CompareRecoveryInputs(Delivery left, Delivery right, Slot slot)
    {
        var leftCohort = CohortInput(left, slot);
        var rightCohort = CohortInput(right, slot);
        if (leftCohort != rightCohort) return leftCohort ? -1 : 1;
        return leftCohort ? RecoveryOrder(left).CompareTo(RecoveryOrder(right)) : left.InboxOrder.CompareTo(right.InboxOrder);
    }

    private static bool BeforeRecoveryControl(Delivery item, Delivery? control) =>
        control is null || !item.Message.IsControl && BeforeCut(item, control);

    /// <summary>
    /// An instance that cannot be given its messages now, as its actor type says (<see cref="IActorType.Withholds"/>).
    /// Kernel messages still pass.
    /// </summary>
    private bool Withheld(Slot slot) => slot.CurrentRow is { } row &&
        (DeferWithholdingCheck(row) ? slot.WithheldAtCheckpoint != false : _type.Withholds(row, this));

    private bool DeferWithholdingCheck(InstanceRow row) => CanWithdrawJobTrials && _type.SupportsFoundationOperator(row) &&
        (Blocked || TrialItems.Count > 0 && !OnTrial(Canonical(row.Id)));

    private bool CanSettleWhileWithheld(Slot slot, Delivery delivery) =>
        slot.Row is { Retired: false } row && _type.SettlesWhileWithheld(row, delivery);

    /// <summary>
    /// Finding F2: suspects are tried apart from everything else. A fenced commit writes that they are on trial at this
    /// epoch; then only they run. If this owner's lease expires, the take that follows adds an attempt to exactly the
    /// rows on trial. So a death counts against what was on trial and never against its neighbours. A group holds
    /// suspects with the same attempts and halves with each attempt, so a poison message ends up alone.
    /// </summary>
    private List<Delivery> NextTrial()
    {
        if (TrialItems.Count > 0 || Blocked) return [];
        var suspects = _items.Values.Where(i => i.Durable && !i.Marked && !i.Taken && i.Suspect)
            .OrderBy(i => i.ReleasedBy is not null && _slots.TryGetValue(Canonical(i.Message.Target.Id), out var slot) && CohortInput(i, slot) ? 0 : 1)
            .ThenBy(i => i.Message.DeliveryOrder(i.Seq)).ToList();
        if (CanWithdrawJobTrials && suspects.Exists(i => i.Ticket && i.Key is not null) && TerminatePrecedesJobRetry(Now))
            suspects.RemoveAll(i => i.Ticket && i.Key is not null);
        if (suspects.Count == 0 || Has(Flaw.BumpAllInherited)) return suspects;
        var (group, skipped) = (new List<Delivery>(), new HashSet<string>());
        int? attempts = null;
        var now = Now;
        var boundaries = new Dictionary<string, Delivery?>(StringComparer.Ordinal);
        var heads = new Dictionary<string, ParkPageCursor?>(StringComparer.Ordinal);
        foreach (var suspect in suspects)
        {
            var target = Canonical(suspect.Message.Target.Id);
            var slot = _slots[target];
            if (!boundaries.TryGetValue(target, out var control))
                boundaries[target] = control = RecoveryBoundary(slot, now, recovering: true);

            // Excluded suspects do not precede any candidate on this side of the boundary, even when
            // their store sequence sorts first. Within the eligible side, take only a prefix per target.
            if (!heads.TryGetValue(target, out var head)) heads[target] = head = FirstUnheldRecoveryInput(slot);
            var cohort = CohortInput(suspect, slot);
            if (WithheldByDrain(suspect, slot) || suspect.Message.Due > now ||
                (head is { } first && (cohort ? RecoveryOrder(suspect) != first : slot.CurrentRow?.ParkCohort is not null)) ||
                !cohort && !BeforeRecoveryControl(suspect, control)) continue;
            // A raw job already converted to terminal/refusal cleanup needs a fence, not physical entry.
            if (skipped.Contains(target) || PoisonPending(slot) &&
                (suspect.Message.Kind == Kinds.Job && suspect.Ticket || !PrecedesPoison(suspect, slot, control))) continue;
            if (suspect.UncheckedReference is null && (Loaded(slot) || Has(Flaw.TrialBeforeLoad)) && (!slot.Busy || Has(Flaw.SuspendDuringTrial) || Has(Flaw.TrialBeforeLoad))
                && (DiscardedAt(suspect, slot) || !Withheld(slot) || CanSettleWhileWithheld(slot, suspect) ||
                    CreationError(slot, suspect.Message) is not null)
                && (attempts ??= suspect.Attempts) == suspect.Attempts)
            {
                group.Add(suspect);
                if (group.Count >= Math.Max(1, _options.TrialGroup >> attempts.Value)) break;
            }
            else skipped.Add(target);
        }
        return group;
    }

    /// <summary>
    /// Finding F3: a polled message is not acted on until a fenced commit has marked it seen at this epoch (and bumped
    /// its attempts if an earlier owner had it). The sketch's rule covers only rows an earlier owner had marked.
    /// </summary>
    private bool Awaits(Delivery item) => item.Durable && !item.Marked && (item.Suspect || !Has(Flaw.RunBeforeMark));

    private static bool BeforeCut(Delivery item, Delivery control)
    {
        // This completion already reached a turn. Its replacement's insertion sequence cannot move it behind
        // the cut. A held completion still needs an eligible stimulus, so a command can pass a lone Hold.
        if (item.Message.FoundationHeldJobResult) return true;
        var at = control.Message.Due ?? control.ObservedAt;
        // A driver's result becomes input when it is ready. Its persisted row is the ticket for
        // running the job; that row cannot prove the result existed before a command.
        if (item.CompletedOrder != 0)
            return item.CompletedAt < at || item.CompletedAt == at && item.CompletedOrder < control.ObservedOrder;
        // An exhausted ticket's error is synthesized on poll. Its first observation is the
        // available evidence that the error was ready, even when the ticket itself is much older.
        var orderedRows = item.Original is null && item.Durable && control.Durable;
        var earlier = orderedRows ? item.Seq < control.Seq : item.ObservedOrder < control.ObservedOrder;
        // Durable sequence proves an earlier immediate input was accepted before the command. An earlier timer
        // also precedes it once due, even when the first poll happens after both maturities.
        if (orderedRows && earlier)
            return item.Message.Due is not { } durableDue || durableDue <= at;
        if (item.ObservedAt > at || (item.ObservedAt == at && !earlier)) return false;
        if (item.Message.Due is not { } due) return true;
        return due == at ? earlier : due < at;
    }

    // During recovery, each mature command keeps its boundary until it leaves the inbox. A late wake can
    // discover several at once. Pick the earliest maturity, with same-source order for equal maturities.
    // Compare the two source heads by first observation: volatile posts have no database sequence.
    private Delivery? RecoveryBoundary(Slot slot, long now, bool recovering, Delivery? skip = null)
    {
        Delivery? durable = null, local = null;
        var earliest = long.MaxValue;
        if (!recovering && !slot.Inbox.Exists(i => i.RecoveryOrdering || i.Suspect)) return null;
        foreach (var item in slot.Inbox)
        {
            if (item == skip || item.Taken || !item.Message.IsControl || item.Message.Kind == Kinds.Suspend && Has(Flaw.SuspendDuringTrial)) continue;
            if (slot.CurrentRow?.ParkRecovery is not null && item.Held && HeldCommandStimulus(slot, item, now) == 0) continue;
            var at = item.Message.Due ?? item.ObservedAt;
            if (at > now) continue;
            item.RecoveryOrdering = true;
            if (at > earliest) continue;
            if (at < earliest)
            {
                earliest = at;
                durable = local = null;
            }
            if (item.Durable)
            {
                if (durable is null || item.Seq < durable.Seq) durable = item;
            }
            else if (local is null || item.ObservedOrder < local.ObservedOrder) local = item;
        }
        if (durable is null) return local;
        return local is not null && local.ObservedOrder < durable.ObservedOrder ? local : durable;
    }

    private bool ObsoleteJobAt(Delivery item, InstanceRow? row) =>
        item.IsJobWork && row is not null &&
        (row.NextJobGeneration is not null || item.Message.FoundationJobResult || _type.SupportsFoundationOperator(row)) &&
        item.IsObsoleteJob(row);

    private bool DiscardedAt(Delivery item, Slot slot) =>
        item.DiscardedAt(slot) || ObsoleteJobAt(item, slot.CurrentRow);

    private List<Delivery> Deliverable(string instance, Slot slot, long now)
    {
        if (slot.CurrentRow is { Status: InstanceStatus.Running, Cascade: { } cascade })
        {
            var next = slot.Inbox.Find(item => item.Message.InboxKey == cascade.Control && !item.Taken);
            return next is not null && !Awaits(next) ? [next] : [];
        }
        if (_exhaustedCatchUps.Count != 0 && slot.CurrentRow is { Status: InstanceStatus.Running } row &&
            _exhaustedCatchUps.Any(ticket => ticket.Message.Target.Id == instance &&
                OwnsFailedEventStep(row, ticket.Message)))
            return [];
        var due = new List<Delivery>();
        var suspended = Withheld(slot);
        var onTrial = OnTrial(instance);
        var open = slot.CurrentRow?.ParkRecovery is not null;
        var head = FirstUnheldRecoveryInput(slot);
        var control = RecoveryBoundary(slot, now, onTrial || open);
        Delivery? waitingDrain = null;
        if (open && !onTrial && control is { Message: { Kind: Kinds.Drain, Due: not null } drain } &&
            drain.InboxKey == slot.CurrentRow?.ParkRecovery?.Control)
        {
            // A reissued control is waiting on credit, a handoff or suspension. Another mature control
            // retains priority. Otherwise let an eligible cohort input advance before probing again.
            waitingDrain = control;
            control = RecoveryBoundary(slot, now, recovering: true, skip: waitingDrain);
        }
        if (open && !onTrial && control is not null) return ReadyControl(control);
        if (head is { } first && !slot.Inbox.Exists(item => RecoveryOrder(item) == first)) return ReadyControl(waitingDrain);
        var cohortFirst = slot.CurrentRow?.ParkCohort is not null && head is not null;
        bool Eligible(Delivery item) => item != waitingDrain && !item.Ticket && !item.Taken && !(item.Message.Due > now) && !WithheldByDrain(item, slot) && (!cohortFirst || CohortInput(item, slot))
            && (!suspended || DiscardedAt(item, slot) || item.Message.IsControl || CanSettleWhileWithheld(slot, item) ||
                CreationError(slot, item.Message) is not null);
        bool InFront(Delivery item) => CohortInput(item, slot) || TrialItems.Contains(item) || BeforeRecoveryControl(item, control);

        // Finish marking ordinary cohort members together. Unselected suspects obtain their own fenced
        // trial later; waiting for their mark while holding the process gate would deadlock.
        if (control is not null && onTrial && slot.Inbox.Exists(i => Eligible(i) && InFront(i) && !i.Suspect
            && i.ObservedOrder <= _trialObserved && Awaits(i))) return [];

        // In normal operation the first deliverable command ends the sequence. Recovery projects the
        // inbox onto the current command's eligible inputs, keeping their ordinary actor order, then the
        // command itself. It must encounter an unmarked predecessor before it can reach that command.
        IEnumerable<Delivery> inputs = slot.Inbox.Where(i => Eligible(i) && InFront(i));
        if (!suspended && RecoveryRelease(slot.CurrentRow) is not null)
            inputs = inputs.OrderBy(item => item, Comparer<Delivery>.Create((left, right) => CompareRecoveryInputs(left, right, slot)));
        if (control is not null && !onTrial && !cohortFirst) inputs = inputs.Append(control);
        var selectedOnly = false;
        foreach (var item in inputs)
        {
            if (selectedOnly && !TrialItems.Contains(item)) continue;
            if (onTrial && item.Suspect && !TrialItems.Contains(item))
            {
                // A narrow horizon can reveal an older suspect after the fenced selection. Its trial
                // must follow that selection; no later ordinary input may leapfrog it.
                selectedOnly = true;
                continue;
            }
            if (item.UncheckedReference is not null || Awaits(item)) break;
            if (CohortInput(item, slot) && PoisonInput(item, slot))
            {
                // Resolve the prefix first. Only already-held predecessors can join the poison turn.
                if (due.TrueForAll(predecessor => predecessor.Held)) due.Add(item);
                break;
            }
            var command = item.Message.IsControl;
            var identityReply = CreationError(slot, item.Message) is not null;
            var continuingStart = !identityReply && slot.Row?.PendingStart == item.Message.InboxKey;
            // Keep lifecycle changes/refusals separate. Compatible messages of this creation retain normal batching.
            if (item.Message.SuppliedCreator && slot.CurrentRow is null ||
                item.Message.Start?.Creation is not null && !continuingStart || identityReply ||
                slot.Row?.Creation > 0 && item.Message.Start is not null && !continuingStart ||
                _type.SelectsAlone(slot.CurrentRow?.Model ?? item.Message.Start?.Model, slot.CurrentRow, item))
            {
                if (due.TrueForAll(i => i.Held)) due = [item];
                break;
            }
            if (command)
            {
                // The kernel's, alone, after what came before it, except held messages, which wait for something new and
                // would wait for ever behind it. A suspend waits while the instance has a message on trial, or the trial
                // could never end.
                if (due.TrueForAll(i => i.Held)) due = [item];
                break;
            }
            due.Add(item);
        }
        if (due is [var held] && held.Held && held.Message.IsControl && HeldCommandStimulus(slot, held, now) > 0)
            return due;
        // An obsolete job result is side-effect-free bookkeeping, even when it was held at a prior checkpoint.
        // A terminal child notice can also settle a registry entry without a new model stimulus.
        return due.TrueForAll(i => i.Held && !ObsoleteJobAt(i, slot.CurrentRow) &&
            (i.Message.Kind != Kinds.ChildFinished || slot.CurrentRow?.Status == InstanceStatus.Running ||
                !CanSettleWhileWithheld(slot, i))) ? ReadyControl(waitingDrain) : due;
    }

    private List<Delivery> ReadyControl(Delivery? item) =>
        item is null || Awaits(item) || item.UncheckedReference is not null ? [] : [item];

    private long HeldCommandStimulus(Slot slot, Delivery held, long now) => slot.Inbox
        .Where(item => !item.Held && !item.Taken && !item.Ticket && !(item.Message.Due > now) &&
            item.ObservedOrder > held.HeldThrough && !Awaits(item) && !DiscardedAt(item, slot) &&
            !WithheldByDrain(item, slot) && CreationError(slot, item.Message) is null)
        .Select(item => item.ObservedOrder).DefaultIfEmpty().Max();

    // A qualified terminate also frees its ordinary predecessors to run. The dispatcher still selects them first.
    private bool TerminatePrecedesJobRetry(long now, bool forWithdrawal = false)
    {
        foreach (var delivery in _items.Values)
        {
            if (!delivery.Durable || delivery.Message.Kind != Kinds.Terminate || delivery.Message.Due > now ||
                delivery.UncheckedReference is not null) continue;
            var instance = Canonical(delivery.Message.Target.Id);
            if (!_slots.TryGetValue(instance, out var slot) || !Loaded(slot)) continue;
            var row = slot.CurrentRow;
            var creator = slot.Row is null ? slot.Inbox.Find(item => item.Durable && item.Message.BindFoundationChild &&
                item.Message.TargetCreation == delivery.Message.TargetCreation && CreationError(slot, item.Message) is null)?.Message : null;
            if (row is not null && CreationError(slot, delivery.Message) is not null ||
                !_type.AuthorizesTrialWithdrawal(row, delivery.Message, creator)) continue;
            if (delivery.Taken && !forWithdrawal) return true;
            if (!delivery.Taken && (!forWithdrawal || !Awaits(delivery)) &&
                (!delivery.Held || HeldCommandStimulus(slot, delivery, now) > 0)) return true;
        }
        return false;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loading || !Owns || !NeedsLoad) return;
        // Prefer live candidates whose address the database cannot supply, including an actor still waiting in memory.
        var requested = _slots.Where(s => !s.Value.Loaded || s.Value.Row is null || (_store.HasActorAliases && s.Value.Unwritten) || s.Value.PendingReferences > 0)
            .OrderByDescending(s => LookupPriority(s.Value)).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        var targets = requested.Keys;
        var pending = _slots.Where(s => s.Value.PendingReferences > 0)
            .SelectMany(s => s.Value.Inbox.Where(i => i.UncheckedReference is not null).Select(i => (Target: s.Key, Item: i, Reference: i.UncheckedReference!))).ToArray();
        IReadOnlyCollection<string> ids = pending.Length == 0 ? targets : targets.Concat(pending.Select(p => p.Reference)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Count == 0) { _retryAliases = false; return; }
        var generation = _generation;
        var aliasRead = _store.HasActorAliases ? ++_aliasRead : 0;
        _aliasesChangedDuringLoad = false;
        _loading = true;
        IReadOnlyList<ActorLookup> rows;
        try { rows = await _store.LoadActorsAsync(ids, cancellationToken); }
        catch (Exception)
        {
            if (generation == _generation) _loading = _aliasesChangedDuringLoad = _retryAliases = false;
            return;
        }
        if (generation != _generation) return;
        _loading = false;
        // The database may return a canonical spelling outside the requested slot set.
        // A new or evicted spelling can even invalidate a miss whose two names were identical.
        // Resolve the aliases and pending self-references against the new set of candidates.
        if (_aliasesChangedDuringLoad)
        {
            _aliasesChangedDuringLoad = false;
            foreach (var slot in requested.Values)
                if (slot.Row is null && !slot.Busy) slot.Loaded = false;
            _retryAliases = true;
            return;
        }
        // Provider equivalence can relate two already resident spellings. A fenced write or
        // an outstanding turn belongs to its original slot even when cache membership did not change.
        HashSet<string>? retry = null;
        if (aliasRead != 0)
            foreach (var match in rows)
            {
                var source = requested.GetValueOrDefault(match.Requested);
                var target = _slots.GetValueOrDefault(match.Canonical);
                if (source?.WrittenDuringAliasRead == aliasRead || target?.WrittenDuringAliasRead == aliasRead ||
                    match.Requested != match.Canonical && source is { Busy: true } or { Unwritten: true })
                    (retry ??= []).Add(match.Canonical);
            }
        // A minted name admitted during the retry is still fresh when the lookup found only
        // persisted actors. An unresolved absence or in-memory actor needs provider equality
        // with that new spelling, so leave it for the next batched read in that case.
        if (_retryAliases && retry is null && rows.All(r => r.Row is not null) && !requested.Values.Any(s => s.Unwritten))
            foreach (var (name, slot) in _slots)
                if (slot.DeferredFresh && !requested.ContainsKey(name))
                    (slot.Loaded, slot.DeferredFresh) = (true, false);
        _retryAliases = retry is not null;
        var byName = pending.Length > 0 ? rows.ToDictionary(r => r.Requested, StringComparer.Ordinal) : null;
        foreach (var match in rows)
        {
            // A new delivery can recreate an evicted slot at the same name in the same lease.
            if (!requested.TryGetValue(match.Requested, out var original) ||
                !_slots.TryGetValue(match.Requested, out var source) || source != original) continue;
            var key = match.Canonical;
            if (retry?.Contains(key) == true)
            {
                if (source.Row is null && !source.Busy) source.Loaded = false;
                continue;
            }
            if (!_slots.TryGetValue(key, out var slot)) _slots[key] = slot = source;
            if (match.Requested != key)
            {
                _slots.Remove(match.Requested);
                // A formerly canonical name may already own aliases. Redirect each of them
                // directly to the new canonical slot, which also owns their eventual eviction.
                var aliases = slot.Aliases ??= [];
                if (source.Aliases is { } earlier)
                {
                    foreach (var alias in earlier) _aliases[alias] = key;
                    if (slot != source) aliases.UnionWith(earlier);
                }
                aliases.Remove(key);
                _aliases.Remove(key);
                aliases.Add(match.Requested);
                _aliases[match.Requested] = key;
                if (slot != source)
                {
                    lock (slot)
                    {
                        foreach (var delivery in source.Inbox) Insert(slot, delivery);
                        slot.PendingReferences += source.PendingReferences;
                        if (source.SelfReferences is { } references)
                            foreach (var (name, same) in references) (slot.SelfReferences ??= [])[name] = same;
                        slot.Replies.UnionWith(source.Replies);
                        if (source.Ambient is not null && source.CurrentRow is { } prior && match.Row is { Retired: false } actual &&
                            prior.Id == actual.Id && prior.Creation == actual.Creation && prior.StartIntent == actual.StartIntent)
                            slot.Ambient = source.Ambient;
                    }
                }
            }
            // A valid physical hit supersedes an older idle cached miss.
            slot.Comparison = aliasRead;
            if (slot.Busy || slot.Loaded && (slot.Row is not null || match.Row is null && slot.AbsentAt == 0)) continue;
            slot.AbsentAt = aliasRead != 0 && match.Row is null ? _aliasBirths : 0;
            (slot.Row, slot.Loaded, slot.DeferredFresh, slot.Used) = (match.Row, true, false, ++_used);
            PublishJobEligibility(slot);
        }
        foreach (var (target, item, reference) in pending)
        {
            if (item.UncheckedReference is null || retry?.Contains(byName![target].Canonical) == true ||
                retry?.Contains(byName![reference].Canonical) == true || !_slots.TryGetValue(Canonical(target), out var slot)) continue;
            lock (slot)
            {
                (slot.SelfReferences ??= [])[reference] = byName![target].Canonical == byName[reference].Canonical;
                item.UncheckedReference = null;
                slot.PendingReferences--;
                RefreshPoisonBoundary(slot);
            }
        }
        foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToArray()) StartJob(ticket, cancellationToken);
        Evict();
    }

    private string Canonical(string target) => _aliases.GetValueOrDefault(target, target);

    private void RemoveSlot(string key)
    {
        if (!_slots.Remove(key, out var slot)) return;
        if (_loading && _store.HasActorAliases) _aliasesChangedDuringLoad = true;
        if (slot.Aliases is not { } aliases) return;
        foreach (var alias in aliases) _aliases.Remove(alias);
    }

    // ---- turns ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Run one turn of an instance that is in <see cref="Ready"/>. The actor type decides first what it handles alone
    /// (a command, or messages to an instance that ended); then a message on trial too often parks the instance, a
    /// message without a target or a start spec is dropped, and otherwise the actor type runs the turn.
    /// </summary>
    public void RunTurn(string instance) => RunTurn(instance, default);

    private void RunTurn(string instance, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        var now = ReceiveJobPublications(cancellationToken);
        instance = Canonical(instance);
        if (_releasing || !LeaseUsable() || !_slots.TryGetValue(instance, out var slot) || !Loaded(slot) || slot.Busy || slot.DetachedAuthoredTurn) return;
        if (Blocked || (TrialItems.Count > 0 && !OnTrial(instance) && !Spared(instance))) return;
        var due = Deliverable(instance, slot, now);
        if (due.Count == 0) return;
        var supplied = due.FindAll(i => DiscardedAt(i, slot));
        if (supplied.Count > 0)
        {
            // A wire message cannot complete a job. Fence consumption before selecting a creator or involving
            // the actor type. Omit the slot so this bookkeeping cannot change actor state, ambient or observers.
            foreach (var item in supplied) item.Taken = true;
            Enqueue(new TurnOutcome(instance) { Consumed = supplied }, cancellationToken);
            return;
        }
        if (slot.Unwritten && (now >= slot.Until || due.Exists(i => i.Durable || i.Message.IsControl)) && !Has(Flaw.NoWriteFirst))
        {
            // Decision 4: a message from outside, or a command, finds an instance kept in memory written first; then it
            // is an ordinary instance, with a row, for whatever the message does.
            slot.Busy = true;
            Enqueue(Bound(_type.Write(slot.Row!, slot.State, slot.Pending), slot), cancellationToken);
            return;
        }
        if (due is [var commandStimulus] && commandStimulus.Message.IsControl)
            commandStimulus.HeldThrough = commandStimulus.Held
                ? HeldCommandStimulus(slot, commandStimulus, now) : commandStimulus.ObservedOrder;
        slot.Inbox.RemoveAll(due.Contains);
        foreach (var item in due) (item.Taken, item.Held) = (true, false);
        (slot.Busy, slot.Used) = (true, ++_used);
        var request = due[0].Message;
        var creationError = CreationError(slot, request);
        if (request.Kind == Kinds.Cascade && creationError is null or "RetiredCreation" &&
            slot.Row is { } discharging && due is [var control] && CascadeProtocol.Matches(discharging, request))
        {
            Enqueue(Bound(CascadeProtocol.Continue(discharging, control), slot), cancellationToken);
            return;
        }
        if (creationError is not null)
        {
            var refusal = creationError == "RetiredCreation" && slot.Row is { } marker && due is [var terminate] &&
                CascadeProtocol.MatchesRetiredTerminate(marker, request)
                    ? CascadeProtocol.OpenRetired(marker, terminate)
                    : CreationProtocol.Refuse(instance, slot.Row, due, creationError);
            Enqueue(Bound(refusal, slot), cancellationToken);
            return;
        }
        if (request.Kind == Kinds.Cascade)
        {
            Enqueue(Bound(CreationProtocol.Refuse(instance, slot.Row, due, "InvalidControl"), slot), cancellationToken);
            return;
        }
        var creator = slot.Row is null or { Retired: true } ? due.FindIndex(i => i.Message.Start is not null) : -1;
        var spec = creator < 0 ? null : due[creator].Message.Start;
        slot.Starting = creator < 0 ? null : due[creator].Message;
        if (creator >= 0) slot.Ambient = due[creator].StarterAmbient;
        var input = new TurnInput(this, slot, instance, due, Math.Max(creator, 0), spec, now);
        if (request.Operator is not null && !_type.AcceptsFoundationOperator(input.Row, request))
            Enqueue(Bound(CreationProtocol.Refuse(instance, input.Row, due, "OperatorUnavailable"), slot), cancellationToken);
        else if (_type.Command(input) is { } command)
            Enqueue(Bound(command, slot), cancellationToken);
        else if (due.Find(i => i.Attempts >= _options.MaxAttempts && i.Message.Kind != Kinds.JobFinished) is { } poison)
        {
            var previous = slot.CurrentRow;
            var park = _type.Park(previous, instance, due, spec, $"message {poison.Message.Id} was on trial when {poison.Attempts} owners were lost");
            Enqueue(Bound(MarkPark(park, previous), slot), cancellationToken);
        }
        else if (slot.Row is null && spec is null)                   // no target and no start spec: dropped and journalled
            Enqueue(new TurnOutcome(instance) { Slot = slot, Consumed = due, Journal = Json.Write(due.Select(i => "dropped " + i.Message.Id)), Status = null }, cancellationToken);
        else
        {
            slot.Driving = true;
            var entered = Drive(input, cancellationToken);
            slot.EnteredTurn = slot.Driving ? entered : null;
        }
    }

    // One classifier for trial/shelving, withholding and dispatch: a refusal never enters the actor's authored work.
    private string? CreationError(Slot slot, Message request) => request.Start is not null && (request.Start.Creation is not null || slot.Row?.Creation > 0)
            ? request.Start.Creation is null ? "IdentityRequired"
                : !CreationIdentityAvailable ? "CreationIdentityUnavailable"
                : request.TargetCreation is { } target && target != request.Start.Creation.Generation ? "WrongCreation"
                : CreationProtocol.StartError(slot.Row, request.Start, request.Kind, request.Payload, request.InboxKey)
            : CreationProtocol.TargetError(slot.Row, request);

    // The host has stopped scheduling new turns. A drain pass reconciles heartbeat answers and flushes
    // completed outcomes. Keeping those passes live lets legitimate waiting work renew through the normal gate.
    internal async Task DrainEnteredTurnsAsync(CancellationToken cancellationToken)
    {
        if (!Owns) return;
        var generation = _generation;
        Task entered;
        lock (_turnSync)
            entered = Task.WhenAll(_slots.Values.Where(slot => slot is { Driving: true, EnteredTurn: not null })
                .Select(slot => slot.EnteredTurn!));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _answered) is not null) await AnsweredAsync(cancellationToken);
            RequireDrainFence();
            if (_queue.Count > 0) await FlushAsync(cancellationToken);
            RequireDrainFence();
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _lastPass, _options.Monotonic());

            if (entered.IsCompleted)
            {
                await entered;
                if (_queue.Count == 0) return;
            }
            if (_queue.Count > 0) continue;
            await Task.WhenAny(entered, Task.Delay(_options.Lease / 3, cancellationToken));
        }

        void RequireDrainFence()
        {
            if (_generation != generation || !LeaseUsable())
                throw new InvalidOperationException("Ownership was lost while draining actor turns during shutdown.");
        }
    }

    private async Task Drive(TurnInput input, CancellationToken cancellationToken)
    {
        var generation = _generation;
        var fence = input.Slot.TurnFence;
        var inline = input.Spec?.FoundationInlineTenant is not null || input.Row?.FoundationInlineBirth is not null;
        TurnOutcome unit;
        try { unit = await _type.TurnAsync(input); }
        catch (Exception e)
        {
            lock (_turnSync)
            {
                if (generation != _generation) return;
                ResumeAfterDetachedTurn(input.Slot);
                if (fence != input.Slot.TurnFence || inline && !LeaseUsable()) return;
                input.Slot.Driving = false;
                input.Slot.EnteredTurn = null;
                input.Slot.RollbackTurn?.Invoke();
                unit = _type.Park(input.Row, input.Actor, input.Taken, input.Spec, e.Message, input.Unwritten ? input.Pending : null);
                MarkPark(unit, input.Row);
                if (input.Unwritten) RetireVolatileJobs(unit);
                Enqueue(Bound(unit, input.Slot), cancellationToken);
            }
            return;
        }
        lock (_turnSync)
        {
            if (generation != _generation) return;
            ResumeAfterDetachedTurn(input.Slot);
            if (fence != input.Slot.TurnFence || inline && !LeaseUsable()) return;
            input.Slot.Driving = false;
            input.Slot.EnteredTurn = null;
            Enqueue(Bound(unit, input.Slot), cancellationToken);
        }
    }

    private void ResumeAfterDetachedTurn(Slot slot)
    {
        if (!slot.DetachedAuthoredTurn) return;
        slot.DetachedAuthoredTurn = false;
        _woken = true;
    }

    private TurnOutcome Bound(TurnOutcome unit, Slot slot)
    {
        unit.Slot = slot;
        if (unit.Row is { Retired: false } row)
        {
            if (slot.Row is null or { Retired: true } && slot.Starting is { Start.Creation: { } start } request)
            {
                unit.Row = row with { Creation = start.Generation, StartIntent = CreationProtocol.Fingerprint(request.Start, request.Kind, request.Payload), ParentCreation = request.Start.ParentCreation,
                    PendingStart = row.Status == InstanceStatus.Running && unit.Held.Any(i => i.Message.InboxKey == request.InboxKey) ? request.InboxKey : (InboxKey?)null };
                unit.Expected = slot.Row?.Revision;
                unit.Revision = unit.Row.Revision;
            }
            else if (slot.Row is { Creation: > 0 } previous)
                unit.Row = row with { Creation = previous.Creation, StartIntent = previous.StartIntent, ParentCreation = previous.ParentCreation,
                    PendingStart = row.Status == InstanceStatus.Running && !unit.Consumed.Any(i => i.Message.InboxKey == previous.PendingStart) ? previous.PendingStart : null };
        }
        // The owner already knows its restricting decision, even if its durable acknowledgement is still in flight.
        // Count each such outcome separately: applying one unit must not release another unit's restriction.
        if (unit.FoundationWithholds || unit.Delete || unit.FoundationCascade is not null || unit.Row is { } next && (next.Retired || next.Status != InstanceStatus.Running ||
            next.Creation != slot.ActiveCreation || _type.Withholds(next, this)) ||
            slot.Unwritten && unit.Row is null && unit.Status is not (null or InstanceStatus.Running))
        {
            lock (slot)
            {
                slot.PendingJobRestrictions++;
                unit.RestrictsJobEntry = true;
            }
        }
        return unit;
    }

    private static void ReleaseJobRestriction(TurnOutcome unit)
    {
        if (unit.RestrictsJobEntry && unit.Slot is { } slot)
            lock (slot) slot.PendingJobRestrictions--;
    }

    /// <summary>The turn has started: for tests (<see cref="WorkerOptions.OnTurn"/>).</summary>
    internal void Started(string instance, IReadOnlyList<Message> inbox, long now) => _options.OnTurn?.Invoke(new InstanceRef(instance), inbox, now);

    // ---- background jobs -------------------------------------------------------------------------------------------

    private void PublishJobEligibility(Slot slot)
    {
        var current = slot.CurrentRow;
        var running = current is { Status: InstanceStatus.Running };
        var deferred = current is not null && DeferWithholdingCheck(current);
        // A new checkpoint may be loaded while another actor's trial is live. Defer its authored predicate.
        var held = deferred ? (bool?)null : running && _type.Withholds(current!, this);
        if (deferred) _deferredJobEligibility = true;
        var runnable = running && held == false ? current!.Creation : -1;
        lock (slot)
        {
            RefreshPoisonBoundary(slot);
            slot.WithheldAtCheckpoint = held;
            Volatile.Write(ref slot.ActiveCreation, current?.Creation ?? -1);
            Volatile.Write(ref slot.RunnableCreation, runnable);
        }
    }

    private void StartJob(Delivery ticket, CancellationToken cancellationToken)
    {
        if (!_slots.TryGetValue(Canonical(ticket.Message.Target.Id), out var loadedSlot) || !Loaded(loadedSlot) || loadedSlot.Busy) return;
        if (ObsoleteJobAt(ticket, loadedSlot.Row))
        {
            CancelJobDriver(ticket);
            ticket.Ticket = false;
            ticket.Key = null;
            return; // the next fenced dispatch drops the obsolete ticket
        }
        if (ticket.JobDeadline is { } deadline && Now >= deadline) return;
        if (loadedSlot.Row is not { Status: InstanceStatus.Running } || CreationProtocol.TargetError(loadedSlot.Row, ticket.Message) is not null)
        {
            // An entered driver of this occurrence retains its completion path until explicit cancellation.
            // Queued callbacks and earlier occurrences still cannot enter or borrow the current row.
            if (Volatile.Read(ref ticket.JobEntered) && loadedSlot.Row is { Retired: false, Status: not InstanceStatus.Running } current &&
                current.Creation == (ticket.Message.TargetCreation ?? 0)) return;
            CancelJobDriver(ticket);
            ticket.Ticket = false; ticket.Key = null;
            return; // the normal fenced dispatch consumes/refuses this stale target
        }
        // Callers finish their current collection operation before standing down, so a refused ticket is never stranded.
        if (_releasing || !CanStartWork || WithheldByDrain(ticket, loadedSlot) || PoisonPending(loadedSlot) || Volatile.Read(ref loadedSlot.PendingJobRestrictions) != 0 || Withheld(loadedSlot) || ticket.Cancel is not null || ticket.Taken || ticket.AwaitingJobMark || Awaits(ticket) || _schedulerRetries.Contains(ticket)) return;
        if (CanWithdrawJobTrials && ticket.Again && TerminatePrecedesJobRetry(Now))
        {
            _jobsWaitingForTerminate = true;
            return;
        }
        if (ticket.DeferredJobTrial)
        {
            lock (loadedSlot)
            {
                if (!_gate.TryBegin(this, [ticket])) { _jobsWaitingForTrial = true; return; }
                _trialObserved = ticket.DeferredTrialObserved;
                ticket.DeferredJobTrial = false;
            }
        }
        if (!_gate.Allows(this, ticket))
        {
            _jobsWaitingForTrial = true;
            return;
        }
        // A read or successful commit may return after its caller stopped. Keep the accepted state and let
        // the next healthy pass submit the ticket. Already submitted jobs use their own cancellation source.
        if (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _jobsDeferredByCancellation, 1);
            return;
        }
        var generation = _generation;
        var job = ticket.Message.Read<JobTicket>();
        if (loadedSlot.Row is { FoundationDispatchHeader: not null } bound &&
            RootJobTicket.Read(ticket.Message, bound.Id, bound.Model, timed: _type.RootTimedJobPublicationClock is not null) is null)
            throw new StoreException(Failure.Definite, "The job ticket does not belong to its generated root.");
        if (!HasPhysicalTrial(ticket))
        {
            ticket.Cancel = new CancellationTokenSource();
            ticket.JobDriver = DriveJob(ticket, job, loadedSlot);
            return;
        }
        // Publish the exit task before a callback can enter. Reset takes this same lock before it can reuse the gate.
        var exit = new TaskCompletionSource();
        lock (_turnSync)
        {
            if (generation != _generation || !CanStartWork || ticket.Cancel is not null ||
                _items.GetValueOrDefault(ticket.Message.InboxKey) != ticket) return;
            ticket.Cancel = new CancellationTokenSource();
            ticket.JobDriver = exit.Task;
        }
        _ = TrackDriverExit(ticket, job, loadedSlot, exit);
    }

    private bool HasPhysicalTrial(Delivery ticket) => CanWithdrawJobTrials && TrialItems.Contains(ticket);

    private async Task TrackDriverExit(Delivery ticket, JobTicket job, Slot slot, TaskCompletionSource exit)
    {
        try { await DriveJob(ticket, job, slot); }
        finally { exit.SetResult(); }
    }

    private bool JobMayRun(long generation)
    {
        if (CanStartGeneration(generation)) return true;
        if (generation == Volatile.Read(ref _generation) && Volatile.Read(ref _expiredGeneration) == generation)
            _options.OnExpiredJob?.Invoke();
        return false;
    }

    private async Task DriveJob(Delivery ticket, JobTicket job, Slot slot)
    {
        var generation = _generation;
        var publications = _jobPublications;
        var original = ticket.Message;
        var creation = original.TargetCreation ?? 0;
        bool MayReturn() => JobMayRun(generation) && Volatile.Read(ref slot.ActiveCreation) == creation;
        var token = ticket.Cancel!.Token;
        bool entered = false;
        ValueTask<object?> Enter()
        {
            int attemptNumber;
            string? inlineTenant;
            string? rootReference;
            lock (slot)
            {
                if (!JobMayRun(generation) || _releasing || PoisonPending(slot) || slot.PendingJobRestrictions != 0 ||
                    Volatile.Read(ref slot.RunnableCreation) != creation || ticket.Taken || token.IsCancellationRequested ||
                    ticket.AwaitingJobMark || ticket.DeferredJobTrial || ticket.WithdrawingJob ||
                    job.Deadline is { } deadline && Now >= deadline || !_gate.Allows(this, ticket))
                    return ValueTask.FromResult<object?>(null);
                // Admission happens on the executing thread. Once admitted, ordinary cancellation signals the body.
                entered = true;
                Volatile.Write(ref ticket.JobEntered, true);
                if (ticket.JobEntries < int.MaxValue) ticket.JobEntries++;
                attemptNumber = ticket.JobEntries;
                inlineTenant = slot.CurrentRow?.FoundationInlineBirth?.Tenant;
                rootReference = slot.CurrentRow?.FoundationDispatchHeader?.Reference;
            }
            var context = new JobContext(original.Target, job.Key, job.Arguments, creation, attemptNumber, job.Deadline)
                { FoundationInlineTenant = inlineTenant, FoundationRootReference = rootReference };
            return _type.ExecuteJobAsync(job.Model, context, token);
        }
        async Task ExecuteAndPublish(bool scheduled)
        {
            var unbounded = Has(Flaw.UnboundedJobResult);
            string? result = null, error = null;
            try
            {
                if (scheduled) _options.BeforeScheduledJobCheck?.Invoke();
                var value = await Enter().ConfigureAwait(false);
                result = unbounded ? Json.Write(value) : JobResultEncoding.Value(value);
            }
            catch (JobResultEncoding.LimitExceeded) { error = JobResultEncoding.Error; }
            catch (Exception e) { error = e.Message; }
            if (!MayReturn() || token.IsCancellationRequested) return;
            var outcome = new JobFinished(job.Key, result, error);
            var completed = entered ? original with { Kind = Kinds.JobFinished,
                Payload = unbounded ? Json.Write(outcome) : JobResultEncoding.Payload(outcome) } : null;
            long? publicationTime = null;
            if (completed is not null && job.Deadline is not null && _type.RootTimedJobPublicationClock is { } readClock)
            {
                try { publicationTime = await readClock(token).ConfigureAwait(false); }
                catch (Exception) { return; } // Without an authoritative sample, leave the ticket for the deadline.
                if (!MayReturn() || token.IsCancellationRequested) return;
            }
            // Timestamp and enqueue together so simultaneous callbacks retain their observation order. The owner
            // may receive this after a command matures. Preserve an authoritative sample taken for this publication.
            lock (publications)
            {
                var at = publicationTime ?? Now;
                if (ticket.WithdrawingJob || ticket.JobDeadlineClosed || job.Deadline is { } deadline && at >= deadline) return;
                publications.Enqueue(new(ticket, slot, token, completed, at, Interlocked.Increment(ref _observed)));
            }
        }
        // Publish a synchronous body's outcome before this scheduled callback returns. An asynchronous
        // body completes its publication on its continuation, independently of the owner's next pass.
        if (_options.JobScheduler is not { } scheduler) await ExecuteAndPublish(false);
        else
        {
            Task<Task> scheduled;
            try
            {
                scheduled = Task.Factory.StartNew(() => ExecuteAndPublish(true), token,
                    TaskCreationOptions.DenyChildAttach, scheduler);
            }
            catch (TaskSchedulerException)
            {
                // The scheduler refused the callback before the model entered. Only the owner may retry its ticket.
                publications.Enqueue(new(ticket, slot, token, null, 0, 0, SchedulerRejected: true));
                return;
            }
            try { await scheduled.Unwrap().ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* cancelled while queued */ }
        }
    }

    private long ReceiveJobPublications(CancellationToken cancellationToken = default)
    {
        foreach (var ticket in TrialItems.Where(ticket => ticket.RetainsTrialGate).ToArray())
            if (ticket.JobDriver?.IsCompleted != false && _items.GetValueOrDefault(ticket.Message.InboxKey) != ticket)
                _gate.Finished(this, ticket);
        DeferPoisonBlockedJobTrials();
        if (_deferredJobEligibility && _gate.Idle)
        {
            _deferredJobEligibility = false;
            foreach (var slot in _slots.Values.Where(slot => slot.WithheldAtCheckpoint is null).ToArray())
            {
                PublishJobEligibility(slot);
                foreach (var ticket in slot.Inbox.Where(ticket => ticket.Key is not null).ToArray()) StartJob(ticket, cancellationToken);
            }
        }
        if (_jobsWaitingForTerminate && !TerminatePrecedesJobRetry(Now))
        {
            _jobsWaitingForTerminate = false;
            foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToList()) StartJob(ticket, cancellationToken);
        }
        if (_jobsWaitingForTrial && _gate.Idle)
        {
            _jobsWaitingForTrial = false;
            foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToList()) StartJob(ticket, cancellationToken);
        }
        long cutoff;
        do
        {
            while (_jobPublications.TryDequeue(out var observation))
            {
                var (ticket, slot, attempt, result, at, order, schedulerRejected) = observation;
                if (ticket.Cancel?.Token != attempt || attempt.IsCancellationRequested ||
                    _items.GetValueOrDefault(ticket.Message.InboxKey) != ticket ||
                    slot.ActiveCreation != (ticket.Message.TargetCreation ?? 0)) continue;
                if (result is null)
                {
                    ticket.Cancel = null;
                    if (schedulerRejected) _schedulerRetries.Add(ticket);
                    else StartJob(ticket, cancellationToken); // a staged restriction may have cleared since the driver checked
                    continue;
                }
                lock (slot)
                {
                    ticket.CompletedJob = true;
                    ticket.Original ??= ticket.Message;
                    ticket.Message = result;
                    ticket.Ticket = false;
                    ticket.CompletedAt = at;
                    ticket.CompletedOrder = order;
                    _lastReceivedJob = order;
                    if (slot.Inbox.Remove(ticket)) slot.Inbox.Insert(0, ticket);
                    RefreshPoisonBoundary(slot);
                }
            }
        } while (ExpireTimedJobs(out cutoff));

        // Selection shares the cutoff used to arbitrate callbacks. A timer that matures after this
        // scan needs a further pass so a timely callback published in the gap can still win.
        _options.AfterTimedJobCutoff?.Invoke();
        return cutoff;
    }

    private List<Delivery> FinishedJobWithdrawals(long now)
    {
        if (!CanWithdrawJobTrials || TrialItems is not { Count: > 0 } selected) return [];
        var jobs = selected.Where(ticket => !ticket.AbandonedTrial && ticket.IsDurableJobTicket && ticket.Ticket &&
            ticket.Message.Kind == Kinds.Job && _items.GetValueOrDefault(ticket.Message.InboxKey) == ticket).ToArray();
        var publications = _jobPublications;
        if (jobs.Any(ticket => !ticket.WithdrawingJob && !ticket.JobDeadlineClosed) && TerminatePrecedesJobRetry(now, forWithdrawal: true))
            foreach (var ticket in jobs)
            {
                lock (publications)
                {
                    if (ticket.WithdrawingJob || ticket.JobDeadlineClosed ||
                        publications.Any(item => item.Ticket == ticket && item.Result is not null)) continue;
                    ticket.WithdrawingJob = true;
                }
                try { ticket.Cancel?.Cancel(); }
                catch (AggregateException) { } // Keep the trial until the actual callback exits.
            }
        // An unknown clear resets this worker. Its gate must still cover every physical callback until it has exited.
        return jobs.Length == selected.Count && jobs.All(ticket =>
            (ticket.WithdrawingJob || ticket.JobDeadlineClosed) && ticket.JobDriver?.IsCompleted != false)
            ? [.. jobs] : [];
    }

    private bool ExpireTimedJobs(out long cutoff)
    {
        if (_timedJobs.Count == 0)
        {
            cutoff = Now;
            return false;
        }
        _options.BeforeTimedJobCutoff?.Invoke();
        var expired = new List<Delivery>();
        var foundTimelyResult = false;
        var publications = _jobPublications;
        lock (publications)
        {
            var now = Now;
            cutoff = now;
            foreach (var timed in _timedJobs.Where(item => item.JobDeadline <= now).ToArray())
            {
                if (timed.WithdrawingJob) continue;
                _timedJobs.Remove(timed);
                if (timed.CompletedJob || timed.Taken) continue;
                if (publications.Any(publication => publication.Ticket == timed && publication.Result is not null))
                {
                    foundTimelyResult = true;
                    continue;
                }
                timed.JobDeadlineClosed = true;
                expired.Add(timed);
            }
        }

        foreach (var timed in expired)
        {
            // Deadline settlement is local to the trial actor. It may remove the ticket while the callback unwinds.
            timed.RetainsTrialGate = HasPhysicalTrial(timed);
            try { timed.Cancel?.Cancel(); }
            catch (AggregateException) { } // A driver's cancellation handler cannot hold up the fenced timeout.
            timed.Suspect = false;
            timed.DeferredJobTrial = false;
            if (!timed.RetainsTrialGate) _gate.Finished(this, timed);
        }
        return foundTimelyResult;
    }

    // ---- events --------------------------------------------------------------------------------------------------

    /// <summary>The subscriber's side of "each side looks after its own commit": read the events for keys just subscribed.</summary>
    public async Task MeetAsync(CancellationToken cancellationToken = default)
    {
        if (_meeting || _flushing || !LeaseUsable()) return;
        var generation = _generation;
        var eventStep = _meets.FirstOrDefault(i => i.Message.HasFencedEventStep && !i.Taken);
        if (eventStep is not null)
        {
            _meets.Remove(eventStep);
            _meets.Add(eventStep);
            _meeting = _flushing = true;
            try { await RunFencedEventStepAsync(eventStep, generation, cancellationToken); }
            finally
            {
                if (generation == _generation) _meeting = _flushing = false;
            }
            if (generation != _generation || cancellationToken.IsCancellationRequested) return;
        }
        var tickets = _meets.Where(i => !i.Message.HasFencedEventStep && !Awaits(i) && !i.Taken).ToList();
        if (tickets.Count == 0) return;
        _meeting = true;
        try
        {
            // NATIVE N1: a publish ticket is the publisher's side, done by the owner after the commit that sent it.
            foreach (var ticket in tickets.Where(t => t.Message.Kind == Kinds.Publish))
            {
                var publication = FoundationEventTicket.Decode(ticket.Message);
                if (publication.FoundationTenant is not null)
                    publication = publication with { FoundationPublishTicket = ticket.Message.InboxKey };
                await Events.PublishAsync(_store, publication, Partitions, cancellationToken);
            }
            var waits = tickets.Where(t => t.Message.Kind == Kinds.Meet).SelectMany(t => t.Message.Read<SubscriptionRow[]>()).ToList();
            if (waits.Count > 0)
                foreach (var match in await _store.KeptMatchesAsync(waits, cancellationToken))
                    await _store.AddEventAsync(match.Delivery(Partitions), cancellationToken);
        }
        catch (Exception)
        {
            if (generation == _generation) _meeting = false;    // the tickets stay: the next call looks again
            return;
        }
        if (generation != _generation) return;
        _meeting = false;
        foreach (var ticket in tickets) ticket.Taken = true;
        Enqueue(new TurnOutcome("") { Consumed = tickets }, cancellationToken);
    }

    private async Task RunFencedEventStepAsync(Delivery ticket, long generation, CancellationToken cancellationToken)
    {
        if (_releasing || cancellationToken.IsCancellationRequested || !Holds(ticket.Message.Target.Partition)) return;
        var instance = ticket.Message.Target.Id;
        var failure = new ParkCatchUpFailure(ticket.Message.InboxKey, ticket.Attempts, _options.MaxAttempts);
        long? publicationSequence = ticket.Message.FoundationPublish is null ? null : ticket.Seq;
        var step = new TurnOutcome(instance) { Consumed = [ticket], FoundationPublicationSequence = publicationSequence };
        if (ticket.Attempts >= _options.MaxAttempts)
        {
            if (CanParkOnEventFailure(ticket.Message))
            {
                if (!_slots.TryGetValue(instance, out var slot))
                {
                    _slots.Add(instance, new Slot());
                    _woken = true;
                    return;
                }
                if (!Loaded(slot) || slot.Busy || slot.DetachedAuthoredTurn) return;
                if (slot.CurrentRow is { Status: InstanceStatus.Running, Retired: false, ParkDiscard: null } row &&
                    OwnsFailedEventStep(row, ticket.Message))
                {
                    slot.Busy = true;
                    var work = publicationSequence is null ? "the kept-event catch-up" : "the authored publication";
                    step = Bound(MarkPark(_type.Park(row, instance, [ticket], null,
                        work + " reached its failed step limit"), row), slot);
                    step.FoundationPublicationSequence = publicationSequence;
                }
            }
            step.FoundationCatchUpFailed = failure;
        }
        else
        {
            if (ParkCatchUpBudget() is null) return;
            var reserved = await CommitAsync([], [], [ticket], generation, cancellationToken);
            if (reserved == Outcome.Refused && generation == _generation) Reset();
            if (reserved != Outcome.Landed) return;
            if (ParkCatchUpBudget() is not { } budget) return;
            step.FoundationCatchUpStep = failure.Key;
            step.FoundationCatchUpBefore = checked(Now + (long)budget.TotalMilliseconds);
            step.FoundationCatchUpBudget = budget;
            step.FoundationCatchUpExpected = ticket.Attempts;
        }
        ticket.Taken = true;
        var outcome = await CommitAsync([step], [], [], generation, cancellationToken);
        if (outcome == Outcome.Landed && publicationSequence is not null)
        {
            _caughtUp = false;
            _woken = true;
        }
        if (outcome != Outcome.Refused || generation != _generation) return;
        if (step.FoundationCatchUpStep is null)
        {
            Reset();
            return;
        }
        var record = new TurnOutcome(instance)
            { FoundationCatchUpFailed = failure, FoundationPublicationSequence = publicationSequence };
        outcome = await CommitAsync([record], [], [], generation, cancellationToken);
        if (generation != _generation) return;
        if (outcome == Outcome.Refused) Reset();
        else if (outcome == Outcome.Landed)
        {
            ticket.Attempts++;
            if (CanParkOnEventFailure(ticket.Message) && ticket.Attempts >= _options.MaxAttempts) _exhaustedCatchUps.Add(ticket);
            ticket.Taken = false;
            _woken = true;
        }
    }

    private static bool CanParkOnEventFailure(Message message) =>
        message.ParkCatchUp is { Starts: false } || message.FoundationPublish?.Authored is not null;

    private bool OwnsFailedEventStep(InstanceRow row, Message message) =>
        message.FoundationPublish?.Owns(row, message) == true || _type.OwnsParkCatchUp(row, message);

    private TimeSpan? ParkCatchUpBudget()
    {
        var limit = _options.ParkCatchUpTimeout ?? _options.Lease;
        if (Has(Flaw.IgnoreLeaseDeadline)) return limit;
        var now = _options.Monotonic();
        var until = Volatile.Read(ref _safeUntil);
        if (until <= now) return null;

        // Reserve part of the existing ownership window for the fenced failure write.
        var reserve = Math.Clamp((long)_options.Lease.TotalMilliseconds / 8, 1, 1000);
        var remaining = until - now - reserve;
        return remaining > 0 ? TimeSpan.FromMilliseconds(Math.Min(limit.TotalMilliseconds, remaining)) : null;
    }

    // ---- the committer -------------------------------------------------------------------------------------------

    // A durable or in-memory birth can establish a previously missing equivalent spelling.
    // Fence old absences before the write (including a delayed acknowledgement) and at publication.
    private void InvalidateAliasMisses(TurnOutcome unit)
    {
        // A new generation at a persisted retirement marker keeps the same physical key.
        if (!_store.HasActorAliases || unit.Row is not { Retired: false } || unit.Slot is not { } slot ||
            slot.Row is not null && !(slot.Unwritten && !unit.InMemory)) return;
        if (_birthComparison != slot.Comparison) (_birthComparison, _previousBirths) = (slot.Comparison, _aliasBirths);
        _aliasBirths++;
    }

    private void Enqueue(TurnOutcome unit, CancellationToken cancellationToken)
    {
        if (unit.Slot is { } slot && HasUnissuedPrivateKind(unit))
        {
            slot.RollbackTurn?.Invoke();
            var preserve = slot.Unwritten && slot.Row?.FoundationInlineBirth is not null;
            var park = ParkRefusedTurn(unit, slot, preserve, Kinds.UnissuedPrivateKind);
            ReleaseJobRestriction(unit);
            _queue.Add(park);
            return;
        }
        InvalidateAliasMisses(unit);
        if (Has(Flaw.DeliverBeforeCommit)) HandOver(unit, null, hold: true, cancellationToken);
        var writeFree = EndsInMemory(unit) || !unit.Durable;
        var release = ReplyRelease.BeforeCommit;
        if (writeFree)
            // Capture the actual birth profile or earlier in-memory turn before Apply clears the slot.
            release = unit.Spec?.Ephemeral == true || unit.Slot is { Unwritten: true }
                ? ReplyRelease.EphemeralWriteFree : ReplyRelease.WriteFree;
        if (Has(Flaw.EmitBeforeCommit)) Release(unit, release);
        else Release(unit, release, early: true);    // callers whose route opted out of waiting
        ReleaseExplicit(unit, early: true);
        if (writeFree) Apply(unit, null, true, cancellationToken, release);
        else
        {
            StageHeldJobResults(unit);
            Promote(unit);
            _queue.Add(unit);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => FlushAsync(false, cancellationToken);

    /// <summary>Force a fenced flush even when idle. The node heartbeat handles renewal.</summary>
    public Task BeatAsync(CancellationToken cancellationToken = default) => FlushAsync(true, cancellationToken);

    private async Task FlushAsync(bool beat, CancellationToken cancellationToken)
    {
        if (_flushing || !Owns) return;
        var withdrawalGeneration = _generation;
        var now = ReceiveJobPublications(cancellationToken);
        if (FinishedJobWithdrawals(now) is { Count: > 0 } cancelled)
        {
            if (withdrawalGeneration != _generation) return;
            _flushing = true;
            var cleared = await CommitAsync([], [], [], withdrawalGeneration, cancellationToken, clearedJobs: cancelled);
            if (cleared == Outcome.Refused && withdrawalGeneration == _generation) Reset();
            if (withdrawalGeneration == _generation) _flushing = false;
            return;
        }
        lock (_turnSync)
        {
            foreach (var (id, slot) in Outgrown().ToList())
            {
                var running = slot.Driving;
                if (running)
                {
                    slot.TurnFence++;
                    slot.UndoTurn?.Invoke();
                    slot.Driving = false;
                    slot.EnteredTurn = null;
                    // The checkpoint can promote while the rejected continuation exits. No second public turn enters meanwhile.
                    slot.DetachedAuthoredTurn = slot.Row?.FoundationInlineBirth is not null;
                }
                slot.Busy = true;
                var unit = Bound(_type.Write(slot.Row!, running ? null : slot.State, slot.Pending), slot);
                Promote(unit);
                _queue.Add(unit);
            }
        }
        var units = _queue.GetRange(0, Math.Min(_queue.Count, _options.MaxBatch));     // rows per flush are capped
        var changing = units.Exists(unit => unit.FoundationRecoveryPage is not null || unit.FoundationRecoveryTimers is { Rearms.Count: > 0 } ||
            unit.FoundationRecoveryJobs is { DeadlineRearms.Count: > 0 })
            ? units.SelectMany(unit => (unit.FoundationRecoveryPage?.Writes().Select(row => row.Cursor.Key) ?? [])
                .Concat(unit.FoundationRecoveryTimers?.Rearms.Select(rearm => rearm.Envelope.InboxKey) ?? [])
                .Concat(unit.FoundationRecoveryJobs?.DeadlineRearms.Select(rearm => rearm.Envelope.InboxKey) ?? [])).ToHashSet()
            : null;
        var markable = changing is null ? _items.Values.AsEnumerable()
            : _items.Values.Where(item => !changing.Contains(item.Message.InboxKey));
        var cascading = units.Where(unit => unit.FoundationCascade is not null).Select(unit => unit.Instance).ToHashSet(StringComparer.Ordinal);
        bool SettlingCascade(Delivery item) => (item.IsJobWork || item.Message.Kind == Kinds.JobDeadline) &&
            cascading.Contains(Canonical(item.Message.Target.Id));
        var marks = markable.Where(item => !SettlingCascade(item) && Unmarked(item)).Take(_options.MaxBatch).ToList();
        var trial = units.Count == _queue.Count ? NextTrial() : [];
        if (changing is not null) trial.RemoveAll(item => changing.Contains(item.Message.InboxKey));    // a trial starts only when this commit leaves nothing else in flight
        if (cascading.Count != 0) trial.RemoveAll(SettlingCascade);
        if (units.Count == 0 && marks.Count == 0 && trial.Count == 0 && !beat) return;
        if (trial.Count > 0 && !Has(Flaw.BumpAllInherited))
        {
            // Another partition can select a trial since NextTrial inspected the gate.
            if (_gate.TryBegin(this, trial)) _trialObserved = _observed;
            else if (units.Count == 0 && marks.Count == 0 && !beat) return;
            else trial = [];
        }
        _queue.RemoveRange(0, units.Count);
        var generation = _generation;
        _flushing = true;
        var outcome = await CommitAsync(units, marks, trial, generation, cancellationToken, beat);
        if (outcome == Outcome.Refused)
        {
            // A definite error: no reset. The batch's instances are committed one by one and the one that fails is parked.
            outcome = units.Count == 0 ? Outcome.Refused : Outcome.Landed;
            for (var i = 0; outcome == Outcome.Landed && i < units.Count; i++)
            {
                var unit = units[i];
                // The marks ride with the first of these commits that lands.
                outcome = await CommitAsync([unit], marks, trial, generation, cancellationToken);
                if (outcome == Outcome.Refused && unit.Slot is { CurrentRow.ParkRecovery: not null } recovering &&
                    unit.Row is { ParkRecovery: null, ParkDiscard.Kind: ParkClearKind.Terminate } &&
                    unit.Consumed is [var terminate] && terminate.Message.Kind == Kinds.Terminate)
                {
                    // A refused Open leaves the committed recovery and its jobs intact. Parent commands
                    // must remain pending for the next take so the parent's cascade can still reach this actor.
                    if (terminate.Message.Operator is null) break;
                    var refusal = CreationProtocol.Refuse(unit.Instance, recovering.CurrentRow, unit.Consumed, "ResolutionUnavailable");
                    Bound(refusal, recovering);
                    ReleaseJobRestriction(unit);
                    outcome = await CommitAsync([refusal], marks, trial, generation, cancellationToken);
                }
                else if (outcome == Outcome.Refused && unit.FoundationCascade?.Input is null &&
                    (unit.FoundationCascade is null || !unit.CreationRefused) && unit.Slot is { } slot)
                {
                    slot.RollbackTurn?.Invoke();
                    var preserve = slot.Unwritten && slot.Row?.FoundationInlineBirth is not null;
                    var park = ParkRefusedTurn(unit, slot, preserve);
                    ReleaseJobRestriction(unit); // the replacement park already owns its restriction
                    outcome = await CommitAsync([park], marks, trial, generation, cancellationToken);
                    if (outcome == Outcome.Refused && preserve)
                    {
                        // The last accepted in-memory checkpoint itself could not be written.
                        var fallback = ParkRefusedTurn(unit, slot, preserve: false);
                        ReleaseJobRestriction(park);
                        outcome = await CommitAsync([fallback], marks, trial, generation, cancellationToken);
                    }
                }
                if (outcome == Outcome.Landed) (marks, trial) = ([], []);
            }
            if (outcome == Outcome.Refused && generation == _generation) Reset();   // nothing left to set apart
        }
        if (generation == _generation) _flushing = false;
    }

    private TurnOutcome ParkRefusedTurn(TurnOutcome unit, Slot slot, bool preserve, string? fault = null)
    {
        var previous = slot.CurrentRow;
        var unwritten = slot.Unwritten;
        if (unwritten && !preserve && previous is not null)
            previous = previous with
            {
                State = "null",
                FoundationNullState = previous.FoundationInlineBirth is not null || previous.FoundationNullState
            };
        var error = fault ?? _refusal;
        var park = unwritten
            ? _type.Park(previous, unit.Instance, unit.Consumed, unit.Spec, error, slot.Pending)
            : _type.Park(previous, unit.Instance, unit.Consumed.Concat(unit.Held).OrderBy(i => i.InboxOrder).ToArray(), unit.Spec, error);
        Bound(MarkPark(park, slot.CurrentRow), slot);
        if (slot.Unwritten) RetireVolatileJobs(park);
        Promote(park);
        return park;
    }

    private static TurnOutcome MarkPark(TurnOutcome unit, InstanceRow? previous)
    {
        unit.OpensPark = unit.Row is { Status: InstanceStatus.Faulted } && previous is null or { Status: InstanceStatus.Running, ParkRecovery: null };
        return unit;
    }

    private async Task<Outcome> CommitAsync(List<TurnOutcome> units, List<Delivery> marks, List<Delivery> trial, long generation,
        CancellationToken cancellationToken, bool beat = false, IReadOnlyList<Delivery>? clearedJobs = null,
        IReadOnlyList<PartitionFence>? pressure = null)
    {
        if (units.Exists(HasUnissuedPrivateKind))
        {
            _refusal = Kinds.UnissuedPrivateKind;
            return Outcome.Refused;
        }
        // Finding F1: until the owner has caught up with the rows it inherited, what it sends to its own partition goes
        // through the table like everything else. Handing it over in memory would let it overtake older rows.
        var handOver = clearedJobs is null && (_caughtUp || Has(Flaw.HandOverWhileBehind));
        // A behind-poll promotion can become durable and unmarked before its acknowledgement arrives.
        // Publish admission for exactly these tickets before this attempt can touch the store. A definite
        // refusal is retried by CommitAsync with its own handover decision, or its tickets are retired by Park.
        foreach (var unit in units)
            foreach (var key in unit.Promoted)
                if (_items.TryGetValue(key, out var ticket) && ticket.Key is not null)
                    ticket.AwaitingJobMark = !handOver && !Has(Flaw.RunBeforeMark);
        var turns = units.Select(PrepareCommit).ToList();
        if (turns.Exists(turn => turn.FoundationReleasedHolds is { Count: > 0 }))
        {
            var removed = turns.SelectMany(turn => turn.Consumed.Concat(turn.Done)).ToHashSet();
            for (var index = 0; index < turns.Count; index++)
                if (turns[index] is { FoundationReleasedHolds: { Count: > 0 } released } turn)
                    turns[index] = turn with { FoundationReleasedHolds = released.Where(id => !removed.Contains(id)).ToArray() };
        }
        List<InboxKey> seen = [.. marks.Select(i => i.Message.InboxKey)], tried = [.. trial.Select(i => i.Message.InboxKey)];
        // A store without owned sets gets only today's positional batch, one partition per batch (a worker of several
        // partitions cannot take on it). With owned sets, one partition held is that same batch, and several are one
        // owned batch over the partitions it touches.
        CommitBatch batch;
        lock (_turnSync)
        {
            if (generation != _generation) return Outcome.Lost;     // a stand-down reset this memory meanwhile
            if (pressure is not null)
            {
                var current = pressure.Where(fence => _held.TryGetValue(fence.Partition, out var held) &&
                    held.Epoch == fence.Epoch && held.Position == fence.Position).ToList();
                if (current.Count == 0) return Outcome.Landed;
                batch = CommitBatch.Owned(current, _options.Lease, false, turns, seen, tried);
            }
            else if (!_store.OwnedSetsAvailable || _held.Count == 1)
            {
                if (_held.Count != 1) throw new InvalidOperationException("A store without owned sets serves one partition per worker.");
                var (only, held) = _held.First();
                batch = new CommitBatch(only, held.Epoch, held.Position, _options.Lease, handOver, turns, seen, tried);
            }
            else batch = CommitBatch.Owned(Touched(units, marks.Concat(trial).Concat(clearedJobs ?? []), handOver, beat), _options.Lease, handOver, turns, seen, tried);
            if (_backlog?.ForCommit(batch.Fenced) is { } estimates) batch = batch with { DueBacklog = estimates };
            if (clearedJobs is not null) batch = batch with { ClearJobTrials = [.. clearedJobs.Select(ticket => ticket.Message.InboxKey)] };
            if (trial is [var ticket] && ticket.Message.FoundationPublish is not null)
                batch = batch with { PublicationReservation = new(ticket.Message.InboxKey, ticket.Seq, ticket.Attempts) };
        }
        foreach (var unit in units)
            if (unit.Window is { } window)
                batch = batch with { Window = batch.Window?.Intersect(window) ?? window };
        if (_options.OnPreparedCheckpoint is { } prepared)
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit.Row is null && !unit.Delete) continue;
                var lifecycleInput = unit.Delete ? unit.Consumed.Find(d => d.Message.Kind == Kinds.Delete)?.Message :
                    unit.Spec is not null ? unit.Slot?.Starting : unit.Slot?.UnwrittenCreator;
                prepared(batch.Turns[i], unit.State ?? unit.Slot?.State, lifecycleInput);
            }
        if (_lifecycle is not null)
            for (var i = 0; i < units.Count; i++) _lifecycle.Prepare(batch.Turns[i], units[i]);
        if (batch.Window is not null && !_store.SupportsCommitWindow)
        {
            Reset();
            throw new NotSupportedException("This store cannot commit decisions conditional on database time.");
        }
        while (true)
        {
            if (!LeaseUsable()) return Outcome.Lost;
            List<int> journaled = [];
            Committed result;
            try
            {
                StoreBatchRules.Validate(batch, ownedSets: _store.OwnedSetsAvailable, supportsJobTrialClear: _store.SupportsJobTrialClear,
                    supportsFoundationParkResolution: true, supportsFoundationValues: true, supportsDueBacklog: _backlog is not null, supportsFoundationSelfRecalls: true);
                StoreBatchRules.RequirePhysicalCapability(batch,_store.PhysicalRetirementAvailable);
                if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(batch, physicalRetirement: _store.PhysicalRetirementAvailable);
                if (!CommitClockAvailable) CommitClockFacts.RejectUnsupported(batch);
                if (units is [{ FoundationCatchUpBudget: { } budget }])
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    try
                    {
                        result = await _store.CommitAsync(batch, attempt.Token).AsTask().WaitAsync(budget, cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        attempt.Cancel();
                        // The counted write races the old request on the exact pending row and attempt.
                        // A landed page removed that row. A timeout won by the counter invalidates the late write.
                        return Outcome.Refused;
                    }
                }
                else result = await _store.CommitAsync(batch, cancellationToken);
            }
            catch (StoreException e) when (e.Failure != Failure.Unknown)
            {
                if (generation != _generation) return Outcome.Lost;
                if (e.Failure == Failure.WindowExpired)
                {
                    // Reread durable deliveries under a fresh take. RPC callers retry their unanswered requests.
                    // The entire batch was rolled back; no stale in-memory decision is applied or parked.
                    Reset();
                    return Outcome.Lost;
                }
                if (e.Failure == Failure.Transient && !units.Exists(unit => unit.FoundationCatchUpStep is not null)) continue;
                _refusal = e.Message;
                return Outcome.Refused;
            }
            catch (Exception)
            {
                // Unknown outcome: the commit may have landed, so its journal may have moved. A spurious round costs two reads.
                if (batch.Fenced.Where(f => batch.PositionsIn(f.Partition) > 0).Select(f => f.Partition).ToList() is { Count: > 0 } doubt) _options.OnJournal?.Invoke(doubt);
                await FailAsync(units, generation, cancellationToken);     // unknown outcome
                return Outcome.Lost;
            }
            if (generation != _generation) return Outcome.Lost; // reset while the commit was in flight: its result is nobody's
            if (!result.Landed)
            {
                await FailAsync(units, generation, cancellationToken);      // the fence matched nothing
                return Outcome.Lost;
            }
            lock (_turnSync)
            {
                // A stand-down on another thread may have reset since the check above: the landed result is then nobody's.
                if (generation != _generation) return Outcome.Lost;
                foreach (var fence in batch.Fenced)
                {
                    var moved = _held[fence.Partition];
                    if (batch.PositionsIn(fence.Partition) > 0) journaled.Add(fence.Partition);
                    moved.Position += batch.PositionsIn(fence.Partition);
                }
                Observe(result.Now);
                _backlog?.Landed(batch, _options.Monotonic());
            }
            if (journaled.Count > 0) _options.OnJournal?.Invoke(journaled);
            if (!LeaseUsable()) return Outcome.Lost; // a late acknowledgement cannot revive the old local deadline
            if (clearedJobs is not null)
            {
                foreach (var ticket in clearedJobs)
                {
                    ticket.Suspect = ticket.Again = true;
                    ticket.Marked = ticket.Shelved = ticket.JobEntered = ticket.WithdrawingJob = ticket.RetainsTrialGate = false;
                    ticket.Cancel?.Dispose();
                    ticket.Cancel = null;
                    ticket.JobDriver = null;
                    _gate.Finished(this, ticket);
                }
                _woken = true;
            }
            foreach (var item in marks.Concat(trial))
                if (item.Suspect && !trial.Contains(item)) item.Shelved = true;
                else
                {
                    item.Marked = true;
                    item.AwaitingJobMark = false;
                }
            var seqs = new Queue<long>(result.Seqs);
            var fenced = batch.Fenced.Select(f => f.Partition).ToHashSet();
            foreach (var unit in units)
            {
                if (unit.Row is { } row) unit.Row = CommitClockFacts.Stamp(row, result.Now);
                if (result.CascadeRemovals?.GetValueOrDefault(unit.Instance) is { } removed)
                {
                    unit.Cancelled.AddRange(removed);
                    _woken = true;
                }
                Apply(unit, seqs, handOver, cancellationToken, ReplyRelease.AfterCommit, fenced);
                if (!LeaseUsable()) return Outcome.Lost;
            }
            if (result.ParkExpiries.Count > 0)
            {
                lock (_turnSync)
                {
                    if (generation != _generation || !LeaseUsable()) return Outcome.Lost;
                    foreach (var expiry in result.ParkExpiries)
                    {
                        var timer = expiry.Message;
                        var partition = timer.Target.Partition;
                        if (!_held.TryGetValue(partition, out var ownership)) continue;
                        if (handOver && expiry.Seen == ownership.Epoch)
                            Hold(timer, expiry.Seq, durable: true, marked: true, cancellationToken: cancellationToken);
                        else
                        {
                            if (timer.Due <= Now + (long)_options.Horizon.TotalMilliseconds)
                                _behind[timer.InboxKey] = partition;
                            _caughtUp = false;
                            _woken = true;
                        }
                    }
                }
            }
            foreach (var item in marks.Concat(trial))
                if (item.Key is not null && _items.ContainsKey(item.Message.InboxKey)) StartJob(item, cancellationToken);
            if (!LeaseUsable()) return Outcome.Lost;
            return Outcome.Landed;
        }
    }

    private static bool HasUnissuedPrivateKind(TurnOutcome unit) =>
        unit.Sent.Exists(Kinds.IsUnissuedPrivate);

    private TurnCommit PrepareCommit(TurnOutcome unit)
    {
        var conversions = unit.HeldJobResults;
        var kept = PrepareKeptInputs(unit);
        var durable = unit.Consumed.Where(i => i.Durable);
        bool Retained(Delivery item) => item.Message.KeepUntil is not null || kept?.ContainsKey(item.Message.InboxKey) == true ||
            (unit.FoundationPublicationSequence is not null || item.Message.ParkCatchUp is { Starts: true }) && unit.FoundationCatchUpFailed is { } failed &&
            failed.Attempts >= failed.Limit && failed.Key == item.Message.InboxKey;
        var consumed = durable.Where(i => !Retained(i)).Select(i => i.Message.InboxKey);
        var done = durable.Where(Retained).Select(i => i.Message.InboxKey);
        var settled = conversions?.Keys.Where(i => i.Durable).Select(i => i.Message.InboxKey) ?? [];
        var cancelled = Has(Flaw.VolatileJobTicket) ? [] : unit.Cancelled.Where(id => !InMemoryOnly(id) && unit.FoundationSelfRecalls?.Contains(id) != true);
        var promotions = conversions?.Values.Select(result => result.InboxKey).ToHashSet();
        if (unit.Row?.FoundationInlineBirth is not null && unit.Held.Count > 0)
        {
            promotions ??= [];
            promotions.UnionWith(unit.Held.Where(i => !i.Durable && conversions?.ContainsKey(i) != true).Select(i => i.Message.InboxKey));
        }
        var releases = unit.Row is { } row && _type.SupportsFoundationOperator(row) && unit.Consumed is [var stimulus] &&
            _type.RevisitsHeld(row.Model, row, stimulus)
            ? unit.Slot!.Inbox.Where(i => i.Held && i.Durable && !i.Taken && i.InboxOrder < stimulus.InboxOrder).Select(i => i.Message.InboxKey).ToArray()
            : null;
        return new TurnCommit(unit.Instance, unit.Row, unit.Expected,
            Has(Flaw.KeepConsumedMessages) ? [] : [.. consumed, .. settled, .. cancelled],
            Has(Flaw.KeepConsumedMessages) ? [] : done.ToList(),
            unit.Sent.Where(m => !unit.Volatile.Contains(m.InboxKey)).ToList(), unit.Subscribed, unit.Unsubscribed, unit.Journal, unit.Unsubscribe,
            unit.Held.Where(i => i.Durable && conversions?.ContainsKey(i) != true).Select(i => i.Message.InboxKey).ToList(), unit.Delete)
        {
            FoundationHeader = unit.FoundationHeader,
            FoundationRootOperator = unit.Row?.FoundationDispatchHeader is not null && unit.Consumed is [var qualified] &&
                qualified.Message.Operator is not null ? qualified.Message : null,
            FoundationRootJobCompletion = unit.Row?.FoundationDispatchHeader is not null &&
                unit.Consumed is [{ IsWorkerJobCompletion: true, Original: { } original } completed] &&
                (completed.JobDeadline is null || _type.RootTimedJobPublicationClock is not null)
                    ? new((original with { }).AsKernelTicket(), (completed.Message with { }).AsKernelTicket(),
                        completed.JobDeadline is null ? null : completed.CompletedAt) : null,
            FoundationSelfRecalls = unit.FoundationSelfRecalls,
            FoundationValues = unit.FoundationValues,
            FoundationHeldPromotions = promotions,
            FoundationReleasedHolds = releases,
            OpensPark = unit.OpensPark,
            FoundationKept = kept,
            FoundationParkClear = unit.FoundationParkClear,
            FoundationRecoveryPage = unit.FoundationRecoveryPage,
            FoundationRetry = unit.FoundationRetry,
            FoundationRecoveryTimers = unit.FoundationRecoveryTimers,
            FoundationRecoveryJobs = unit.FoundationRecoveryJobs,
            FoundationRecoverySubscriptions = unit.FoundationRecoverySubscriptions,
            FoundationCatchUpStep = unit.FoundationCatchUpStep,
            FoundationCatchUpBefore = unit.FoundationCatchUpBefore,
            FoundationCatchUpExpected = unit.FoundationCatchUpExpected,
            FoundationCatchUpFailed = unit.FoundationCatchUpFailed,
            FoundationPublicationSequence = unit.FoundationPublicationSequence,
            FoundationCascade = unit.FoundationCascade
        };
    }

    private Dictionary<InboxKey, ParkKeepReason>? PrepareKeptInputs(TurnOutcome unit)
    {
        if (unit.FoundationKeep is not { } reason || unit.Row is not { } row || Has(Flaw.KeepConsumedMessages)) return null;
        return unit.Consumed.Where(item => item.Durable && !item.DiscardedInput &&
            item.Message.Target.Id == row.Id && item.Message.TargetCreation == row.Creation)
            .ToDictionary(item => item.Message.InboxKey, _ => reason);
    }

    /// <summary>
    /// The fences of an owned batch: the partitions of its turns, of what they consume, of its marks, and, when it hands
    /// over, of the held partitions it sends to (the store writes those rows seen at that partition's epoch). A beat, or
    /// a batch that touches nothing, fences every partition held.
    /// </summary>
    private List<PartitionFence> Touched(List<TurnOutcome> units, IEnumerable<Delivery> marks, bool handOver, bool beat)
    {
        var touched = new SortedSet<int>();
        foreach (var unit in units)
        {
            if (StoreBatchRules.AddressPartition(unit.Instance) is { } partition) touched.Add(partition);
            foreach (var item in unit.Consumed) touched.Add(item.Message.Target.Partition);
            if (handOver) touched.UnionWith(unit.Sent.Select(m => m.Target.Partition).Where(Holds));
        }
        touched.UnionWith(marks.Select(i => i.Message.Target.Partition));
        if (beat || touched.Count == 0) touched.UnionWith(_held.Keys);
        return [.. touched.Where(Holds).Select(p => new PartitionFence(p, _held[p].Epoch, _held[p].Position))];
    }

    /// <summary>A message held in memory and never written: one that needs no delete when it is cancelled.</summary>
    internal Dictionary<InboxKey, Message> CaptureVolatile(string actor)
    {
        lock (_turnSync)
            return _items.Values.Where(item => !item.Durable && item.Message.Target.Id == actor)
                .ToDictionary(item => item.Message.InboxKey, item => item.Original ?? item.Message);
    }

    private bool InMemoryOnly(InboxKey id) => _items.TryGetValue(id, out var held) && !held.Durable;

    // A never-written parked instance has no durable jobs to resume. Retire its volatile tickets atomically with
    // the faulted row so a new owner does not launch fresh work for a run that was already parked.
    private void RetireVolatileJobs(TurnOutcome unit)
    {
        if (unit.Row is not { } row) return;
        var tickets = _items.Values.Where(i => !i.Durable && i.Key is not null && Canonical(i.Message.Target.Id) == unit.Instance).ToList();
        var keys = tickets.Select(i => new JobRegistration(i.Key!, i.Message.JobGeneration)).ToHashSet();
        if (row.Jobs is { } jobs)
        {
            var remaining = JobRegistry.Read(jobs).Where(job => !keys.Contains(job)).ToList();
            unit.Row = row with { Jobs = JobRegistry.Write(remaining) };
        }
        foreach (var ticket in tickets)
        {
            Cancel(ticket.Message.InboxKey);
            if (row.FoundationInlineBirth is not null && (ticket.Original ?? ticket.Message).Read<JobTicket>().Timer is { } timer)
            {
                var deadline = InboxKey.Issued(timer);
                if (InMemoryOnly(deadline)) Cancel(deadline);
            }
        }

        void Cancel(InboxKey key)
        {
            if (!unit.Cancelled.Contains(key)) unit.Cancelled.Add(key);
            Cancelling(key);
        }
    }

    /// <summary>Decision 4: instances in memory whose short wait is over, and that no turn or commit has now.</summary>
    private IEnumerable<KeyValuePair<string, Slot>> Outgrown()
    {
        var now = Now;
        return _slots.Where(s => s.Value.Unwritten && (!s.Value.Busy || s.Value.Driving) && now >= s.Value.Until);
    }

    private void StageHeldJobResults(TurnOutcome unit)
    {
        if (unit.Held.Count == 0 || !_type.SupportsFoundationOperator(unit.Row) || Has(Flaw.HoldRetainsJobTicket)) return;
        // Sequences in this commit increase. Persist this cohort in reverse so newest-first replay keeps the order offered to the actor.
        for (var index = unit.Held.Count - 1; index >= 0; index--)
        {
            var item = unit.Held[index];
            if (!unit.ConvertsHeldJobResult(item)) continue;
            var message = item.Message with { Id = item.Message.Id + "/result", FoundationJobResult = true, FoundationHeldJobResult = true };
            (unit.HeldJobResults ??= []).Add(item, message);
            unit.Sent.Add(message);
            unit.Promoted.Add(message.InboxKey);
        }
    }

    /// <summary>
    /// Decision 4: an outcome that writes an instance held in memory until now writes what it waits for in memory too:
    /// its timers and job tickets become rows in the same commit, so a crash after it loses none of them.
    /// </summary>
    private void Promote(TurnOutcome unit)
    {
        if (unit.Slot is not { Unwritten: true } && unit.Row?.FoundationInlineBirth is null) return;
        // Whoever wrote the outcome (a turn, a command, a park) saw the row in memory: it is not in the table, so it is
        // inserted, not updated at its revision.
        if (unit.Row is not null) unit.Expected = null;
        if (Has(Flaw.ForgetShortWaits)) return;
        // Public results held by the turn already have their settled row staged above. Legacy jobs retain their F7 ticket.
        var prior = _items.Values.Where(i => !i.Durable && Canonical(i.Message.Target.Id) == unit.Instance && !unit.Consumed.Contains(i)
                     && !unit.Cancelled.Contains(i.Message.InboxKey) && unit.HeldJobResults?.ContainsKey(i) != true)
            .OrderBy(i => i.InboxOrder).ThenByDescending(i => i.CompletedOrder).ToList();
        foreach (var item in prior)
        {
            unit.Promoted.Add(item.Message.InboxKey);
        }
        unit.Sent.InsertRange(0, prior.Select(item => item.Original ?? item.Message));
    }

    /// <summary>Decision 4: an instance kept in memory ended there, and its turn leaves nothing that is a row.</summary>
    private bool EndsInMemory(TurnOutcome unit) =>
        unit.Slot is { Unwritten: true } && unit.Window is null && !unit.Replied.Exists(r => r.Rpc) && !unit.InMemory && unit.Row is null && unit.Journal is null && !unit.Delete
        && !unit.Consumed.Exists(i => i.Durable) && unit.Sent.TrueForAll(m => unit.Volatile.Contains(m.InboxKey)) && unit.Cancelled.TrueForAll(InMemoryOnly);

    private void ForgetInMemory(string instance)
    {
        foreach (var item in _items.Values.Where(i => !i.Durable && Canonical(i.Message.Target.Id) == instance).ToList()) Forget(item);   // its timers go, its jobs are cancelled
    }

    /// <summary>
    /// A refused fence or an unknown outcome: reset. Over several partitions, the worker then takes back the ones still
    /// its own (<see cref="RetakeAsync"/>), unless it is standing down. A refused fence does not say which row was
    /// stale, and a new epoch also fences out a commit whose answer was lost and that lands later.
    /// </summary>
    private async Task FailAsync(List<TurnOutcome> units, long generation, CancellationToken cancellationToken)
    {
        IReadOnlyList<PartitionFence> held;
        bool retake;
        // One critical section: a stand-down on another thread (the host's DeactivateAsync, DisposeAsync) takes the same
        // lock in ReleaseAsync, so it sees this worker owning, or retaking, never neither; otherwise the retake would install
        // ownership on fresh leases after the stand-down had returned.
        lock (_turnSync)
        {
            if (generation != _generation) return;      // a stand-down or a take reset this memory meanwhile
            (held, var releasing) = (Fences, _releasing);
            Fail(units);
            retake = held.Count > 1 && !releasing && !Has(Flaw.NoResetAfterFailedCommit);
            if (retake) (_retaking, _retakeStoodDown, _retakeAt) = (true, false, _options.Monotonic());
            if (retake) _retakeSet.UnionWith(held.Select(f => f.Partition));
        }
        if (retake) await RetakeAsync(held, cancellationToken);
    }

    private void Fail(List<TurnOutcome> units)
    {
        if (!Has(Flaw.NoResetAfterFailedCommit))
        {
            Reset();
            return;
        }
        foreach (var unit in units)     // MUTATION: carry on with what is in memory
            if (unit.Slot is { } slot) slot.Busy = false;
    }

    /// <summary>The commit landed (or there was nothing to write): now, and only now, memory follows.</summary>
    private void Apply(TurnOutcome unit, Queue<long>? seqs, bool handOver, CancellationToken cancellationToken, ReplyRelease release, IReadOnlySet<int>? fenced = null)
    {
        if (unit.Row is { } ending && unit.FoundationCascade is { Input: null } cascade)
            foreach (var job in cascade.Entries.Jobs)
                if (_items.GetValueOrDefault(InboxKey.Ticket(ending.Id, job.Key, ending.Creation, job.Generation)) is { } ticket)
                    CancelJobDriver(ticket);
        if (unit.CreationRefused && release is ReplyRelease.WriteFree or ReplyRelease.EphemeralWriteFree)
            _lifecycle?.WriteFreeRefusal(unit.Consumed);
        // A command can mature while the last recovery checkpoint awaits acknowledgement. Capture
        // these boundaries before removing the selected member, exhausted error or previous command.
        if (unit.Slot is { } pending && (OnTrial(unit.Instance) || unit.Consumed.Exists(i => i.RecoveryOrdering)
            || unit.Held.Exists(i => i.RecoveryOrdering)))
            RecoveryBoundary(pending, Now, recovering: true);
        foreach (var item in unit.Consumed)
        {
            _behind.Remove(item.Message.InboxKey);
            Forget(item);
            if (_slots.TryGetValue(Canonical(item.Message.Target.Id), out var empty) && DiscardedAt(item, empty) &&
                empty.Row is null && empty.Loaded && !empty.Busy && empty.Inbox.Count == 0 && empty.Replies.Count == 0)
                RemoveSlot(Canonical(item.Message.Target.Id));
        }
        foreach (var item in unit.Held)     // back in its place; its turn ran, so it is no suspect and no longer on trial
        {
            (item.Taken, item.Held, item.Suspect, item.Marked, item.HeldHere) = (false, true, false, true, true);
            _gate.Finished(this, item);
            if (unit.HeldJobResults?.GetValueOrDefault(item) is { } result)
            {
                var ticket = item.Message.InboxKey;
                _items.Remove(ticket);
                _behind.Remove(ticket);
                if (item.Durable && _polling && !Has(Flaw.PollRace)) _consumed.Add(ticket);
                item.Cancel?.Dispose();
                (item.Message, item.Original, item.Key, item.Cancel) = (result, null, null, null);
                _items.Add(result.InboxKey, item);
            }
            Insert(unit.Slot!, item);
        }
        foreach (var id in unit.Cancelled)      // a cancelled job's ticket goes whether or not this owner had taken it into memory yet
        {
            _cancelling.Remove(id);
            _behind.Remove(id);
            if (_items.TryGetValue(id, out var ticket)) Forget(ticket);
            else if (_polling && !Has(Flaw.PollRace)) _consumed.Add(id);
        }
        if (unit.FoundationRecoveryPage is { } page)
        {
            foreach (var row in page.Writes())
            {
                var key = row.Cursor.Key;
                if (_items.TryGetValue(key, out var cached))
                {
                    if (cached.HeldHere && row is { Reason: null, ReleasedBy: not null } && !page.TerminalInputs &&
                        unit.Row is { Status: InstanceStatus.Running, ParkRecovery: { } drain } running && drain.Restores(row, running.Status))
                    {
                        // A committed model hold still belongs to this owner. Refresh its envelope
                        // and mark from the next poll without offering it again on its own.
                        cached.ReleasedBy = new(running.Creation, running.ParkResolutionGeneration);
                        (cached.Marked, cached.AwaitPoll) = (false, true);
                    }
                    else Forget(cached);
                }
                else if (_polling && !Has(Flaw.PollRace)) _consumed.Add(key);
            }
            _woken = true;
        }
        if (unit.FoundationRecoveryTimers is { Rearms.Count: > 0 } || unit.FoundationRecoveryJobs is { DeadlineRearms.Count: > 0 })
        {
            foreach (var rearm in (unit.FoundationRecoveryTimers?.Rearms ?? []).Concat(unit.FoundationRecoveryJobs?.DeadlineRearms ?? []))
            {
                var key = rearm.Envelope.InboxKey;
                if (_items.TryGetValue(key, out var cached)) Forget(cached);
                else if (_polling && !Has(Flaw.PollRace)) _consumed.Add(key);
            }
            _woken = true;
        }
        if (unit.FoundationRecoveryJobs is { } jobs)
        {
            foreach (var key in jobs.Pending)
            {
                if (!_items.TryGetValue(key, out var waiting) || waiting.Key is not null || waiting.Message.Kind != Kinds.Job ||
                    waiting.Attempts >= _options.MaxAttempts || waiting.Taken) continue;
                var ticket = waiting.Message.Read<JobTicket>();
                if (waiting.Cancel is { IsCancellationRequested: true } cancelled)
                {
                    cancelled.Dispose();
                    waiting.Cancel = null;
                }
                (waiting.Ticket, waiting.Key, waiting.JobDeadline) = (true, ticket.Key, ticket.Deadline);
                if (ticket.Deadline is not null) _timedJobs.Add(waiting);
            }
            if (jobs.Writes.Count > 0) _woken = true;
        }
        if (unit.FoundationCatchUpStep is not null || unit.Subscribed.Exists(wait => wait.FoundationKeepsEvents)) _woken = true;
        if (!handOver)
            foreach (var message in unit.Sent)
                if (Holds(message.Target.Partition) && message.Due is null && !unit.Volatile.Contains(message.InboxKey) && !unit.Promoted.Contains(message.InboxKey)) _behind[message.InboxKey] = message.Target.Partition;
        if (unit.Slot is { } committed)
        {
            InvalidateAliasMisses(unit);
            if (_loading && _store.HasActorAliases && (unit.Row is not null || unit.Delete || committed.Unwritten))
                committed.WrittenDuringAliasRead = _aliasRead;
            // Complete is itself the stimulus to drop a previous Park's locally held release.
            if (committed.Row is { ParkDiscard: not null } clearing && unit.Row is { ParkDiscard: null })
                foreach (var item in committed.Inbox)
                    if (item.Held && EarlierParkRelease(item, clearing))
                        (item.Held, item.HeldHere) = (false, false);
            if (unit.Row is not null)
                (committed.Row, committed.AbsentAt) = (unit.InMemory ? unit.Row : unit.Row.WithoutInlineBirth(), 0);
            if (unit.Delete && unit.Row is not { Retired: true } || committed.Unwritten && !unit.InMemory && unit.Row is null)
                (committed.Row, committed.AbsentAt) = (null, _store.HasActorAliases ? _aliasBirths : 0);
            // An unpersisted minted start may leave more inputs in the slot after its own
            // outcome. Its no-read privilege was for that birth, not a later ordinary input.
            else if (_store.HasActorAliases && committed.Row is null && committed.AbsentAt == 0) committed.AbsentAt = -1;
            PublishJobEligibility(committed);
            ReleaseJobRestriction(unit);
        }
        HandOver(unit, seqs, hold: handOver && !Has(Flaw.DeliverBeforeCommit), cancellationToken, fenced);
        if (unit.Slot is not { } slot) return;
        if (unit.InMemory)       // decision 4: kept in memory, not written
        {
            if (!slot.Unwritten && (_options.OnPreparedCheckpoint is not null || _lifecycle is not null)) slot.UnwrittenCreator = slot.Starting;
            (slot.Unwritten, slot.Until, slot.Pending) = (true, unit.Until, unit.Pending);
            _options.OnKeptInMemory?.Invoke(unit.Instance, unit.Revision);
        }
        else if (slot.Unwritten)
        {
            (slot.Unwritten, slot.Pending, slot.UnwrittenCreator) = (false, null, null);
            if (unit.Row is null)       // it ended in memory: never written at all, and what it still waited for goes with it
            {
                _options.OnLeftMemory?.Invoke(unit.Instance, unit.Status);
                slot.Row = null;
                ForgetInMemory(unit.Instance);
            }
        }
        if (slot.CurrentRow is not { Status: InstanceStatus.Running }) slot.Ambient = null;
        slot.State = unit.Row is { Status: InstanceStatus.Running } ? unit.State : null;
        slot.Busy = false;
        // Committed completion can be a new stimulus without requiring singleton selection. Revisit only after its
        // checkpoint lands; identity refusals have no row and must not activate that authored input.
        if (unit.Row is not null && unit.Consumed is [var completion] && _type.RevisitsHeld(unit.Row.Model, unit.Row, completion))
            foreach (var held in slot.Inbox.Where(i => i.Held && i.InboxOrder < completion.InboxOrder)) held.Held = false;
        if (!Has(Flaw.EmitBeforeCommit)) Release(unit, release);
        ReleaseExplicit(unit, early: false);
        // A load, poll or gate release can encounter any of this actor's jobs while its checkpoint is busy.
        foreach (var ticket in slot.Inbox.Where(i => i.Key is not null).ToArray()) StartJob(ticket, cancellationToken);
        if (slot.Row is null && slot.Inbox.Count == 0) RemoveSlot(unit.Instance);
        Evict();
    }

    private void ReleaseExplicit(TurnOutcome unit, bool early)
    {
        for (var i = 0; i < unit.Replied.Count; i++)
        {
            var reply = unit.Replied[i];
            if ((reply.FromFoundation && ReplyTo.Parse(reply.To).BeforeCommit) != early) continue;
            Send(reply.Route, $"{Origin(unit)}#{unit.Revision}.reply:{i}", reply.Channel, reply.Item, refusedInput: reply.RefusedInput);
        }
    }

    /// <summary>
    /// Evictable: its committed row is all there is to it. Not while a turn or commit is in flight (a load would read
    /// the row before that commit), not while messages wait for it, not while a caller waits for its output.
    /// </summary>
    private bool Evictable(Slot slot) => slot.Loaded && (!slot.Busy || Has(Flaw.EvictBusy)) && slot.Inbox.Count == 0 && slot.Replies.Count == 0 && !slot.Unwritten && !slot.DetachedAuthoredTurn;

    /// <summary>
    /// Decision 6: a caller's reply address expires at its deadline (the route's timeout). The owner then forgets it, so
    /// it no longer keeps the instance in memory, and sends it nothing more; the caller has had its timeout.
    /// </summary>
    private bool Expired(string to) => !Has(Flaw.KeepExpiredCallers) && ReplyTo.Parse(to).Deadline <= Now;

    private void Unpin()
    {
        foreach (var slot in _slots.Values) slot.Replies.RemoveWhere(route => Expired(route.Address));
    }

    private void Evict()
    {
        Unpin();
        var idle = _slots.Where(s => Evictable(s.Value)).ToList();
        foreach (var (id, _) in idle.OrderBy(s => s.Value.Used).Take(idle.Count - _options.MaxInstances).ToList())
        {
            RemoveSlot(id);
            Evicted++;
        }
    }

    /// <summary>
    /// Live output, released only now: the turn's commit has landed, or it had nothing to write. A message that carried a
    /// reply address joins the instance's callers; they get what the turn emitted, and the end when the instance ends.
    /// </summary>
    private void Release(TurnOutcome unit, ReplyRelease release, bool early = false)
    {
        if (unit.Slot is not { } slot) return;
        // An explicit winner also supersedes a route attached by an earlier turn of this actor.
        foreach (var reply in unit.Replied)
            if (reply.FromFoundation) slot.Replies.Remove(reply.Route);
        foreach (var item in unit.Consumed)
            if (item.ReplyRoute is { } route && !Expired(route.Address) && !unit.Replied.Any(r => r.Message == item.Message.InboxKey))
                slot.Replies.Add(route);
        var callers = slot.Replies.Where(route => Has(Flaw.EmitBeforeCommit) || ReplyTo.Parse(route.Address).BeforeCommit == early).ToList();
        for (var i = 0; i < unit.Emitted.Count; i++)
            foreach (var to in callers) Send(to, $"{Origin(unit)}#{unit.Revision}.{i}", unit.Emitted[i].Channel, unit.Emitted[i].Item, revision: unit.Revision, release: release);
        if (early || unit.Status == InstanceStatus.Running) return;
        foreach (var to in slot.Replies) Send(to, Origin(unit) + "#end", null, null, unit.Status, unit.Status is null ? "missing" : null);
        slot.Replies.Clear();
    }

    private static string Origin(TurnOutcome unit) => CreationProtocol.Origin(unit.Instance, (unit.Row ?? unit.Slot?.Row)?.Creation ?? 0);

    private void Send(LiveReplyRoute route, string key, string? channel, object? item, InstanceStatus? status = null, string? error = null, long revision = 0, ReplyRelease? release = null, Message? refusedInput = null)
    {
        if (Expired(route.Address)) return;        // decision 6: past its deadline the reply is dropped
        var (node, request, _, _) = ReplyTo.Parse(route.Address);
        if (_options.Transport is not { } transport) return;
        var reply = new Reply(request, key, channel, item, status, error) { Tenant = route.Tenant };
        try { transport.Send(node, reply); }
        catch (Exception) { return; } // Losing live output cannot abandon a prepared or landed commit.
        if (release is { } phase) _options.OnEmittedReply?.Invoke(node, reply, revision, phase);
        if (refusedInput is not null) _options.OnCreationRefused?.Invoke(node, reply, refusedInput);
    }

    /// <summary>For a recipient in the same partition the owner hands the message over in memory; its row says seen already.</summary>
    private void HandOver(TurnOutcome unit, Queue<long>? seqs, bool hold, CancellationToken cancellationToken, IReadOnlySet<int>? fenced = null)
    {
        foreach (var message in unit.Sent)
        {
            var durable = !unit.Volatile.Contains(message.InboxKey);
            // A durable send is handed over only into a partition the batch fenced, where the store wrote it seen at that
            // epoch. One into a partition gained after the batch was built (a gain while it was in flight) is an unseen row:
            // it waits for the poll, and until then this owner has not caught up (F1), so nothing overtakes it in memory.
            if (hold && durable && fenced is not null && !fenced.Contains(message.Target.Partition) && Holds(message.Target.Partition) && !unit.Promoted.Contains(message.InboxKey))
            {
                if (seqs is not null) seqs.Dequeue();
                // Tracked as the promoted path tracks it: a row the poll's horizon reads must be read before a catch-up.
                if (message.Due is null || message.Due <= Now + (long)_options.Horizon.TotalMilliseconds) _behind[message.InboxKey] = message.Target.Partition;
                _caughtUp = false;
                continue;
            }
            var seq = durable && seqs is not null ? seqs.Dequeue() : long.MaxValue / 2 + ++_local;
            if (unit.Promoted.Contains(message.InboxKey))     // held in memory already; from now on a row, seen at this epoch if handed over
            {
                if (_items.TryGetValue(message.InboxKey, out var held))
                {
                    held.Seq = seq;
                    if (!hold)
                    {
                        // Keep the existing job/result, but do not mark or deliver it until poll reaches its row.
                        if (message.Due is null || message.Due <= Now + (long)_options.Horizon.TotalMilliseconds) _behind[message.InboxKey] = message.Target.Partition;
                        (held.Durable, held.Marked, held.AwaitPoll, held.Taken) = (true, false, true, false);
                        if (_slots.TryGetValue(Canonical(message.Target.Id), out var waiting))
                        {
                            if (held.CompletedOrder == 0 || held.Message.FoundationHeldJobResult) waiting.Inbox.Remove(held);
                            if (!waiting.Inbox.Contains(held)) Insert(waiting, held);
                        }
                        continue;
                    }
                    (held.Durable, held.Marked) = (true, hold);
                    if (held.Taken) held.Taken = false;
                    if (_slots.TryGetValue(Canonical(message.Target.Id), out var slot))
                    {
                        // Equal-priority completed results were inserted at the head in completion order.
                        // Keep their existing positions: re-inserting equal sequence numbers reverses that order.
                        if (held.CompletedOrder == 0 || held.Message.FoundationHeldJobResult) slot.Inbox.Remove(held);
                        if (!slot.Inbox.Contains(held)) Insert(slot, held);
                    }
                    if (held.Ticket && held.Key is not null && held.Cancel is null) StartJob(held, cancellationToken);
                }
                continue;
            }
            if ((hold || !durable) && Holds(message.Target.Partition)) Hold(message, seq, durable, marked: true, fresh: unit.Fresh.Contains(message.InboxKey), cancellationToken: cancellationToken);
        }
    }

    private void Forget(Delivery item)
    {
        _timedJobs.Remove(item);
        _schedulerRetries.Remove(item);
        _items.Remove(item.Message.InboxKey);
        if (HasPhysicalTrial(item) && item.JobDriver?.IsCompleted == false) item.RetainsTrialGate = true;
        else _gate.Finished(this, item);
        if (item.Durable && _polling && !Has(Flaw.PollRace)) _consumed.Add(item.Message.InboxKey);
        if (item.Key is not null) CancelJobDriver(item);
        if (_slots.TryGetValue(Canonical(item.Message.Target.Id), out var slot))
        {
            lock (slot)
            {
                slot.Inbox.Remove(item);
                if (item.UncheckedReference is not null) { item.UncheckedReference = null; slot.PendingReferences--; }
                RefreshPoisonBoundary(slot);
            }
        }
        _meets.Remove(item);
        _exhaustedCatchUps.Remove(item);
    }

    private static void CancelJobDriver(Delivery ticket)
    {
        try { ticket.Cancel?.Cancel(); }
        catch (AggregateException) { } // Activity cleanup cannot interrupt ownership or checkpoint bookkeeping.
    }
}

/// <summary>
/// One per process. At most one of its workers has a trial in flight, and then the others start nothing. A death kills
/// every partition the process holds, so a trial in one of a worker's partitions holds back its other partitions too.
/// Within a worker, the trial filters of <c>ReadyForTurns</c> and <c>RunTurn</c> enforce that
/// (<see cref="Flaw.TrialSparesOtherPartitions"/> breaks them); <see cref="Flaw.TrialPerPartition"/> gives each worker its
/// own gate, which is per partition only while a worker owns one, as on a store without owned sets. A reset releases the
/// gate, so a trial in a partition the worker dropped gates nothing it took back.
/// </summary>
public sealed class TrialGate
{
    // Each selection owns its ordered array. Published arrays are never modified.
    private sealed record Selection(Worker Owner, Delivery[] Tickets);
    private Selection? _selection;

    internal bool Idle => Volatile.Read(ref _selection) is null;
    internal bool Blocks(Worker worker) => Volatile.Read(ref _selection) is { } active && active.Owner != worker;
    internal IReadOnlyList<Delivery> Selected(Worker worker) =>
        Volatile.Read(ref _selection) is { } active && active.Owner == worker ? active.Tickets : [];
    internal bool Allows(Worker worker, Delivery ticket) =>
        Volatile.Read(ref _selection) is not { } active || active.Owner == worker && active.Tickets.Contains(ticket);

    internal bool TryBegin(Worker worker, List<Delivery> tickets) =>
        Interlocked.CompareExchange(ref _selection, new(worker, tickets.ToArray()), null) is null;

    internal void Finished(Worker worker, Delivery ticket)
    {
        while (Volatile.Read(ref _selection) is { } active && active.Owner == worker && active.Tickets.Contains(ticket))
        {
            var remaining = active.Tickets.Where(t => t != ticket).ToArray();
            if (Interlocked.CompareExchange(ref _selection, remaining.Length == 0 ? null : new(worker, remaining), active) == active)
                return;
        }
    }

}

internal sealed class Slot
{
    public int PendingReferences;
    public Dictionary<string, bool>? SelfReferences; // exact provider equality only, held for the slot lifetime
    public HashSet<string>? Aliases; // resolved database spellings, retained only while this slot is cached
    public long PoisonAt = long.MaxValue; // owner-published due boundary; callbacks never scan the mutable inbox
    public int PendingJobRestrictions; // synchronized with physical driver entry by locking this slot
    public long RunnableCreation = -1; // checked at physical entry, including after waiting on a scheduler
    public bool? WithheldAtCheckpoint; // null if another actor's trial deferred publication
    public long ActiveCreation;    // volatile latch captured by background drivers; retired/missing is -1
    public InstanceRow? Row;        // committed actor row or retired lifecycle predecessor
    public InstanceRow? CurrentRow => Row is { Retired: false } row ? row : null;
    public Message? Starting;     // semantic creation envelope, including a failed initial turn; excludes transport retry identity
    public object? State;           // the live state between turns, equal to Row.State
    public bool Loaded, Busy, DeferredFresh, DetachedAuthoredTurn;
    public long WrittenDuringAliasRead; // current lookup whose snapshot this slot's commit can invalidate
    public long AbsentAt; // date of an alias-aware absence, whether observed or locally minted
    public long Comparison; // latest accepted provider lookup that included this canonical slot
    public bool Driving;            // a model turn, as distinct from a queued commit, is in flight
    public Task? EnteredTurn;        // completion queues the outcome for a graceful shutdown drain
    public long TurnFence;          // invalidates an uncommitted turn during graceful stand-down
    public Action? RollbackTurn;    // abandon a failed unit without restarting unrelated physical work
    public Action? UndoTurn;        // unwind only the invalidated turn's provisional cancellations
    public long Used;               // when a turn last started or it was loaded: the least recent idle one goes first
    public readonly List<Delivery> Inbox = [];
    public object? Ambient;                         // from the starter on this node; gone with this memory
    public readonly HashSet<LiveReplyRoute> Replies = [];   // callers waiting for what the instance emits
    public Message? UnwrittenCreator;                // optional observer: selected birth until its first physical disposition
    public bool Unwritten;                          // decision 4: Row is in memory only, never written
    public long Until;                              // and may stay so until then
    public object? Pending;                         // the history its turns in memory left, the actor type's
}

internal sealed record JobTicket(string Model, [property: System.Text.Json.Serialization.JsonConverter(typeof(DurableKeyJson))] string Key, string? Arguments,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? Deadline = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Timer = null);

internal sealed record JobDeadline([property: System.Text.Json.Serialization.JsonConverter(typeof(DurableKeyJson))] string Key);

internal sealed class JobContext(InstanceRef instance, string key, string? arguments, long creation, int attemptNumber, long? deadline) : IJob
{
    internal string? FoundationInlineTenant { get; init; }
    internal string? FoundationRootReference { get; init; }
    public InstanceRef Instance { get; } = instance;
    public string Key { get; } = key;
    public string? Arguments { get; } = arguments;
    public long Creation { get; } = creation;
    public int AttemptNumber { get; } = attemptNumber;
    public long? Deadline { get; } = deadline;

    private string? _attemptId;
    public string AttemptId => LazyInitializer.EnsureInitialized(ref _attemptId, static () => Guid.NewGuid().ToString("N"));
}
