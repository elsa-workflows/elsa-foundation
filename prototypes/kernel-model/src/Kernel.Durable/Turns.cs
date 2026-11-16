using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// The seam between layers 2 and 3: what a kind of durable actor does with its messages. The durable layer delivers,
/// commits and recovers; the actor type decides what a turn means. Layer 3's workflow instance is one; a worker serves one.
/// </summary>
public interface IActorType
{
    /// <summary>A referenced address whose equality with this actor affects admission. Routers can identify
    /// references from the wire kind before the stored model is loaded. This grants no sender authority.</summary>
    string? SelfReference(Delivery delivery) => null;

    /// <summary>An owner command that needs a single-message boundary but still obeys normal withholding.</summary>
    bool SingleMessage(Message message) => false;

    /// <summary>A mixed host dispatches the classifier by the committed model. Single-model actors keep their existing boundary.</summary>
    bool SingleMessage(string? model, InstanceRow? row, Delivery delivery) => SingleMessage(delivery.Message);

    /// <summary>Pre-turn selection keeps legacy batching unless the actor explicitly opts in.
    /// The existing contextual classifier selects alone by default only on a qualified creation.</summary>
    bool SelectsAlone(string? model, InstanceRow? row, Delivery delivery) =>
        row is { Creation: > 0 } && SingleMessage(model, row, delivery);

    /// <summary>Whether a sole consumed delivery, after its row commits, revisits earlier held input.
    /// Existing actor types keep their contextual boundary as the default committed stimulus.</summary>
    bool RevisitsHeld(string? model, InstanceRow row, Delivery delivery) => SingleMessage(model, row, delivery);

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
    TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error);
    TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error, object? pending) =>
        Park(row, actor, taken, spec, error);

    /// <summary>
    /// Decision 4: an actor kept in memory past its short wait is written as it is, without a turn of its own code.
    /// <paramref name="pending"/> is what its turns in memory left for the history (<see cref="TurnOutcome.Pending"/>).
    /// </summary>
    TurnOutcome Write(InstanceRow row, object? state, object? pending) =>
        throw new NotSupportedException("This actor type does not support unwritten short waits.");

    /// <summary>A background job, outside the actor's turns: the job's code is the actor type's, found by <paramref name="model"/>.</summary>
    ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken);
}

/// <summary>What the durable layer lends a turn.</summary>
public interface ITurnServices
{
    /// <summary>Database time as far as this owner knows.</summary>
    long Now { get; }
    bool CommitClockAvailable { get; }
    bool CreationIdentityAvailable { get; }

    /// <summary>How many partitions the store has: to place an actor in another one.</summary>
    int Partitions { get; }

    /// <summary>Decision 4: how long an Ephemeral actor may wait in memory when its start spec does not say.</summary>
    TimeSpan ShortWait { get; }

    bool Has(Flaw flaw);

    /// <summary>A job's ticket (or a recalled message) goes with this turn's commit: from here on its result is nobody's.</summary>
    void Cancelling(InboxKey ticket);
}

