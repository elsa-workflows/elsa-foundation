namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static class RootEventWaits
    {
        internal static TurnCommit Lower(WorkerRoot root, TurnCommit submitted, TurnCommit journal)
        {
            var prefix = root.Tenant + "_";
            if (submitted.Subscribed.Concat(submitted.Unsubscribed).Any(wait =>
                wait.Creation != 1 || wait.Instance != root.Actor || wait.FoundationTenant != root.Tenant ||
                wait.Start is not null || wait.FoundationKeepsEvents || !wait.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                !FoundationMetadataLimits.FitsLiveWaitName(wait.Key[prefix.Length..]) || !FoundationMetadataLimits.FitsLiveWaitName(wait.Wait)))
                throw RootShape();

            // The bound generated-root address embeds its partition and cannot be reused. Pass only the physical
            // columns to the owned writer, which writes waits and their checkpoint under the same fence.
            return submitted.Unsubscribe
                ? journal with { Unsubscribe = true }
                : journal with
                {
                    Subscribed = LowerRows(submitted.Subscribed),
                    Unsubscribed = LowerRows(submitted.Unsubscribed)
                };
        }

        internal static void CollectPublications(WorkerRoot root, TurnCommit turn, IReadOnlyList<Message> tickets,
            FoundationSelfIssuer issuer, List<EventRow> publications)
        {
            if (turn.Row is not { Retired: false, Creation: 1 } row || turn.FoundationRootOperator is not null ||
                turn.OpensPark || tickets.Count is 0 or > FoundationMetadataLimits.MaxTurnSends) throw RootShape();
            var origin = CreationProtocol.Origin(root.Actor, row.Creation);
            var previousOrdinal = -1;
            foreach (var ticket in tickets)
            {
                if (ticket.FoundationPublish is not { Authored: { AcceptedAt: null, KeepFor: >= 0 } authored,
                        After: null, Incarnation: var incarnation } work || incarnation != Guid.Empty || !work.Owns(row, ticket) ||
                    authored.Tenant != root.Tenant || !work.Key.StartsWith(root.Tenant + "_", StringComparison.Ordinal) ||
                    ticket is not { Kind: Kinds.Publish, HasIssuedTicket: true, Payload: not null, Due: null, Start: null,
                        KeepUntil: null, ReplyTo: null } ||
                    ticket.InboxKey != InboxKey.Issued(ticket.Id) || FoundationSelfSend.AuthoredOrdinal(ticket) is not { } ordinal ||
                    ordinal <= previousOrdinal || work.Id != FoundationEventTicket.Issue(issuer.Handle, origin, row.Revision, ordinal) ||
                    work.Id != "~" + ticket.Id ||
                    !FoundationMetadataLimits.FitsLiveWaitName(work.Key[(root.Tenant.Length + 1)..]))
                    throw RootShape();
                var publication = FoundationEventTicket.Decode(ticket);
                if (publication.Key != work.Key || publication.Id != work.Id || publication.KeepUntil != authored.KeepFor ||
                    publication.FoundationTenant != root.Tenant ||
                    !FoundationMetadataLimits.FitsContent(publication.Payload, FoundationMetadataLimits.EventContentBytes))
                    throw RootShape();
                publications.Add(publication);
                previousOrdinal = ordinal;
            }
        }

        private static SubscriptionRow[] LowerRows(IReadOnlyList<SubscriptionRow> rows) =>
            rows.Select(wait => new SubscriptionRow(wait.Key, wait.Instance, wait.Wait)).ToArray();
    }
}
