using Kernel.Runtime;

namespace Kernel.Durable;

public sealed record WorkerOptions
{
    /// <summary>The owner's default of 12 November: 10 s. A takeover waits for it to run out, so failover takes about one lease.</summary>
    public TimeSpan Lease { get; init; } = Defaults.Lease;
    public TimeSpan Horizon { get; init; } = TimeSpan.FromSeconds(10);
    public int PollLimit { get; init; } = 500;
    public int MaxBatch { get; init; } = 500;

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
    /// simulation needs. A host that runs a worker's passes on a scheduler of its own (RuntimeNode, an Orleans grain)
    /// sets another, so that a long or blocking job does not hold up the node's passes; what comes after the job, the
    /// result into the instance's inbox, comes back on the host's scheduler, which the job's await captured.
    /// </summary>
    public TaskScheduler? JobScheduler { get; init; }

    /// <summary>Called when a turn starts, with the worker's idea of database time. For tests.</summary>
    public Action<InstanceRef, IReadOnlyList<Message>, long>? OnTurn { get; init; }
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
    private enum Outcome { Landed, Refused, Lost }

    private readonly IKernelStore _store;
    private readonly IActorType _type;
    private readonly WorkerOptions _options;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Delivery> _items = new(StringComparer.Ordinal);    // every message held, by id
    private readonly List<Delivery> _meets = [];
    private readonly HashSet<string> _cancelling = new(StringComparer.Ordinal);        // tickets of jobs cancelled by a commit in flight
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);          // while a poll is in flight
    private readonly HashSet<string> _behind = new(StringComparer.Ordinal);            // own rows written while catching up, not yet polled
    private readonly List<TurnOutcome> _queue = [];
    private long _generation, _anchor = long.MinValue, _local, _observed;
    private long _renewed;      // database time of the take, or of the sending of the last commit that landed: when the lease was renewed at the latest
    private long _safeUntil;    // local monotonic deadline, measured from the start of a successful take or commit
    private long _expiredGeneration = -1; // monotonically latched by an off-turn callback; only the owner resets state
    private bool _taking, _polling, _loading, _flushing, _meeting, _caughtUp, _woken, _releasing;
    private readonly object _turnSync = new(); // serialize asynchronous turn completion with stand-down fencing
    private string _refusal = "";
    private readonly HashSet<Delivery> _trial = [];     // the suspects being tried now, with nothing else in flight (finding F2)
    private long _trialObserved; // last first-observation ordinal when the trial mark was sent
    private readonly HashSet<long> _mine = [];      // the epochs this process has held: what it marked then did not kill it

    public Worker(string node, int partition, IKernelStore store, IActorType type, WorkerOptions? options = null)
    {
        Node = node;
        Partition = partition;
        _store = store;
        _type = type;
        _options = options ?? new WorkerOptions();
        _gate = _options.Gate ?? new TrialGate();
        _gate.Workers.Add(this);
    }

    private readonly TrialGate _gate;

    // Finding F11: another worker of this process has a trial in flight, so this one starts nothing.
    private bool Blocked => _gate.Holder is { } holder && holder != this && !Has(Flaw.TrialPerPartition);

    public string Node { get; }
    public int Partition { get; }
    public long Epoch { get; private set; }
    public long Position { get; private set; }
    public bool Owns => Epoch != 0;
    public long Evicted { get; private set; }
    public int Idle => _slots.Values.Count(s => s.Loaded && !s.Busy && s.Inbox.Count == 0 && s.Replies.Count == 0);    // for tests

    /// <summary>The instances held in memory only, never written (decision 4). For tests.</summary>
    public IEnumerable<string> Unwritten => _slots.Where(s => s.Value.Unwritten).Select(s => s.Key);

    /// <summary>The reply addresses of the callers this owner holds, for tests of decision 6.</summary>
    public IEnumerable<string> Callers => _slots.Values.SelectMany(s => s.Replies);
    private long _used;

    public bool CreationIdentityAvailable => _store.CreationIdentityAvailable;
    public bool CommitClockAvailable => _store.CommitClockAvailable;
    /// <summary>Database time as far as this worker knows: the latest reading plus what its monotonic clock has counted since.</summary>
    public long Now => Has(Flaw.WallClockTimers) ? _options.WallClock!() : _anchor + _options.Monotonic();

    /// <summary>Database time as far as this worker knows, whatever the flaws: what the lease is reckoned in.</summary>
    private long DbNow => _anchor + _options.Monotonic();

    public bool Taking => _taking;
    public bool Polling => _polling;
    public bool Loading => _loading;
    public bool Flushing => _flushing;
    public bool Meeting => _meeting;
    public int Queued => _queue.Count;
    public bool NeedsLoad => _slots.Values.Any(s => !s.Loaded);
    public bool NeedsFlush => _queue.Count > 0 || _items.Values.Any(Unmarked) || NextTrial().Count > 0 || Outgrown().Any();
    public IEnumerable<string> Trial => _trial.Select(i => i.Message.Id);
    public bool NeedsMeet => _meets.Exists(i => !Awaits(i) && !i.Taken);
    public IEnumerable<string> Ready => _slots.Where(s => s.Value.Loaded && !s.Value.Busy && Deliverable(s.Key, s.Value).Count > 0).Select(s => s.Key)
        .Where(id => !Blocked && (_trial.Count == 0 || OnTrial(id)));    // while a trial runs, nothing else in the process does

    // What is in memory, for tests of invariant 9.
    public IEnumerable<(string Id, InstanceRow? Row, string? State, bool Busy)> Instances =>
        _slots.Where(s => s.Value.Loaded).Select(s => (s.Key, s.Value.Row, s.Value.State is null ? null : Json.Write(s.Value.State), s.Value.Busy));
    public IEnumerable<(string Id, bool Durable, bool Taken)> Messages => _items.Values.Select(i => (i.Message.Id, i.Durable, i.Taken));

    /// <summary>Test observation of a completed job result, after it has been placed in the instance inbox.</summary>
    public bool JobResultReady(string instance, string key, long creation = 0) => _slots.TryGetValue(instance, out var slot)
        && slot.Inbox.Exists(i => !i.Taken && i.Message.Kind == Kinds.JobFinished && i.Message.Id == TicketId(instance, key, creation));

    /// <summary>
    /// Diagnostic: capture before a ticket is removed. The task ends after its driver queues or discards the result;
    /// it does not establish that a model consumed the result or committed a logical job outcome.
    /// </summary>
    public Task? JobCompletion(string instance, string key, long creation = 0) =>
        _items.GetValueOrDefault(TicketId(instance, key, creation)) is { } ticket
            && ticket.Message.Target.Id == instance && ticket.Key == key ? ticket.JobDriver : null;

    public bool Has(Flaw flaw) => (_options.Flaws & flaw) != 0;
    public int Partitions => _options.Partitions;
    public TimeSpan ShortWait => _options.ShortWait;

    /// <summary>A turn cancelled a job. Its ticket may be in memory, or still on its way in with a poll.</summary>
    internal void Cancelling(string ticket)
    {
        _cancelling.Add(ticket);
        if (_items.TryGetValue(ticket, out var held)) held.Taken = true;
    }
    void ITurnServices.Cancelling(string ticket) => Cancelling(ticket);

    internal ITurnServices ServicesFor(Slot slot)
    {
        var services = new TurnServices(this, slot, _generation, slot.TurnFence);
        slot.UndoTurn = services.Invalidate;
        return services;
    }

    private sealed class TurnServices(Worker worker, Slot slot, long generation, long fence) : ITurnServices
    {
        private readonly HashSet<string> _reservations = [];
        public long Now => worker.Now;
        public bool CreationIdentityAvailable => worker.CreationIdentityAvailable;
        public bool CommitClockAvailable => worker.CommitClockAvailable;
        public int Partitions => worker.Partitions;
        public TimeSpan ShortWait => worker.ShortWait;
        public bool Has(Flaw flaw) => worker.Has(flaw);
        public void Cancelling(string ticket)
        {
            lock (worker._turnSync)
                if (generation == worker._generation && fence == slot.TurnFence)
                {
                    if (!worker._cancelling.Contains(ticket)) _reservations.Add(ticket);
                    worker.Cancelling(ticket);
                }
        }
        public void Invalidate()
        {
            foreach (var id in _reservations)
            {
                worker._cancelling.Remove(id);
                if (!worker._items.TryGetValue(id, out var item)) continue;
                if (item.Key is not null && item.Message.Kind == Kinds.Job)
                {
                    // The old job may already have discarded its result while reserved. Reticket it after commit.
                    item.Taken = true;
                    item.Cancel?.Cancel();
                    var replacement = new Delivery(item.Message, item.Seq) { Key = item.Key, Ticket = true, Durable = item.Durable, Marked = item.Marked, ObservedOrder = item.ObservedOrder, ObservedAt = item.ObservedAt };
                    worker._items[id] = replacement;
                    slot.Inbox.Remove(item);
                    Insert(slot, replacement);
                }
                else item.Taken = false;
            }
            _reservations.Clear();
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
    /// One pass, as the hosts drive a worker: read the inbox if woken, load, read kept events, run every turn that is
    /// ready (in id order), and flush; then, if no commit has renewed the lease for a third of it, beat (an empty fenced
    /// commit, which also tells a stale owner that it is one). Zero when the pass found work at its start; otherwise the
    /// time until the earliest timer held in memory or the next beat, whichever is sooner; null once the scope is not
    /// held. A runtime that calls again when asked therefore keeps an idle scope's lease. A cancelled token stops the
    /// pass before the next turn, the flush and the beat.
    /// </summary>
    public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_releasing) return null;
        if (!LeaseUsable()) return null;
        if (_woken)
        {
            _woken = false;
            await PollAsync(cancellationToken);
        }
        if (!LeaseUsable()) return null;
        var busy = NeedsLoad || NeedsMeet || NeedsFlush || Ready.Any();
        await LoadAsync(cancellationToken);
        await MeetAsync(cancellationToken);
        foreach (var id in Ready.Order(StringComparer.Ordinal).ToList())
        {
            if (cancellationToken.IsCancellationRequested) break;
            RunTurn(id);
        }
        if (!cancellationToken.IsCancellationRequested) await FlushAsync(cancellationToken);
        if (!cancellationToken.IsCancellationRequested && Owns && DbNow >= RenewBy) await BeatAsync(cancellationToken);
        if (!Owns) return null;
        Evict();
        if (busy) return TimeSpan.Zero;
        var now = Now;
        var next = _items.Values.Where(i => !i.Taken && i.Message.Due is { } due && due > now).Select(i => i.Message.Due!.Value - now)
            .Concat(_slots.Values.Where(s => s.Unwritten && s.Until > now).Select(s => s.Until - now)).DefaultIfEmpty(long.MaxValue).Min();
        var caller = Callers.Select(to => ReplyTo.Parse(to).Deadline - now).DefaultIfEmpty(long.MaxValue).Min();
        return TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(caller, Math.Min(next, RenewBy - DbNow))));
    }

    /// <summary>When the pass beats if nothing commits before: a third of the lease after it was last renewed (the sketch's cadence).</summary>
    private long RenewBy => _renewed + (long)(_options.Lease.TotalMilliseconds / 3);

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

    private bool LeaseUsable()
    {
        if (!Owns) return false;
        if (CanStartWork) return true;
        Reset();
        return false;
    }

    // ---- ownership ---------------------------------------------------------------------------------------------

    internal async Task TakeAsync(CancellationToken cancellationToken = default)
    {
        if (_taking) return;
        _taking = true;
        var started = _options.Monotonic();
        Lease? lease = null;
        try { lease = await _store.TakeAsync(Partition, Node, _options.Lease, cancellationToken); }
        catch (Exception) { /* not taken, or taken without knowing: the next take bumps the epoch again */ }
        finally { _taking = false; }
        if (lease is null) return;
        Reset();    // a take starts from empty memory
        Observe(lease.Now);
        _renewed = lease.Now;
        Epoch = lease.Epoch;
        Volatile.Write(ref _safeUntil, started + (long)_options.Lease.TotalMilliseconds);
        Position = lease.Position;
        _mine.Add(lease.Epoch);
        LeaseUsable(); // an answer delayed past the safe deadline cannot authorize work
    }

    /// <summary>Stand down: drop what is in memory and give the partition up. Whoever takes it next finds no death.</summary>
    internal async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        long generation, epoch;
        lock (_turnSync)
        {
            if (!Owns || _releasing) return;
            generation = _generation;
            epoch = Epoch;
            _releasing = true;
        }
        // Decision 4: a stand-down hands its partition over, so what it holds in memory only is written first, unless a
        // commit is in flight (then those instances are lost with this memory, as in a crash, and their callers told).
        if (!_flushing && !Has(Flaw.ForgetShortWaits))
        {
            lock (_turnSync)
            {
                if (generation != _generation || epoch != Epoch) return;
                foreach (var (_, slot) in _slots.Where(s => s.Value.Unwritten && (!s.Value.Busy || s.Value.Driving)).ToList())
                {
                    // A suspended turn may be mutating its live object. Fence its eventual result and write only the
                    // previous sealed row; its taken volatile inputs are still in _items and Promote writes them too.
                    if (slot.Busy) { slot.TurnFence++; slot.UndoTurn?.Invoke(); slot.Driving = false; }
                    slot.Busy = true;
                    var unit = Bound(_type.Write(slot.Row!, null, slot.Pending), slot);
                    Promote(unit);
                    _queue.Add(unit);
                }
            }
            while (generation == _generation && epoch == Epoch && _queue.Count > 0 && LeaseUsable())
                await FlushAsync(cancellationToken);
            if (generation != _generation || epoch != Epoch || !Owns) return;
        }
        lock (_turnSync)
        {
            if (generation != _generation || epoch != Epoch) return;
            Reset();
        }
        try { await _store.ReleaseAsync(Partition, epoch, cancellationToken); }
        catch (Exception) { /* then the lease runs out instead */ }
    }

    /// <summary>Recovery after any doubt is the same path as start-up: drop everything; the host takes the partition again.</summary>
    private void Reset()
    {
        lock (_turnSync)
        {
            Interlocked.Increment(ref _generation);
            Epoch = 0;
            _polling = _loading = _flushing = _meeting = _releasing = false;
            foreach (var item in _items.Values.ToList()) item.Cancel?.Cancel();
            // Every caller this memory knows of is told at once; a new owner answers only what is still in the rows.
            foreach (var to in _slots.Values.SelectMany(s => s.Replies).Concat(_items.Values.Select(i => i.Message.ReplyTo).OfType<string>()).Distinct().ToList())
                Send(to, "lost", null, null, error: "lost");
            _slots.Clear();
            _items.Clear();
            _meets.Clear();
            _queue.Clear();
            _consumed.Clear();
            _behind.Clear();
            _cancelling.Clear();
            _caughtUp = false;
            _trial.Clear();
            _gate.Release(this);
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
        try { polled = await _store.PollAsync(Partition, Epoch, _options.Horizon, _options.PollLimit, cancellationToken); }
        catch (Exception)
        {
            if (generation == _generation) _polling = false;    // a read: nothing is in doubt, the next poll sees the same rows
            return;
        }
        if (generation != _generation) return;
        _polling = false;
        Observe(polled.Now);    // anchored when the answer arrives, so the estimate never runs ahead of the database
        if (!LeaseUsable()) return;
        foreach (var row in polled.Messages)
        {
            if (!_items.ContainsKey(row.Message.Id) && !_consumed.Contains(row.Message.Id)) _options.OnObservedDelivery?.Invoke(row);
            Hold(row.Message, row.Seq, durable: true, marked: false, attempts: row.Attempts, suspect: Suspect(row, polled.Now), again: row.Seen > 0 || row.Trial > 0);
            if (!LeaseUsable()) return;
        }
        // Finding F1: caught up when a poll comes back short and every row this owner wrote meanwhile has come back with it.
        if (polled.Messages.Count < _options.PollLimit && _behind.Count == 0) _caughtUp = true;
    }

    /// <summary>
    /// A message handed to the owner in memory and never written: a start without a caller's key. <paramref name="fresh"/>
    /// says the target's id was just minted, so no read is needed to know that it does not exist.
    /// </summary>
    public bool Post(Message message, bool fresh = false, object? ambient = null)
    {
        if (_releasing || !LeaseUsable() || message.Target.Partition != Partition) return false;
        Hold(message, long.MaxValue / 2 + ++_local, durable: false, marked: true, fresh: fresh);
        if (!LeaseUsable()) return false;
        if (_slots.TryGetValue(message.Target.Id, out var slot)) slot.Ambient = ambient;     // lives with the instance in this memory, nowhere else
        return true;
    }

    /// <summary>
    /// A message that another owner had marked, or that is on trial already, may be what killed that owner. What this
    /// same process marked in an epoch it held earlier is not: the process is alive.
    /// </summary>
    private bool Suspect(Stored row, long now) => row.Message.Kind is not (Kinds.Meet or Kinds.Publish) && !Kinds.IsControl(row.Message.Kind) && row.Attempts < _options.MaxAttempts
        && (row.Trial > 0 || (row.Seen > 0 && !_mine.Contains(row.Seen) && (row.Message.Due is not { } due || due <= now)));

    private bool OnTrial(string instance) => _trial.Any(t => t.Message.Target.Id == instance);

    private void Hold(Message message, long seq, bool durable, bool marked, bool suspect = false, int attempts = 0, bool fresh = false, bool again = false)
    {
        if (durable && _items.TryGetValue(message.Id, out var promoted) && promoted.AwaitPoll)
        {
            _behind.Remove(message.Id);
            promoted.AwaitPoll = false;
            promoted.Marked = marked;
            promoted.Suspect = suspect;
            promoted.Attempts = attempts;
            promoted.Again = again;
            promoted.RecoveryOrdering |= again;
            promoted.Seq = seq;
            if (_slots.TryGetValue(message.Target.Id, out var waiting) && promoted.CompletedOrder == 0 && waiting.Inbox.Remove(promoted)) Insert(waiting, promoted);
            return;
        }
        // The owner skips ids it holds, and (finding F5) ids it consumed while the poll that returned them was in flight.
        if (!Has(Flaw.PollWithoutSkip) && (_items.ContainsKey(message.Id) || _consumed.Contains(message.Id))) return;
        _behind.Remove(message.Id);
        // A recalled message (Recall) whose row a poll brings in before the commit that deletes it lands: taken, never given.
        // A previously marked row is durable evidence of recovery, even when it was marked by this
        // process. The process can rule out a death for trial accounting, not reconstruct its lost inbox.
        var item = new Delivery(message, seq)
        {
            Durable = durable, Marked = marked, Suspect = suspect, Attempts = attempts,
            Again = again, RecoveryOrdering = again, Taken = _cancelling.Contains(message.Id),
            ObservedOrder = Interlocked.Increment(ref _observed), ObservedAt = Now
        };
        _items[message.Id] = item;
        if (message.Kind is Kinds.Meet or Kinds.Publish)     // NATIVE N1: a publish ticket is handled like a meet ticket
        {
            item.Ticket = true;
            _meets.Add(item);
            return;
        }
        if (message.Kind == Kinds.Job && attempts >= _options.MaxAttempts)
        {
            // A job that was on trial when too many owners were lost is parked: its result is an error.
            var error = $"the job was on trial when {attempts} owners were lost";
            item.Original = message;
            item.Message = message with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(message.Read<JobTicket>().Key, null, error)) };
        }
        else if (message.Kind == Kinds.Job)
        {
            item.Ticket = true;
            item.Key = message.Read<JobTicket>().Key;
            item.Taken = _cancelling.Contains(message.Id);
        }
        if (!_slots.TryGetValue(message.Target.Id, out var slot)) _slots[message.Target.Id] = slot = new Slot { Loaded = fresh };
        Insert(slot, item);
        if (item.Key is not null) StartJob(item);
    }

    private static void Insert(Slot slot, Delivery item)
    {
        var at = slot.Inbox.FindIndex(i => i.InboxOrder > item.InboxOrder);
        slot.Inbox.Insert(at < 0 ? slot.Inbox.Count : at, item);    // a target's messages are handled in seq order
    }

    private bool Unmarked(Delivery item) => item.Durable && !item.AwaitPoll && !item.Marked && !item.Taken && (!item.Suspect || (!item.Shelved && Shelf(item)));

    /// <summary>
    /// Finding F12: an authored suspect whose instance is suspended cannot be tried, and while its row says unseen it fills a place
    /// in every poll; a poll limit of them hides the resume behind them. So it is shelved: its row is marked seen at this
    /// epoch, which moves the polls on, but it is neither on trial nor cleared to run. The next owner still suspects it.
    /// </summary>
    private bool Shelf(Delivery suspect) => !Has(Flaw.NoShelf) && _slots.TryGetValue(suspect.Message.Target.Id, out var slot) && Withheld(slot)
        && CreationError(slot, suspect.Message) is null;

    /// <summary>
    /// An instance that cannot be given its messages now, as its actor type says (<see cref="IActorType.Withholds"/>).
    /// Kernel messages still pass.
    /// </summary>
    private bool Withheld(Slot slot) => slot.Row is { Retired: false } row && _type.Withholds(row, this);

    /// <summary>
    /// Finding F2: suspects are tried apart from everything else. A fenced commit writes that they are on trial at this
    /// epoch; then only they run. If this owner's lease expires, the take that follows adds an attempt to exactly the
    /// rows on trial. So a death counts against what was on trial and never against its neighbours. A group holds
    /// suspects with the same attempts and halves with each attempt, so a poison message ends up alone.
    /// </summary>
    private List<Delivery> NextTrial()
    {
        if (_trial.Count > 0 || Blocked) return [];
        var suspects = _items.Values.Where(i => i.Durable && !i.Marked && !i.Taken && i.Suspect).OrderBy(i => i.Seq).ToList();
        if (suspects.Count == 0 || Has(Flaw.BumpAllInherited)) return suspects;
        var (group, skipped) = (new List<Delivery>(), new HashSet<string>());
        int? attempts = null;
        var now = Now;
        var boundaries = new Dictionary<string, Delivery?>(StringComparer.Ordinal);
        foreach (var suspect in suspects)
        {
            var target = suspect.Message.Target.Id;
            var slot = _slots[target];
            if (!boundaries.TryGetValue(target, out var control))
                boundaries[target] = control = RecoveryBoundary(slot, now, recovering: true);

            // Excluded suspects do not precede any candidate on this side of the boundary, even when
            // their store sequence sorts first. Within the eligible side, take only a prefix per target.
            if (suspect.Message.Due > now || control is not null && !BeforeCut(suspect, control)) continue;
            if (skipped.Contains(target)) continue;
            if ((slot.Loaded || Has(Flaw.TrialBeforeLoad)) && (!slot.Busy || Has(Flaw.SuspendDuringTrial) || Has(Flaw.TrialBeforeLoad))
                && (!Withheld(slot) || CreationError(slot, suspect.Message) is not null)
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
    private Delivery? RecoveryBoundary(Slot slot, long now, bool recovering)
    {
        Delivery? durable = null, local = null;
        var earliest = long.MaxValue;
        if (!recovering && !slot.Inbox.Exists(i => i.RecoveryOrdering || i.Suspect)) return null;
        foreach (var item in slot.Inbox)
        {
            if (item.Taken || !Kinds.IsControl(item.Message.Kind) || item.Message.Kind == Kinds.Suspend && Has(Flaw.SuspendDuringTrial)) continue;
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

    private List<Delivery> Deliverable(string instance, Slot slot)
    {
        var now = Now;
        var due = new List<Delivery>();
        var suspended = Withheld(slot);
        var onTrial = OnTrial(instance);
        var control = RecoveryBoundary(slot, now, onTrial);
        bool Eligible(Delivery item) => !item.Ticket && !item.Taken && !(item.Message.Due > now)
            && (!suspended || Kinds.IsControl(item.Message.Kind) || CreationError(slot, item.Message) is not null);
        bool InFront(Delivery item) => control is null || _trial.Contains(item)
            || !Kinds.IsControl(item.Message.Kind) && BeforeCut(item, control);

        // Finish marking ordinary cohort members together. Unselected suspects obtain their own fenced
        // trial later; waiting for their mark while holding the process gate would deadlock.
        if (control is not null && onTrial && slot.Inbox.Exists(i => Eligible(i) && InFront(i) && !i.Suspect
            && i.ObservedOrder <= _trialObserved && Awaits(i))) return [];

        // In normal operation the first deliverable command ends the sequence. Recovery projects the
        // inbox onto the current command's eligible inputs, keeping their ordinary actor order, then the
        // command itself. It must encounter an unmarked predecessor before it can reach that command.
        IEnumerable<Delivery> inputs = slot.Inbox.Where(i => Eligible(i) && InFront(i));
        if (control is not null && !onTrial) inputs = inputs.Append(control);
        var selectedOnly = false;
        foreach (var item in inputs)
        {
            if (selectedOnly && !_trial.Contains(item)) continue;
            if (onTrial && item.Suspect && !_trial.Contains(item))
            {
                // A narrow horizon can reveal an older suspect after the fenced selection. Its trial
                // must follow that selection; no later ordinary input may leapfrog it.
                selectedOnly = true;
                continue;
            }
            if (Awaits(item)) break;
            var command = Kinds.IsControl(item.Message.Kind);
            var identityReply = CreationError(slot, item.Message) is not null;
            var continuingStart = !identityReply && slot.Row?.PendingStart == item.Message.Id;
            // Keep lifecycle changes/refusals separate. Compatible messages of this creation retain normal batching.
            if (item.Message.Start?.Creation is not null && !continuingStart || identityReply ||
                slot.Row?.Creation > 0 && (item.Message.Start is not null && !continuingStart || _type.SingleMessage(item.Message)))
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
        return due.TrueForAll(i => i.Held) ? [] : due;    // held messages wait for something new to come with
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var ids = _slots.Where(s => !s.Value.Loaded).Select(s => s.Key).ToArray();
        if (_loading || !Owns || ids.Length == 0) return;
        var generation = _generation;
        _loading = true;
        IReadOnlyList<InstanceRow> rows;
        try { rows = await _store.LoadAsync(ids, cancellationToken); }
        catch (Exception)
        {
            if (generation == _generation) _loading = false;
            return;
        }
        if (generation != _generation) return;
        _loading = false;
        var found = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var id in ids)
            if (_slots.TryGetValue(id, out var slot) && !slot.Loaded)
            {
                (slot.Row, slot.Loaded, slot.Used) = (found.GetValueOrDefault(id), true, ++_used);
                Volatile.Write(ref slot.ActiveCreation, slot.Row is { Retired: false } row ? row.Creation : -1);
            }
        foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToArray()) StartJob(ticket);
        Evict();
    }

    // ---- turns ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Run one turn of an instance that is in <see cref="Ready"/>. The actor type decides first what it handles alone
    /// (a command, or messages to an instance that ended); then a message on trial too often parks the instance, a
    /// message without a target or a start spec is dropped, and otherwise the actor type runs the turn.
    /// </summary>
    public void RunTurn(string instance)
    {
        if (_releasing || !LeaseUsable() || !_slots.TryGetValue(instance, out var slot) || !slot.Loaded || slot.Busy) return;
        if (Blocked || (_trial.Count > 0 && !OnTrial(instance))) return;
        var due = Deliverable(instance, slot);
        if (due.Count == 0) return;
        if (slot.Unwritten && (Now >= slot.Until || due.Exists(i => i.Durable || Kinds.IsControl(i.Message.Kind))) && !Has(Flaw.NoWriteFirst))
        {
            // Decision 4: a message from outside, or a command, finds an instance kept in memory written first; then it
            // is an ordinary instance, with a row, for whatever the message does.
            slot.Busy = true;
            Enqueue(Bound(_type.Write(slot.Row!, slot.State, slot.Pending), slot));
            return;
        }
        slot.Inbox.RemoveAll(due.Contains);
        foreach (var item in due) (item.Taken, item.Held) = (true, false);
        (slot.Busy, slot.Used) = (true, ++_used);
        var request = due[0].Message;
        var creationError = CreationError(slot, request);
        if (creationError is not null)
        {
            Enqueue(Bound(CreationProtocol.Refuse(instance, slot.Row, due, creationError), slot));
            return;
        }
        var creator = slot.Row is null or { Retired: true } ? due.FindIndex(i => i.Message.Start is not null) : -1;
        var spec = creator < 0 ? null : due[creator].Message.Start;
        slot.Starting = creator < 0 ? null : due[creator].Message;
        var input = new TurnInput(this, slot, instance, due, Math.Max(creator, 0), spec);
        if (_type.Command(input) is { } command)
            Enqueue(Bound(command, slot));
        else if (due.Find(i => i.Attempts >= _options.MaxAttempts && i.Message.Kind != Kinds.JobFinished) is { } poison)
            Enqueue(Bound(_type.Park(slot.CurrentRow, instance, due, spec, $"message {poison.Message.Id} was on trial when {poison.Attempts} owners were lost"), slot));
        else if (slot.Row is null && spec is null)                   // no target and no start spec: dropped and journalled
            Enqueue(new TurnOutcome(instance) { Slot = slot, Consumed = due, Journal = Json.Write(due.Select(i => "dropped " + i.Message.Id)), Status = null });
        else
        {
            slot.Driving = true;
            _ = Drive(input);
        }
    }

    // One classifier for trial/shelving, withholding and dispatch: a refusal never enters the actor's authored work.
    private string? CreationError(Slot slot, Message request) => request.Start is not null && (request.Start.Creation is not null || slot.Row?.Creation > 0)
            ? request.Start.Creation is null ? "IdentityRequired"
                : !CreationIdentityAvailable ? "CreationIdentityUnavailable"
                : request.TargetCreation is { } target && target != request.Start.Creation.Generation ? "WrongCreation"
                : CreationProtocol.StartError(slot.Row, request.Start, request.Kind, request.Payload, request.Id)
            : CreationProtocol.TargetError(slot.Row, request);

    private async Task Drive(TurnInput input)
    {
        var generation = _generation;
        var fence = input.Slot.TurnFence;
        TurnOutcome unit;
        try { unit = await _type.TurnAsync(input); }
        catch (Exception e)
        {
            lock (_turnSync)
            {
                if (generation != _generation || fence != input.Slot.TurnFence) return;
                input.Slot.Driving = false;
                if (input.Unwritten) input.Slot.UndoTurn?.Invoke();
                unit = _type.Park(input.Row, input.Actor, input.Taken, input.Spec, e.Message, input.Unwritten ? input.Pending : null);
                if (input.Unwritten) RetireVolatileJobs(unit);
                Enqueue(Bound(unit, input.Slot));
            }
            return;
        }
        lock (_turnSync)
        {
            if (generation == _generation && fence == input.Slot.TurnFence)
            {
                input.Slot.Driving = false;
                Enqueue(Bound(unit, input.Slot));
            }
        }
    }

    private static TurnOutcome Bound(TurnOutcome unit, Slot slot)
    {
        unit.Slot = slot;
        if (unit.Row is { Retired: false } row)
        {
            if (slot.Row is null or { Retired: true } && slot.Starting is { Start.Creation: { } start } request)
            {
                unit.Row = row with { Creation = start.Generation, StartIntent = CreationProtocol.Fingerprint(request.Start, request.Kind, request.Payload), ParentCreation = request.Start.ParentCreation,
                    PendingStart = row.Status == InstanceStatus.Running && unit.Held.Any(i => i.Message.Id == request.Id) ? request.Id : null };
                unit.Expected = slot.Row?.Revision;
                unit.Revision = unit.Row.Revision;
            }
            else if (slot.Row is { Creation: > 0 } previous)
                unit.Row = row with { Creation = previous.Creation, StartIntent = previous.StartIntent, ParentCreation = previous.ParentCreation,
                    PendingStart = row.Status == InstanceStatus.Running && !unit.Consumed.Any(i => i.Message.Id == previous.PendingStart) ? previous.PendingStart : null };
        }
        return unit;
    }

    /// <summary>The turn has started: for tests (<see cref="WorkerOptions.OnTurn"/>).</summary>
    internal void Started(string instance, IReadOnlyList<Message> inbox) => _options.OnTurn?.Invoke(new InstanceRef(instance), inbox, Now);

    // ---- background jobs -------------------------------------------------------------------------------------------

    private void StartJob(Delivery ticket)
    {
        if (!_slots.TryGetValue(ticket.Message.Target.Id, out var loadedSlot) || !loadedSlot.Loaded || loadedSlot.Busy) return;
        if (loadedSlot.Row is null || CreationProtocol.TargetError(loadedSlot.Row, ticket.Message) is not null)
        {
            ticket.Cancel?.Cancel();
            ticket.Ticket = false; ticket.Key = null;
            return; // the normal fenced dispatch consumes/refuses this stale target
        }
        Slot? creationSlot = ticket.Message.TargetCreation is not null ? loadedSlot : null;
        // Callers finish their current collection operation before standing down, so a refused ticket is never stranded.
        if (_releasing || !CanStartWork || ticket.Cancel is not null || ticket.Taken || Awaits(ticket) || Blocked || (_trial.Count > 0 && !_trial.Contains(ticket))) return;
        ticket.Cancel = new CancellationTokenSource();
        ticket.JobDriver = DriveJob(ticket, ticket.Message.Read<JobTicket>(), creationSlot);
    }

    private bool JobMayRun(long generation)
    {
        if (CanStartGeneration(generation)) return true;
        if (generation == Volatile.Read(ref _generation) && Volatile.Read(ref _expiredGeneration) == generation)
            _options.OnExpiredJob?.Invoke();
        return false;
    }

    private async Task DriveJob(Delivery ticket, JobTicket job, Slot? creationSlot)
    {
        var generation = _generation;
        bool MayRun() => JobMayRun(generation) && (creationSlot is null || Volatile.Read(ref creationSlot.ActiveCreation) == ticket.Message.TargetCreation);
        string? result = null, error = null;
        var (context, token) = (new JobContext(ticket.Message.Target, job.Key, job.Arguments, ticket.Message.TargetCreation ?? 0), ticket.Cancel!.Token);
        try
        {
            if (!MayRun()) return;
            result = Json.Write(_options.JobScheduler is { } scheduler
                ? await Task.Factory.StartNew(() =>
                {
                    _options.BeforeScheduledJobCheck?.Invoke();
                    return MayRun() ? _type.ExecuteJobAsync(job.Model, context, token).AsTask()
                        : Task.FromCanceled<object?>(new CancellationToken(true));
                }, token, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap()
                : await _type.ExecuteJobAsync(job.Model, context, token));
        }
        catch (Exception e) { error = e.Message; }
        if (ticket.Taken || !MayRun()) return; // an old actor creation cannot publish a job result
        // The ticket becomes the result: same id, so it is consumed once, by the turn that handles it.
        ticket.Original ??= ticket.Message;
        ticket.Message = ticket.Message with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(job.Key, result, error)) };
        ticket.Ticket = false;
        // Finding F7: a result goes ahead of whatever else waits, so a turn never sees a cancel first and the result after it.
        ticket.CompletedAt = Now;
        ticket.CompletedOrder = Interlocked.Increment(ref _observed);
        if (_slots.TryGetValue(ticket.Message.Target.Id, out var slot) && slot.Inbox.Remove(ticket)) slot.Inbox.Insert(0, ticket);
    }

    // ---- events --------------------------------------------------------------------------------------------------

    /// <summary>The subscriber's side of "each side looks after its own commit": read the events for keys just subscribed.</summary>
    public async Task MeetAsync(CancellationToken cancellationToken = default)
    {
        var tickets = _meets.Where(i => !Awaits(i) && !i.Taken).ToList();
        if (_meeting || !Owns || tickets.Count == 0) return;
        var generation = _generation;
        _meeting = true;
        try
        {
            // NATIVE N1: a publish ticket is the publisher's side, done by the owner after the commit that sent it.
            foreach (var ticket in tickets.Where(t => t.Message.Kind == Kinds.Publish))
                await Events.PublishAsync(_store, ticket.Message.Read<EventRow>(), Partitions, cancellationToken);
            var waits = tickets.Where(t => t.Message.Kind == Kinds.Meet).SelectMany(t => t.Message.Read<SubscriptionRow[]>()).ToList();
            IReadOnlyList<EventRow> events = waits.Count == 0 ? [] : await _store.FindEventsAsync(waits.Select(w => w.Key).Distinct().ToArray(), cancellationToken);
            foreach (var e in events)
                foreach (var wait in waits.Where(w => w.Key == e.Key))
                    await _store.AddAsync(Events.Deliver(e, wait), cancellationToken);
        }
        catch (Exception)
        {
            if (generation == _generation) _meeting = false;    // the tickets stay: the next call looks again
            return;
        }
        if (generation != _generation) return;
        _meeting = false;
        foreach (var ticket in tickets) ticket.Taken = true;
        Enqueue(new TurnOutcome("") { Consumed = tickets });
    }

    // ---- the committer -------------------------------------------------------------------------------------------

    private void Enqueue(TurnOutcome unit)
    {
        if (Has(Flaw.DeliverBeforeCommit)) HandOver(unit, null, hold: true);
        if (Has(Flaw.EmitBeforeCommit)) Release(unit);
        else Release(unit, early: true);    // to callers whose route opted out of waiting for the commit
        if (EndsInMemory(unit)) Apply(unit, null, true);     // decision 4: it ended in memory; nothing of it was ever a row
        else if (unit.Durable)
        {
            Promote(unit);
            _queue.Add(unit);
        }
        else Apply(unit, null, true);   // nothing to write: an ephemeral run, or messages that were never rows
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => FlushAsync(false, cancellationToken);

    /// <summary>An empty commit: renews the lease of an idle partition and tells a stale owner that it is one.</summary>
    public Task BeatAsync(CancellationToken cancellationToken = default) => FlushAsync(true, cancellationToken);

    private async Task FlushAsync(bool beat, CancellationToken cancellationToken)
    {
        if (_flushing || !Owns) return;
        lock (_turnSync)
        {
            foreach (var (id, slot) in Outgrown().ToList())
            {
                var running = slot.Driving;
                if (running) { slot.TurnFence++; slot.UndoTurn?.Invoke(); slot.Driving = false; }
                slot.Busy = true;
                var unit = Bound(_type.Write(slot.Row!, running ? null : slot.State, slot.Pending), slot);
                Promote(unit);
                _queue.Add(unit);
            }
        }
        var units = _queue.GetRange(0, Math.Min(_queue.Count, _options.MaxBatch));     // rows per flush are capped
        var marks = _items.Values.Where(Unmarked).Take(_options.MaxBatch).ToList();
        var trial = units.Count == _queue.Count ? NextTrial() : [];    // a trial starts only when this commit leaves nothing else in flight
        if (units.Count == 0 && marks.Count == 0 && trial.Count == 0 && !beat) return;
        _queue.RemoveRange(0, units.Count);
        if (!Has(Flaw.BumpAllInherited) && trial.Count > 0)
        {
            _trialObserved = _observed;
            _trial.UnionWith(trial);      // from here on nothing else starts
        }
        if (_trial.Count > 0) _gate.Holder = this;
        var generation = _generation;
        _flushing = true;
        var outcome = await CommitAsync(units, marks, trial, generation, cancellationToken);
        if (outcome == Outcome.Refused)
        {
            // A definite error: no reset. The batch's instances are committed one by one and the one that fails is parked.
            outcome = units.Count == 0 ? Outcome.Refused : Outcome.Landed;
            for (var i = 0; outcome == Outcome.Landed && i < units.Count; i++)
            {
                var unit = units[i];
                // The marks ride with the first of these commits that lands.
                outcome = await CommitAsync([unit], marks, trial, generation, cancellationToken);
                if (outcome == Outcome.Refused && unit.Slot is { } slot)
                {
                    // An instance kept in memory has no accepted row: it is parked without the state the store refused.
                    if (slot.Unwritten) slot.UndoTurn?.Invoke();
                    var park = Bound(slot.Unwritten ? _type.Park(slot.Row! with { State = "null" }, unit.Instance, unit.Consumed, unit.Spec, _refusal, slot.Pending)
                        : _type.Park(slot.CurrentRow, unit.Instance, unit.Consumed.Concat(unit.Held).OrderBy(i => i.InboxOrder).ToArray(), unit.Spec, _refusal), slot);
                    if (slot.Unwritten) RetireVolatileJobs(park);
                    Promote(park);
                    outcome = await CommitAsync([park], marks, trial, generation, cancellationToken);
                }
                if (outcome == Outcome.Landed) (marks, trial) = ([], []);
            }
            if (outcome == Outcome.Refused && generation == _generation) Reset();   // nothing left to set apart
        }
        if (generation == _generation) _flushing = false;
    }

    private async Task<Outcome> CommitAsync(List<TurnOutcome> units, List<Delivery> marks, List<Delivery> trial, long generation, CancellationToken cancellationToken)
    {
        // Finding F1: until the owner has caught up with the rows it inherited, what it sends to its own partition goes
        // through the table like everything else. Handing it over in memory would let it overtake older rows.
        var handOver = _caughtUp || Has(Flaw.HandOverWhileBehind);
        var batch = new CommitBatch(Partition, Epoch, Position, _options.Lease, handOver,
            units.Select(u => new TurnCommit(u.Instance, u.Row, u.Expected,
                Has(Flaw.KeepConsumedMessages) ? [] : [.. u.Consumed.Where(i => i.Durable && i.Message.KeepUntil is null).Select(i => i.Message.Id), .. Has(Flaw.VolatileJobTicket) ? [] : u.Cancelled.Where(id => !InMemoryOnly(id))],
                Has(Flaw.KeepConsumedMessages) ? [] : u.Consumed.Where(i => i.Durable && i.Message.KeepUntil is not null).Select(i => i.Message.Id).ToList(),
                u.Sent.Where(m => !u.Volatile.Contains(m.Id)).ToList(), u.Subscribed, u.Unsubscribed, u.Journal, u.Unsubscribe,
                u.Held.Where(i => i.Durable).Select(i => i.Message.Id).ToList(), u.Delete)).ToList(),
            marks.Select(i => i.Message.Id).ToList(), trial.Select(i => i.Message.Id).ToList());
        while (true)
        {
            // When the lease is renewed at the latest, as far as this worker can tell: its estimate of database time
            // never runs ahead of the database, and the commit executes after it is sent. A store may answer without
            // the database's time (Now 0). The EF store with the fence in the batch writes the lease from the node's
            // own clock instead (ENGINES.md); a beat at a third of the lease then tolerates up to two thirds of a lease
            // of skew between the node's clock and the database's.
            if (!LeaseUsable()) return Outcome.Lost;
            var sending = DbNow;
            var started = _options.Monotonic();
            Committed result;
            try
            {
                if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(batch);
                if (!CommitClockAvailable) CommitClockFacts.RejectUnsupported(batch);
                result = await _store.CommitAsync(batch, cancellationToken);
            }
            catch (StoreException e) when (e.Failure != Failure.Unknown)
            {
                if (generation != _generation) return Outcome.Lost;
                if (e.Failure == Failure.Transient) continue;   // a deadlock victim: the same batch again
                _refusal = e.Message;
                return Outcome.Refused;
            }
            catch (Exception)
            {
                if (generation == _generation) Fail(units);     // unknown outcome
                return Outcome.Lost;
            }
            if (generation != _generation) return Outcome.Lost; // reset while the commit was in flight: its result is nobody's
            if (!result.Landed)
            {
                Fail(units);                                    // the fence matched nothing
                return Outcome.Lost;
            }
            Position += batch.Positions;
            Observe(result.Now);
            _renewed = Math.Max(_renewed, sending);      // a commit that lands renews the lease
            Volatile.Write(ref _safeUntil, Math.Max(Volatile.Read(ref _safeUntil), started + (long)_options.Lease.TotalMilliseconds));
            if (!LeaseUsable()) return Outcome.Lost; // a late acknowledgement cannot revive the old local deadline
            foreach (var item in marks.Concat(trial))
                if (item.Suspect && !trial.Contains(item)) item.Shelved = true;
                else item.Marked = true;
            var seqs = new Queue<long>(result.Seqs);
            foreach (var unit in units)
            {
                if (unit.Row is { } row) unit.Row = CommitClockFacts.Stamp(row, result.Now);
                Apply(unit, seqs, handOver);
                if (!LeaseUsable()) return Outcome.Lost;
            }
            foreach (var item in marks.Concat(trial))
                if (item.Key is not null && _items.ContainsKey(item.Message.Id)) StartJob(item);
            if (!LeaseUsable()) return Outcome.Lost;
            return Outcome.Landed;
        }
    }

    /// <summary>A message held in memory and never written: one that needs no delete when it is cancelled.</summary>
    private bool InMemoryOnly(string id) => _items.TryGetValue(id, out var held) && !held.Durable;

    // A never-written parked instance has no durable jobs to resume. Retire its volatile tickets atomically with
    // the faulted row so a new owner does not launch fresh work for a run that was already parked.
    private void RetireVolatileJobs(TurnOutcome unit)
    {
        if (unit.Row is not { } row) return;
        var tickets = _items.Values.Where(i => !i.Durable && i.Key is not null && i.Message.Target.Id == unit.Instance).ToList();
        var keys = tickets.Select(i => i.Key!).ToHashSet(StringComparer.Ordinal);
        if (row.Jobs is { } jobs)
        {
            var remaining = Json.Read<List<string>>(jobs).Where(k => !keys.Contains(k)).ToList();
            unit.Row = row with { Jobs = remaining.Count == 0 ? null : Json.Write(remaining) };
        }
        foreach (var ticket in tickets)
        {
            if (!unit.Cancelled.Contains(ticket.Message.Id)) unit.Cancelled.Add(ticket.Message.Id);
            Cancelling(ticket.Message.Id);
        }
    }

    /// <summary>Decision 4: instances in memory whose short wait is over, and that no turn or commit has now.</summary>
    private IEnumerable<KeyValuePair<string, Slot>> Outgrown()
    {
        var now = Now;
        return _slots.Where(s => s.Value.Unwritten && (!s.Value.Busy || s.Value.Driving) && now >= s.Value.Until);
    }

    /// <summary>
    /// Decision 4: an outcome that writes an instance held in memory until now writes what it waits for in memory too:
    /// its timers and job tickets become rows in the same commit, so a crash after it loses none of them.
    /// </summary>
    private void Promote(TurnOutcome unit)
    {
        if (unit.Slot is not { Unwritten: true }) return;
        // Whoever wrote the outcome (a turn, a command, a park) saw the row in memory: it is not in the table, so it is
        // inserted, not updated at its revision.
        if (unit.Row is not null) unit.Expected = null;
        if (Has(Flaw.ForgetShortWaits)) return;
        // Held ones too: a held result is still in flight in the row's jobs. A finished job is written as its ticket, so a
        // later owner runs it again and puts its result first (F7), instead of a result row ordered behind a cancel.
        var prior = _items.Values.Where(i => !i.Durable && i.Message.Target.Id == unit.Instance && !unit.Consumed.Contains(i)
                     && !unit.Cancelled.Contains(i.Message.Id)).OrderBy(i => i.InboxOrder).ThenByDescending(i => i.CompletedOrder).ToList();
        foreach (var item in prior)
        {
            unit.Promoted.Add(item.Message.Id);
        }
        unit.Sent.InsertRange(0, prior.Select(item => item.Original ?? item.Message));
    }

    /// <summary>Decision 4: an instance kept in memory ended there, and its turn leaves nothing that is a row.</summary>
    private bool EndsInMemory(TurnOutcome unit) =>
        unit.Slot is { Unwritten: true } && !unit.InMemory && unit.Row is null && unit.Journal is null && !unit.Delete
        && !unit.Consumed.Exists(i => i.Durable) && unit.Sent.TrueForAll(m => unit.Volatile.Contains(m.Id)) && unit.Cancelled.TrueForAll(InMemoryOnly);

    private void ForgetInMemory(string instance)
    {
        foreach (var item in _items.Values.Where(i => !i.Durable && i.Message.Target.Id == instance).ToList()) Forget(item);   // its timers go, its jobs are cancelled
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
    private void Apply(TurnOutcome unit, Queue<long>? seqs, bool handOver)
    {
        // A command can mature while the last recovery checkpoint awaits acknowledgement. Capture
        // these boundaries before removing the selected member, exhausted error or previous command.
        if (unit.Slot is { } pending && (OnTrial(unit.Instance) || unit.Consumed.Exists(i => i.RecoveryOrdering)
            || unit.Held.Exists(i => i.RecoveryOrdering)))
            RecoveryBoundary(pending, Now, recovering: true);
        foreach (var item in unit.Consumed) { _behind.Remove(item.Message.Id); Forget(item); }
        foreach (var item in unit.Held)     // back in its place; its turn ran, so it is no suspect and no longer on trial
        {
            (item.Taken, item.Held, item.Suspect, item.Marked) = (false, true, false, true);
            Insert(unit.Slot!, item);
            if (_trial.Remove(item) && _trial.Count == 0) _gate.Release(this);
        }
        foreach (var id in unit.Cancelled)      // a cancelled job's ticket goes whether or not this owner had taken it into memory yet
        {
            _cancelling.Remove(id);
            _behind.Remove(id);
            if (_items.TryGetValue(id, out var ticket)) Forget(ticket);
            else if (_polling && !Has(Flaw.PollRace)) _consumed.Add(id);
        }
        if (!handOver)
            foreach (var message in unit.Sent)
                if (message.Target.Partition == Partition && message.Due is null && !unit.Volatile.Contains(message.Id) && !unit.Promoted.Contains(message.Id)) _behind.Add(message.Id);
        if (unit.Slot is { } committed)
        {
            if (unit.Row is not null) committed.Row = unit.Row;
            if (unit.Delete && unit.Row is not { Retired: true }) committed.Row = null;
            Volatile.Write(ref committed.ActiveCreation, committed.Row is { Retired: false } row ? row.Creation : -1);
        }
        HandOver(unit, seqs, hold: handOver && !Has(Flaw.DeliverBeforeCommit));
        if (unit.Slot is not { } slot) return;
        if (unit.InMemory)       // decision 4: kept in memory, not written
        {
            (slot.Unwritten, slot.Until, slot.Pending) = (true, unit.Until, unit.Pending);
            _options.OnKeptInMemory?.Invoke(unit.Instance, unit.Revision);
        }
        else if (slot.Unwritten)
        {
            (slot.Unwritten, slot.Pending) = (false, null);
            if (unit.Row is null)       // it ended in memory: never written at all, and what it still waited for goes with it
            {
                _options.OnLeftMemory?.Invoke(unit.Instance, unit.Status);
                slot.Row = null;
                ForgetInMemory(unit.Instance);
            }
        }
        slot.State = unit.Row is { Status: InstanceStatus.Running } ? unit.State : null;
        slot.Busy = false;
        // Successful standalone completion is a new stimulus for earlier deferred input. Revisit only after its
        // checkpoint lands; identity refusals have no row and must not activate that authored input.
        if (unit.Row is not null && unit.Consumed is [var completion] && _type.SingleMessage(completion.Message))
            foreach (var held in slot.Inbox.Where(i => i.Held && i.InboxOrder < completion.InboxOrder)) held.Held = false;
        if (!Has(Flaw.EmitBeforeCommit)) Release(unit);
        for (var i = 0; i < unit.Replied.Count; i++)
        {
            var reply = unit.Replied[i];
            Send(reply.To, $"{Origin(unit)}#{unit.Revision}.reply:{i}", reply.Channel, reply.Item);
        }
        // A load, poll or gate release can encounter any of this actor's jobs while its checkpoint is busy.
        foreach (var ticket in slot.Inbox.Where(i => i.Key is not null).ToArray()) StartJob(ticket);
        if (slot.Row is null && slot.Inbox.Count == 0) _slots.Remove(unit.Instance);
        Evict();
    }

    /// <summary>
    /// Evictable: its committed row is all there is to it. Not while a turn or commit is in flight (a load would read
    /// the row before that commit), not while messages wait for it, not while a caller waits for its output.
    /// </summary>
    private bool Evictable(Slot slot) => slot.Loaded && (!slot.Busy || Has(Flaw.EvictBusy)) && slot.Inbox.Count == 0 && slot.Replies.Count == 0 && !slot.Unwritten;

    /// <summary>
    /// Decision 6: a caller's reply address expires at its deadline (the route's timeout). The owner then forgets it, so
    /// it no longer keeps the instance in memory, and sends it nothing more; the caller has had its timeout.
    /// </summary>
    private bool Expired(string to) => !Has(Flaw.KeepExpiredCallers) && ReplyTo.Parse(to).Deadline <= Now;

    private void Unpin()
    {
        foreach (var slot in _slots.Values) slot.Replies.RemoveWhere(Expired);
    }

    private void Evict()
    {
        Unpin();
        var idle = _slots.Where(s => Evictable(s.Value)).ToList();
        foreach (var (id, _) in idle.OrderBy(s => s.Value.Used).Take(idle.Count - _options.MaxInstances).ToList())
        {
            _slots.Remove(id);
            Evicted++;
        }
    }

    /// <summary>
    /// Live output, released only now: the turn's commit has landed, or it had nothing to write. A message that carried a
    /// reply address joins the instance's callers; they get what the turn emitted, and the end when the instance ends.
    /// </summary>
    private void Release(TurnOutcome unit, bool early = false)
    {
        if (unit.Slot is not { } slot) return;
        foreach (var item in unit.Consumed)
            if (item.Message.ReplyTo is { } to && !Expired(to) && !unit.Replied.Any(r => r.Message == item.Message.Id)) slot.Replies.Add(to);
        var callers = slot.Replies.Where(to => Has(Flaw.EmitBeforeCommit) || ReplyTo.Parse(to).BeforeCommit == early).ToList();
        for (var i = 0; i < unit.Emitted.Count; i++)
            foreach (var to in callers) Send(to, $"{Origin(unit)}#{unit.Revision}.{i}", unit.Emitted[i].Channel, unit.Emitted[i].Item);
        if (early || unit.Status == InstanceStatus.Running) return;
        foreach (var to in slot.Replies) Send(to, Origin(unit) + "#end", null, null, unit.Status, unit.Status is null ? "missing" : null);
        slot.Replies.Clear();
    }

    private static string Origin(TurnOutcome unit) => CreationProtocol.Origin(unit.Instance, (unit.Row ?? unit.Slot?.Row)?.Creation ?? 0);

    private void Send(string to, string key, string? channel, object? item, InstanceStatus? status = null, string? error = null)
    {
        if (Expired(to)) return;        // decision 6: past its deadline the reply is dropped
        var (node, request, _, _) = ReplyTo.Parse(to);
        _options.Transport?.Send(node, new Reply(request, key, channel, item, status, error));
    }

    /// <summary>For a recipient in the same partition the owner hands the message over in memory; its row says seen already.</summary>
    private void HandOver(TurnOutcome unit, Queue<long>? seqs, bool hold)
    {
        foreach (var message in unit.Sent)
        {
            var durable = !unit.Volatile.Contains(message.Id);
            var seq = durable && seqs is not null ? seqs.Dequeue() : long.MaxValue / 2 + ++_local;
            if (unit.Promoted.Contains(message.Id))     // held in memory already; from now on a row, seen at this epoch if handed over
            {
                if (_items.TryGetValue(message.Id, out var held))
                {
                    held.Seq = seq;
                    if (!hold)
                    {
                        // Keep the existing job/result, but do not mark or deliver it until poll reaches its row.
                        if (message.Due is null || message.Due <= Now + (long)_options.Horizon.TotalMilliseconds) _behind.Add(message.Id);
                        (held.Durable, held.Marked, held.AwaitPoll, held.Taken) = (true, false, true, false);
                        if (_slots.TryGetValue(message.Target.Id, out var waiting))
                        {
                            if (held.CompletedOrder == 0) waiting.Inbox.Remove(held);
                            if (!waiting.Inbox.Contains(held)) Insert(waiting, held);
                        }
                        continue;
                    }
                    (held.Durable, held.Marked) = (true, hold);
                    if (held.Taken) held.Taken = false;
                    if (_slots.TryGetValue(message.Target.Id, out var slot))
                    {
                        // Equal-priority completed results were inserted at the head in completion order.
                        // Keep their existing positions: re-inserting equal sequence numbers reverses that order.
                        if (held.CompletedOrder == 0) slot.Inbox.Remove(held);
                        if (!slot.Inbox.Contains(held)) Insert(slot, held);
                    }
                    if (held.Ticket && held.Key is not null && held.Cancel is null) StartJob(held);
                }
                continue;
            }
            if ((hold || !durable) && message.Target.Partition == Partition) Hold(message, seq, durable, marked: true, fresh: unit.Fresh.Contains(message.Id));
        }
    }

    /// <summary>A trial ended somewhere in the process: background jobs that waited for it may start.</summary>
    internal void Unblocked()
    {
        foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToList()) StartJob(ticket);
    }

    private void Forget(Delivery item)
    {
        _items.Remove(item.Message.Id);
        if (_trial.Remove(item) && _trial.Count == 0) _gate.Release(this);     // the trial is over: what waited for it may start
        if (item.Durable && _polling && !Has(Flaw.PollRace)) _consumed.Add(item.Message.Id);
        if (item.Key is not null) item.Cancel?.Cancel();
        if (_slots.TryGetValue(item.Message.Target.Id, out var slot)) slot.Inbox.Remove(item);
        _meets.Remove(item);
    }
}

/// <summary>One per process. At most one of its workers has a trial in flight, and then the others start nothing.</summary>
public sealed class TrialGate
{
    internal readonly List<Worker> Workers = [];
    internal Worker? Holder;

    internal void Release(Worker worker)
    {
        if (Holder != worker) return;
        Holder = null;
        foreach (var each in Workers) each.Unblocked();
    }
}

internal sealed class Slot
{
    public long ActiveCreation;    // volatile latch captured by background drivers; retired/missing is -1
    public InstanceRow? Row;        // committed actor row or retired lifecycle predecessor
    public InstanceRow? CurrentRow => Row is { Retired: false } row ? row : null;
    public Message? Starting;     // semantic creation envelope, including a failed initial turn; excludes transport retry identity
    public object? State;           // the live state between turns, equal to Row.State
    public bool Loaded, Busy;
    public bool Driving;            // a model turn, as distinct from a queued commit, is in flight
    public long TurnFence;          // invalidates an uncommitted turn during graceful stand-down
    public Action? UndoTurn;        // unwind only the invalidated turn's provisional cancellations
    public long Used;               // when a turn last started or it was loaded: the least recent idle one goes first
    public readonly List<Delivery> Inbox = [];
    public object? Ambient;                         // from the starter on this node; gone with this memory
    public readonly HashSet<string> Replies = [];   // callers waiting for what the instance emits
    public bool Unwritten;                          // decision 4: Row is in memory only, never written
    public long Until;                              // and may stay so until then
    public object? Pending;                         // the history its turns in memory left, the actor type's
}

internal sealed record JobTicket(string Model, string Key, string? Arguments);

internal sealed record JobContext(InstanceRef Instance, string Key, string? Arguments, long Creation = 0) : IJob;
