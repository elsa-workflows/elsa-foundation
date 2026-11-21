using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>Translates the original prototype turn at the Native model boundary.</summary>
internal sealed class LegacyTurn
{
    private readonly ITurn _turn;
    private readonly Dictionary<Contract.Message, Message> _source = new(ReferenceEqualityComparer.Instance);

    public LegacyTurn(ITurn turn)
    {
        _turn = turn;
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

    public IReadOnlyList<Contract.Message> Items { get; }
    public bool MayHaveRun() => _turn.Inbox.Any(_turn.Redelivered);

    public void Hold(Contract.Message message) => _turn.Hold(_source[message]);
    public void Reply(Contract.Message message, object? reply) => _turn.Reply(_source[message], Request.Response, reply);

    public bool TargetsCurrentLife(Contract.Message message) =>
        _turn.Instance.Creation > 0 && _source[message].TargetCreation == _turn.Instance.Creation;

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

    public Contract.ChildFinished ChildFinished(Contract.Message message)
    {
        var notice = _source[message].Read<ChildFinished>();
        return new(new(notice.Child), (Contract.ActorStatus)notice.Status, notice.Output, Final: true);
    }
}
