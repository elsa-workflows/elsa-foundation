namespace Kernel;

/// <summary>What a model sees of one step, and the collector of what the step did. Nothing leaves it before <see cref="Seal"/>.</summary>
internal sealed class Step : IStep
{
    private readonly Worker _worker;
    private readonly List<Message> _sent = [];
    private readonly HashSet<string> _fresh = [], _volatile = [], _started = [], _held = [];
    private readonly List<SubscriptionRow> _subscribed = [], _unsubscribed = [];
    private readonly List<object> _entries = [];
    private readonly List<(string, object?)> _emitted = [];
    private readonly HashSet<SubscriptionRow> _kept = [];
    private readonly List<string> _runs, _children, _cancelled = [];
    private readonly long _revision;
    private InstanceStatus _status = InstanceStatus.Running;
    private object? _output;
    private int _ordinal;
    private string? _definition;

    internal Step(Worker worker, Slot slot, string id, List<Item> taken, int from, StartSpec? spec)
    {
        _worker = worker;
        Slot = slot;
        Taken = taken;
        Spec = spec;
        var row = slot.Row;
        Instance = new InstanceInfo(new InstanceRef(id), row?.Model ?? spec!.Model, row?.Definition ?? spec!.Definition, row is null ? spec!.Parent : row.Parent);
        Inbox = taken.Skip(from).Select(i => i.Message).ToList();   // messages ahead of the one that creates the target had no target
        _runs = row?.Runs is { } runs ? Json.Read<List<string>>(runs) : [];
        _children = row?.Children is { } children ? Json.Read<List<string>>(children) : [];
        foreach (var message in Inbox)
        {
            if (message.Kind == Kinds.RunFinished) _runs.Remove(message.Read<RunFinished>().Key);    // its result is being handled: no longer in flight
            if (message.Kind == Kinds.ChildFinished) _children.Remove(message.Read<ChildFinished>().Child);
        }
        _revision = (row?.Revision ?? 0) + 1;
    }

    internal Slot Slot { get; }
    internal List<Item> Taken { get; }
    internal StartSpec? Spec { get; }

    public InstanceInfo Instance { get; }
    public IReadOnlyList<Message> Inbox { get; }
    public object State { get; set; } = null!;
    public object? Ambient => Slot.Ambient;
    public void Emit(string channel, object? item) => _emitted.Add((channel, item));

    public bool Redelivered(Message message) => Taken.Find(i => i.Message.Id == message.Id)?.Again ?? false;

    private string Id => Instance.Ref.Id;

