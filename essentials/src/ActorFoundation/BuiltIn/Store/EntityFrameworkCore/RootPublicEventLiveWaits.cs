using System.Runtime.CompilerServices;
using Kernel.Durable;
using Microsoft.EntityFrameworkCore;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal static class RootPublicEventLiveWaits
{
    internal const string Table = "FoundationPublicEventLiveWaits";
    internal const string CatchUpIndex = "IX_FoundationPublicEventLiveWaits_CatchUp";
    internal const string CatchUpColumns = """("Tenant","Key","Instance","Wait") WHERE "CatchAfter" < "CatchThrough" """;
    internal const string Receipts = "FoundationPublicEventLiveReceipts";

    internal static string Columns(bool sqlite)
    {
        var text = TextType(sqlite);
        var number = sqlite ? "INTEGER" : "bigint";
        return $"""
            "Tenant" {text} NOT NULL, "Key" {text} NOT NULL, "Instance" {text} NOT NULL, "Wait" {text} NOT NULL,
            "Registration" {text} NOT NULL,
            "RetainFrom" {number} NOT NULL, "CatchAfter" {number} NOT NULL,
            "CatchThrough" {number} NOT NULL CHECK ("CatchAfter" <= "CatchThrough"),
            PRIMARY KEY ("Key", "Instance", "Wait"),
            FOREIGN KEY ("Key", "Instance", "Wait") REFERENCES "Subscriptions" ("Key", "Instance", "Wait") ON DELETE CASCADE
            """;
    }

    internal static string ReceiptColumns(bool sqlite)
    {
        var text = TextType(sqlite);
        return $"""
            "Occurrence" {text} NOT NULL REFERENCES "FoundationPublicEventPublications" ("Occurrence") ON DELETE CASCADE,
            "Delivery" {text} NOT NULL,
            PRIMARY KEY ("Occurrence", "Delivery")
            """;
    }

    private static string TextType(bool sqlite) => sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;

    internal sealed record Candidate(bool SourceReady, string? Tenant, string? Key, string? Actor, string? Wait, string? Registration);
    private sealed record NewWait(bool SourceReady, int? Index);
    private sealed record Position(bool SourceReady, string? Reference, int? Partition, long? After, long? Through, long? RetainFrom);
    private sealed record Publication(bool SourceReady, long? PageEnd, long? Ordinal, string? Occurrence, long? KeepUntil, string? EventId, string? Payload);

    // Ordinary waits use insert-if-absent. A second Subscribe cannot upgrade a wait that was already present.
    internal static async Task<SubscriptionRow[]> NewAsync(KernelDb db, SubscriptionRow[] proposed,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var packed = Json.Write(proposed.Select((wait, index) => new { wait.Key, wait.Instance, wait.Wait, index }));
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), proposed AS (/*waits*/),
            absent AS (
                SELECT w."Index" FROM proposed w WHERE (SELECT "SourceReady" FROM source)
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s
                        WHERE s."Key" = w."Key" AND s."Instance" = w."Instance" AND s."Wait" = w."Wait")
            )
            SELECT source."SourceReady", absent."Index" FROM source LEFT JOIN absent ON true
            """;
        var rows = await db.Database.SqlQuery<NewWait>(BindProposed(query, packed, bind)).ToListAsync(token);
        if (!rows[0].SourceReady) throw SourceChanged();
        return rows[0].Index is null ? [] : rows.Select(row => proposed[row.Index!.Value]).ToArray();
    }

    internal static async Task CaptureAsync(KernelDb db, IReadOnlyList<SubscriptionRow> waits, long now,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        foreach (var group in waits.GroupBy(w => w.FoundationTenant!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var retainedFrom = await RootPublicEventPublications.RetentionThresholdAsync(db, group.Key, now, bind, token);
            // Record the key's high-water ordinal by index. The owner applies eligibility in its later bounded catch-up steps.
            var packed = Json.Write(group.Select(w => new { w.Key, w.Instance, w.Wait, registration = Guid.NewGuid().ToString("N") }));
            FormattableString query = $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/), proposed AS (/*waits*/)
                INSERT INTO "FoundationPublicEventLiveWaits"
                    ("Tenant", "Key", "Instance", "Wait", "Registration", "RetainFrom", "CatchAfter", "CatchThrough")
                SELECT {group.Key}, s."Key", s."Instance", s."Wait", w."Registration", {retainedFrom}, 0,
                    coalesce((SELECT p."Ordinal" FROM "FoundationPublicEventPublications" p
                        WHERE p."Tenant" = {group.Key} AND p."Key" = s."Key"
                        ORDER BY p."Ordinal" DESC LIMIT 1), 0)
                FROM source, proposed w JOIN "Subscriptions" s
                    ON s."Key" = w."Key" AND s."Instance" = w."Instance" AND s."Wait" = w."Wait"
                JOIN "Instances" i ON i."Id" = s."Instance" AND i."Partition" = s."Partition"
                WHERE "SourceReady" AND s."Start" IS NULL AND i."FoundationTenant" = {group.Key} AND i."FoundationReference" IS NOT NULL
                    AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND i."FoundationParkAt" IS NULL
                """;
            if (await db.Database.ExecuteSqlInterpolatedAsync(BindProposed(query, packed, bind), token) != group.Count())
                throw SourceChanged();
        }
    }

    private static FormattableString BindProposed(FormattableString query, string packed,
        Func<FormattableString, FormattableString>? bind)
    {
        // JSON reduces the proposed waits to one parameter for either engine. The shape has fields for either step.
        var parameter = "{" + query.ArgumentCount + "}";
        var unpack = bind is null
            ? $"""
                SELECT value->>'$.Key' AS "Key", value->>'$.Instance' AS "Instance", value->>'$.Wait' AS "Wait",
                    value->>'$.index' AS "Index", value->>'$.registration' AS "Registration" FROM json_each({parameter})
                """
            : $"""
                SELECT "Key", "Instance", "Wait", "index" AS "Index", "registration" AS "Registration"
                FROM jsonb_to_recordset({parameter}::jsonb) AS p("Key" text, "Instance" text, "Wait" text, "index" integer, "registration" text)
                """;
        return RootPublicEventStarts.Bind(FormattableStringFactory.Create(query.Format.Replace("/*waits*/", unpack, StringComparison.Ordinal),
            [.. query.GetArguments(), packed]), bind);
    }

    internal static async Task<Candidate?> FindAsync(KernelDb db, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var candidate = await db.Database.SqlQuery<Candidate>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), pending AS (
                SELECT "Tenant", "Key", "Instance" AS "Actor", "Wait", "Registration"
                FROM "FoundationPublicEventLiveWaits" WHERE "CatchAfter" < "CatchThrough"
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Tenant", "Key", "Instance", "Wait" LIMIT 1
            )
            SELECT source."SourceReady", pending.* FROM source LEFT JOIN pending ON true
            """, bind)).SingleAsync(token);
        if (!candidate.SourceReady) throw SourceChanged();
        return candidate.Registration is null ? null : candidate;
    }

    internal static async Task<bool> CatchUpAsync(KernelDb db, Candidate candidate, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        var postgres = bind is not null;
        var query = (FormattableString)$"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), position AS MATERIALIZED (
                SELECT b."Reference", i."Partition", r."CatchAfter" AS "After", r."CatchThrough" AS "Through", r."RetainFrom"
                FROM "FoundationPublicEventLiveWaits" r JOIN "Subscriptions" s
                    ON s."Key" = r."Key" AND s."Instance" = r."Instance" AND s."Wait" = r."Wait"
                JOIN "Instances" i ON i."Id" = s."Instance" AND i."Partition" = s."Partition"
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference" AND b."Creation" = 1
                WHERE r."Tenant" = {candidate.Tenant} AND r."Key" = {candidate.Key} AND r."Instance" = {candidate.Actor}
                    AND r."Wait" = {candidate.Wait} AND r."Registration" = {candidate.Registration}
                    AND r."CatchAfter" < r."CatchThrough" AND s."Start" IS NULL AND i."FoundationTenant" = r."Tenant"
                    AND i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired" AND i."FoundationParkAt" IS NULL
                    AND (SELECT "SourceReady" FROM source)
                /*lock-wait*/
            )
            SELECT source."SourceReady", position.* FROM source LEFT JOIN position ON true
            """;
        query = FormattableStringFactory.Create(
            query.Format.Replace("/*lock-wait*/", postgres ? "FOR SHARE OF i, s" : "", StringComparison.Ordinal), query.GetArguments());
        var position = await db.Database.SqlQuery<Position>(RootPublicEventStarts.Bind(query, bind)).SingleAsync(token);
        if (!position.SourceReady) throw SourceChanged();
        if (position.Reference is null)
        {
            // An inactive wait has no coverage. Drop the cursor if a lifecycle operation left its base row behind.
            return await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                DELETE FROM "FoundationPublicEventLiveWaits"
                WHERE "Key" = {candidate.Key} AND "Instance" = {candidate.Actor} AND "Wait" = {candidate.Wait}
                    AND "Registration" = {candidate.Registration} AND (SELECT "SourceReady" FROM source)
                """, bind), token) == 1;
        }

        var publication = await db.Database.SqlQuery<Publication>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), page AS MATERIALIZED (
                SELECT "Ordinal", "KeepUntil", "CleanupAt"
                FROM "FoundationPublicEventPublications"
                WHERE "Tenant" = {candidate.Tenant} AND "Key" = {candidate.Key}
                    AND "Ordinal" > {position.After} AND "Ordinal" <= {position.Through}
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Ordinal" LIMIT {InMemoryStore.ParkPageLimit}
            ), matching AS (
                SELECT "Ordinal" FROM page
                WHERE "KeepUntil" >= {position.RetainFrom} AND "CleanupAt" = "KeepUntil"
                ORDER BY "Ordinal" LIMIT 1
            )
            SELECT source."SourceReady", (SELECT max("Ordinal") FROM page) AS "PageEnd",
                p."Ordinal", p."Occurrence", p."KeepUntil", p."EventId", p."Payload"
            FROM source LEFT JOIN matching ON true
            LEFT JOIN "FoundationPublicEventPublications" p ON p."Ordinal" = matching."Ordinal"
            """, bind)).SingleAsync(token);
        if (!publication.SourceReady) throw SourceChanged();
        if (publication.Occurrence is { } occurrence)
        {
            var id = DurableKeyJson.Read<string>(publication.EventId!);
            var delivery = PublicEventOccurrence(position.Reference, occurrence, id, candidate.Wait!);
            if (await RememberAsync(db, occurrence, delivery, bind, token))
            {
                var payload = publication.Payload is null ? null : DurableKeyJson.Read<string>(publication.Payload);
                var (header, body, kind) = EncodeDirect(id, 1, Kinds.Event, payload, null, held: false,
                    new(candidate.Key!, candidate.Wait!), eventOccurrence: occurrence);
                if (!FitsEnvelope(header, body)) throw SourceChanged();
                await RootEventAdmission.EnqueueRetainedLiveAsync(db, new MessageEntity
                {
                    Id = delivery, InboxScope = InboxKey.FoundationOccurrenceScope, Target = candidate.Actor!, Partition = position.Partition!.Value,
                    Kind = kind, Start = header, Payload = body
                }, publication.KeepUntil!.Value, bind, token);
            }
        }
        if (await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            UPDATE "FoundationPublicEventLiveWaits" SET "CatchAfter" = {publication.Ordinal ?? publication.PageEnd ?? position.Through}
            WHERE "Key" = {candidate.Key} AND "Instance" = {candidate.Actor} AND "Wait" = {candidate.Wait}
                AND "Registration" = {candidate.Registration} AND "CatchAfter" = {position.After}
                AND (SELECT "SourceReady" FROM source)
            """, bind), token) != 1) throw SourceChanged();
        return true;
    }

    internal static async Task RememberPublishedAsync(KernelDb db, string occurrence, string[] deliveryIds,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (deliveryIds.Length == 0) return;
        var query = bind is null
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationPublicEventLiveReceipts" ("Occurrence", "Delivery")
                SELECT {occurrence}, deliveries.value FROM json_each({Json.Write(deliveryIds)}) deliveries CROSS JOIN source WHERE "SourceReady"
                """
            : $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationPublicEventLiveReceipts" ("Occurrence", "Delivery")
                SELECT {occurrence}, deliveries.value FROM unnest({deliveryIds}::text[]) AS deliveries(value) CROSS JOIN source WHERE "SourceReady"
                """;
        if (await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind(query, bind), token) != deliveryIds.Length)
            throw SourceChanged();
    }

    private static async Task<bool> RememberAsync(KernelDb db, string occurrence, string delivery,
        Func<FormattableString, FormattableString>? bind, CancellationToken token) =>
        await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationPublicEventLiveReceipts" ("Occurrence", "Delivery")
            SELECT {occurrence}, {delivery} FROM source WHERE "SourceReady" ON CONFLICT DO NOTHING
            """, bind), token) == 1;

    private static StoreFormatException SourceChanged() => new("The pinned public event live wait source changed.");
}