/// <summary>A message as the durable layer holds it in memory. An actor type reads the message; the rest is layer 2's.</summary>
public sealed class Delivery(Message message, long seq)
{
    public Message Message { get; internal set; } = message;
    internal object? StarterAmbient;    // belongs to this local creator; never written
    internal string? UncheckedReference;
    internal long Seq = seq;         // allocated store sequence, or local post order until promotion
    internal long InboxOrder => CompletedOrder == 0 ? Seq : 0; // F7 priority among eligible inputs
    internal long ObservedOrder;     // in-memory order of first observation, for local/durable tie-breaking
    internal long ObservedAt;        // monotonic observation time, used when a command matures before trial selection
    internal long CompletedOrder;    // ordinal when the driver published its result in this owner
    internal long CompletedAt;       // a durable ticket alone cannot establish when its result became ready
    internal volatile bool AwaitingJobMark; // a queued volatile job promoted behind the poll horizon
    public bool HasRequestAddress => RequestAddress is not null;
    internal string? RequestAddress;   // a single live response to a retryable PostRequest; never persisted
    internal bool Durable;            // it is a row
    /// <summary>A completion produced by this worker from its own issued ticket, including an unwritten job.</summary>
    public bool IsWorkerJobCompletion => CompletedJob && Message.Kind == Kinds.JobFinished;
    public bool IsDurableJobTicket => Durable && Message.InboxScope > 0 && Message.Kind is Kinds.Job or Kinds.JobFinished;
    internal bool AwaitPoll;          // promoted while catching up: poll must observe its row before marking it
    internal bool Marked;             // its row says seen at this epoch
    internal bool RecoveryOrdering;   // a previously marked row read on takeover, or a command that matured during recovery
    internal bool Suspect;            // another owner had it: it is tried apart before anything else is believed of it
    internal bool Shelved;            // a suspect whose row says seen at this epoch, not yet tried
    internal bool Again;              // its polled row already had a seen or trial marker (DurableTurn.Redelivered)
    internal bool Held;               // a turn handed it back: it waits for something new to come with
    internal Message? Original;       // a job's ticket as it was before its result replaced it (decision 4: what is written)
    internal int Attempts;
    internal bool SuppliedCompletion => Message.Kind == Kinds.JobFinished && !CompletedJob;
    internal bool SuppliedTicket => Message.InboxScope == 0 && Message.Kind is Kinds.Meet or Kinds.Publish or Kinds.Job;
    internal bool DiscardedInput => SuppliedCompletion || SuppliedTicket;
    internal bool DiscardedAt(Slot slot) => DiscardedInput || Message.SuppliedCreator && slot.CurrentRow is null;
    internal bool JobEntered;         // physical model entry, distinct from a scheduled driver
    internal bool DeferredJobTrial;   // durable fence retained while runtime trial isolation is yielded before entry
    internal long DeferredTrialObserved; // preserve the original cohort horizon when isolation is reacquired
    internal bool CompletedJob;       // only the Worker can turn a ticket into a result
    internal bool Ticket;             // a kernel message, not (yet) for the model
    internal bool Taken;              // a turn or a commit in flight has it
    internal string? Key;             // of a job
    internal CancellationTokenSource? Cancel;
    internal Task? JobDriver;
}

/// <summary>What a turn is given: the actor, its committed row and live state, and the messages that are due, in order.</summary>
public sealed class TurnInput
{
    private readonly Worker _worker;
    private readonly ITurnServices _services;

    internal TurnInput(Worker worker, Slot slot, string actor, List<Delivery> taken, int from, StartSpec? spec)
    {
        _worker = worker;
        ChildIds = worker.ChildIds;
        _services = worker.ServicesFor(slot);
        Slot = slot;
        Actor = actor;
        Taken = taken;
        From = from;
        Spec = spec;
    }

    internal ChildIssuer ChildIds { get; }
    internal Slot Slot { get; }
    public string Actor { get; }
    public bool IsSelf(Message delivery, string? address) => address == Actor || address == delivery.Target.Id ||
        address is not null && (Slot.Aliases?.Contains(address) == true || Slot.SelfReferences?.GetValueOrDefault(address) == true);
    public InstanceRow? Row => Slot.CurrentRow;
    public InstanceRow? Previous => Slot.Row;
    public long Creation => Row?.Creation ?? Spec?.Creation?.Generation ?? 0;

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
    public ITurnServices Services => _services;

    /// <summary>Say that the actor's code is about to run, with what it is given (for tests).</summary>
    public void Started(IReadOnlyList<Message> inbox) => _worker.Started(Actor, inbox);
}

/// <summary>What a turn did, for the committer: the row, what it consumed and held, sent, subscribed, cancelled.</summary>
public sealed class TurnOutcome(string instance)
{
    internal Slot? Slot;
    internal bool RestrictsJobEntry;
    public string Instance => instance;
    public InstanceRow? Row;
    public long? Expected;
    public object? State;
    public StartSpec? Spec;
    public string? Journal;
    public InstanceStatus? Status = InstanceStatus.Running;     // after this turn; null when the instance does not exist
    public long Revision;
    public List<(string Channel, object? Item)> Emitted = [];
    internal CommitWindow? Window;
    public List<RequestReply> Replied = [];

