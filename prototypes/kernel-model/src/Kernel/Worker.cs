namespace Kernel;

public sealed record WorkerOptions
{
    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan Horizon { get; init; } = TimeSpan.FromSeconds(10);
    public int PollLimit { get; init; } = 500;
    public int MaxBatch { get; init; } = 500;
    /// <summary>How many owners may be lost with a message on trial before it is parked.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// How many suspects are tried together at first; the group halves with every attempt. 1 tries them one at a time,
    /// which is exact and slow. A larger group recovers a backlog faster and needs MaxAttempts above its base-2 logarithm.
    /// </summary>
    public int TrialGroup { get; init; } = 1;

    /// <summary>A monotonic clock in milliseconds. Only differences are used: the database tells the time.</summary>
    public Func<long> Monotonic { get; init; } = static () => Environment.TickCount64;

    public Func<long>? WallClock { get; init; }     // read only under Flaw.WallClockTimers
    public Flaw Flaws { get; init; }

    /// <summary>Called when a step starts, with the worker's idea of database time. For tests.</summary>
    public Action<InstanceRef, IReadOnlyList<Message>, long>? OnStep { get; init; }
}

/// <summary>
/// The owner of one partition on one node. It holds instances and their messages in memory, runs steps and
/// hands their results to one committer. It is a single-threaded state machine: the host calls
/// <see cref="TakeAsync"/>, <see cref="PollAsync"/>, <see cref="LoadAsync"/>, <see cref="Run"/>,
/// <see cref="MeetAsync"/> and <see cref="FlushAsync(CancellationToken)"/> one at a time, in any order, and decides
/// when. Nothing here reads a clock, starts a thread or picks at random.
/// </summary>
public sealed class Worker
{
    private enum Outcome { Landed, Refused, Lost }

