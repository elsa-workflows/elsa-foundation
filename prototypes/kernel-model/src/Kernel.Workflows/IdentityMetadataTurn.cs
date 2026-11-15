using Kernel.Durable;

namespace Kernel.Workflows;

internal sealed class IdentityMetadataTurn(TurnInput input) : IIdentityMetadataTurn
{
    private readonly TurnOutcome _unit = new(input.Actor) { Consumed=[..input.Taken],Status=input.Row!.Status };
    private readonly DurableTurn _durable=new(input);
    private readonly HashSet<InboxKey> _answered=[];
    public TerminalIdentity Identity=>TerminalIdentity.From(input.Row!);
    public long Now=>input.Services.Now;
    public ICommitClock Clock=>_durable.Clock;
    public IReadOnlyList<Message> Inbox=>input.Taken.Select(t=>t.Message).ToList();
    public void Reply(Message message,object? value)
    {
        var delivery=input.Taken.SingleOrDefault(d=>d.Message==message) ?? throw new ArgumentException("Reply must name this delivery.");
        if (!_answered.Add(message.InboxKey)) throw new InvalidOperationException("A metadata delivery can be answered only once.");
        _unit.ReplyTo(delivery,value);
    }
    public void Refuse(Message message)=>Reply(message,new IdentityMetadataUnavailable(input.Actor,input.Row!.Model,input.Row.StateVersion));
    public TurnOutcome Seal()
    {
        foreach(var message in Inbox.Where(m=>!_answered.Contains(m.InboxKey)))Refuse(message);
        var row=input.Row!;
        _unit.Revision=_durable.Revision;
        // State-free metadata still owns a durable turn: clock changes, handle cleanup and the
        // original committed revision follow the same seal path as ordinary model turns.
        return _durable.Seal(_unit,row with {Revision=_durable.Revision,Jobs=_durable.Jobs()},null,WorkflowInstance.Journal(row.History,[]));
    }
    internal static TurnOutcome Unavailable(TurnInput input)
    {
        var turn=new IdentityMetadataTurn(input);
        foreach(var message in turn.Inbox)turn.Refuse(message);
        return turn.Seal();
    }
}