    /// <summary>Reply on the consumed delivery's own route when this outcome lands.</summary>
    public void ReplyTo(Delivery delivery, object? value)
    {
        if ((delivery.RequestAddress ?? delivery.Message.ReplyTo) is { } route)
            Replied.Add(new(delivery.Message.InboxKey, route, Request.Response, value) { Rpc = delivery.RequestAddress is not null });
    }
    public List<Delivery> Consumed = [], Held = [];
    public List<InboxKey> Cancelled = [];
    public List<Message> Sent = [];
    public HashSet<InboxKey> Fresh = [], Volatile = [];
    public List<SubscriptionRow> Subscribed = [], Unsubscribed = [];
    public bool Unsubscribe, Delete;

    /// <summary>Decision 4: the turn keeps its actor in memory, unwritten, until <see cref="Until"/>; <see cref="Row"/> is not written.</summary>
    public bool InMemory;
    public long Until;
    public object? Pending;     // what the turns in memory left for the history, kept with the actor until it is written
    internal HashSet<InboxKey> Promoted = [];     // messages held in memory only, written with this outcome

    internal bool Durable => Window is not null || Replied.Exists(r => r.Rpc) || !InMemory && (Row is not null || Journal is not null || Cancelled.Count > 0 || Consumed.Exists(i => i.Durable) || Sent.Exists(m => !Volatile.Contains(m.InboxKey)));
}

/// <summary>A reply to one consumed request, after commit; it does not subscribe a workflow observer.</summary>
public sealed record RequestReply(InboxKey Message, string To, string Channel, object? Item)
{
    internal bool Rpc { get; init; }
}

/// <summary>
/// The durable half of one turn: what it sends, holds, recalls, subscribes, publishes and emits, and the background jobs
/// it starts and cancels. An actor type collects a turn with it; nothing leaves it before the commit.
/// </summary>
public sealed class DurableTurn
{
    private readonly TurnInput _input;
    private readonly List<Message> _sent = [];
    private readonly HashSet<InboxKey> _fresh = [], _volatile = [], _held = [];
    private readonly HashSet<string> _started = [];
    private readonly List<SubscriptionRow> _subscribed = [], _unsubscribed = [];
    private readonly List<(string, object?)> _emitted = [];
    private readonly List<RequestReply> _replied = [];
    private readonly HashSet<SubscriptionRow> _kept = [];
    private readonly HashSet<string> _custom;
    private readonly List<string> _jobs;
    private readonly List<InboxKey> _cancelled = [];
    private int _ordinal;
    private CommitWindow? _window;

    public DurableTurn(TurnInput input)
    {
        _input = input;
        Actor = new InstanceRef(input.Actor);
        _custom = input.Row?.CustomHandles is { } handles ? DurableKeyJson.ReadKeys(handles).ToHashSet(StringComparer.Ordinal) : [];
        Inbox = input.Taken.Skip(input.From).Select(i => i.Message).ToList();
        _jobs = input.Row?.Jobs is { } jobs ? DurableKeyJson.ReadKeys(jobs) : [];
        foreach (var delivery in input.Taken.Skip(input.From))
            if (CompletedTicket(delivery, out var key)) _jobs.Remove(key);
        Revision = (input.Row?.Revision ?? 0) + 1;
        Clock = new TurnClock(input.Row, Revision, input.Services);
    }

    public InstanceRef Actor { get; }
    public ICommitClock Clock { get; }
    public IReadOnlyList<Message> Inbox { get; }

    /// <summary>The revision this turn's commit writes.</summary>
    public long Revision { get; }

    /// <summary>The ordinal the next derived message id gets.</summary>
    public int Ordinal => _ordinal;

    public object? Ambient => _input.Ambient;
    public int SentCount => _sent.Count;
    public int SubscribedCount => _subscribed.Count;
    private string Id => Actor.Id;
    private string Origin => CreationProtocol.Origin(Id, _input.Creation);
    private ITurnServices Services => _input.Services;

    /// <summary>All authorized decisions in this turn must still hold when the database commits.</summary>
    public void RequireWindow(long fromInclusive, long untilExclusive)
    {
        var next = new CommitWindow(fromInclusive, untilExclusive);
        _window = _window?.Intersect(next) ?? next;
    }

    public void Emit(string channel, object? item) => _emitted.Add((channel, item));

    public void Reply(Message request, string channel, object? item)
    {
        if (!Inbox.Contains(request) || _held.Contains(request.InboxKey) || _replied.Any(r => r.Message == request.InboxKey))
            throw new InvalidOperationException("Reply must name one consumed request of this turn, once.");
        if (request.ReplyTo is { } to) _replied.Add(new(request.InboxKey, to, channel, item));
    }

