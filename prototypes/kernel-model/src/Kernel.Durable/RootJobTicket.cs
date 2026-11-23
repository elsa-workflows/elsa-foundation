namespace Kernel.Durable;

internal sealed record RootJobCompletion(Message Original, Message Result);

/// <summary>Checks the private job envelopes against the exact generated root and job generation.</summary>
internal static class RootJobTicket
{
    internal static JobTicket? Read(Message message, string actor, string model, bool timed = false)
    {
        if (message is not { Kind: Kinds.Job, JobGeneration: >= 0 and < long.MaxValue, Payload: { } payload } ||
            Json.Read<JobTicket>(payload) is not { Key.Length: > 0 } ticket || ticket.Model != model ||
            (timed ? ticket.Deadline is not > 0 || ticket.Timer != DurableTurn.FoundationJobDeadlineId(actor, 1, message.JobGeneration)
                : ticket.Deadline is not null || ticket.Timer is not null)) return null;

        var key = InboxKey.Ticket(actor, ticket.Key, 1, message.JobGeneration);
        var expected = (new Message(key.Id, new(actor), Kinds.Job, payload, actor, TargetCreation: 1, SenderCreation: 1)
            { JobGeneration = message.JobGeneration }).WithInboxScope(key.Scope);
        return message.WithoutTicketAuthority() == expected ? ticket : null;
    }

    internal static Message Deadline(string actor, JobTicket ticket) =>
        new Message(ticket.Timer!, new(actor), Kinds.JobDeadline, Json.Write(new JobDeadline(ticket.Key)), actor,
            Due: ticket.Deadline, TargetCreation: 1, SenderCreation: 1).AsIssued();

    internal static bool StartsTimedPair(IReadOnlyList<Message> sends, string actor, string model) =>
        sends is [var deadline, var ticket] && deadline.HasIssuedTicket && ticket.HasIssuedTicket &&
        Read(ticket, actor, model, timed: true) is { } job && deadline.WithoutTicketAuthority() == Deadline(actor, job);

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
