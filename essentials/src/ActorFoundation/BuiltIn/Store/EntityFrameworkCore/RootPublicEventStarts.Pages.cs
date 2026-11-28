using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootPublicEventStarts
{
    internal sealed record MatchPage(string? Through, bool Complete);
    private sealed record MatchCandidate(bool SourceReady, string? Wait, string? Registration, long? TemplateBytes);

    internal static async Task<bool> HasMatchesAsync(KernelDb db, string tenant, string key,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        if (await RootPublicEventHost.ReadAsync(db, bind, token) is { ProtectsStarts: true }) return false;
        var result = await db.Database.SqlQuery<MatchCandidate>(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT source."SourceReady", found."Wait", CAST(NULL AS TEXT) AS "Registration", CAST(NULL AS BIGINT) AS "TemplateBytes"
            FROM source LEFT JOIN (
                SELECT "Wait" FROM "FoundationPublicEventStarts" WHERE "Tenant" = {tenant} AND "Key" = {key} LIMIT 1
            ) found ON source."SourceReady"
            """, bind)).SingleAsync(token);
        RequireSource(result.SourceReady);
        return result.Wait is not null;
    }

    internal static async Task<MatchPage> MatchPageAsync(KernelDb db, RootPublicEventDispatch.Publication publication,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var after = publication.Position.StartThrough;
        if (await RootPublicEventHost.ReadAsync(db, bind, token) is { ProtectsStarts: true }) return new(after, Complete: true);
        FormattableString select = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), candidates AS MATERIALIZED (
                SELECT "Wait", "Incarnation" AS "Registration", CAST(/*template-bytes*/ AS BIGINT) AS "TemplateBytes"
                FROM "FoundationPublicEventStarts"
                WHERE "Tenant" = {publication.Tenant} AND "Key" = {publication.Key} AND "Wait" > {after ?? ""}
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Wait" LIMIT {InMemoryStore.ParkPageLimit}
            )
            SELECT source."SourceReady", candidates.* FROM source LEFT JOIN candidates ON true ORDER BY candidates."Wait"
            """;
        var templateBytes = bind is null ? """length(CAST("Start" AS BLOB))""" : """octet_length("Start")""";
        select = FormattableStringFactory.Create(select.Format.Replace("/*template-bytes*/", templateBytes, StringComparison.Ordinal), select.GetArguments());
        var candidates = await db.Database.SqlQuery<MatchCandidate>(Bind(select, bind)).ToListAsync(token);
        RequireSource(candidates[0].SourceReady);
        if (candidates is [{ Wait: null }]) return new(after, Complete: true);

        var (text, escaped) = publication.Payload;
        var payloadBytes = text is null ? 0 : Encoding.UTF8.GetByteCount(text);
        var waits = new List<string>(candidates.Count);
        var registrations = new List<string>(candidates.Count);
        var bytes = 0L;
        foreach (var candidate in candidates)
        {
            // The capture stores the template and payload, and dispatch writes them again in the creator/Event pair.
            var size = 2 * (candidate.TemplateBytes!.Value + payloadBytes) + Encoding.UTF8.GetByteCount(candidate.Wait!) + 1024;
            if (waits.Count > 0 && bytes + size > ParkRecoveryCredit.LimitBytes) break;
            waits.Add(candidate.Wait!);
            registrations.Add(candidate.Registration!);
            bytes += size;
        }

        var packed = Json.Write(waits);
        FormattableString write = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), selected AS (/*selected-waits*/)
            INSERT INTO "FoundationPublicEventTargets"
                ("Tenant", "Publication", "Registration", "Key", "Wait", "EventId", "Start", "Payload", "PayloadEscaped")
            SELECT r."Tenant", {publication.Occurrence}, r."Incarnation", r."Key", r."Wait", {DurableKeyJson.Write(publication.EventId)},
                r."Start", CAST({text} AS TEXT), {escaped}
            FROM selected JOIN "FoundationPublicEventStarts" r
                ON r."Tenant" = {publication.Tenant} AND r."Key" = {publication.Key} AND r."Wait" = selected.value
            CROSS JOIN source WHERE "SourceReady"
            ON CONFLICT ("Tenant", "Publication", "Registration") DO NOTHING
            """;
        var parameter = "{" + write.ArgumentCount + "}";
        var selected = bind is null ? $"SELECT value FROM json_each({parameter})" :
            $"SELECT value FROM jsonb_array_elements_text({parameter}::jsonb)";
        write = FormattableStringFactory.Create(write.Format.Replace("/*selected-waits*/", selected, StringComparison.Ordinal),
            [.. write.GetArguments(), packed]);
        await db.Database.ExecuteSqlInterpolatedAsync(Bind(write, bind), token);
        await DispatchAsync(db, publication.Tenant, publication.Occurrence, publication.KeepUntil, bind, token,
            rememberDelivery: true, selectedRegistrations: registrations);
        return new(waits[^1], waits.Count == candidates.Count && candidates.Count < InMemoryStore.ParkPageLimit);
    }
}
