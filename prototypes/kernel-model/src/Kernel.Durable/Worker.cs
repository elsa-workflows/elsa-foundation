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
    private long _generation, _anchor = long.MinValue, _local;
    private long _renewed;      // database time of the take, or of the sending of the last commit that landed: when the lease was renewed at the latest
    private long _safeUntil;    // local monotonic deadline, measured from the start of a successful take or commit
    private long _expiredGeneration = -1; // monotonically latched by an off-turn callback; only the owner resets state
    private bool _taking, _polling, _loading, _flushing, _meeting, _caughtUp, _woken;
    private string _refusal = "";
    private readonly HashSet<Delivery> _trial = [];     // the suspects being tried now, with nothing else in flight (finding F2)
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
    public bool NeedsFlush => _queue.Count > 0 || _items.Values.Any(Unmarked) || NextTrial().Count > 0;
    public IEnumerable<string> Trial => _trial.Select(i => i.Message.Id);
    public bool NeedsMeet => _meets.Exists(i => !Awaits(i) && !i.Taken);
    public IEnumerable<string> Ready => _slots.Where(s => s.Value.Loaded && !s.Value.Busy && Deliverable(s.Key, s.Value).Count > 0).Select(s => s.Key)
        .Where(id => !Blocked && (_trial.Count == 0 || OnTrial(id)));    // while a trial runs, nothing else in the process does

    // What is in memory, for tests of invariant 9.
    public IEnumerable<(string Id, InstanceRow? Row, string? State, bool Busy)> Instances =>
        _slots.Where(s => s.Value.Loaded).Select(s => (s.Key, s.Value.Row, s.Value.State is null ? null : Json.Write(s.Value.State), s.Value.Busy));
    public IEnumerable<(string Id, bool Durable, bool Taken)> Messages => _items.Values.Select(i => (i.Message.Id, i.Durable, i.Taken));

    /// <summary>
    /// Diagnostic: capture before a ticket is removed. The task ends after its driver queues or discards the result;
    /// it does not establish that a model consumed the result or committed a logical job outcome.
    /// </summary>
    public Task? JobCompletion(string instance, string key, long creation = 0) =>
        _items.GetValueOrDefault(TicketId(instance, key, creation)) is { } ticket
            && ticket.Message.Target.Id == instance && ticket.Key == key ? ticket.JobDriver : null;

    public bool Has(Flaw flaw) => (_options.Flaws & flaw) != 0;
    public int Partitions => _options.Partitions;

    /// <summary>A turn cancelled a job. Its ticket may be in memory, or still on its way in with a poll.</summary>
    internal void Cancelling(string ticket)
    {
        _cancelling.Add(ticket);
        if (_items.TryGetValue(ticket, out var held)) held.Taken = true;
    }
    void ITurnServices.Cancelling(string ticket) => Cancelling(ticket);

    /// <summary>A job's ticket: derived from instance and key, so a later turn can delete it without holding it.</summary>
    public static string TicketId(string instance, string key, long creation = 0) => creation > 0 ? $"{instance}@{creation}!{key}" : $"{instance}!{key}";

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
        var next = _items.Values.Where(i => !i.Taken && i.Message.Due is { } due && due > now).Select(i => i.Message.Due!.Value - now).DefaultIfEmpty(long.MaxValue).Min();
        var caller = Callers.Select(to => ReplyTo.Parse(to).Deadline - now).DefaultIfEmpty(long.MaxValue).Min();
        return TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(caller, Math.Min(next, RenewBy - DbNow))));     // never zero: zero means there is work
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
        if (!Owns) return;
        var epoch = Epoch;
        Reset();
        try { await _store.ReleaseAsync(Partition, epoch, cancellationToken); }
        catch (Exception) { /* then the lease runs out instead */ }
    }

    /// <summary>Recovery after any doubt is the same path as start-up: drop everything; the host takes the partition again.</summary>
    private void Reset()
    {
        Interlocked.Increment(ref _generation);
        Epoch = 0;
        _polling = _loading = _flushing = _meeting = false;
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
        if (!LeaseUsable() || message.Target.Partition != Partition) return false;
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
        // The owner skips ids it holds, and (finding F5) ids it consumed while the poll that returned them was in flight.
        if (!Has(Flaw.PollWithoutSkip) && (_items.ContainsKey(message.Id) || _consumed.Contains(message.Id))) return;
        _behind.Remove(message.Id);
        // A recalled message (Recall) whose row a poll brings in before the commit that deletes it lands: taken, never given.
        var item = new Delivery(message, seq) { Durable = durable, Marked = marked, Suspect = suspect, Attempts = attempts, Again = again, Taken = _cancelling.Contains(message.Id) };
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
        var at = slot.Inbox.FindIndex(i => i.Seq > item.Seq);
        slot.Inbox.Insert(at < 0 ? slot.Inbox.Count : at, item);    // a target's messages are handled in seq order
    }

    private bool Unmarked(Delivery item) => item.Durable && !item.Marked && !item.Taken && (!item.Suspect || (!item.Shelved && Shelf(item)));

    /// <summary>
    /// Finding F12: a suspect whose instance is suspended cannot be tried, and while its row says unseen it fills a place
    /// in every poll; a poll limit of them hides the resume behind them. So it is shelved: its row is marked seen at this
    /// epoch, which moves the polls on, but it is neither on trial nor cleared to run. The next owner still suspects it.
    /// </summary>
    private bool Shelf(Delivery suspect) => !Has(Flaw.NoShelf) && _slots.TryGetValue(suspect.Message.Target.Id, out var slot) && Withheld(slot);

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
        foreach (var suspect in suspects)
        {
            // A member must not wait behind an older suspect of the same target that is not in the group.
            var target = suspect.Message.Target.Id;
            if (!skipped.Contains(target) && CanTry(suspect) && (attempts ??= suspect.Attempts) == suspect.Attempts && group.Count < Math.Max(1, _options.TrialGroup >> attempts.Value)) group.Add(suspect);
            else skipped.Add(target);
        }
        return group;
    }

    /// <summary>
    /// A trial holds up the process until its members are consumed, so a suspect is tried only when its turn can run at
    /// once: its instance is loaded, has nothing in flight, is not suspended, and no kernel message waits ahead of it.
    /// </summary>
    private bool CanTry(Delivery suspect) =>
        _slots.TryGetValue(suspect.Message.Target.Id, out var slot) && (slot.Loaded || Has(Flaw.TrialBeforeLoad))
        && (!slot.Busy || Has(Flaw.SuspendDuringTrial) || Has(Flaw.TrialBeforeLoad)) && !Withheld(slot)
        && !slot.Inbox.TakeWhile(i => i != suspect).Any(i => Kinds.IsControl(i.Message.Kind));

    /// <summary>
    /// Finding F3: a polled message is not acted on until a fenced commit has marked it seen at this epoch (and bumped
    /// its attempts if an earlier owner had it). The sketch's rule covers only rows an earlier owner had marked.
    /// </summary>
    private bool Awaits(Delivery item) => item.Durable && !item.Marked && (item.Suspect || !Has(Flaw.RunBeforeMark));

    private List<Delivery> Deliverable(string instance, Slot slot)
    {
        var now = Now;
        var due = new List<Delivery>();
        var suspended = Withheld(slot);
        foreach (var item in slot.Inbox)
        {
            if (item.Ticket || item.Taken) continue;    // taken here: recalled by a commit in flight
            var control = Kinds.IsControl(item.Message.Kind);
            var identityReply = item.Message.Start?.Creation is not null || CreationProtocol.TargetError(slot.Row, item.Message) is not null;
            if (suspended && !control && !identityReply) continue;    // identity refusal does not activate suspended authored work
            if (Awaits(item)) break;    // nothing overtakes a message that waits for its mark
            // Qualified creation envelopes are dispatched individually, including stale controls before hydration.
            // This first prototype favors an explicit boundary over batching mixed creation commands.
            if (slot.Row?.Creation > 0 || item.Message.TargetCreation is not null || item.Message.Start?.Creation is not null)
            {
                if (due.Count == 0 && (item.Message.Due is not { } dueAt || dueAt <= now)) due.Add(item);
                break;
            }
            if (control)
            {
                // The kernel's, alone, after what came before it, except held messages, which wait for something new and
                // would wait for ever behind it. A suspend waits while the instance has a message on trial, or the trial
                // could never end.
                if (due.TrueForAll(i => i.Held) && !(item.Message.Kind == Kinds.Suspend && OnTrial(instance) && !Has(Flaw.SuspendDuringTrial))) due = [item];
                break;
            }
            if (item.Message.Due is not { } at || at <= now) due.Add(item);
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
        if (!LeaseUsable() || !_slots.TryGetValue(instance, out var slot) || !slot.Loaded || slot.Busy) return;
        if (Blocked || (_trial.Count > 0 && !OnTrial(instance))) return;
        var due = Deliverable(instance, slot);
        if (due.Count == 0) return;
        slot.Inbox.RemoveAll(due.Contains);
        foreach (var item in due) (item.Taken, item.Held) = (true, false);
        (slot.Busy, slot.Used) = (true, ++_used);
        var request = due[0].Message;
        var creationError = request.Start?.Creation is not null
            ? !CreationIdentityAvailable ? "CreationIdentityUnavailable"
                : request.TargetCreation is { } target && target != request.Start.Creation.Generation ? "WrongCreation"
                : CreationProtocol.StartError(slot.Row, request.Start)
            : CreationProtocol.TargetError(slot.Row, request);
        if (creationError is not null)
        {
            Enqueue(Bound(CreationProtocol.Refuse(instance, slot.Row, due, creationError), slot));
            return;
        }
        var creator = slot.Row is null or { Retired: true } ? due.FindIndex(i => i.Message.Start is not null) : -1;
        var spec = creator < 0 ? null : due[creator].Message.Start;
        slot.Starting = spec;
        var input = new TurnInput(this, slot, instance, due, Math.Max(creator, 0), spec);
        if (_type.Command(input) is { } command)
            Enqueue(Bound(command, slot));
        else if (due.Find(i => i.Attempts >= _options.MaxAttempts && i.Message.Kind != Kinds.JobFinished) is { } poison)
            Enqueue(Bound(_type.Park(slot.Row, instance, due, spec, $"message {poison.Message.Id} was on trial when {poison.Attempts} owners were lost"), slot));
        else if (slot.Row is null && spec is null)                   // no target and no start spec: dropped and journalled
            Enqueue(new TurnOutcome(instance) { Slot = slot, Consumed = due, Journal = Json.Write(due.Select(i => "dropped " + i.Message.Id)), Status = null });
        else
            _ = Drive(input);
    }

    private async Task Drive(TurnInput input)
    {
        var generation = _generation;
        TurnOutcome unit;
        try { unit = await _type.TurnAsync(input); }
        catch (Exception e)
        {
            if (generation != _generation) return;
            unit = _type.Park(input.Row, input.Actor, input.Taken, input.Spec, e.Message);   // a turn that throws parks its instance
        }
        if (generation == _generation) Enqueue(Bound(unit, input.Slot));   // otherwise it ran under an older epoch: discarded
    }

    private static TurnOutcome Bound(TurnOutcome unit, Slot slot)
    {
        unit.Slot = slot;
        if (unit.Row is { Retired: false } row)
        {
            if (slot.Row is null or { Retired: true } && slot.Starting?.Creation is { } start)
            {
                unit.Row = row with { Creation = start.Generation, StartIntent = CreationProtocol.Fingerprint(slot.Starting), ParentCreation = slot.Starting.ParentCreation };
                unit.Expected = slot.Row?.Revision;
                unit.Revision = unit.Row.Revision;
            }
            else if (slot.Row is { Creation: > 0 } previous)
                unit.Row = row with { Creation = previous.Creation, StartIntent = previous.StartIntent, ParentCreation = previous.ParentCreation };
        }
        return unit;
    }

    /// <summary>The turn has started: for tests (<see cref="WorkerOptions.OnTurn"/>).</summary>
    internal void Started(string instance, IReadOnlyList<Message> inbox) => _options.OnTurn?.Invoke(new InstanceRef(instance), inbox, Now);

    // ---- background jobs -------------------------------------------------------------------------------------------

    private void StartJob(Delivery ticket)
    {
        Slot? creationSlot = null;
        if (ticket.Message.TargetCreation is not null)
        {
            if (!_slots.TryGetValue(ticket.Message.Target.Id, out creationSlot) || !creationSlot.Loaded) return;
            if (CreationProtocol.TargetError(creationSlot.Row, ticket.Message) is not null)
            {
                ticket.Cancel?.Cancel();
                ticket.Ticket = false; ticket.Key = null;
                return; // the normal fenced dispatch consumes/refuses this stale target
            }
        }
        // Callers finish their current collection operation before standing down, so a refused ticket is never stranded.
        if (!CanStartWork || ticket.Cancel is not null || ticket.Taken || Awaits(ticket) || Blocked || (_trial.Count > 0 && !_trial.Contains(ticket))) return;
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
        ticket.Message = ticket.Message with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(job.Key, result, error)) };
        ticket.Ticket = false;
        // Finding F7: a result goes ahead of whatever else waits, so a turn never sees a cancel first and the result after it.
        ticket.Seq = 0;
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
        if (unit.Durable) _queue.Add(unit);
        else Apply(unit, null, true);   // nothing to write: an ephemeral run, or messages that were never rows
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => FlushAsync(false, cancellationToken);

    /// <summary>An empty commit: renews the lease of an idle partition and tells a stale owner that it is one.</summary>
    public Task BeatAsync(CancellationToken cancellationToken = default) => FlushAsync(true, cancellationToken);

    private async Task FlushAsync(bool beat, CancellationToken cancellationToken)
    {
        if (_flushing || !Owns) return;
        var units = _queue.GetRange(0, Math.Min(_queue.Count, _options.MaxBatch));     // rows per flush are capped
        var marks = _items.Values.Where(Unmarked).Take(_options.MaxBatch).ToList();
        var trial = units.Count == _queue.Count ? NextTrial() : [];    // a trial starts only when this commit leaves nothing else in flight
        if (units.Count == 0 && marks.Count == 0 && trial.Count == 0 && !beat) return;
        _queue.RemoveRange(0, units.Count);
        if (!Has(Flaw.BumpAllInherited)) _trial.UnionWith(trial);      // from here on nothing else starts
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
                    outcome = await CommitAsync([Bound(_type.Park(slot.Row, unit.Instance, unit.Consumed, unit.Spec, _refusal), slot)], marks, trial, generation, cancellationToken);
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
                Has(Flaw.KeepConsumedMessages) ? [] : [.. u.Consumed.Where(i => i.Durable && i.Message.KeepUntil is null).Select(i => i.Message.Id), .. Has(Flaw.VolatileJobTicket) ? [] : u.Cancelled],
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
        foreach (var item in unit.Consumed) Forget(item);
        foreach (var item in unit.Held)     // back in its place; its turn ran, so it is no suspect and no longer on trial
        {
            (item.Taken, item.Held, item.Suspect, item.Marked) = (false, true, false, true);
            Insert(unit.Slot!, item);
            if (_trial.Remove(item) && _trial.Count == 0) _gate.Release(this);
        }
        foreach (var id in unit.Cancelled)      // a cancelled job's ticket goes whether or not this owner had taken it into memory yet
        {
            _cancelling.Remove(id);
            if (_items.TryGetValue(id, out var ticket)) Forget(ticket);
            else if (_polling && !Has(Flaw.PollRace)) _consumed.Add(id);
        }
        if (!handOver)
            foreach (var message in unit.Sent)
                if (message.Target.Partition == Partition && message.Due is null && !unit.Volatile.Contains(message.Id)) _behind.Add(message.Id);
        if (unit.Slot is { } committed)
        {
            if (unit.Row is not null) committed.Row = unit.Row;
            if (unit.Delete && unit.Row is not { Retired: true }) committed.Row = null;
            Volatile.Write(ref committed.ActiveCreation, committed.Row is { Retired: false } row ? row.Creation : -1);
        }
        HandOver(unit, seqs, hold: handOver && !Has(Flaw.DeliverBeforeCommit));
        if (unit.Slot is not { } slot) return;
        slot.State = unit.Row is { Status: InstanceStatus.Running } ? unit.State : null;
        slot.Busy = false;
        if (!Has(Flaw.EmitBeforeCommit)) Release(unit);
        for (var i = 0; i < unit.Replied.Count; i++)
        {
            var reply = unit.Replied[i];
            Send(reply.To, $"{Origin(unit)}#{unit.Revision}.reply:{i}", reply.Channel, reply.Item);
        }
        if (slot.Row is null && slot.Inbox.Count == 0) _slots.Remove(unit.Instance);
        Evict();
    }

    /// <summary>
    /// Evictable: its committed row is all there is to it. Not while a turn or commit is in flight (a load would read
    /// the row before that commit), not while messages wait for it, not while a caller waits for its output.
    /// </summary>
    private bool Evictable(Slot slot) => slot.Loaded && (!slot.Busy || Has(Flaw.EvictBusy)) && slot.Inbox.Count == 0 && slot.Replies.Count == 0;

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

    private static string Origin(TurnOutcome unit) => (unit.Row ?? unit.Slot?.Row)?.Creation is > 0 and var creation ? $"{unit.Instance}@{creation}" : unit.Instance;

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
    public InstanceRow? Row;        // the committed row; null while the instance does not exist
    public StartSpec? Starting;     // creation intent of this staged activation, including a failed initial turn
    public object? State;           // the live state between turns, equal to Row.State
    public bool Loaded, Busy;
    public long Used;               // when a turn last started or it was loaded: the least recent idle one goes first
    public readonly List<Delivery> Inbox = [];
    public object? Ambient;                         // from the starter on this node; gone with this memory
    public readonly HashSet<string> Replies = [];   // callers waiting for what the instance emits
}

internal sealed record JobTicket(string Model, string Key, string? Arguments);

internal sealed record JobContext(InstanceRef Instance, string Key, string? Arguments, long Creation = 0) : IJob;
