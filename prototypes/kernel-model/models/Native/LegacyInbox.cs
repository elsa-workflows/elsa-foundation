using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>Projects the original prototype inbox for the Native model's contract-facing input handling.</summary>
internal sealed class LegacyInbox
{
    private readonly ITurn _turn;
    private readonly Dictionary<Contract.Message, Message> _source = new(ReferenceEqualityComparer.Instance);

    public LegacyInbox(ITurn turn)
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

    public Contract.ChildFinished ChildFinished(Contract.Message message)
    {
        var notice = _source[message].Read<ChildFinished>();
        return new(new(notice.Child), (Contract.ActorStatus)notice.Status, notice.Output, Final: true);
    }
}
