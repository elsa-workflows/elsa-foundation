using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The caller still owns the transaction and has checked all its fences. A count mismatch
    // rolls back the whole clear, including any matching tickets and the fence write.
    private async Task<bool> ClearJobTrialsAsync(KernelDb db, CommitBatch batch, CancellationToken cancellationToken)
    {
        var physicalKeys = batch.ClearJobTrials.Select(key => MailKey(key));
        var keys = Engine is SqlServerEngine or MySqlEngine
            ? JsonSerializer.Serialize(physicalKeys.Select(key => new { Wire = Convert.ToHexString(key.IssuedWire), key.InboxScope }))
            : JsonSerializer.Serialize(physicalKeys.Select(key => new { key.Id, key.InboxScope }));
        var fences = JsonSerializer.Serialize(batch.Fenced.Select(fence => new { fence.Partition, fence.Epoch }));
        const string update = """
            UPDATE "Messages" SET "Trial" = 0
            WHERE "Kind" = {0} AND NOT "Done" AND "Trial" > 0 AND "EventMatch" IS NULL
            """;
        const string postgresSql = update + """
             AND ("Id", "InboxScope") IN (SELECT * FROM jsonb_to_recordset(CAST({1} AS jsonb)) AS k("Id" text, "InboxScope" bigint))
             AND ("Partition", "Trial") IN (SELECT * FROM jsonb_to_recordset(CAST({2} AS jsonb)) AS f("Partition" integer, "Epoch" bigint))
            """;
        const string sqliteSql = update + """
             AND ("Id", "InboxScope") IN (SELECT value ->> '$.Id', value ->> '$.InboxScope' FROM json_each({1}))
             AND ("Partition", "Trial") IN (SELECT value ->> '$.Partition', value ->> '$.Epoch' FROM json_each({2}))
            """;
        // These engines store issued ids in a binary column with empty Id. Decode to unbounded
        // binary so an oversized input can never be truncated into a different ticket.
        const string sqlServerSql = """
            UPDATE m SET Trial = 0 FROM Messages m
            JOIN OPENJSON({1}) WITH (Wire varchar(max), InboxScope bigint) k
              ON m.Id = '' AND m.InboxScope = k.InboxScope AND m.IssuedWire = CONVERT(varbinary(max), k.Wire, 2)
                 AND DATALENGTH(m.IssuedWire) = DATALENGTH(k.Wire) / 2
            JOIN OPENJSON({2}) WITH ([Partition] int, Epoch bigint) f ON m.[Partition] = f.[Partition] AND m.Trial = f.Epoch
            WHERE m.Trial > 0 AND m.Done = 0 AND m.EventMatch IS NULL
              AND m.Kind COLLATE Latin1_General_100_BIN2 = {0} AND DATALENGTH(m.Kind) = DATALENGTH({0});
            SELECT @@ROWCOUNT AS [Value]
            """;
        const string mySql = """
            UPDATE Messages m
            JOIN JSON_TABLE({1}, '$[*]' COLUMNS (Wire LONGTEXT PATH '$.Wire', InboxScope BIGINT PATH '$.InboxScope')) k
              ON m.Id = '' AND m.InboxScope = k.InboxScope AND m.IssuedWire = UNHEX(k.Wire)
            JOIN JSON_TABLE({2}, '$[*]' COLUMNS ("Partition" INT PATH '$.Partition', Epoch BIGINT PATH '$.Epoch')) f
              ON m."Partition" = f."Partition" AND m.Trial = f.Epoch
            SET m.Trial = 0
            WHERE m.Trial > 0 AND m.Done = 0 AND m.EventMatch IS NULL AND CAST(m.Kind AS BINARY) = CAST({0} AS BINARY)
            """;
        // Owned SQL sets NOCOUNT ON, so SqlClient's affected-row count cannot qualify this update.
        if (Engine is SqlServerEngine)
        {
            var counts = await db.Database.SqlQueryRaw<int>(sqlServerSql, Kinds.Job, keys, fences).ToListAsync(cancellationToken);
            return counts.Single() == batch.ClearJobTrials.Count;
        }
        var sql = Engine is MySqlEngine ? Q(mySql) : postgres ? postgresSql : sqliteSql;
        var count = await db.Database.ExecuteSqlRawAsync(sql, [Kinds.Job, keys, fences], cancellationToken);
        return count == batch.ClearJobTrials.Count;
    }
}