    // A message's id is derived from its sender and the sender's revision. Finding F8: the ordinal has a fixed width,
    // because Entity Framework sends the inserts of one commit in key order and the database hands out seq in that order.
    public string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null) =>
        Send($"{Id}#{_revision}.{_ordinal++:D4}", target, kind, payload, options);

    private string Send(string id, InstanceRef target, string kind, object? payload, SendOptions? options = null)
    {
        _sent.Add(new Message(id, target, kind, payload is null ? null : Json.Write(payload), Id, options?.Start,
            options?.Delay is { } delay ? _worker.Now + (long)delay.TotalMilliseconds : null));
        return id;
    }

    public void Recall(string message)
    {
        if (!message.StartsWith(Id + "#", StringComparison.Ordinal)) throw new InvalidOperationException($"Message '{message}' was not sent by {Id}.");
        if (Inbox.Any(m => m.Id == message) || _cancelled.Contains(message)) return;     // given to this step: consumed with it
        if (_sent.RemoveAll(m => m.Id == message) > 0) return;      // sent by this step: never written
        _cancelled.Add(message);        // its row goes with this commit, as a cancelled run's ticket does
        _worker.Cancelling(message);
    }

    public InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false)
    {
        // With its parent by default, so a call tree talks in memory and commits together; spread, for a wide fan-out.
        var name = $"{Id[(Id.IndexOf('/') + 1)..]}~{_revision}.{_ordinal}";
        var child = InstanceRef.In(placement == Placement.Spread ? InstanceRef.Spread(name, _worker.Partitions) : Instance.Ref.Partition, name);
        Send(child, Kinds.Start, null, new SendOptions(Start: detached ? spec with { Parent = null } : spec with { Parent = Id }));
        if (!detached) _children.Add(child.Id);    // the kernel's list: whom a terminate goes on to
        if (child.Partition == Instance.Ref.Partition) _fresh.Add(_sent[^1].Id);   // minted and owned here, so it cannot exist: no read
        return child;
    }

    public void Run(string key, object? arguments = null)
    {
        if (_runs.Contains(key)) throw new InvalidOperationException($"Run '{key}' is in flight already.");
        _runs.Add(key);
        _started.Add(key);
        // Finding F4: the intent is also a message to self, a ticket, so after a crash the inbox alone says what to do next.
        // Its id is derived from instance and key, so a later step can delete it without holding it.
        Send(Worker.TicketId(Id, key), Instance.Ref, Kinds.Run, new RunTicket(Instance.Model, key, arguments is null ? null : Json.Write(arguments)));
        if (_worker.Has(Flaw.VolatileRunTicket)) _volatile.Add(_sent[^1].Id);
    }

    public void CancelRun(string key)
    {
        if (!_runs.Remove(key)) return;     // not in flight: never started, done, or its result is in this step's inbox
        var ticket = Worker.TicketId(Id, key);
        if (_started.Remove(key)) _sent.RemoveAll(m => m.Id == ticket);
        else _cancelled.Add(ticket);
        _worker.Cancelling(ticket);     // from here on its result is nobody's
    }

    public void Hold(Message message)
    {
        if (!Inbox.Contains(message)) throw new InvalidOperationException($"Message '{message.Id}' is not in this step's inbox.");
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
        var id = $"{Id}#{_revision}.{_ordinal:D4}";
        Send(Instance.Ref, Kinds.Publish, new EventRow(key, id, payload, _worker.Now + (long)keep.TotalMilliseconds));
        return id;
    }

    public void Record(object entry) => _entries.Add(entry);
    public void Rebind(string definition) => _definition = _worker.Has(Flaw.RebindForgotten) ? null : definition;
    public void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null) => (_status, _output) = (status, output);

    /// <summary>The step's end is its commit, and the only one.</summary>
    internal Unit Seal(int version)
    {
        var created = Slot.Row is null;
        var history = Slot.Row?.History ?? Spec!.History;
        if (_status != InstanceStatus.Running || _worker.Has(Flaw.HoldLoses)) _held.Clear();     // nothing is held into an ended instance
        foreach (var message in Inbox)
            if (message.Kind == Kinds.RunFinished && _held.Contains(message.Id)) _runs.Add(message.Read<RunFinished>().Key);   // a held result: still in flight
        var row = new InstanceRow(Id, Instance.Model, _definition ?? Instance.Definition, _status, Json.Write(State), _revision, Instance.Parent, Runs: _runs.Count == 0 ? null : Json.Write(_runs), History: history,
            Children: _children.Count == 0 ? null : Json.Write(_children), StateVersion: version);
        if (_status != InstanceStatus.Running && row.Parent is not null) _sent.Add(Worker.Finished(row, _output is null ? null : Json.Write(_output)));
        var unit = new Unit(Slot, Id) { Consumed = Taken.FindAll(i => !_held.Contains(i.Message.Id)), Held = Taken.FindAll(i => _held.Contains(i.Message.Id)), Cancelled = _cancelled, Spec = Spec, Status = _status, Revision = _revision, Emitted = _emitted };
        // An ephemeral instance that ends in the step that created it, and leaves nothing for anyone else, is never written.
        if (created && Spec!.Ephemeral && _status != InstanceStatus.Running && _sent.Count == 0 && _subscribed.Count == 0) return unit;
        if (_kept.Count > 0)
        {
            // Finding F6: "read the events for the keys I subscribed" is a message to self, so a crash after this commit cannot lose it.
            // Only waits that asked for kept events need it: the others get what is published from this commit on.
            Send(Instance.Ref, Kinds.Meet, _subscribed.Where(_kept.Contains).ToArray());
            if (_worker.Has(Flaw.MeetWithoutTicket)) _volatile.Add(_sent[^1].Id);
        }
        unit.Row = row;
        unit.Expected = created ? null : _revision - 1;
        unit.State = State;
        unit.Journal = Worker.Journal(history, _entries);
        unit.Sent = _sent;
        unit.Fresh = _fresh;
        unit.Volatile = _volatile;
        unit.Subscribed = _subscribed;
        unit.Unsubscribed = _unsubscribed;
        return unit;
    }
}
