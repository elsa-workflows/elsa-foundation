using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed partial class ActorAdapter
{
    private Durable.TurnOutcome RefuseDuringDiscard(Durable.TurnInput input, Durable.InstanceRow row)
    {
        var refusal = Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, "ResolutionOpen");
        if (row.Cascade is not null || !store.AuthorizesDrainingTerminate(row, input.Taken[input.From].Message)) return refusal;
        var entries = Durable.CascadeRegistry.From(row);
        if (entries.Count == 0) return refusal;

        refusal.Row = row with { Revision = checked(row.Revision + 1) };
        refusal.Expected = row.Revision;
        refusal.Revision = refusal.Row.Revision;
        return Durable.CascadeProtocol.Open(refusal, entries);
    }

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
        var outcome = BeginClear(input, row, park, expiry, Durable.ParkClearKind.Discard);
        if (expiry?.Armed is { } armed) outcome.Cancelled.Add(armed);
        outcome.ReplyTo(input.Taken[input.From], "started");
        return outcome;
    }

    private Durable.TurnOutcome OpenParkTerminate(Durable.TurnInput input, Durable.InstanceRow row, Durable.InMemoryStore.FoundationParkMark park)
    {
        var message = input.Taken[input.From].Message;
        if (!store.AuthorizesDrainingTerminate(row, message))
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, message.Operator is null ? "ResolutionUnavailable" : "StaleSelection");

        var expiry = store.FoundationExpiry(row);
        var outcome = BeginClear(input, row, park, expiry, Durable.ParkClearKind.Terminate);
        if ((row.ParkRecovery?.Control ?? expiry?.Armed) is { } recalled) outcome.Cancelled.Add(recalled);
        outcome.Unsubscribe = true;
        outcome.FoundationWithholds = true;
        outcome.Journal = Durable.InMemoryStore.InterruptedRecoverJournal(row.ParkRecovery);
        outcome.ReplyTo(input.Taken[input.From], "started");
        return Durable.CascadeProtocol.Open(outcome, Durable.CascadeRegistry.From(row));
    }

    private Durable.TurnOutcome OpenParkDelete(Durable.TurnInput input, Durable.InstanceRow row, Durable.InMemoryStore.ActorCheckpoint checkpoint)
    {
        var delivery = input.Taken[input.From];
        var header = Header(checkpoint);
        var protection = DeletionRefusal(header, row.State, input.Services.Now);
        if (protection is not null)
            return Durable.CreationProtocol.Refuse(input.Actor, row, input.Taken, protection);

        var expiry = store.FoundationExpiry(row);
        var outcome = BeginClear(input, row, checkpoint.Park!, expiry, Durable.ParkClearKind.Delete);
        if (expiry?.Armed is { } armed) outcome.Cancelled.Add(armed);
        outcome.ReplyTo(delivery, "started");
        return outcome;
    }

    private string? DeletionRefusal(Contract.ActorHeader header, string state, long now)
    {
        try { return actor.ProtectsDeletion(header, state, now) ? "Protected" : null; }
        catch (Exception) { return "ProtectionUnavailable"; }
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
            return BeginClear(input, row, park, expiry, Durable.ParkClearKind.Expiry);

        var early = ParkCheckpoint(input, row, row);
        early.Sent.Add(envelope.Issue(row, checked(expiry.Serial + 1), expiryAt: expiry.At));
        return early;
    }

    private static Durable.TurnOutcome BeginClear(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InMemoryStore.FoundationParkMark park, Durable.ParkExpiry? expiry, Durable.ParkClearKind kind)
    {
        var next = row with
        {
            ParkResolutionGeneration = checked(row.ParkResolutionGeneration + 1),
            ParkRecovery = null, ParkCohort = null
        };
        return AdvanceClear(input, row, next, park, input.Taken[input.From].Message.Id, null, 0,
            checked((row.ParkRecovery?.Serial ?? expiry?.Serial ?? 0) + 1), kind);
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
            return AdvanceClear(input, row, row, park, drain.Operation, page, checked(drain.Cleared + page.Count), checked(drain.Serial + 1), drain.Kind);

        var completed = row with { ParkDiscard = null, ParkDiscarded = true, ParkTimerDispositions = null, ParkRestartedJobs = null };
        if (drain.Kind == Durable.ParkClearKind.Terminate)
            completed = completed with { Status = Durable.InstanceStatus.Cancelled, Suspended = false };
        var outcome = ParkCheckpoint(input, row, completed);
        if (drain.Kind == Durable.ParkClearKind.Delete)
        {
            if (outcome.Row!.Parent is not null) outcome.Sent.Add(Notice(outcome.Row, null, final: true));
            var retire = DeletionRefusal(Header(store.FoundationTurnHeader(row)), row.State, input.Services.Now) is null;
            outcome.Journal = Durable.InMemoryStore.ParkDeleteJournal(retire, drain);
            if (retire)
            {
                outcome.Row = Durable.CreationProtocol.Retire(row, Durable.CascadeRegistry.ForRetirement(row));
                outcome.Delete = outcome.Unsubscribe = true;
            }
            return outcome;
        }
        var resolution = drain.Kind switch
        {
            Durable.ParkClearKind.Terminate => "terminated",
            Durable.ParkClearKind.Expiry => "expired",
            _ => "discarded"
        };
        outcome.Journal = Durable.Json.Write(new { Outcome = resolution, drain.Operation, drain.Cleared });
        if (outcome.Row!.Parent is not null)
            outcome.Sent.Add(Notice(outcome.Row, null, final: true));
        return outcome;
    }

    private static Durable.TurnOutcome AdvanceClear(Durable.TurnInput input, Durable.InstanceRow row,
        Durable.InstanceRow next, Durable.InMemoryStore.FoundationParkMark park, string operation, IReadOnlyList<Durable.ParkPageRow>? page,
        long cleared, long serial, Durable.ParkClearKind kind)
    {
        var control = Durable.ParkDrainEnvelope.For(next, park).Issue(next, serial);
        var drain = new Durable.ParkDiscardDrain(operation, page is null ? null : page[^1].Cursor, cleared, serial, control.InboxKey)
        {
            Kind = kind
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
