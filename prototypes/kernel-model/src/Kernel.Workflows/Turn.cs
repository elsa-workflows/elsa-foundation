using Kernel.Durable;
using Kernel.Runtime;

namespace Kernel.Workflows;

/// <summary>
/// What a model sees of one turn of a workflow instance. The instance's part (status, definition, children, the
/// parent's notice, the history envelope) is kept here; the durable part (sends, jobs, holds, waits, live output) is
/// layer 2's <see cref="DurableTurn"/>. Nothing leaves it before <see cref="Seal"/>.
/// </summary>
internal sealed class Turn : ITurn
{
    private readonly TurnInput _input;
    private readonly DurableTurn _durable;
    private readonly List<object> _entries = [];
    private readonly List<string> _children;
    private InstanceStatus _status = InstanceStatus.Running;
    private object? _output;
    private string? _definition;

    internal Turn(TurnInput input)
    {
        _input = input;
        _durable = new DurableTurn(input);
        var (row, spec) = (input.Row, input.Spec);
        Instance = new InstanceInfo(new InstanceRef(input.Actor), row?.Model ?? spec!.Model, row?.Definition ?? spec!.Definition, row is null ? spec!.Parent : row.Parent);
        _children = row?.Children is { } children ? Json.Read<List<string>>(children) : [];
        foreach (var message in Inbox)
            if (message.Kind == WorkflowKinds.ChildFinished) _children.Remove(message.Read<ChildFinished>().Child);
    }

    public InstanceInfo Instance { get; }
    public ICommitClock Clock => _durable.Clock;
    public IReadOnlyList<Message> Inbox => _durable.Inbox;
    public object State { get; set; } = null!;
    public object? Ambient => _durable.Ambient;
    private string Id => Instance.Ref.Id;
    private bool Has(Flaw flaw) => _input.Services.Has(flaw);

    public void Emit(string channel, object? item) => _durable.Emit(channel, item);
    public void Reply(Message request, string channel, object? item) => _durable.Reply(request, channel, item);
    public bool Redelivered(Message message) => _durable.Redelivered(message);
    public string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null) => _durable.Send(target, kind, payload, options);
    public void Recall(string message) => _durable.Recall(message);

    public InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false)
    {
        // With its parent by default, so a call tree talks in memory and commits together; spread, for a wide fan-out.
        var name = $"{Id[(Id.IndexOf('/') + 1)..]}~{_durable.Revision}.{_durable.Ordinal}";
        var child = InstanceRef.In(placement == Placement.Spread ? InstanceRef.Spread(name, _input.Services.Partitions) : Instance.Ref.Partition, name);
        _durable.Send(child, Kinds.Start, null, new SendOptions(Start: detached ? spec with { Parent = null } : spec with { Parent = Id }));
        if (!detached) _children.Add(child.Id);    // the instance's list: whom a terminate goes on to
        if (child.Partition == Instance.Ref.Partition) _durable.Fresh();   // minted and owned here, so it cannot exist: no read
        return child;
    }

    public void StartJob(string key, object? arguments = null) => _durable.StartJob(key, arguments);
    public void CancelJob(string key) => _durable.CancelJob(key);
    public void Hold(Message message) => _durable.Hold(message);
    public void Subscribe(string key, string wait, bool keep = false) => _durable.Subscribe(key, wait, keep);
    public void Unsubscribe(string key, string wait) => _durable.Unsubscribe(key, wait);
    public string Publish(string key, string? payload, TimeSpan keep) => _durable.Publish(key, payload, keep);

    public void Record(object entry) => _entries.Add(entry);
    public void Rebind(string definition) => _definition = Has(Flaw.RebindForgotten) ? null : definition;
    public void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null) => (_status, _output) = (status, output);

    /// <summary>The turn's end is its commit, and the only one.</summary>
    internal TurnOutcome Seal(int version)
    {
        var created = _input.Row is null;
        var history = _input.Row?.History ?? _input.Spec!.History;
        if (_status != InstanceStatus.Running || Has(Flaw.HoldLoses)) _durable.HoldNothing();     // nothing is held into an ended instance
        var row = new InstanceRow(Id, Instance.Model, _definition ?? Instance.Definition, _status, Json.Write(State), _durable.Revision, Instance.Parent, Jobs: _durable.Jobs(), History: history,
            Children: _children.Count == 0 ? null : Json.Write(_children), StateVersion: version);
        if (_status != InstanceStatus.Running && row.Parent is not null) _durable.Add(WorkflowInstance.Finished(row, _output is null ? null : Json.Write(_output)));
        var unit = _durable.Outcome(_status);
        // An ephemeral instance that ends in the turn that created it, and leaves nothing for anyone else, is never written.
        if (created && _input.Spec!.Ephemeral && _status != InstanceStatus.Running && _durable.SentCount == 0 && _durable.SubscribedCount == 0) return unit;
        unit = _durable.Seal(unit, row, State, WorkflowInstance.Journal(history, _entries));
        if (_status != InstanceStatus.Running && _input.Row is not { Status: not InstanceStatus.Running })
            unit.Row = WorkflowCommitClock.End(unit.Row!);
        return unit;
    }
}
