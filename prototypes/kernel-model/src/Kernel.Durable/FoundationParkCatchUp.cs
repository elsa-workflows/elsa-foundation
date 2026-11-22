using System.Text;

namespace Kernel.Durable;

internal readonly record struct CatchUpPosition(string Key, string Wait, string Event) : IComparable<CatchUpPosition>
{
    public int CompareTo(CatchUpPosition other)
    {
        var key = StringComparer.Ordinal.Compare(Key, other.Key);
        if (key != 0) return key;
        var wait = StringComparer.Ordinal.Compare(Wait, other.Wait);
        return wait != 0 ? wait : StringComparer.Ordinal.Compare(Event, other.Event);
    }
}

internal sealed record ParkCatchUpTicket(string Chain, long Page, long Entitlement, CatchUpPosition? After = null);
internal sealed record ParkCatchUpFailure(InboxKey Key, int Attempts, int Limit);

public sealed partial class InMemoryStore
{
    private sealed record CatchUpCoverage(SubscriptionRow Wait, ParkCatchUpTicket Ticket);
    private sealed record CatchUpStep(ParkCatchUpTicket Ticket, IReadOnlyList<EventDelivery> Deliveries, Message? Next);
    private readonly Dictionary<Guid, CatchUpCoverage> _foundationCatchUpCoverage = [];
    private readonly Dictionary<string, long> _foundationSweepHorizon = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, SortedList<string, EventRow>>> _foundationPublicationsByTenant = new(StringComparer.Ordinal);

    private static Message CatchUpMessage(string id, string actor, long creation, ParkCatchUpTicket ticket) =>
        new Message(id, new(actor), Kinds.Meet, Sender: actor, TargetCreation: creation, SenderCreation: creation)
            { ParkCatchUp = ticket }.AsIssued().AsKernelTicket();

    private Message? PrepareParkCatchUpOpen(TurnCommit turn, long now)
    {
        if (turn.FoundationRecoverySubscriptions is null ||
            turn.Subscribed.FirstOrDefault(subscription => subscription.FoundationKeepsEvents) is not { } keeping)
            return null;
        var row = turn.Row!;
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        var first = InboxKey.Issued(FormattableString.Invariant($"{origin}#{row.Revision}.0001"));
        var horizon = _foundationSweepHorizon.GetValueOrDefault(keeping.FoundationTenant!, long.MinValue);
        var ticket = new ParkCatchUpTicket(FoundationEventIdentity.CatchUpId(first), 0, Math.Max(now, checked(horizon + 1)));
        return CatchUpMessage(first.Id, row.Id, row.Creation, ticket);
    }

    private void RestoreFoundationSubscriptions(TurnCommit turn, Message? message, long now)
    {
        foreach (var subscription in turn.Subscribed)
        {
            var registered = subscription with { FoundationIncarnation = Guid.NewGuid(), FoundationRegisteredAt = now };
            Subscriptions.Add(registered);
            if (registered.FoundationKeepsEvents)
                _foundationCatchUpCoverage.Add(registered.FoundationIncarnation!.Value, new(registered, message!.ParkCatchUp!));
        }
        if (message is not null) Messages.Add(message.InboxKey, new(message, ++_seq, 0));
    }

    private CatchUpStep? PrepareParkCatchUp(TurnCommit turn, IReadOnlyDictionary<int, long> epochs, IReadOnlySet<Guid>? revoked)
    {
        if (turn.FoundationCatchUpFailed is { } failed)
        {
            ValidateParkCatchUpFailure(turn, failed, epochs);
            return null;
        }
        if (turn.FoundationCatchUpStep is not { } key)
        {
            if (turn.FoundationCatchUpBefore is not null || turn.FoundationCatchUpExpected is not null) throw InvalidCatchUpFailure();
            if (turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []).Any(key => Messages.GetValueOrDefault(key)?.Message.ParkCatchUp is not null))
                throw new StoreException(Failure.Definite, "A recovery catch-up ticket requires its own fenced step.");
            return null;
        }
        if (turn.Consumed is not [var input] || input != key || turn.Row is not null || turn.Delete || turn.Journal is not null ||
            turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Unsubscribe || turn.Done.Count != 0 ||
            turn.Held is { Count: > 0 } || turn.FoundationReleasedHolds is { Count: > 0 } ||
            turn.FoundationCatchUpExpected is not { } expected || turn.FoundationCatchUpBefore is null ||
            Messages.GetValueOrDefault(key) is not { Done: false, Message: { ParkCatchUp: { } ticket, Kind: Kinds.Meet } source } stored ||
            expected != stored.Attempts ||
            source.Target.Id != turn.Instance || !epochs.ContainsKey(source.Target.Partition))
            throw new StoreException(Failure.Definite, "A catch-up step must consume its exact ticket under its partition's fence.");

