using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private async ValueTask<Committed> CommitRootPublicationAsync(CommitBatch batch, long sentAt, CancellationToken token)
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
        var committed = await CommitAsync(batch with { HandOver = false, Trial = [], Turns = [], PublicationReservation = null },
            work, sentAt, token);
        if (committed.Landed && work.Exhausted == false)
            KernelMetrics.Add(KernelMetrics.EventTicketFailures, 1, new("kind", "publish"));
        return committed;
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
                turn.FoundationCatchUpExpected is not { } attempts ||
                turn.Consumed.Concat(turn.Done).ToArray() is not [var consumed] || consumed != key)
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
            if (Exhausted == true && ticket.Author is not null)
                return await ticket.DeadLetterAuthoredAsync(db, Sequence, Attempts, fromPark: false, _bind, token);

            if (ticket.Initial)
            {
                await RequireWindowAsync();
                var author = ticket.Author!;
                var accepted = author.AcceptedAt!.Value;
                var until = checked(accepted + author.KeepFor);
                var payload = RootAdmissionWire.DecodePayload(ticket.Payload.Text, ticket.Payload.Escaped);
                await RootPublicEventPublications.InsertAsync(db, author.Tenant, ticket.QualifiedKey, ticket.EventId,
                    ticket.Occurrence, ticket.Incarnation, ticket.Occurrence, payload, accepted, receiveThrough: null, until,
                    sql => RootPublicEventStarts.Bind(sql, _bind), token);
                await RootPublicEventDispatch.AddAsync(db, ticket.Occurrence, ticket.Id, ticket.Target.Id, Fence.Partition, _bind, token);
                if (!await ChangeTicketAsync(RootPublicEventTicket.Change.Materialize, Fence.Epoch)) return false;
                await RequireWindowAsync();
                return true;
            }

            var item = await RootPublicEventDispatch.ReadPendingAsync(db, ticket.Occurrence, ticket.Id, ticket.Target.Id,
                Fence.Partition, _bind, token);
            if (item is null || item.Key != ticket.QualifiedKey || item.EventId != ticket.EventId)
                throw PublicationShape();
            if (Exhausted == true)
                return await RootPublicEventDispatch.ChangeAsync(db, item, item.Position, Contract.PublicationStatus.DeadLettered, _bind, token) &&
                    await ChangeTicketAsync(RootPublicEventTicket.Change.Settle, epoch: null);

            await RequireWindowAsync();
            var (next, complete) = await MatchAsync(item);
            if (!await RootPublicEventDispatch.ChangeAsync(db, item, next,
                complete ? Contract.PublicationStatus.Matched : Contract.PublicationStatus.Pending, _bind, token) ||
                !await ChangeTicketAsync(complete ? RootPublicEventTicket.Change.Settle : RootPublicEventTicket.Change.Advance, Fence.Epoch))
                return false;
            await RequireWindowAsync();
            return true;

            async Task<(RootPublicEventDispatch.Cursor Next, bool Complete)> MatchAsync(RootPublicEventDispatch.Publication publication)
            {
                var position = publication.Position;
                if (position.StartsComplete)
                {
                    if (ticket.Author is null) throw PublicationShape();
                    var targets = await RootEventStarts.CapturePageAsync(db, publication, _bind, token);
                    return (position with { ProtectedThrough = targets.Through }, targets.Complete);
                }
                if (position.LiveComplete)
                {
                    var starts = await RootPublicEventStarts.MatchPageAsync(db, publication, _bind, token);
                    return (position with { StartThrough = starts.Through, StartsComplete = starts.Complete },
                        starts.Complete && !await HasProtectedStartsAsync());
                }

                var live = await RootEventAdmission.MatchLivePageAsync(db, publication.Tenant, publication.Key, publication.EventId,
                    publication.Occurrence, publication.Payload, publication.KeepUntil, position.LiveThrough, _bind, token);
                var next = position with { LiveThrough = live.Through, LiveComplete = live.Complete };
                if (!live.Complete || await RootPublicEventStarts.HasMatchesAsync(db, publication.Tenant, publication.Key, _bind, token))
                    return (next, false);
                return (next with { StartsComplete = true }, !await HasProtectedStartsAsync());

                async Task<bool> HasProtectedStartsAsync() => ticket.Author is not null &&
                    await RootEventStarts.HasMatchesAsync(db, publication.Tenant, publication.Key, _bind, token);
            }

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
