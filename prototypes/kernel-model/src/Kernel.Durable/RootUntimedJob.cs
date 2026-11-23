namespace Kernel.Durable;

/// <summary>The generated-root preview can issue and complete one untimed job under its existing typed ticket.</summary>
internal static class RootUntimedJob
{
    internal static JobTicket? Read(Message message, string actor, string model)
    {
        if (message is not { Kind: Kinds.Job, JobGeneration: >= 0 and < long.MaxValue, Payload: { } payload } ||
            Json.Read<JobTicket>(payload) is not { Deadline: null, Timer: null, Key.Length: > 0 } ticket ||
            ticket.Model != model) return null;

        var key = InboxKey.Ticket(actor, ticket.Key, 1, message.JobGeneration);
        var expected = (new Message(key.Id, new(actor), Kinds.Job, payload, actor, TargetCreation: 1, SenderCreation: 1)
            { JobGeneration = message.JobGeneration }).WithInboxScope(key.Scope);
        return message.WithoutTicketAuthority() == expected ? ticket : null;
    }

    internal static bool IsCompletion(Delivery delivery, InstanceRow row) =>
        delivery is { Durable: true, IsWorkerJobCompletion: true, Original: { } original } &&
        Read(original, row.Id, row.Model) is { } ticket &&
        JobRegistry.Generation(row, ticket.Key) == original.JobGeneration &&
        MatchesCompletion(original, delivery.Message, ticket.Key);

    internal static bool MatchesCompletion(Message original, Message completion, string key) =>
        completion.Kind == Kinds.JobFinished && completion.Payload is not null &&
        completion.WithoutTicketAuthority() == (original with { Kind = Kinds.JobFinished, Payload = completion.Payload }).WithoutTicketAuthority() &&
        completion.Read<JobFinished>() is { Key: var completed } && completed == key;
}