    private readonly IKernelStore _store;
    private readonly Dictionary<string, IExecutionModel> _models;
    private readonly WorkerOptions _options;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Item> _items = new(StringComparer.Ordinal);    // every message held, by id
    private readonly List<Item> _meets = [];
    private readonly HashSet<string> _cancelling = new(StringComparer.Ordinal);        // tickets of runs cancelled by a commit in flight
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);          // while a poll is in flight
    private readonly HashSet<string> _behind = new(StringComparer.Ordinal);            // own rows written while catching up, not yet polled
    private readonly List<Unit> _queue = [];
    private long _generation, _anchor = long.MinValue, _local;
    private bool _taking, _polling, _loading, _flushing, _meeting, _caughtUp;
    private string _refusal = "";
    private readonly HashSet<Item> _trial = [];     // the suspects being tried now, with nothing else in flight (finding F2)
    private readonly HashSet<long> _mine = [];      // the epochs this process has held: what it marked then did not kill it

    public Worker(string node, int partition, IKernelStore store, IEnumerable<IExecutionModel> models, WorkerOptions? options = null)
    {
        Node = node;
        Partition = partition;
        _store = store;
        _models = models.ToDictionary(m => m.Key, StringComparer.Ordinal);
        _options = options ?? new WorkerOptions();
    }

    public string Node { get; }
    public int Partition { get; }
    public long Epoch { get; private set; }
    public long Position { get; private set; }
    public bool Owns => Epoch != 0;

    /// <summary>Database time as far as this worker knows: the latest reading plus what its monotonic clock has counted since.</summary>
    public long Now => Has(Flaw.WallClockTimers) ? _options.WallClock!() : _anchor + _options.Monotonic();

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
    public IEnumerable<string> Ready => _slots.Where(s => s.Value.Loaded && !s.Value.Busy && Deliverable(s.Value).Count > 0).Select(s => s.Key)
        .Where(id => _trial.Count == 0 || OnTrial(id));    // while a trial runs, nothing else does

    // What is in memory, for tests of invariant 9.
    public IEnumerable<(string Id, InstanceRow? Row, string? State, bool Busy)> Instances =>
        _slots.Where(s => s.Value.Loaded).Select(s => (s.Key, s.Value.Row, s.Value.State is null ? null : Json.Write(s.Value.State), s.Value.Busy));
    public IEnumerable<(string Id, bool Durable, bool Taken)> Messages => _items.Values.Select(i => (i.Message.Id, i.Durable, i.Taken));

    internal bool Has(Flaw flaw) => (_options.Flaws & flaw) != 0;

    /// <summary>A step cancelled a run. Its ticket may be in memory, or still on its way in with a poll.</summary>
    internal void Cancelling(string ticket)
    {
        _cancelling.Add(ticket);
        if (_items.TryGetValue(ticket, out var held)) held.Taken = true;
    }
    internal static string TicketId(string instance, string key) => $"{instance}!{key}";

    // ---- ownership ---------------------------------------------------------------------------------------------

    public async Task TakeAsync(CancellationToken cancellationToken = default)
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
    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
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
        _generation++;
        Epoch = 0;
        _polling = _loading = _flushing = _meeting = false;
        foreach (var item in _items.Values.ToList()) item.Cancel?.Cancel();
        _slots.Clear();
        _items.Clear();
        _meets.Clear();
        _queue.Clear();
        _consumed.Clear();
        _behind.Clear();
        _cancelling.Clear();
        _caughtUp = false;
        _trial.Clear();
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
            Hold(row.Message, row.Seq, durable: true, marked: false, attempts: row.Attempts, suspect: Suspect(row, polled.Now));
        // Finding F1: caught up when a poll comes back short and every row this owner wrote meanwhile has come back with it.
        if (polled.Messages.Count < _options.PollLimit && _behind.Count == 0) _caughtUp = true;
    }

    /// <summary>
    /// A message handed to the owner in memory and never written: a start without a caller's key. <paramref name="fresh"/>
    /// says the target's id was just minted, so no read is needed to know that it does not exist.
    /// </summary>
    public bool Post(Message message, bool fresh = false)
    {
        if (!Owns || message.Target.Partition != Partition) return false;
        Hold(message, long.MaxValue / 2 + ++_local, durable: false, marked: true, fresh: fresh);
        return true;
    }

    /// <summary>
    /// A message that another owner had marked, or that is on trial already, may be what killed that owner. What this
    /// same process marked in an epoch it held earlier is not: the process is alive.
    /// </summary>
    private bool Suspect(Stored row, long now) => row.Message.Kind != Kinds.Meet && row.Attempts < _options.MaxAttempts
        && (row.Trial > 0 || (row.Seen > 0 && !_mine.Contains(row.Seen) && (row.Message.Due is not { } due || due <= now)));

    private bool OnTrial(string instance) => _trial.Any(t => t.Message.Target.Id == instance);

    private void Hold(Message message, long seq, bool durable, bool marked, bool suspect = false, int attempts = 0, bool fresh = false)
    {
        // The owner skips ids it holds, and (finding F5) ids it consumed while the poll that returned them was in flight.
        if (!Has(Flaw.PollWithoutSkip) && (_items.ContainsKey(message.Id) || _consumed.Contains(message.Id))) return;
        _behind.Remove(message.Id);
        var item = new Item(message, seq) { Durable = durable, Marked = marked, Suspect = suspect, Attempts = attempts };
        _items[message.Id] = item;
        if (message.Kind == Kinds.Meet)
        {
            item.Ticket = true;
            _meets.Add(item);
            return;
        }
        if (message.Kind == Kinds.Run && attempts >= _options.MaxAttempts)
        {
            // Off-step work that was on trial when too many owners were lost is parked: its result is an error.
            var error = $"the run was on trial when {attempts} owners were lost";
            item.Message = message with { Kind = Kinds.RunFinished, Payload = Json.Write(new RunFinished(message.Read<RunTicket>().Key, null, error)) };
        }
        else if (message.Kind == Kinds.Run)
        {
            item.Ticket = true;
            item.Key = message.Read<RunTicket>().Key;
            item.Taken = _cancelling.Contains(message.Id);
            StartRun(item);
        }
        if (!_slots.TryGetValue(message.Target.Id, out var slot)) _slots[message.Target.Id] = slot = new Slot { Loaded = fresh };
        var at = slot.Inbox.FindIndex(i => i.Seq > seq);
        slot.Inbox.Insert(at < 0 ? slot.Inbox.Count : at, item);    // a target's messages are handled in seq order
    }

    private static bool Unmarked(Item item) => item.Durable && !item.Marked && !item.Taken && !item.Suspect;

    /// <summary>
    /// Finding F2: suspects are tried apart from everything else. A fenced commit writes that they are on trial at this
    /// epoch; then only they run. If this owner's lease expires, the take that follows adds an attempt to exactly the
    /// rows on trial. So a death counts against what was on trial and never against its neighbours. A group holds
    /// suspects with the same attempts and halves with each attempt, so a poison message ends up alone.
    /// </summary>
    private List<Item> NextTrial()
    {
        if (_trial.Count > 0) return [];
        var suspects = _items.Values.Where(i => i.Durable && !i.Marked && !i.Taken && i.Suspect).OrderBy(i => i.Seq).ToList();
        if (suspects.Count == 0 || Has(Flaw.BumpAllInherited)) return suspects;
        var (attempts, group, skipped) = (suspects[0].Attempts, new List<Item>(), new HashSet<string>());
        foreach (var suspect in suspects)
        {
            // A member must not wait behind an older suspect of the same target that is not in the group.
            if (suspect.Attempts == attempts && group.Count < Math.Max(1, _options.TrialGroup >> attempts) && !skipped.Contains(suspect.Message.Target.Id)) group.Add(suspect);
            else skipped.Add(suspect.Message.Target.Id);
        }
        return group;
    }

    /// <summary>
    /// Finding F3: a polled message is not acted on until a fenced commit has marked it seen at this epoch (and bumped
    /// its attempts if an earlier owner had it). The sketch's rule covers only rows an earlier owner had marked.
    /// </summary>
    private bool Awaits(Item item) => item.Durable && !item.Marked && (item.Suspect || !Has(Flaw.RunBeforeMark));

    private List<Item> Deliverable(Slot slot)
    {
        var now = Now;
        var due = new List<Item>();
        foreach (var item in slot.Inbox)
        {
            if (item.Ticket) continue;
            if (Awaits(item)) break;    // nothing overtakes a message that waits for its mark
            if (item.Message.Due is not { } at || at <= now) due.Add(item);
        }
        return due;
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
            if (_slots.TryGetValue(id, out var slot) && !slot.Loaded) (slot.Row, slot.Loaded) = (found.GetValueOrDefault(id), true);
    }

    // ---- steps ---------------------------------------------------------------------------------------------------

    /// <summary>Run one step of an instance that is in <see cref="Ready"/>.</summary>
    public void Run(string instance)
    {
        if (!Owns || !_slots.TryGetValue(instance, out var slot) || !slot.Loaded || slot.Busy) return;
        if (_trial.Count > 0 && !OnTrial(instance)) return;
        var due = Deliverable(slot);
        if (due.Count == 0) return;
        slot.Inbox.RemoveAll(due.Contains);
        foreach (var item in due) item.Taken = true;
        slot.Busy = true;
        var creator = slot.Row is null ? due.FindIndex(i => i.Message.Start is not null) : -1;
        var spec = creator < 0 ? null : due[creator].Message.Start;
        if (slot.Row is { Status: not InstanceStatus.Running })
            Enqueue(new Unit(slot, instance) { Consumed = due });    // a finished target: consumed and dropped
        else if (due.Find(i => i.Attempts >= _options.MaxAttempts && i.Message.Kind != Kinds.RunFinished) is { } poison)
            Enqueue(Park(slot, instance, due, spec, $"message {poison.Message.Id} was on trial when {poison.Attempts} owners were lost"));
        else if (slot.Row is null && spec is null)                   // no target and no start spec: dropped and journalled
            Enqueue(new Unit(slot, instance) { Consumed = due, Journal = Json.Write(due.Select(i => "dropped " + i.Message.Id)) });
        else
            _ = Drive(new Step(this, slot, instance, due, Math.Max(creator, 0), spec));
    }

    private async Task Drive(Step step)
    {
        var generation = _generation;
        Unit unit;
        try
        {
            var model = _models[step.Instance.Model];
            step.State = step.Slot.State ?? (step.Slot.Row is { } row ? Json.Read(row.State, model.StateType) : model.CreateState(step.Spec!));
            _options.OnStep?.Invoke(step.Instance.Ref, step.Inbox, Now);
            await model.StepAsync(step, CancellationToken.None);
            unit = step.Seal();
        }
        catch (Exception e)
        {
            if (generation != _generation) return;
            unit = Park(step.Slot, step.Instance.Ref.Id, step.Taken, step.Spec, e.Message);   // a step that throws parks its instance
        }
        if (generation == _generation) Enqueue(unit);   // otherwise it ran under an older epoch: discarded
    }

    /// <summary>Park: the instance becomes faulted with the error, the messages are consumed, the partition goes on.</summary>
    private static Unit Park(Slot slot, string instance, List<Item> taken, StartSpec? spec, string error)
    {
        var row = slot.Row is { } r ? r with { Status = InstanceStatus.Faulted, Revision = r.Revision + 1, Fault = error }
            : spec is null ? null
            : new InstanceRow(instance, spec.Model, spec.Definition, InstanceStatus.Faulted, "null", 1, spec.Parent, error);
        var unit = new Unit(slot, instance) { Row = row, Expected = slot.Row?.Revision, Consumed = taken, Journal = Json.Write(new[] { "parked: " + error }) };
        if (row?.Parent is not null) unit.Sent.Add(Finished(row, null));
        return unit;
    }

    internal static Message Finished(InstanceRow row, string? output) =>
        new($"{row.Id}#{row.Revision}.end", new InstanceRef(row.Parent!), Kinds.ChildFinished, Json.Write(new ChildFinished(row.Id, row.Status, output)), row.Id);

    // ---- off-step work -------------------------------------------------------------------------------------------

    private void StartRun(Item ticket)
    {
        if (ticket.Cancel is not null || ticket.Taken || Awaits(ticket) || (_trial.Count > 0 && !_trial.Contains(ticket))) return;
        ticket.Cancel = new CancellationTokenSource();
        _ = DriveRun(ticket, ticket.Message.Read<RunTicket>());
    }

    private async Task DriveRun(Item ticket, RunTicket run)
    {
        var generation = _generation;
        string? result = null, error = null;
        try { result = Json.Write(await _models[run.Model].RunAsync(new RunContext(ticket.Message.Target, run.Key, run.Arguments), ticket.Cancel!.Token)); }
        catch (Exception e) { error = e.Message; }
        if (generation != _generation || ticket.Taken) return;      // reset, or cancelled beside completion
        // The ticket becomes the result: same id, so it is consumed once, by the step that handles it.
        ticket.Message = ticket.Message with { Kind = Kinds.RunFinished, Payload = Json.Write(new RunFinished(run.Key, result, error)) };
        ticket.Ticket = false;
        // Finding F7: a result goes ahead of whatever else waits, so a step never sees a cancel first and the result after it.
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
            var waits = tickets.SelectMany(t => t.Message.Read<SubscriptionRow[]>()).ToList();
            var events = await _store.FindEventsAsync(waits.Select(w => w.Key).Distinct().ToArray(), cancellationToken);
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
        Enqueue(new Unit(null, "") { Consumed = tickets });
    }

    // ---- the committer -------------------------------------------------------------------------------------------

    private void Enqueue(Unit unit)
    {
        if (Has(Flaw.DeliverBeforeCommit)) HandOver(unit, null, hold: true);
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
                    outcome = await CommitAsync([Park(slot, unit.Instance, unit.Consumed, unit.Spec, _refusal)], marks, trial, generation, cancellationToken);
                if (outcome == Outcome.Landed) (marks, trial) = ([], []);
            }
            if (outcome == Outcome.Refused && generation == _generation) Reset();   // nothing left to set apart
        }
        if (generation == _generation) _flushing = false;
    }

    private async Task<Outcome> CommitAsync(List<Unit> units, List<Item> marks, List<Item> trial, long generation, CancellationToken cancellationToken)
    {
        // Finding F1: until the owner has caught up with the rows it inherited, what it sends to its own partition goes
        // through the table like everything else. Handing it over in memory would let it overtake older rows.
        var handOver = _caughtUp || Has(Flaw.HandOverWhileBehind);
        var batch = new CommitBatch(Partition, Epoch, Position, _options.Lease, handOver,
            units.Select(u => new StepCommit(u.Instance, u.Row, u.Expected,
                Has(Flaw.KeepConsumedMessages) ? [] : [.. u.Consumed.Where(i => i.Durable && i.Message.KeepUntil is null).Select(i => i.Message.Id), .. Has(Flaw.VolatileRunTicket) ? [] : u.Cancelled],
                Has(Flaw.KeepConsumedMessages) ? [] : u.Consumed.Where(i => i.Durable && i.Message.KeepUntil is not null).Select(i => i.Message.Id).ToList(),
                u.Sent.Where(m => !u.Volatile.Contains(m.Id)).ToList(), u.Subscribed, u.Unsubscribed, u.Journal)).ToList(),
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
            foreach (var item in marks.Concat(trial)) item.Marked = true;
            var seqs = new Queue<long>(result.Seqs);
            foreach (var unit in units) Apply(unit, seqs, handOver);
            foreach (var item in marks.Concat(trial))
                if (item.Key is not null && _items.ContainsKey(item.Message.Id)) StartRun(item);
            return Outcome.Landed;
        }
    }

    private void Fail(List<Unit> units)
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
    private void Apply(Unit unit, Queue<long>? seqs, bool handOver)
    {
        foreach (var item in unit.Consumed) Forget(item);
        foreach (var id in unit.Cancelled)      // a cancelled run's ticket goes whether or not this owner had taken it into memory yet
        {
            _cancelling.Remove(id);
            if (_items.TryGetValue(id, out var ticket)) Forget(ticket);
            else if (_polling && !Has(Flaw.PollRace)) _consumed.Add(id);
        }
        if (!handOver)
            foreach (var message in unit.Sent)
                if (message.Target.Partition == Partition && message.Due is null && !unit.Volatile.Contains(message.Id)) _behind.Add(message.Id);
        HandOver(unit, seqs, hold: handOver && !Has(Flaw.DeliverBeforeCommit));
        if (unit.Slot is not { } slot) return;
        if (unit.Row is not null) slot.Row = unit.Row;
        slot.State = unit.Row is { Status: InstanceStatus.Running } ? unit.State : null;
        slot.Busy = false;
        if (slot.Row is null && slot.Inbox.Count == 0) _slots.Remove(unit.Instance);
    }

    /// <summary>For a recipient in the same partition the owner hands the message over in memory; its row says seen already.</summary>
    private void HandOver(Unit unit, Queue<long>? seqs, bool hold)
    {
        foreach (var message in unit.Sent)
        {
            var durable = !unit.Volatile.Contains(message.Id);
            var seq = durable && seqs is not null ? seqs.Dequeue() : long.MaxValue / 2 + ++_local;
            if ((hold || !durable) && message.Target.Partition == Partition) Hold(message, seq, durable, marked: true, fresh: unit.Fresh.Contains(message.Id));
        }
    }

    private void Forget(Item item)
    {
        _items.Remove(item.Message.Id);
        if (_trial.Remove(item) && _trial.Count == 0)      // the trial is over: what waited for it may start
            foreach (var ticket in _items.Values.Where(i => i.Key is not null).ToList()) StartRun(ticket);
        if (item.Durable && _polling && !Has(Flaw.PollRace)) _consumed.Add(item.Message.Id);
        if (item.Key is not null) item.Cancel?.Cancel();
        if (_slots.TryGetValue(item.Message.Target.Id, out var slot)) slot.Inbox.Remove(item);
        _meets.Remove(item);
    }
}

