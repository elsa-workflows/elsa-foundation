using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static readonly string UnkeptRootSelf = $"""
        "InboxScope" = {RootSelfScope} AND "EventMatch" IS NULL AND "FoundationKeptBy" IS NULL
        """;

    private static string TerminalRootSelfIndex(KernelDb db) =>
        $"""("Partition", "Target", "Id" COLLATE {TerminalRootSelfCollation(db)}) WHERE {UnkeptRootSelf}""";

    private static string TerminalRootSelfCollation(KernelDb db) => db.Database.IsSqlite() ? "BINARY" : "\"C\"";

    private sealed record TerminalSelfRow(string Id, string Kind, long Due, string Header);

    // End only this creation's private sends. A Park's kept dispositions and every other scope still belong to their writer.
    // Read small pages of the signed headers, without transferring any private content, in the locked terminal checkpoint.
    private static async Task<bool> ClearTerminalRootSelfAsync(KernelDb db, CreationAddress actor, int partition, long revision,
        FoundationSelfIssuer? issuer, CancellationToken token)
    {
        if (!db.UsesRootPrivateParkInputs) return true;
        if (actor.Creation != 1) return false;
        const int pageSize = 64;
        var sqlite = db.UsesSqliteRootRuntime;
        var origin = CreationProtocol.Origin(actor.Id, actor.Creation);
        var rowLock = sqlite ? "" : "FOR UPDATE";
        var collation = TerminalRootSelfCollation(db);
        var header = sqlite
            ? """substr(cut, 1, instr(cut || char(10), char(10)) - 1)"""
            : """split_part(cut, chr(10), 1)""";
        var sql = $$"""
            WITH candidates AS MATERIALIZED (
                SELECT "Id", "Kind", "Due", substr("Payload", 1, {{RootSelfHeaderBytes + 1}}) AS cut
                FROM "Messages" WHERE "Target" = {0} AND "Partition" = {1} AND {{UnkeptRootSelf}} AND "Sender" = {0}
                    AND "Start" IS NULL AND "KeepUntil" IS NULL AND "ReplyTo" IS NULL AND "JobGeneration" = 0
                    AND "Kind" IN ({2}, {3}) AND "Payload" IS NOT NULL
                    AND "Id" COLLATE {{collation}} > {4} AND "Id" COLLATE {{collation}} < {5}
                ORDER BY "Id" COLLATE {{collation}} LIMIT {{pageSize}} {{rowLock}}
            )
            SELECT "Id", "Kind", "Due", {{header}} AS "Header" FROM candidates ORDER BY "Id" COLLATE {{collation}}
            """;

        var cursor = origin + "#";
        while (true)
        {
            var page = await db.Database.SqlQuery<TerminalSelfRow>(FormattableStringFactory.Create(sql,
                actor.Id, partition, RootSelfWire, RootSelfTimerWire, cursor, origin + "$")).ToListAsync(token);
            if (page.Count == 0) return true;
            if (issuer is not { DistinguishesTimers: true }) return false;
            var exact = new List<string>(page.Count);
            foreach (var candidate in page)
            {
                try
                {
                    var body = ReadRootSelfHeader(candidate.Header);
                    if (body.Revision > revision) continue;
                    AuthenticateRootSelfId(issuer, actor.Id, candidate.Id, candidate.Kind, candidate.Due, body);
                    exact.Add(candidate.Id);
                }
                catch (Exception error) when (error is NotSupportedException or FormatException or System.Text.Json.JsonException)
                {
                    // An unrecognized row is not this writer's to delete. Retirement will still check the remaining residue.
                }
            }

            if (exact.Count > 0)
            {
                var count = sqlite
                    ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM "Messages" WHERE "Target" = {actor.Id} AND "Partition" = {partition}
                            AND "InboxScope" = {RootSelfScope} AND "EventMatch" IS NULL AND "FoundationKeptBy" IS NULL
                            AND "Id" IN (SELECT value FROM json_each({Json.Write(exact)}))
                        """, token)
                    : await db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM "Messages" WHERE "Target" = {actor.Id} AND "Partition" = {partition}
                            AND "InboxScope" = {RootSelfScope} AND "EventMatch" IS NULL AND "FoundationKeptBy" IS NULL
                            AND "Id" = ANY({exact.ToArray()})
                        """, token);
                if (count != exact.Count) return false;
            }
            if (page.Count < pageSize) return true;
            cursor = page[^1].Id;
        }
    }
}
