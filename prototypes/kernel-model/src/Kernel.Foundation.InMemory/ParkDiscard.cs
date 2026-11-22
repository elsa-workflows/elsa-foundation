using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed partial class ActorAdapter
{
    private Durable.TurnOutcome OpenDiscard(Durable.TurnInput input)
    {
        var message = input.Taken[input.From].Message;
        if (input.Row is not { Status: Durable.InstanceStatus.Faulted } row ||
            message.Operator?.Selection is not { } selection ||
            store.FoundationTurnHeader(row).Park is not { } park)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "StaleSelection");
        var identity = store.FoundationIdentity(row.Id, row.Creation);
        if (!store.MatchesFoundationSelection(identity.Tenant, identity.Reference, selection))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "StaleSelection");

        var expiry = store.FoundationExpiry(row);
        var outcome = BeginDiscard(input, row, park, expiry, expired: false);
        if (expiry?.Armed is { } armed) outcome.Cancelled.Add(armed);
        outcome.ReplyTo(input.Taken[input.From], "started");
        return outcome;
    }

    private Durable.TurnOutcome ExpirePark(Durable.TurnInput input)
    {
        var message = input.Taken[input.From].Message;
        if (input.Row is not { Status: Durable.InstanceStatus.Faulted, ParkDiscard: null } row ||
            store.FoundationTurnHeader(row).Park is not { } park ||
            store.FoundationExpiry(row) is not { } expiry)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "InvalidControl");

        var envelope = Durable.ParkDrainEnvelope.For(row, park);
        if (!envelope.MatchesExpiry(message, row, expiry))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
        if (input.Services.Now >= expiry.At)
            return BeginDiscard(input, row, park, expiry, expired: true);

        var early = ParkCheckpoint(input, row, row);
        early.Sent.Add(envelope.Issue(row, checked(expiry.Serial + 1), expiryAt: expiry.At));
        return early;
    }

    private static Durable.TurnOutcome BeginDiscard(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InMemoryStore.FoundationParkMark park, Durable.ParkExpiry? expiry, bool expired)
    {
        var next = row with { ParkResolutionGeneration = checked(row.ParkResolutionGeneration + 1) };
        return AdvanceDiscard(input, row, next, park, input.Taken[input.From].Message.Id, null, 0,
            checked((expiry?.Serial ?? 0) + 1), expired);
    }

    private Durable.TurnOutcome ContinueDiscard(Durable.TurnInput input)
    {
        var message = input.Taken[input.From].Message;
        if (input.Row is not { ParkDiscard: { } drain } row ||
            store.FoundationTurnHeader(row).Park is not { } park ||
            !Durable.ParkDrainEnvelope.For(row, park).Matches(message, row, drain.Control))
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "InvalidControl");

        var page = store.ReadFoundationKeptPage(new(row.Id, row.Creation), park, drain.Cursor);
        if (page is null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidControl");
        if (page.Count > 0)
            return AdvanceDiscard(input, row, row, park, drain.Operation, page, checked(drain.Cleared + page.Count), checked(drain.Serial + 1), drain.Expired);

        var completed = row with { ParkDiscard = null, ParkDiscarded = true, ParkTimerDispositions = null, ParkRestartedJobs = null };
        var outcome = ParkCheckpoint(input, row, completed);
        outcome.Journal = Durable.Json.Write(new { Outcome = drain.Expired ? "expired" : "discarded", drain.Operation, drain.Cleared });
        if (outcome.Row!.Parent is not null)
            outcome.Sent.Add(Notice(outcome.Row, null, final: true));
        return outcome;
    }

    private static Durable.TurnOutcome AdvanceDiscard(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InstanceRow next, Durable.InMemoryStore.FoundationParkMark park, string operation, IReadOnlyList<Durable.ParkPageRow>? page,
        long cleared, long serial, bool expired)
    {
        var control = Durable.ParkDrainEnvelope.For(next, park).Issue(next, serial);
        var drain = new Durable.ParkDiscardDrain(operation, page is null ? null : page[^1].Cursor, cleared, serial, control.InboxKey)
        {
            Expired = expired
        };
        var outcome = ParkCheckpoint(input, row, next with { ParkDiscard = drain });
        outcome.Sent.Add(control);
        if (page is not null)
            outcome.FoundationParkClear = new(park, page);
        return outcome;
    }

    private static Durable.TurnOutcome ParkCheckpoint(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InstanceRow next)
    {
        var revision = checked(row.Revision + 1);
        return new(row.Id)
        {
            Row = next with { Revision = revision }, Expected = row.Revision, Revision = revision, Status = next.Status,
            Consumed = [.. input.Taken]
        };
    }
}
