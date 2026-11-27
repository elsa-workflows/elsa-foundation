using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private ValueTask<Committed> CommitRootPublicationAsync(CommitBatch batch, long sentAt, CancellationToken token)
    {
        if (_ordinaryRoots is not { PagesPublicEvents: true } || !OwnedCommit || batch.Fenced is not [var fence])
            throw PublicationShape();

        StoreBatchRules.Validate(batch, ownedSets: OwnedSetsAvailable, supportsFoundationParkResolution: true,
            supportsDueBacklog: SupportsDueBacklog);
        var work = batch.PublicationReservation is { } reservation
            ? new RootPublicationCheckpoint(fence, reservation.Key, reservation.Sequence, reservation.Attempts, null, null, null)
            : RootPublicationCheckpoint.ForTurn(fence, batch.Turns.Single());
        if (work.Key is not { Scope: RootPublicEventTicket.Scope, Match: null } || work.Sequence <= 0 || work.Attempts < 0)
            throw PublicationShape();
        return CommitAsync(batch with { HandOver = false, Trial = [], Turns = [], PublicationReservation = null }, work, sentAt, token);
    }

    private sealed record RootPublicationCheckpoint(PartitionFence Fence, InboxKey Key, long Sequence, int Attempts,
        string? Target, long? Deadline, bool? Exhausted) : QualifiedRootCheckpoint(0)
    {
        private RootPublicEventTicket? _ticket;
        private Func<FormattableString, FormattableString>? _bind;

        internal static RootPublicationCheckpoint ForTurn(PartitionFence fence, TurnCommit turn)
        {
            if (turn.Row is not null || turn.Expected is not null || turn.Delete || turn.Journal is not null || turn.Unsubscribe ||
                turn.Subscribed.Count + turn.Unsubscribed.Count + turn.Sent.Count + (turn.Held?.Count ?? 0) != 0 ||
                turn.FoundationPublicationSequence is not { } sequence || StoreBatchRules.AddressPartition(turn.Instance) != fence.Partition)
                throw PublicationShape();
            if (turn.FoundationCatchUpFailed is { } failure)
            {
                var exhausted = failure.Attempts >= failure.Limit;
                if (failure.Limit <= 0 || turn.Consumed.Count != 0 ||
                    (exhausted ? turn.Done is not [var settled] || settled != failure.Key : turn.Done.Count != 0))
                    throw PublicationShape();
                return new(fence, failure.Key, sequence, failure.Attempts, turn.Instance, null, exhausted);
            }
            if (turn.FoundationCatchUpStep is not { } key || turn.FoundationCatchUpBefore is not { } deadline ||
                turn.FoundationCatchUpExpected is not { } attempts || turn.Consumed.Count != 0 || turn.Done is not [var done] || done != key)
                throw PublicationShape();
            return new(fence, key, sequence, attempts, turn.Instance, deadline, null);
        }

        internal async Task PrepareAsync(KernelDb db, uint? marker, CancellationToken token)
        {
            if (!db.UsesRootPagedPublicEvents) throw PublicationShape();
            _bind = db.Database.IsNpgsql() ? await PostgresRootAdmission.PinAuthoredEventSourceAsync(db, marker, token) : null;
            var source = await db.Messages.AsNoTracking().SingleOrDefaultAsync(message => message.Id == Key.Id &&
                message.InboxScope == Key.Scope && message.Partition == Fence.Partition && message.Seq == Sequence &&
                message.Attempts == Attempts && !message.Done, token);
            _ticket = source is null ? null : RootPublicEventTicket.Decode(source);
            if (Target is not null && _ticket is { } ticket && ticket.Target.Id != Target) throw PublicationShape();
            // The key lock precedes the fence, as in authored checkpoint and subscription commits.
            if (_bind is not null && _ticket is { } page && Target is not null)
                await RootEventAdmission.LockPostgresKeysAsync(db, [page.QualifiedKey], token);
        }

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (_ticket is not { } ticket) return false;
            if (Target is null)
                return await ChangeTicketAsync(RootPublicEventTicket.Change.Reserve, Fence.Epoch);
            if (Exhausted == false)
                return await ChangeTicketAsync(RootPublicEventTicket.Change.RecordFailure, Fence.Epoch);

            var item = await RootPublicEventDispatch.ReadPendingAsync(db, ticket.Occurrence, ticket.Id, ticket.Target.Id,
                Fence.Partition, _bind, token);
            if (item is null || item.Key != ticket.QualifiedKey || item.EventId != ticket.EventId)
                throw PublicationShape();
            if (Exhausted == true)
                return await RootPublicEventDispatch.ChangeAsync(db, item, item.Position, Contract.PublicationStatus.DeadLettered, _bind, token) &&
                    await ChangeTicketAsync(RootPublicEventTicket.Change.Settle, epoch: null);

            await RequireWindowAsync();
            var page = await RootEventAdmission.MatchLivePageAsync(db, item.Tenant, item.Key, item.EventId, item.Occurrence,
                item.Payload, item.KeepUntil, item.Position.LiveThrough, _bind, token);
            if (item.Position.LiveComplete || item.Position.StartThrough is not null) throw PublicationShape();
            if (!await RootPublicEventDispatch.ChangeAsync(db, item, new(page.Through, page.Complete, null),
                page.Complete ? Contract.PublicationStatus.Matched : Contract.PublicationStatus.Pending, _bind, token) ||
                !await ChangeTicketAsync(page.Complete ? RootPublicEventTicket.Change.Settle : RootPublicEventTicket.Change.Advance, Fence.Epoch))
                return false;
            await RequireWindowAsync();
            return true;

            Task<bool> ChangeTicketAsync(RootPublicEventTicket.Change change, long? epoch) =>
                ticket.ChangeAsync(db, Sequence, Attempts, epoch, change, _bind, token);

            async Task RequireWindowAsync()
            {
                var now = _bind is null ? await SqliteRootAdmission.ReadClockAsync(db, token) :
                    await RootEventAdmission.ReadPostgresClockAsync(db, token);
                if (now >= Deadline!.Value) throw new StoreException(Failure.Definite, "The public Event publication page deadline elapsed.");
            }
        }
    }

    private static StoreException PublicationShape() => new(Failure.Definite,
        "A public Event publication page requires its exact issued ticket, sequence, trial and partition fence.");
}
