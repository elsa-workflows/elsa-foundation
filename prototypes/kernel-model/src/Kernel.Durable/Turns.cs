using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// The seam between layers 2 and 3: what a kind of durable actor does with its messages. The durable layer delivers,
/// commits and recovers; the actor type decides what a turn means. Layer 3's workflow instance is one; a worker serves one.
/// </summary>
public interface IActorType
{
    /// <summary>The actor cannot be given its messages now; commands still pass. Suspended, say.</summary>
    bool Withholds(InstanceRow row, ITurnServices services);

    /// <summary>
    /// A turn the actor type handles alone, without any of the actor's own code: a command (<see cref="Kinds.IsControl"/>),
    /// or messages to an actor that has ended. Null when the turn is not one of those.
    /// </summary>
    TurnOutcome? Command(TurnInput input);

    /// <summary>One turn of the actor's own code. If it throws, the durable layer parks the actor (<see cref="Park"/>).</summary>
    ValueTask<TurnOutcome> TurnAsync(TurnInput input);

    /// <summary>
    /// Park: the actor faults with the error, its messages are consumed, the partition goes on. <paramref name="pending"/>
    /// is the history its turns in memory left, for an actor that was never written (decision 4).
    /// </summary>
    TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error, object? pending = null);

    /// <summary>
    /// Decision 4: an actor kept in memory past its short wait is written as it is, without a turn of its own code.
    /// <paramref name="pending"/> is what its turns in memory left for the history (<see cref="TurnOutcome.Pending"/>).
    /// </summary>
    TurnOutcome Write(InstanceRow row, object? state, object? pending);

    /// <summary>A background job, outside the actor's turns: the job's code is the actor type's, found by <paramref name="model"/>.</summary>
    ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken);
}

/// <summary>What the durable layer lends a turn.</summary>
public interface ITurnServices
{
    /// <summary>Database time as far as this owner knows.</summary>
    long Now { get; }

    /// <summary>How many partitions the store has: to place an actor in another one.</summary>
    int Partitions { get; }

    /// <summary>Decision 4: how long an Ephemeral actor may wait in memory when its start spec does not say.</summary>
    TimeSpan ShortWait { get; }

    bool Has(Flaw flaw);

    /// <summary>A job's ticket (or a recalled message) goes with this turn's commit: from here on its result is nobody's.</summary>
    void Cancelling(string ticket);
}

/// <summary>A message as the durable layer holds it in memory. An actor type reads the message; the rest is layer 2's.</summary>
public sealed class Delivery(Message message, long seq)
{
    public Message Message { get; internal set; } = message;
    internal long Seq = seq;
    internal bool Durable;            // it is a row
    internal bool Marked;             // its row says seen at this epoch
    internal bool Suspect;            // another owner had it: it is tried apart before anything else is believed of it
    internal bool Shelved;            // a suspect whose row says seen at this epoch, not yet tried
    internal bool Again;              // its row was seen at an earlier epoch: a turn may have run with it (DurableTurn.Redelivered)
    internal bool Held;               // a turn handed it back: it waits for something new to come with
    internal Message? Original;       // a job's ticket as it was before its result replaced it (decision 4: what is written)
    internal int Attempts;
    internal bool Ticket;             // a kernel message, not (yet) for the model
    internal bool Taken;              // a turn or a commit in flight has it
    internal string? Key;             // of a job
    internal CancellationTokenSource? Cancel;
}

/// <summary>What a turn is given: the actor, its committed row and live state, and the messages that are due, in order.</summary>
public sealed class TurnInput
{
    private readonly Worker _worker;

    internal TurnInput(Worker worker, Slot slot, string actor, List<Delivery> taken, int from, StartSpec? spec)
    {
        _worker = worker;
        Slot = slot;
        Actor = actor;
        Taken = taken;
        From = from;
        Spec = spec;
    }

    internal Slot Slot { get; }
    public string Actor { get; }
    public InstanceRow? Row => Slot.Row;

