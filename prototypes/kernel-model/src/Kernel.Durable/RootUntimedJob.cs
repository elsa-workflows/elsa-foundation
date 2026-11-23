namespace Kernel.Durable;

/// <summary>The generated-root preview can issue and complete one untimed job under its existing typed ticket.</summary>
internal static class RootUntimedJob
{
    internal static JobTicket? Read(Message message, string actor, string model)
    {
        if (message is not
            {
                Kind: Kinds.Job, Due: null, TargetCreation: 1, SenderCreation: 1, JobGeneration: >= 0 and < long.MaxValue,
                EventMatch: null, ActorAuthored: false, Start: null, ReplyTo: null, KeepUntil: null, Payload: { } payload
            } || message.Target.Id != actor || message.Sender != actor) return null;
        var ticket = Json.Read<JobTicket>(payload);
        return ticket is { Deadline: null, Timer: null, Key.Length: > 0 } && ticket.Model == model &&
            message.InboxKey == InboxKey.Ticket(actor, ticket.Key, 1, message.JobGeneration) ? ticket : null;
    }

    internal static bool IsCompletion(Delivery delivery, InstanceRow row) =>
        delivery is { Durable: true, IsWorkerJobCompletion: true, Original: { } original } &&
        Read(original, row.Id, row.Model) is { } ticket &&
        JobRegistry.Generation(row, ticket.Key) == original.JobGeneration &&
        MatchesCompletion(original, delivery.Message, ticket.Key);

    internal static bool MatchesCompletion(Message original, Message completion, string key) =>
        completion.Kind == Kinds.JobFinished && completion.Payload is not null &&
        completion.WithoutTicketAuthority() == (original with { Kind = Kinds.JobFinished, Payload = completion.Payload }).WithoutTicketAuthority() &&
        completion.Read<JobFinished>().Key == key;
}