internal sealed class Slot
{
    public InstanceRow? Row;        // the committed row; null while the instance does not exist
    public object? State;           // the live state between steps, equal to Row.State
    public bool Loaded, Busy;
    public readonly List<Item> Inbox = [];
}

internal sealed class Item(Message message, long seq)
{
    public Message Message = message;
    public long Seq = seq;
    public bool Durable;            // it is a row
    public bool Marked;             // its row says seen at this epoch
    public bool Suspect;            // another owner had it: it is tried apart before anything else is believed of it
    public int Attempts;
    public bool Ticket;             // a kernel message, not (yet) for the model
    public bool Taken;              // a step or a commit in flight has it
    public string? Key;             // of a run
    public CancellationTokenSource? Cancel;
}

internal sealed class Unit(Slot? slot, string instance)
{
    public Slot? Slot => slot;
    public string Instance => instance;
    public InstanceRow? Row;
    public long? Expected;
    public object? State;
    public StartSpec? Spec;
    public string? Journal;
    public List<Item> Consumed = [];
    public List<string> Cancelled = [];
    public List<Message> Sent = [];
    public HashSet<string> Fresh = [], Volatile = [];
    public List<SubscriptionRow> Subscribed = [], Unsubscribed = [];

    public bool Durable => Row is not null || Journal is not null || Cancelled.Count > 0 || Consumed.Exists(i => i.Durable) || Sent.Exists(m => !Volatile.Contains(m.Id));
}

internal sealed record RunTicket(string Model, string Key, string? Arguments);

internal sealed record RunContext(InstanceRef Instance, string Key, string? Arguments) : IRun;