    /// <summary>The live state between turns, equal to the row's; null when it is not in memory.</summary>
    public object? State => Slot.State;

    /// <summary>What the starter on this node gave: this activation only, never stored.</summary>
    public object? Ambient => Slot.Ambient;

    /// <summary>Every message this turn takes, in order.</summary>
    public IReadOnlyList<Delivery> Taken { get; }

    /// <summary>The message that creates the actor; those ahead of it had no target and are consumed unseen.</summary>
    public int From { get; }

    /// <summary>The start spec that creates the actor, when it has no row.</summary>
    public StartSpec? Spec { get; }

    /// <summary>Decision 4: the actor's row is in memory only, never written; <see cref="Until"/> is how long it may stay so.</summary>
    public bool Unwritten => Slot.Unwritten;
    public long Until => Slot.Until;

    /// <summary>What the actor's turns in memory left for its history, to be written with it (opaque to the durable layer).</summary>
    public object? Pending => Slot.Pending;

    public string Model => Row?.Model ?? Spec!.Model;
    public ITurnServices Services => _worker;

    /// <summary>Say that the actor's code is about to run, with what it is given (for tests).</summary>
    public void Started(IReadOnlyList<Message> inbox) => _worker.Started(Actor, inbox);
}

/// <summary>What a turn did, for the committer: the row, what it consumed and held, sent, subscribed, cancelled.</summary>
public sealed class TurnOutcome(string instance)
{
    internal Slot? Slot;
    public string Instance => instance;
    public InstanceRow? Row;
    public long? Expected;
    public object? State;
    public StartSpec? Spec;
    public string? Journal;
    public InstanceStatus? Status = InstanceStatus.Running;     // after this turn; null when the instance does not exist
    public long Revision;
    public List<(string Channel, object? Item)> Emitted = [];
    public List<Delivery> Consumed = [], Held = [];
    public List<string> Cancelled = [];
    public List<Message> Sent = [];
    public HashSet<string> Fresh = [], Volatile = [];
    public List<SubscriptionRow> Subscribed = [], Unsubscribed = [];
    public bool Unsubscribe, Delete;

    /// <summary>Decision 4: the turn keeps its actor in memory, unwritten, until <see cref="Until"/>; <see cref="Row"/> is not written.</summary>
    public bool InMemory;
    public long Until;
    public object? Pending;     // what the turns in memory left for the history, kept with the actor until it is written
    internal HashSet<string> Promoted = [];     // messages held in memory only, written with this outcome

    internal bool Durable => !InMemory && (Row is not null || Journal is not null || Cancelled.Count > 0 || Consumed.Exists(i => i.Durable) || Sent.Exists(m => !Volatile.Contains(m.Id)));
}

/// <summary>
/// The durable half of one turn: what it sends, holds, recalls, subscribes, publishes and emits, and the background jobs
/// it starts and cancels. An actor type collects a turn with it; nothing leaves it before the commit.
/// </summary>
public sealed class DurableTurn
{
    private readonly TurnInput _input;
    private readonly List<Message> _sent = [];
    private readonly HashSet<string> _fresh = [], _volatile = [], _started = [], _held = [];
    private readonly List<SubscriptionRow> _subscribed = [], _unsubscribed = [];
    private readonly List<(string, object?)> _emitted = [];
    private readonly HashSet<SubscriptionRow> _kept = [];
    private readonly List<string> _jobs, _cancelled = [];
    private int _ordinal;

    public DurableTurn(TurnInput input)
    {
        _input = input;
        Actor = new InstanceRef(input.Actor);
        Inbox = input.Taken.Skip(input.From).Select(i => i.Message).ToList();   // messages ahead of the one that creates the target had no target
        _jobs = input.Row?.Jobs is { } jobs ? Json.Read<List<string>>(jobs) : [];
        foreach (var message in Inbox)
            if (message.Kind == Kinds.JobFinished) _jobs.Remove(message.Read<JobFinished>().Key);    // its result is being handled: no longer in flight
        Revision = (input.Row?.Revision ?? 0) + 1;
    }

