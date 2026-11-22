using System.Text;
using Kernel.Runtime;

namespace Kernel.Durable;

internal sealed record FoundationPublishWork(string Key, string Id, Guid Incarnation, SubscriptionPosition? After = null);
internal sealed record FoundationPublicationReservation(InboxKey Key, long Sequence, int Attempts);
internal enum FoundationPublicationStatus { Pending, Matched, DeadLettered }
internal sealed record FoundationPublicationReceipt(bool Duplicate, EventRow? Event, FoundationPublicationStatus Status);

public sealed partial class InMemoryStore
{
    internal Func<Guid> NewPublicationIncarnation { get; init; } = Guid.NewGuid;

    private sealed record PublicationPage(MessageRow Source, IReadOnlyList<EventWrite> Writes, long Serial,
        Message? Next = null, ParkCatchUpFailure? Failed = null);

    internal FoundationPublicationReceipt AdmitFoundationPublication(string tenant, string correlation, string id, string? payload,
        TimeSpan? keepFor, CancellationToken cancellationToken)
    {
        FoundationMetadataLimits.RequireContent(payload, nameof(payload), FoundationMetadataLimits.EventContentBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepFor ?? TimeSpan.Zero, TimeSpan.Zero, nameof(keepFor));
        var key = (tenant + "_" + correlation, id);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = clock();
            if (_foundationEvents.TryGetValue(key, out var original))
            {
                var prior = original.FoundationPublishTicket is { } linked ? Messages.GetValueOrDefault(linked) : null;
                var status = original.FoundationPublishFailed ? FoundationPublicationStatus.DeadLettered :
                    prior is { Done: false } ? FoundationPublicationStatus.Pending : FoundationPublicationStatus.Matched;
                return new(true, original, status);
            }
            if (_foundationEventReceipts.TryGetValue(key, out var legacy) && legacy >= now)
                return new(true, null, FoundationPublicationStatus.Matched);
            if (!CreationIdentityAvailable)
                throw new StoreException(Failure.Definite, "A publication requires qualified creation identity.");

            var incarnation = NewPublicationIncarnation();
            var keepUntil = checked(now + (long)(keepFor ?? TimeSpan.Zero).TotalMilliseconds);
            var receiptUntil = checked(now + (long)IngressDeduplicationWindow.TotalMilliseconds);
            var publication = new EventRow(key.Item1, id, payload, keepUntil)
            {
                FoundationTenant = tenant, FoundationIncarnation = incarnation, FoundationAcceptedAt = now
            };
            var ticketId = FoundationEventIdentity.PublishId(publication, incarnation);
            var address = InstanceRef.In(InstanceRef.Spread(ticketId, partitions), "publisher");
            var work = new FoundationPublishWork(publication.Key, publication.Id, incarnation);
            var ticket = new Message(ticketId, address, Kinds.Publish, KeepUntil: receiptUntil) { FoundationPublish = work }
                .AsIssued().AsKernelTicket();
            if (Messages.ContainsKey(ticket.InboxKey) || _seq == long.MaxValue)
                throw new StoreException(Failure.Definite, "The publication ticket cannot be inserted.");
            publication = publication with { FoundationPublishTicket = ticket.InboxKey };
            RecordFoundationPublication(publication);
            Messages.Add(ticket.InboxKey, new MessageRow(ticket, ++_seq, 0));
            return new(false, publication, FoundationPublicationStatus.Pending);
        }
    }

    private PublicationPage PreparePublicationPage(TurnCommit turn, IReadOnlyDictionary<int, long> epochs)
    {
        var failed = turn.FoundationCatchUpFailed;
        var key = failed?.Key ?? turn.FoundationCatchUpStep;
        if (key is not { } exact || Messages.GetValueOrDefault(exact) is not
                { Done: false, Message: { Kind: Kinds.Publish, FoundationPublish: { } work } source } stored ||
            !source.HasIssuedTicket || source.Target.Id != turn.Instance || turn.FoundationPublicationSequence != stored.Seq ||
            !epochs.TryGetValue(source.Target.Partition, out var epoch) || turn.Row is not null || turn.Expected is not null ||
            turn.Delete || turn.Journal is not null || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 ||
            turn.Unsubscribed.Count != 0 || turn.Unsubscribe || turn.Consumed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn.FoundationReleasedHolds is { Count: > 0 } || turn.FoundationParkClear is not null ||
            turn.FoundationRecoveryPage is not null || turn.FoundationRetry is not null ||
            turn.FoundationRecoveryTimers is not null || turn.FoundationRecoveryJobs is not null ||
            turn.FoundationRecoverySubscriptions is not null)
            throw InvalidPublicationPage();

        if (failed is not null)
        {
            if (failed.Limit <= 0 || stored.Attempts != failed.Attempts || turn.FoundationCatchUpStep is not null ||
                turn.FoundationCatchUpBefore is not null || turn.FoundationCatchUpExpected is not null ||
                (failed.Attempts < failed.Limit ? turn.Done.Count != 0 || stored.Trial != epoch :
                    turn.Done is not [var terminal] || terminal != exact))
                throw InvalidPublicationPage();
            return new(stored, [], _eventSerial, Failed: failed);
        }

        if (turn.Done is not [var done] || done != exact || stored.Trial != epoch ||
            turn.FoundationCatchUpBefore is null || turn.FoundationCatchUpExpected != stored.Attempts ||
            _foundationEvents.GetValueOrDefault((work.Key, work.Id)) is not { FoundationTenant: { } tenant } publication ||
            publication.FoundationIncarnation != work.Incarnation || publication.FoundationPublishTicket != exact)
            throw InvalidPublicationPage();

        var writes = new List<EventWrite>();
        var keys = new HashSet<InboxKey>();
        var bytes = 0L;
        var serial = _eventSerial;
        var matched = 0;
        var after = work.After;
        var cut = false;
        var match = FoundationEventIdentity.Match(publication, work.Incarnation);
        foreach (var wait in FoundationWaits(tenant, publication.Key, work.After).Take(ParkPageLimit))
        {
            var deliveryKey = new InboxKey(FoundationEventIdentity.DeliveryId(publication, wait, work.Incarnation), InboxKey.EventDeliveryScope, match);
            if (!Messages.ContainsKey(deliveryKey) && !keys.Contains(deliveryKey))
            {
                var delivery = MatchRoute(publication, wait).Delivery(partitions, null, deliveryKey);
                ValidateEventIdentity(delivery);
                var candidateSerial = serial;
                var write = PrepareEventWrite(delivery, ref candidateSerial);
                var size = PublicationWriteBytes(write);
                if (writes.Count != 0 && bytes + size > ParkRecoveryCredit.LimitBytes)
                {
                    cut = true;
                    break;
                }
                serial = candidateSerial;
                keys.Add(deliveryKey);
                writes.Add(write);
                bytes += size;
            }
            matched++;
            after = new(wait.Instance, wait.Wait);
        }

        Message? next = null;
        if (cut || matched == ParkPageLimit)
            next = (source with { FoundationPublish = work with { After = after } }).AsKernelTicket();
        _ = checked(_seq + writes.Count + (serial - _eventSerial) + (next is null ? 0 : 1));
        return new(stored, writes, serial, next);
    }

    private static StoreException InvalidPublicationPage() =>
        new(Failure.Definite, "A publication step requires its exact issued ticket, sequence and partition fence.");

    private static long PublicationWriteBytes(EventWrite write)
    {
        var bytes = MessageBytes(write.Delivery);
        if (write.Creator is { } creator && write.Birth is { } birth)
            bytes += MessageBytes(creator) + 64L + TextBytes(birth.Tenant) + TextBytes(birth.Reference) +
                TextBytes(birth.Address.Id) + TextBytes(birth.Creator.Id) + TextBytes(birth.Creator.Match) + TextBytes(birth.Fingerprint);
        return bytes;

        static long MessageBytes(Message message)
        {
            var bytes = CatchUpDeliveryBytes(message) + TextBytes(message.Sender) + TextBytes(message.ReplyTo) +
                TextBytes(message.StorageId) + TextBytes(message.EventStartAmbientKey);
            if (message.Start is { } start)
                bytes += 64L + TextBytes(start.Model) + TextBytes(start.Definition) + TextBytes(start.Parent) +
                    TextBytes(start.Creation?.Intent) + (message.StoreStartInputOnce ? 0 : TextBytes(start.Input));
            return bytes;
        }

        static int TextBytes(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);
    }

    private void ApplyPublicationPage(PublicationPage page)
    {
        var key = page.Source.Message.InboxKey;
        ClearParkCatchUpTrial(key, page.Source);
        if (page.Failed is { } failed)
        {
            if (failed.Attempts < failed.Limit) page.Source.Attempts++;
            else if (page.Source.Message.FoundationPublish is { } work &&
                _foundationEvents.GetValueOrDefault((work.Key, work.Id)) is { } publication &&
                publication.FoundationIncarnation == work.Incarnation && publication.FoundationPublishTicket == key)
                RecordFoundationPublication(publication with { FoundationPublishFailed = true });
            return;
        }
        foreach (var write in page.Writes) ApplyEventWrite(write);
        _eventSerial = page.Serial;
        if (page.Next is { } next) Messages[key] = new(next, ++_seq, 0);
    }
}