        var deliveries = new List<EventDelivery>();
        var bytes = 0L;
        var matched = 0;
        CatchUpPosition? last = null;
        var needsNext = false;
        foreach (var (publication, wait) in CatchUpMatches(ticket, source, revoked).Take(ParkPageLimit))
        {
            var delivery = MatchRoute(publication, wait).Delivery(partitions);
            var estimatedBytes = CatchUpDeliveryBytes(delivery.Message);
            if (matched != 0 && bytes + estimatedBytes > ParkRecoveryCredit.LimitBytes)
            {
                needsNext = true;
                break;
            }
            ValidateEventIdentity(delivery);
            if (!Messages.ContainsKey(delivery.Message.InboxKey)) deliveries.Add(delivery);
            matched++;
            bytes += estimatedBytes;
            last = new(wait.Key, wait.Wait, publication.Id);
        }
        if (!needsNext && matched < ParkPageLimit) return new(ticket, deliveries, null);
        var next = ticket with { Page = checked(ticket.Page + 1), After = last };
        var successor = CatchUpMessage(FoundationEventIdentity.CatchUpSuccessor(next.Chain, next.Page), turn.Instance,
            source.TargetCreation!.Value, next);
        return new(ticket, deliveries, successor);
    }

    private void ValidateParkCatchUpFailure(TurnCommit turn, ParkCatchUpFailure failed, IReadOnlyDictionary<int, long> epochs)
    {
        if (turn.FoundationCatchUpStep is not null || turn.FoundationCatchUpBefore is not null ||
            turn.FoundationCatchUpExpected is not null || failed.Limit <= 0 ||
            Messages.GetValueOrDefault(failed.Key) is not { Done: false, Message: { Kind: Kinds.Meet, ParkCatchUp: not null } envelope } stored ||
            stored.Attempts != failed.Attempts || envelope.Target.Id != turn.Instance || !epochs.ContainsKey(envelope.Target.Partition) ||
            turn.Delete || turn.Held is { Count: > 0 } || turn.FoundationReleasedHolds is { Count: > 0 } ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0)
            throw InvalidCatchUpFailure();

        if (failed.Attempts >= failed.Limit && Instances.GetValueOrDefault(turn.Instance) is
                { Status: InstanceStatus.Running, Retired: false } actor && OwnsParkCatchUp(actor, envelope))
        {
            if (turn.Row is not { Status: InstanceStatus.Faulted, Retired: false } next || next.Creation != actor.Creation ||
                !turn.Unsubscribe || turn.FoundationKept?.GetValueOrDefault(failed.Key) != ParkKeepReason.Consumed ||
                !turn.Done.Contains(failed.Key))
                throw InvalidCatchUpFailure();
        }
        else if (turn.Row is not null || turn.Unsubscribe || turn.Journal is not null || turn.Sent.Count != 0 ||
            turn.Done.Count != 0 || (failed.Attempts < failed.Limit ? turn.Consumed.Count != 0 :
                turn.Consumed is not [var consumed] || consumed != failed.Key))
            throw InvalidCatchUpFailure();
    }

    internal bool OwnsParkCatchUp(InstanceRow actor, Message source)
    {
        lock (_gate)
            return actor.Id == source.Target.Id && actor.Creation == source.TargetCreation &&
                source.ParkCatchUp is { } ticket && _foundationCatchUpCoverage.Values.Any(entry =>
                    entry.Ticket.Chain == ticket.Chain && entry.Wait.Instance == actor.Id && entry.Wait.Creation == actor.Creation);
    }

    private static StoreException InvalidCatchUpFailure() =>
        new(Failure.Definite, "A catch-up failure must match the pending ticket, its attempts and its owner's fence.");

    private void ApplyParkCatchUpFailure(TurnCommit turn)
    {
        if (turn.FoundationCatchUpFailed is not { } failed) return;
        var ticket = Messages[failed.Key];
        if (failed.Attempts < failed.Limit)
            ticket.Attempts++;
        else if (turn.Row is null)
            ApplyParkCatchUp(new(ticket.Message.ParkCatchUp!, [], null));
    }

    private IEnumerable<(EventRow Publication, SubscriptionRow Wait)> CatchUpMatches(ParkCatchUpTicket ticket, Message source, IReadOnlySet<Guid>? revoked)
    {
        var waits = _foundationCatchUpCoverage.Where(pair => revoked?.Contains(pair.Key) != true &&
                pair.Value.Ticket.Chain == ticket.Chain && pair.Value.Wait.Instance == source.Target.Id && pair.Value.Wait.Creation == source.TargetCreation)
            .Select(pair => pair.Value.Wait).OrderBy(wait => wait.Key, StringComparer.Ordinal).ThenBy(wait => wait.Wait, StringComparer.Ordinal);
        foreach (var wait in waits)
        {
            var position = new CatchUpPosition(wait.Key, wait.Wait, "");
            var relative = ticket.After is { } after ? position.CompareTo(after with { Event = "" }) : 1;
            if (relative < 0 || !_foundationPublicationsByTenant.TryGetValue(wait.FoundationTenant!, out var keys) ||
                !keys.TryGetValue(wait.Key, out var publications)) continue;
            var first = relative == 0 ? FirstAfter(publications.Keys, ticket.After!.Value.Event) : 0;
            for (var index = first; index < publications.Count; index++)
            {
                var publication = publications.Values[index];
                if (publication.KeepUntil >= ticket.Entitlement) yield return (publication, wait);
            }
        }
    }

    private static int FirstAfter(IList<string> ids, string after)
    {
        int first = 0, end = ids.Count;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            if (StringComparer.Ordinal.Compare(ids[middle], after) <= 0) first = middle + 1;
            else end = middle;
        }
        return first;
    }

    private void ValidateCatchUpWrites(CommitBatch batch, IEnumerable<Message> opens, IEnumerable<CatchUpStep> steps,
        IEnumerable<Message> expiries)
    {
        var writes = new HashSet<InboxKey>(batch.Seen.Concat(batch.Trial));
        writes.UnionWith(expiries.Select(expiry => expiry.InboxKey));
        foreach (var turn in batch.Turns)
        {
            writes.UnionWith(turn.Sent.Select(message => message.InboxKey));
            writes.UnionWith(turn.FoundationRecoveryTimers?.Rearms.Select(timer => timer.Envelope.InboxKey) ?? []);
            writes.UnionWith(turn.FoundationRecoveryJobs?.Writes.Select(job => job.InboxKey) ?? []);
        }
        foreach (var open in opens) Reserve(open);
        foreach (var step in steps)
        {
            if (step.Next is { } next) Reserve(next);
            foreach (var delivery in step.Deliveries) Reserve(delivery.Message);
        }

        void Reserve(Message message)
        {
            if (Messages.ContainsKey(message.InboxKey) || !writes.Add(message.InboxKey))
                throw new StoreException(Failure.Definite, "A recovery catch-up write collides with another inbox key.");
        }
    }

    private static long CatchUpDeliveryBytes(Message message) => 64L + Encoding.UTF8.GetByteCount(message.Id) +
        Encoding.UTF8.GetByteCount(message.Kind) + Encoding.UTF8.GetByteCount(message.Target.Id) +
        (message.EventMatch is { } match ? Encoding.UTF8.GetByteCount(match) : 0) +
        (message.Payload is { } payload ? Encoding.UTF8.GetByteCount(payload) : 0);

    private HashSet<Guid>? CatchUpRevocations(CommitBatch batch)
    {
        if (!batch.Turns.Any(turn => turn.FoundationCatchUpStep is not null)) return null;
        var deleted = new HashSet<Guid>();
        foreach (var turn in batch.Turns)
        {
            if (turn.Unsubscribe)
                foreach (var (id, coverage) in _foundationCatchUpCoverage)
                    if (coverage.Wait.Instance == turn.Instance) deleted.Add(id);
            foreach (var subscription in turn.Unsubscribed)
                if (RegisteredSubscription(subscription) is { FoundationIncarnation: { } id } found &&
                    found.Creation == subscription.Creation) deleted.Add(id);
        }
        return deleted;
    }

    private void ApplyParkCatchUp(CatchUpStep step)
    {
        foreach (var delivery in step.Deliveries) InsertEventDelivery(delivery);
        if (step.Next is { } next) Messages.Add(next.InboxKey, new(next, ++_seq, 0));
        var cursor = step.Next?.ParkCatchUp?.After;
        var passed = _foundationCatchUpCoverage.Where(pair => pair.Value.Ticket.Chain == step.Ticket.Chain &&
            (cursor is null || new CatchUpPosition(pair.Value.Wait.Key, pair.Value.Wait.Wait, "").CompareTo(cursor.Value with { Event = "" }) < 0))
            .Select(pair => pair.Key).ToArray();
        foreach (var incarnation in passed) _foundationCatchUpCoverage.Remove(incarnation);
    }

    private bool OwedToCatchUp(EventRow publication) => _foundationCatchUpCoverage.Values.Any(entry =>
        entry.Wait.FoundationTenant == publication.FoundationTenant && _foundationEventEquality.Equals(entry.Wait.Key, publication.Key) &&
        entry.Ticket.Entitlement <= publication.KeepUntil);

    private bool RemoveRegisteredSubscription(SubscriptionRow subscription)
    {
        RemoveCatchUpCoverage(subscription);
        return Subscriptions.Remove(subscription);
    }

    private void RemoveCatchUpCoverage(SubscriptionRow subscription)
    {
        if (subscription.FoundationIncarnation is { } incarnation) _foundationCatchUpCoverage.Remove(incarnation);
    }

    private void SweepFoundationPublication((string Key, string Id) key, EventRow publication)
    {
        _foundationEvents.Remove(key);
        UnindexFoundationPublication(publication);
        if (publication.FoundationTenant is { } tenant &&
            (!_foundationSweepHorizon.TryGetValue(tenant, out var previous) || publication.KeepUntil > previous))
            _foundationSweepHorizon[tenant] = publication.KeepUntil;
    }

    private void UnindexFoundationPublication(EventRow publication)
    {
        var tenant = publication.FoundationTenant!;
        var keys = _foundationPublicationsByTenant[tenant];
        var publications = keys[publication.Key];
        publications.Remove(publication.Id);
        if (publications.Count != 0) return;
        keys.Remove(publication.Key);
        if (keys.Count == 0) _foundationPublicationsByTenant.Remove(tenant);
    }

    // Shared by ordinary insertion and the simulation's legacy persisted row fixture.
    internal void RecordFoundationPublication(EventRow publication)
    {
        var key = (publication.Key, publication.Id);
        if (_foundationEvents.TryGetValue(key, out var previous)) UnindexFoundationPublication(previous);
        _foundationEvents[key] = publication;
        var tenant = publication.FoundationTenant!;
        if (!_foundationPublicationsByTenant.TryGetValue(tenant, out var keys))
            _foundationPublicationsByTenant.Add(tenant, keys = new(_foundationEventEquality));
        if (!keys.TryGetValue(publication.Key, out var publications))
            keys.Add(publication.Key, publications = new(StringComparer.Ordinal));
        publications.Add(publication.Id, publication);
    }
}