    public InstanceRef Actor { get; }
    public IReadOnlyList<Message> Inbox { get; }

    /// <summary>The revision this turn's commit writes.</summary>
    public long Revision { get; }

    /// <summary>The ordinal the next derived message id gets.</summary>
    public int Ordinal => _ordinal;

    public object? Ambient => _input.Ambient;
    public int SentCount => _sent.Count;
    public int SubscribedCount => _subscribed.Count;
    private string Id => Actor.Id;
    private ITurnServices Services => _input.Services;

    public void Emit(string channel, object? item) => _emitted.Add((channel, item));

    public bool Redelivered(Message message) => _input.Taken.FirstOrDefault(i => i.Message.Id == message.Id)?.Again ?? false;

    // A message's id is derived from its sender and the sender's revision. Finding F8: the ordinal has a fixed width,
    // because Entity Framework sends the inserts of one commit in key order and the database hands out seq in that order.
    public string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null) =>
        Send($"{Id}#{Revision}.{_ordinal++:D4}", target, kind, payload, options);

    private string Send(string id, InstanceRef target, string kind, object? payload, SendOptions? options = null)
    {
        _sent.Add(new Message(id, target, kind, payload is null ? null : Json.Write(payload), Id, options?.Start,
            options?.Delay is { } delay ? Services.Now + (long)delay.TotalMilliseconds : null));
        return id;
    }

    /// <summary>A message the actor type composed itself: a parent's notice, say. It takes no ordinal.</summary>
    public void Add(Message message) => _sent.Add(message);

    /// <summary>The last message sent goes to an actor whose id was just minted in this partition: it cannot exist, so no read.</summary>
    public void Fresh() => _fresh.Add(_sent[^1].Id);

    public void Recall(string message)
    {
        if (!message.StartsWith(Id + "#", StringComparison.Ordinal)) throw new InvalidOperationException($"Message '{message}' was not sent by {Id}.");
        if (Inbox.Any(m => m.Id == message) || _cancelled.Contains(message)) return;     // given to this turn: consumed with it
        if (_sent.RemoveAll(m => m.Id == message) > 0) return;      // sent by this turn: never written
        _cancelled.Add(message);        // its row goes with this commit, as a cancelled job's ticket does
        Services.Cancelling(message);
    }

    public void StartJob(string key, object? arguments = null)
    {
        if (_jobs.Contains(key)) throw new InvalidOperationException($"Job '{key}' is in flight already.");
        _jobs.Add(key);
        _started.Add(key);
        // Finding F4: the intent is also a message to self, a ticket, so after a crash the inbox alone says what to do next.
        // Its id is derived from instance and key, so a later turn can delete it without holding it.
        Send(Worker.TicketId(Id, key), Actor, Kinds.Job, new JobTicket(_input.Model, key, arguments is null ? null : Json.Write(arguments)));
        if (Services.Has(Flaw.VolatileJobTicket)) _volatile.Add(_sent[^1].Id);
    }

    public void CancelJob(string key)
    {
        if (!_jobs.Remove(key)) return;     // not in flight: never started, done, or its result is in this turn's inbox
        var ticket = Worker.TicketId(Id, key);
        if (_started.Remove(key)) _sent.RemoveAll(m => m.Id == ticket);
        else _cancelled.Add(ticket);
        Services.Cancelling(ticket);     // from here on its result is nobody's
    }

    public void Hold(Message message)
    {
        if (!Inbox.Contains(message)) throw new InvalidOperationException($"Message '{message.Id}' is not in this turn's inbox.");
        _held.Add(message.Id);
    }

    public void Subscribe(string key, string wait, bool keep = false)
    {
        _subscribed.Add(new SubscriptionRow(key, Id, wait));
        if (keep) _kept.Add(_subscribed[^1]);
    }
    public void Unsubscribe(string key, string wait) => _unsubscribed.Add(new SubscriptionRow(key, Id, wait));

    // NATIVE N1: the publish is a ticket to self, so "the inbox alone says what is owed" holds for it too.
    public string Publish(string key, string? payload, TimeSpan keep)
    {
        var id = $"{Id}#{Revision}.{_ordinal:D4}";
        Send(Actor, Kinds.Publish, new EventRow(key, id, payload, Services.Now + (long)keep.TotalMilliseconds));
        return id;
    }

    /// <summary>
    /// Decision 4: what this turn leaves can wait in memory until <paramref name="until"/>. Everything it sends goes to the
    /// actor itself and is a job's ticket, or a message due by then (one without a due time is due now); it subscribes nothing, publishes nothing, and cancels or recalls
    /// only what is in memory too. And it took no message that is a row: consuming one needs a commit, and a start that is
    /// a row (an outside start, say) must not be lost with the memory.
    /// </summary>
    public bool CanWaitInMemory(long until) =>
        _subscribed.Count == 0 && (_cancelled.Count == 0 || _input.Unwritten) && !_input.Taken.Any(i => i.Durable)
        && _sent.TrueForAll(m => m.Target == Actor && m.Kind is not (Kinds.Publish or Kinds.Meet) && (m.Kind == Kinds.Job || (m.Due ?? Services.Now) <= until));

    /// <summary>Everything this turn sends stays in memory: none of it is a row.</summary>
    public void WaitInMemory() => _volatile.UnionWith(_sent.Select(m => m.Id));

    // ---- the end of the turn: the actor type calls these in this order ---------------------------------------------

    /// <summary>Nothing is held: every message of the turn is consumed.</summary>
    public void HoldNothing() => _held.Clear();

    /// <summary>A held job result is still in flight; then the row's <c>Jobs</c>.</summary>
    public string? Jobs()
    {
        foreach (var message in Inbox)
            if (message.Kind == Kinds.JobFinished && _held.Contains(message.Id)) _jobs.Add(message.Read<JobFinished>().Key);   // a held result: still in flight
        return _jobs.Count == 0 ? null : Json.Write(_jobs);
    }

    /// <summary>What the turn consumed, held, cancelled and emitted. The row comes with <see cref="Seal"/>.</summary>
    public TurnOutcome Outcome(InstanceStatus status) => new(Id)
    {
        Consumed = _input.Taken.Where(i => !_held.Contains(i.Message.Id)).ToList(),
        Held = _input.Taken.Where(i => _held.Contains(i.Message.Id)).ToList(),
        Cancelled = _cancelled, Spec = _input.Spec, Status = status, Revision = Revision, Emitted = _emitted,
    };

    /// <summary>The turn's end is its commit, and the only one: the row, the journal, and everything it sent.</summary>
    public TurnOutcome Seal(TurnOutcome unit, InstanceRow row, object state, string? journal)
    {
        if (_kept.Count > 0)
        {
            // Finding F6: "read the events for the keys I subscribed" is a message to self, so a crash after this commit cannot lose it.
            // Only waits that asked for kept events need it: the others get what is published from this commit on.
            Send(Actor, Kinds.Meet, _subscribed.Where(_kept.Contains).ToArray());
            if (Services.Has(Flaw.MeetWithoutTicket)) _volatile.Add(_sent[^1].Id);
        }
        unit.Row = row;
        unit.Expected = _input.Row is null || _input.Unwritten ? null : Revision - 1;    // a row kept in memory is inserted when written
        unit.State = state;
        unit.Journal = journal;
        unit.Sent = _sent;
        unit.Fresh = _fresh;
        unit.Volatile = _volatile;
        unit.Subscribed = _subscribed;
        unit.Unsubscribed = _unsubscribed;
        return unit;
    }
}