    /// <summary>Answer a volatile RPC through the committed per-request reply pipeline. Other deliveries use their
    /// own reply contract. A fresh attempt after transport failure can recover the committed decision.</summary>
    public bool HasRequestRoute(Message request) => _input.Taken.Any(i => ReferenceEquals(i.Message, request) && i.RequestAddress is not null);

    public bool TryReply(Message request, object? item)
    {
        var delivery = _input.Taken.FirstOrDefault(i => ReferenceEquals(i.Message, request));
        if (delivery?.RequestAddress is not { } to) return false;
        if (_held.Contains(request.InboxKey) || _replied.Any(r => r.Message == request.InboxKey))
            throw new InvalidOperationException("An RPC reply must consume its request once.");
        _replied.Add(new RequestReply(request.InboxKey, to, Request.Response, item) { Rpc = true });
        return true;
    }

    public bool Redelivered(Message message) => !Services.Has(Flaw.HideDeliveryHistory) && (_input.Taken.FirstOrDefault(i => i.Message.InboxKey == message.InboxKey)?.Again ?? false);

    // A message's id is derived from its sender and revision; fixed-width ordinals preserve order within one turn.
    // EF's send-order comparer also preserves ordering across revisions promoted in the same commit (F8).
    public string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null)
    {
        Kinds.RequireApplicationSend(kind);
        return SendNext(target, kind, payload, options);
    }

    /// <summary>A trusted actor type replies from the address on an input routed to it, or that address saved
    /// in its checkpoint. The physical message ID and sender creation always come from this actor.</summary>
    public string SendFromReceivedAddress(string? receivedAt, InstanceRef target, string kind, object? payload, SendOptions? options = null)
    {
        Kinds.RequireApplicationSend(kind);
        return SendNext(target, kind, payload, options, receivedAt);
    }

    private string SendNext(InstanceRef target, string kind, object? payload, SendOptions? options = null, string? receivedAt = null)
    {
        return Send(InboxKey.Issued($"{Origin}#{Revision}.{_ordinal++:D4}"), target, kind, payload, options, receivedAt);
    }

    private string Send(InboxKey id, InstanceRef target, string kind, object? payload, SendOptions? options = null, string? receivedAt = null)
    {
        _sent.Add(new Message(id.Id, target, kind, payload is null ? null : Json.Write(payload), receivedAt ?? Id, options?.Start,
            options?.Delay is { } delay ? Services.Now + (long)delay.TotalMilliseconds : null, TargetCreation: options?.TargetCreation ?? options?.Start?.Creation?.Generation ?? (target == Actor && _input.Creation > 0 ? _input.Creation : null), SenderCreation: _input.Creation).WithInboxScope(id.Scope));
        return id.Id;
    }

    /// <summary>A message the actor type composed itself: a parent's notice, say. It takes no ordinal.</summary>
    public void Add(Message message)
    {
        Kinds.RequireApplicationSend(message.Kind);
        if (message.Sender == Id) message = message with { SenderCreation = _input.Creation };
        // An actor may compose its own recallable ID. Unrelated caller-chosen IDs stay in ordinary ingress.
        if (message.Id.StartsWith(Origin + "#", StringComparison.Ordinal) && !OwnsHandle(message.Id))
        {
            _sent.Add(message.WithInboxScope(InboxKey.Custom(Origin, message.Id).Scope));
        }
        else _sent.Add(OwnsHandle(message.Id) ? message.AsIssued() : message.AsOrdinary());
    }

    /// <summary>The terminal notification, addressed under this actor and revision.</summary>
    public void AddTerminalNotice(Message message)
    {
        Kinds.RequireApplicationSend(message.Kind);
        if (message.Sender != Id || message.Id != $"{Origin}#{Revision}.end")
            throw new ArgumentException("A terminal notice must use its issuing actor and revision.", nameof(message));
        _sent.Add((message with { SenderCreation = _input.Creation }).AsIssued());
    }

    /// <summary>Allocate the child and commit its identity and creator together with the parent's state.</summary>
    public InstanceRef StartChild(StartSpec spec, int destination)
    {
        if ((uint)destination >= (uint)Services.Partitions) throw new ArgumentOutOfRangeException(nameof(destination));
        var child = _input.ChildIds.Next(destination);
        if (_input.Creation > 0)
            spec = spec with { Creation = new CreationStart(1, 0, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(child.Id)))) };
        var handle = $"{Origin}#{Revision}.{_ordinal++:D4}";
        _sent.Add(new Message(handle, child, Kinds.Start, Sender: Id, SenderCreation: _input.Creation, Start: spec, TargetCreation: spec.Creation?.Generation).WithInboxScope(InboxKey.ChildCreatorScope));
        // An exact derived handle in this set denotes a child creator. Composed custom handles have an
        // additional '#' in the suffix, so both forms can share the existing optional checkpoint column.
        if (destination == Actor.Partition) _fresh.Add(_sent[^1].InboxKey);
        return child;
    }

    public void Recall(string message)
    {
        if (!message.StartsWith(Origin + "#", StringComparison.Ordinal)) throw new InvalidOperationException($"Message '{message}' was not sent by {Id}.");
        // Removing this turn's own staged message cannot delete a stored row, whatever its scope.
        if (_sent.RemoveAll(m => m.Id == message) > 0) return;
        var custom = _custom.Contains(message);
        if (!custom && !OwnsHandle(message)) throw new InvalidOperationException($"Message '{message}' was not sent by {Id}.");
        var key = custom && OwnsHandle(message) ? new InboxKey(message, InboxKey.ChildCreatorScope)
            : custom ? InboxKey.Custom(Origin, message) : InboxKey.Issued(message);
        if (Inbox.Any(m => m.InboxKey == key) || _cancelled.Contains(key)) return;     // given to this turn: consumed with it
        _cancelled.Add(key);        // its row goes with this commit, as a cancelled job's ticket does
        Services.Cancelling(key);
    }

    private bool OwnsHandle(string handle) => handle.LastIndexOf('#') == Origin.Length && handle.StartsWith(Origin + "#", StringComparison.Ordinal);

    public void StartJob(string key, object? arguments = null)
    {
        if (_jobs.Contains(key)) throw new InvalidOperationException($"Job '{key}' is in flight already.");
        _jobs.Add(key);
        _started.Add(key);
        // Finding F4: the intent is also a message to self, a ticket, so after a crash the inbox alone says what to do next.
        // Its id is derived from instance and key, so a later turn can delete it without holding it.
        Send(InboxKey.Ticket(Id, key, _input.Creation), Actor, Kinds.Job, new JobTicket(_input.Model, key, arguments is null ? null : Json.Write(arguments)));
        if (Services.Has(Flaw.VolatileJobTicket)) _volatile.Add(_sent[^1].InboxKey);
    }

    public void CancelJob(string key)
    {
        if (!_jobs.Remove(key)) return;     // not in flight: never started, done, or its result is in this turn's inbox
        var ticket = InboxKey.Ticket(Id, key, _input.Creation);
        if (_started.Remove(key)) _sent.RemoveAll(m => m.InboxKey == ticket);
        else _cancelled.Add(ticket);
        Services.Cancelling(ticket);     // from here on its result is nobody's
    }

    public void Hold(Message message)
    {
        if (!Inbox.Contains(message)) throw new InvalidOperationException($"Message '{message.Id}' is not in this turn's inbox.");
        if (_replied.Any(r => r.Message == message.InboxKey)) throw new InvalidOperationException("A replied request cannot be held.");
        _held.Add(message.InboxKey);
    }

    public void Subscribe(string key, string wait, bool keep = false)
    {
        _subscribed.Add(new SubscriptionRow(key, Id, wait, Creation: _input.Creation));
        if (keep) _kept.Add(_subscribed[^1]);
    }
    public void Unsubscribe(string key, string wait) => _unsubscribed.Add(new SubscriptionRow(key, Id, wait, Creation: _input.Creation));

    // NATIVE N1: the publish is a ticket to self, so "the inbox alone says what is owed" holds for it too.
    public string Publish(string key, string? payload, TimeSpan keep)
    {
        var id = $"{Origin}#{Revision}.{_ordinal:D4}";
        SendNext(Actor, Kinds.Publish, new EventRow(key, id, payload, Services.Now + (long)keep.TotalMilliseconds));
        return id;
    }

    /// <summary>
    /// Decision 4: what this turn leaves can wait in memory until <paramref name="until"/>. Everything it sends goes to the
    /// actor itself and is a job's ticket, or a message due by then (one without a due time is due now); it subscribes nothing, publishes nothing, and cancels or recalls
    /// only what is in memory too. And it took no message that is a row: consuming one needs a commit, and a start that is
    /// a row (an outside start, say) must not be lost with the memory.
    /// </summary>
    public bool CanWaitInMemory(long until) =>
        until > Services.Now && _window is null && _replied.Count == 0 && _subscribed.Count == 0 && (_cancelled.Count == 0 || _input.Unwritten) && !_input.Taken.Any(i => i.Durable)
        && _sent.TrueForAll(m => m.Target == Actor && m.Kind is not (Kinds.Publish or Kinds.Meet) && (m.Kind == Kinds.Job || (m.Due ?? Services.Now) <= until));

    /// <summary>Everything this turn sends stays in memory: none of it is a row.</summary>
    public void WaitInMemory() => _volatile.UnionWith(_sent.Select(m => m.InboxKey));

    // ---- the end of the turn: the actor type calls these in this order ---------------------------------------------

    /// <summary>Nothing is held: every message of the turn is consumed.</summary>
    public void HoldNothing() => _held.Clear();

    /// <summary>A held job result is still in flight; then the row's <c>Jobs</c>.</summary>
    public string? Jobs()
    {
        var jobs = new List<string>(_jobs);
        foreach (var delivery in _input.Taken.Skip(_input.From))
            if (_held.Contains(delivery.Message.InboxKey) && CompletedTicket(delivery, out var key) && !jobs.Contains(key))
                jobs.Add(key);
        return jobs.Count == 0 ? null : DurableKeyJson.WriteKeys(jobs);
    }

    private bool CompletedTicket(Delivery delivery, out string key)
    {
        key = "";
        if (!delivery.IsWorkerJobCompletion) return false;
        key = delivery.Message.Read<JobFinished>().Key;
        return delivery.Message.Target == Actor && delivery.Message.InboxKey == InboxKey.Ticket(Id, key, _input.Creation);
    }

    /// <summary>What the turn consumed, held, cancelled and emitted. The row comes with <see cref="Seal"/>.</summary>
    public TurnOutcome Outcome(InstanceStatus status) => new(Id)
    {
        Consumed = _input.Taken.Where(i => !_held.Contains(i.Message.InboxKey)).ToList(),
        Held = _input.Taken.Where(i => _held.Contains(i.Message.InboxKey)).ToList(),
        Cancelled = _cancelled, Spec = _input.Spec, Status = status, Revision = Revision, Emitted = _emitted, Replied = _replied, Window = _window,
    };

    /// <summary>The turn's end is its commit, and the only one: the row, the journal, and everything it sent.</summary>
    public TurnOutcome Seal(TurnOutcome unit, InstanceRow row, object? state, string? journal)
    {
        if (_kept.Count > 0)
        {
            // Finding F6: "read the events for the keys I subscribed" is a message to self, so a crash after this commit cannot lose it.
            // Only waits that asked for kept events need it: the others get what is published from this commit on.
            SendNext(Actor, Kinds.Meet, _subscribed.Where(_kept.Contains).ToArray());
            if (Services.Has(Flaw.MeetWithoutTicket)) _volatile.Add(_sent[^1].InboxKey);
        }
        foreach (var taken in unit.Consumed)
            if (taken.Message.InboxKey == InboxKey.Custom(Origin, taken.Message.Id)) _custom.Remove(taken.Message.Id);
        foreach (var key in _cancelled)
            if (key.Scope == InboxKey.ChildCreatorScope || key == InboxKey.Custom(Origin, key.Id)) _custom.Remove(key.Id);
        // Received and recalled rows lose their old memberships. Only surviving sends establish new ones:
        // retracting a staged twin leaves a previously committed row and its old membership intact.
        foreach (var message in _sent)
            if (message.InboxKey.Scope == InboxKey.ChildCreatorScope || message.InboxKey == InboxKey.Custom(Origin, message.Id)) _custom.Add(message.Id);
        unit.Row = row with { CommitTimes = ((TurnClock)Clock).Snapshot(), CustomHandles = _custom.Count == 0 ? null : DurableKeyJson.WriteKeys(_custom) };
        unit.Expected = _input.Unwritten ? null : _input.Previous?.Revision;
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
