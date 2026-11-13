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

    /// <summary>Called when a turn starts, with the worker's idea of database time. For tests.</summary>
    public Action<InstanceRef, IReadOnlyList<Message>, long>? OnTurn { get; init; }
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

    /// <summary>The instances held in memory only, never written (decision 4). For tests.</summary>
    public IEnumerable<string> Unwritten => _slots.Where(s => s.Value.Unwritten).Select(s => s.Key);

    /// <summary>The reply addresses of the callers this owner holds, for tests of decision 6.</summary>
    public IEnumerable<string> Callers => _slots.Values.SelectMany(s => s.Replies);
    private long _used;

    /// <summary>Database time as far as this worker knows: the latest reading plus what its monotonic clock has counted since.</summary>
    public long Now => Has(Flaw.WallClockTimers) ? _options.WallClock!() : _anchor + _options.Monotonic();

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

    /// <summary>A job's ticket: derived from instance and key, so a later turn can delete it without holding it.</summary>
    public static string TicketId(string instance, string key) => $"{instance}!{key}";

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
    /// ready (in id order), and flush. Zero when the pass found work at its start; otherwise the time until the
    /// earliest timer held in memory, or null. A cancelled token stops it before the next turn and before the flush.
    /// </summary>
    public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!Owns) return null;
        if (_woken)
        {
            _woken = false;
            await PollAsync(cancellationToken);
        }
        var busy = NeedsLoad || NeedsMeet || NeedsFlush || Ready.Any();
        await LoadAsync(cancellationToken);
        await MeetAsync(cancellationToken);
        foreach (var id in Ready.Order(StringComparer.Ordinal).ToList())
        {
            if (cancellationToken.IsCancellationRequested) break;
            RunTurn(id);
        }
        if (!cancellationToken.IsCancellationRequested) await FlushAsync(cancellationToken);
        Unpin();
        if (busy) return TimeSpan.Zero;
        var now = Now;
        var next = _items.Values.Where(i => !i.Taken && i.Message.Due is { } due && due > now).Select(i => i.Message.Due!.Value)
            .Concat(_slots.Values.Where(s => s.Unwritten && s.Until > now).Select(s => s.Until)).DefaultIfEmpty(long.MaxValue).Min();     // or an instance's short wait ends
        return next == long.MaxValue ? null : TimeSpan.FromMilliseconds(next - now);
    }

    // ---- ownership ---------------------------------------------------------------------------------------------

    internal async Task TakeAsync(CancellationToken cancellationToken = default)
    {
        if (_taking) return;
        _taking = true;
        Lease? lease = null;
        try { lease = await _store.TakeAsync(Partition, Node, _options.Lease, cancellationToken); }
        catch (Exception) { /* not taken, or taken without knowing: the next take bumps the epoch again */ }
        finally { _taking = false; }
        if (lease is null) return;
        Reset();    // a take starts from empty memory
        Observe(lease.Now);
        Epoch = lease.Epoch;
        Position = lease.Position;
        _mine.Add(lease.Epoch);
    }

    /// <summary>Stand down: drop what is in memory and give the partition up. Whoever takes it next finds no death.</summary>
    internal async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (!Owns) return;
        // Decision 4: a stand-down hands its partition over, so what it holds in memory only is written first, unless a
        // commit is in flight (then those instances are lost with this memory, as in a crash, and their callers told).
        if (!_flushing && !Has(Flaw.ForgetShortWaits))
        {
            foreach (var (_, slot) in _slots.Where(s => s.Value is { Unwritten: true, Busy: false }).ToList())
            {
                slot.Busy = true;
                var unit = Bound(_type.Write(slot.Row!, slot.State, slot.Pending), slot);
                Promote(unit);
                _queue.Add(unit);
            }
            if (_queue.Count > 0) await FlushAsync(cancellationToken);
            if (!Owns) return;      // the flush lost the fence: the partition is someone else's already
        }
        var epoch = Epoch;
        Reset();
        try { await _store.ReleaseAsync(Partition, epoch, cancellationToken); }
        catch (Exception) { /* then the lease runs out instead */ }
    }

    /// <summary>Recovery after any doubt is the same path as start-up: drop everything; the host takes the partition again.</summary>
    private void Reset()
    {
        _generation++;
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
        if (_polling || !Owns) return;
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
        foreach (var row in polled.Messages)
            Hold(row.Message, row.Seq, durable: true, marked: false, attempts: row.Attempts, suspect: Suspect(row, polled.Now), again: row.Seen > 0 || row.Trial > 0);
        // Finding F1: caught up when a poll comes back short and every row this owner wrote meanwhile has come back with it.
        if (polled.Messages.Count < _options.PollLimit && _behind.Count == 0) _caughtUp = true;
    }

    /// <summary>
    /// A message handed to the owner in memory and never written: a start without a caller's key. <paramref name="fresh"/>
    /// says the target's id was just minted, so no read is needed to know that it does not exist.
    /// </summary>
    public bool Post(Message message, bool fresh = false, object? ambient = null)
    {
        if (!Owns || message.Target.Partition != Partition) return false;
        Hold(message, long.MaxValue / 2 + ++_local, durable: false, marked: true, fresh: fresh);
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
            StartJob(item);
        }
        if (!_slots.TryGetValue(message.Target.Id, out var slot)) _slots[message.Target.Id] = slot = new Slot { Loaded = fresh };
        Insert(slot, item);
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
    private bool Withheld(Slot slot) => slot.Row is { } row && _type.Withholds(row, this);

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
            if (suspended && !control) continue;    // a suspended instance is given nothing; its messages wait in order
            if (Awaits(item)) break;    // nothing overtakes a message that waits for its mark
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
            if (_slots.TryGetValue(id, out var slot) && !slot.Loaded) (slot.Row, slot.Loaded, slot.Used) = (found.GetValueOrDefault(id), true, ++_used);
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
        if (!Owns || !_slots.TryGetValue(instance, out var slot) || !slot.Loaded || slot.Busy) return;
        if (Blocked || (_trial.Count > 0 && !OnTrial(instance))) return;
        var due = Deliverable(instance, slot);
        if (due.Count == 0) return;
        if (slot.Unwritten && due.Exists(i => i.Durable || Kinds.IsControl(i.Message.Kind)) && !Has(Flaw.NoWriteFirst))
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
        var creator = slot.Row is null ? due.FindIndex(i => i.Message.Start is not null) : -1;
        var spec = creator < 0 ? null : due[creator].Message.Start;
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
            unit = _type.Park(input.Row, input.Actor, input.Taken, input.Spec, e.Message, input.Unwritten ? input.Pending : null);   // a turn that throws parks its instance
        }
        if (generation == _generation) Enqueue(Bound(unit, input.Slot));   // otherwise it ran under an older epoch: discarded
    }

    private static TurnOutcome Bound(TurnOutcome unit, Slot slot)
    {
        unit.Slot = slot;
        return unit;
    }

    /// <summary>The turn has started: for tests (<see cref="WorkerOptions.OnTurn"/>).</summary>
    internal void Started(string instance, IReadOnlyList<Message> inbox) => _options.OnTurn?.Invoke(new InstanceRef(instance), inbox, Now);

    // ---- background jobs -------------------------------------------------------------------------------------------

    private void StartJob(Delivery ticket)
    {
        if (ticket.Cancel is not null || ticket.Taken || Awaits(ticket) || Blocked || (_trial.Count > 0 && !_trial.Contains(ticket))) return;
        ticket.Cancel = new CancellationTokenSource();
        _ = DriveJob(ticket, ticket.Message.Read<JobTicket>());
    }

    private async Task DriveJob(Delivery ticket, JobTicket job)
    {
        var generation = _generation;
        string? result = null, error = null;
        try { result = Json.Write(await _type.ExecuteJobAsync(job.Model, new JobContext(ticket.Message.Target, job.Key, job.Arguments), ticket.Cancel!.Token)); }
        catch (Exception e) { error = e.Message; }
        if (generation != _generation || ticket.Taken) return;      // reset, or cancelled beside completion
        // The ticket becomes the result: same id, so it is consumed once, by the turn that handles it.
        ticket.Original ??= ticket.Message;
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
        foreach (var (id, slot) in Outgrown().ToList())     // decision 4: past its short wait an instance in memory is written as it is
        {
            slot.Busy = true;
            var unit = Bound(_type.Write(slot.Row!, slot.State, slot.Pending), slot);
            Promote(unit);
            _queue.Add(unit);
        }
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
                {
                    // An instance kept in memory has no accepted row: it is parked without the state the store refused.
                    var park = Bound(slot.Unwritten ? _type.Park(slot.Row! with { State = "null" }, unit.Instance, unit.Consumed, unit.Spec, _refusal, slot.Pending)
                        : _type.Park(slot.Row, unit.Instance, unit.Consumed, unit.Spec, _refusal), slot);
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
            Committed result;
            try { result = await _store.CommitAsync(batch, cancellationToken); }
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
            foreach (var item in marks.Concat(trial))
                if (item.Suspect && !trial.Contains(item)) item.Shelved = true;
                else item.Marked = true;
            var seqs = new Queue<long>(result.Seqs);
            foreach (var unit in units) Apply(unit, seqs, handOver);
            foreach (var item in marks.Concat(trial))
                if (item.Key is not null && _items.ContainsKey(item.Message.Id)) StartJob(item);
            return Outcome.Landed;
        }
    }

    /// <summary>A message held in memory and never written: one that needs no delete when it is cancelled.</summary>
    private bool InMemoryOnly(string id) => _items.TryGetValue(id, out var held) && !held.Durable;

    /// <summary>Decision 4: instances in memory whose short wait is over, and that no turn or commit has now.</summary>
    private IEnumerable<KeyValuePair<string, Slot>> Outgrown()
    {
        var now = Now;
        return _slots.Where(s => s.Value.Unwritten && !s.Value.Busy && now >= s.Value.Until);
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
        foreach (var item in _items.Values.Where(i => !i.Durable && i.Message.Target.Id == unit.Instance && !unit.Consumed.Contains(i)
                     && !unit.Cancelled.Contains(i.Message.Id)).OrderBy(i => i.Seq).ToList())
        {
            unit.Sent.Add(item.Original ?? item.Message);
            unit.Promoted.Add(item.Message.Id);
        }
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
                if (message.Target.Partition == Partition && message.Due is null && !unit.Volatile.Contains(message.Id) && !unit.Promoted.Contains(message.Id)) _behind.Add(message.Id);
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
        if (unit.Row is not null) slot.Row = unit.Row;
        if (unit.Delete) slot.Row = null;
        slot.State = unit.Row is { Status: InstanceStatus.Running } ? unit.State : null;
        slot.Busy = false;
        if (!Has(Flaw.EmitBeforeCommit)) Release(unit);
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
            if (item.Message.ReplyTo is { } to && !Expired(to)) slot.Replies.Add(to);
        var callers = slot.Replies.Where(to => Has(Flaw.EmitBeforeCommit) || ReplyTo.Parse(to).BeforeCommit == early).ToList();
        for (var i = 0; i < unit.Emitted.Count; i++)
            foreach (var to in callers) Send(to, $"{unit.Instance}#{unit.Revision}.{i}", unit.Emitted[i].Channel, unit.Emitted[i].Item);
        if (early || unit.Status == InstanceStatus.Running) return;
        foreach (var to in slot.Replies) Send(to, unit.Instance + "#end", null, null, unit.Status, unit.Status is null ? "missing" : null);
        slot.Replies.Clear();
    }

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
                if (_items.TryGetValue(message.Id, out var held)) (held.Durable, held.Marked) = (true, hold);
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
    public InstanceRow? Row;        // the committed row; null while the instance does not exist
    public object? State;           // the live state between turns, equal to Row.State
    public bool Loaded, Busy;
    public long Used;               // when a turn last started or it was loaded: the least recent idle one goes first
    public readonly List<Delivery> Inbox = [];
    public object? Ambient;                         // from the starter on this node; gone with this memory
    public readonly HashSet<string> Replies = [];   // callers waiting for what the instance emits
    public bool Unwritten;                          // decision 4: Row is in memory only, never written
    public long Until;                              // and may stay so until then
    public object? Pending;                         // the history its turns in memory left, the actor type's
}

internal sealed record JobTicket(string Model, string Key, string? Arguments);

internal sealed record JobContext(InstanceRef Instance, string Key, string? Arguments) : IJob;
