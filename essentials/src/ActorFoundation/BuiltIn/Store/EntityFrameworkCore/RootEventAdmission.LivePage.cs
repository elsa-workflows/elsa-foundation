using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal static partial class RootEventAdmission
{
    internal sealed record LiveMatchPage(SubscriptionPosition? Through, bool Complete);
    private sealed record LiveMatch(bool SourceReady, string? Instance, string? Wait, string? Reference, int? Partition);

    internal static async Task<LiveMatchPage> MatchLivePageAsync(KernelDb db, string tenant, string key, string eventId,
        string occurrence, (string? Text, bool Escaped) payload, long keepUntil, SubscriptionPosition? after,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null)
            throw new StoreFormatException("A publication page requires an open owned transaction.");
        var postgres = bind is not null;
        FormattableString BindLive(FormattableString sql) => BindEventSource(sql, bind);
        var waitPage = await ReadLiveMatchPageAsync(db, tenant, key, after, BindLive, postgres, token);
        if (waitPage is [{ Instance: null }]) return new(after, Complete: true);

        var pending = new List<MessageEntity>(waitPage.Count);
        var through = after;
        var bytes = 0L;
        var bodyBytes = payload.Text is { } body ? Encoding.UTF8.GetByteCount(body) : 0;
        foreach (var wait in waitPage)
        {
            var (header, stored, kind) = EncodeDirect(eventId, 1, Kinds.Event, payload, null, held: false,
                new(key, wait.Wait!), eventOccurrence: occurrence);
            var wireBytes = (long)bodyBytes + Encoding.UTF8.GetByteCount(header);
            if (!FoundationMetadataLimits.FitsLiveWaitName(wait.Wait!) || wireBytes > EfStore.RootContentBytes)
                throw new StoreFormatException("A publication has a wait outside the committed Event envelope.");
            var id = PublicEventOccurrence(wait.Reference!, occurrence, eventId, wait.Wait!);
            var size = wireBytes + Encoding.UTF8.GetByteCount(id) + Encoding.UTF8.GetByteCount(wait.Instance!);
            if (pending.Count > 0 && bytes + size > ParkRecoveryCredit.LimitBytes) break;
            pending.Add(new MessageEntity
            {
                Id = id, InboxScope = InboxKey.FoundationOccurrenceScope, Target = wait.Instance!, Partition = wait.Partition!.Value,
                Kind = kind, Start = header, Payload = stored
            });
            bytes += size;
            through = new(wait.Instance!, wait.Wait!);
        }

        var examined = pending.Count;
        var deliveries = await RootPublicEventLiveWaits.ClaimPublishedAsync(db, occurrence,
            pending.Select(message => message.Id).ToArray(), bind, token);
        pending.RemoveAll(message => !deliveries.Contains(message.Id));
        if (pending.Count != 0) await EnqueueAsync(db, pending, keepUntil, BindLive, postgres, token);
        return new(through, examined == waitPage.Count && waitPage.Count < InMemoryStore.ParkPageLimit);
    }

    private static async Task<List<LiveMatch>> ReadLiveMatchPageAsync(KernelDb db, string tenant, string key,
        SubscriptionPosition? after, Func<FormattableString, FormattableString> bind, bool postgres, CancellationToken token)
    {
        // The subscription primary key is (Key, Instance, Wait). Use the same key order for the seek and the cursor.
        // PostgreSQL takes the same instance and subscription locks as its atomic live Event admission.
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*event-source*/), matches AS MATERIALIZED (
                SELECT s."Instance", s."Wait", b."Reference", s."Partition"
                FROM "Subscriptions" s
                JOIN "Instances" i ON i."Id" = s."Instance" AND i."Partition" = s."Partition"
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference" AND b."Creation" = 1
                WHERE s."Key" = {key} AND s."Start" IS NULL AND i."FoundationTenant" = {tenant}
                    AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND i."FoundationParkAt" IS NULL
                    AND (SELECT "SourceReady" FROM source) /*event-cursor*/
                ORDER BY s."Instance", s."Wait" LIMIT {InMemoryStore.ParkPageLimit}
                /*lock-waits*/
            )
            SELECT source."SourceReady", matches.* FROM source LEFT JOIN matches ON true
            ORDER BY matches."Instance", matches."Wait"
            """;
        var format = query.Format.Replace("/*lock-waits*/", postgres ? "FOR SHARE OF i, s" : "", StringComparison.Ordinal);
        if (after is { } cursor)
        {
            format = format.Replace("/*event-cursor*/", """AND (s."Instance", s."Wait") > ({4}, {5})""", StringComparison.Ordinal);
            query = FormattableStringFactory.Create(format, [.. query.GetArguments(), cursor.Instance, cursor.Wait]);
        }
        else query = FormattableStringFactory.Create(format, query.GetArguments());
        var matches = await db.Database.SqlQuery<LiveMatch>(bind(query)).ToListAsync(token);
        RequireSource(matches[0].SourceReady);
        return matches;
    }
}
