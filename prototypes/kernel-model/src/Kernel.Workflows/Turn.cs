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
    private readonly List<CreationAddress> _children;
    private InstanceStatus _status = InstanceStatus.Running;
    private object? _output;
    private string? _definition;

    internal Turn(TurnInput input)
    {
        _input = input;
        _durable = new DurableTurn(input);
        var (row, spec) = (input.Row, input.Spec);
        _status = row?.Status ?? InstanceStatus.Running;
        Instance = new InstanceInfo(new InstanceRef(input.Actor), row?.Model ?? spec!.Model, row?.Definition ?? spec!.Definition, row is null ? spec!.Parent : row.Parent, input.Creation);
        _children = ChildRegistry.Read(row?.Children);
        foreach (var message in Inbox)
            if (message.Kind == WorkflowKinds.ChildFinished)
            {
                var finished = message.Read<ChildFinished>();
                _children.Remove(new(finished.Child, finished.Creation));
            }
    }

    public InstanceInfo Instance { get; }
    public long Now => _input.Services.Now;
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
        if (Instance.Creation == 0 && spec.Creation is not null) throw new NotSupportedException("an unqualified parent cannot pin a qualified child");
        var stem = Instance.Creation > 0 ? CreationProtocol.Origin(Id, Instance.Creation) : Id[(Id.IndexOf('/') + 1)..];
        var name = $"{stem}~{_durable.Revision}.{_durable.Ordinal}";
        var child = InstanceRef.In(placement == Placement.Spread ? InstanceRef.Spread(name, _input.Services.Partitions) : Instance.Ref.Partition, name);
        var creation = Instance.Creation > 0 ? new CreationStart(1, 0,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(child.Id)))) : null;
        spec = spec with { Creation = creation, Parent = detached ? null : Id, ParentCreation = detached ? 0 : Instance.Creation };
        _durable.Send(child, Kinds.Start, null, new SendOptions(Start: spec));
        if (!detached) _children.Add(new(child.Id, creation?.Generation ?? 0));    // the exact child addressed even before its first commit
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
            Children: ChildRegistry.Write(_children), StateVersion: version, Creation: Instance.Creation,
            StartIntent: _input.Row?.StartIntent ?? CreationProtocol.Fingerprint(_input.Spec, _input.Taken[_input.From].Message.Kind, _input.Taken[_input.From].Message.Payload),
            ParentCreation: _input.Row?.ParentCreation ?? _input.Spec?.ParentCreation ?? 0);
        if (_input.Row is { Status: not InstanceStatus.Running } ended)
            row = ended with { State = Json.Write(State), Revision = _durable.Revision, StateVersion = version };
        if (_status != InstanceStatus.Running && _input.Row is not { Status: not InstanceStatus.Running } && row.Parent is not null) _durable.Add(WorkflowInstance.Finished(row, _output is null ? null : Json.Write(_output)));
        var unit = _durable.Outcome(_status);
        // An ephemeral instance that ends in the turn that created it, and leaves nothing for anyone else, is never written.
        if (created && Instance.Creation == 0 && _input.Spec!.Ephemeral && _status != InstanceStatus.Running && _durable.SentCount == 0 && _durable.SubscribedCount == 0) return unit;
        unit = _durable.Seal(unit, row, State, WorkflowInstance.Journal(history, _entries));
        if (_status != InstanceStatus.Running && _input.Row is not { Status: not InstanceStatus.Running })
            unit.Row = WorkflowCommitClock.End(unit.Row!);
        return unit;
    }
}
