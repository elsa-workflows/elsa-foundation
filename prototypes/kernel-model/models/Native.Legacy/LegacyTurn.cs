using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>Translates the original prototype turn at the Native model boundary.</summary>
internal sealed class LegacyTurn : INativeTurn
{
    private readonly ITurn _turn;
    private readonly NativeOptions _options;
    private readonly Dictionary<Contract.Message, Message> _source = new(ReferenceEqualityComparer.Instance);

    public LegacyTurn(ITurn turn, NativeOptions options)
    {
        _turn = turn;
        _options = options;
        Clock = new LegacyReceiptClock(turn.Clock);
        var items = new List<Contract.Message>();
        foreach (var original in turn.Inbox)
        {
            var payload = original.Kind == Contract.Kinds.Start ? original.Start?.Input : original.Payload;
            var message = new Contract.Message(original.Id, original.Kind, payload,
                original.Sender is { } sender ? new Contract.ActorRef(sender) : null);
            items.Add(message);
            _source.Add(message, original);
        }
        Items = items;
    }

    public Contract.ActorRef Actor => new(_turn.Instance.Ref.Id);
    public string Definition => _turn.Instance.Definition;
    public bool HasExactIdentity => _turn.Instance.Creation > 0;
    public bool SavesWaitRecipes => false;
    public string OperationOrigin => CreationProtocol.Origin(Actor.Id, _turn.Instance.Creation);
    public long Now => _turn.Now;
    public NativeState State => (NativeState)_turn.State;
    public object? Ambient => _turn.Ambient;
    public IReceiptClock Clock { get; }
    public IReadOnlyList<Contract.Message> Items { get; }
    public bool MayHaveRun() => _turn.Inbox.Any(_turn.Redelivered);

    public void Hold(Contract.Message message) => _turn.Hold(_source[message]);
    public void Reply(Contract.Message message, object? reply) => _turn.Reply(_source[message], Request.Response, reply);

    public bool TargetsCurrentLife(Contract.Message message) =>
        _turn.Instance.Creation > 0 && _source[message].TargetCreation == _turn.Instance.Creation;

    public string TimerWait(Contract.Message message) => _source[message].Read<TimerFired>().Wait;
    public string? EventWait(Contract.Message message) => Events.Delivered(_source[message])?.Wait;

    public string SendToSelf(string kind, object? payload = null, long? dueAt = null) =>
        _turn.Send(_turn.Instance.Ref, kind, payload, dueAt is null ? null : new SendOptions(DueAt: dueAt));

    public WaitEntry StartChild(string frame, string workflow, string mode, JsonObject? input)
    {
        var child = _turn.Start(NativeModel.Spec(workflow, mode, input));
        return new(frame, "child", child.Id, _turn.ChildAddress(child).Creation);
    }

    public Contract.ActorRef StartDetached(string workflow, string mode, JsonObject? input) =>
        new(_turn.Start(NativeModel.Spec(workflow, mode, input), detached: true).Id);

    public void CancelChild(WaitEntry child) =>
        _turn.Send(new InstanceRef(child.Key), NativeKinds.Cancel, options: new SendOptions(TargetCreation: child.Creation > 0 ? child.Creation : null));

    public void StartJob(string key, BackgroundArgs arguments) => _turn.StartJob(key, arguments);
    public void CancelJob(string key) => _turn.CancelJob(key);
    public void Subscribe(string key, string wait) => _turn.Subscribe(key, wait);
    public void Unsubscribe(string key, string wait) => _turn.Unsubscribe(key, wait);
    public void Recall(string id) => _turn.Recall(id);
    public void Publish(string key, string? payload, TimeSpan keep) => _turn.Publish(key, payload, keep);
    public void Emit(string channel, object? item) => _turn.Emit(channel, item);
    public void Record(string entry) => _turn.Record(entry);

    public void SetStatus(Contract.ActorStatus status, string? subStatus, JsonNode? output) =>
        _turn.SetStatus((InstanceStatus)status, subStatus, output);

    public string EffectKey(string node, string iteration, int attempt) =>
        Kernel.Workflows.EffectKey.For(_turn.Instance, node, iteration, attempt);

    public void ExternalAttempt(string frame, string node, int attempt, bool mayHaveRun) =>
        _options.OnExternal?.Invoke(_turn.Instance.Ref, frame, node, attempt, mayHaveRun);

    public void SendOutbound(string frame, string node, string iteration, int attempt, string key, OutboundEffect effect) =>
        _options.OnOutbound?.Invoke(new OutboundSending(_turn.Instance, frame, node, iteration, attempt, key, effect));

    public Contract.ChildFinished ChildFinished(Contract.Message message)
    {
        var notice = _source[message].Read<ChildFinished>();
        return new(new(notice.Child), (Contract.ActorStatus)notice.Status, notice.Output, Final: true);
    }
}

internal sealed class LegacyReceiptClock(ICommitClock clock) : IReceiptClock
{
    public string Terminal => WorkflowCommitClock.Terminal;
    public void Require() => clock.Require();
    public string Capture() => clock.Capture();
    public long? Resolve(string reference) => clock.Resolve(reference);
    public void Release(string reference) => clock.Release(reference);
    public bool IsModelReference(string reference) => reference.StartsWith(CommitClockFacts.ModelPrefix, StringComparison.Ordinal);
}
