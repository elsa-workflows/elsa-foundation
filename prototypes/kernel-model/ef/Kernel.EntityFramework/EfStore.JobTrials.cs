using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The caller still owns the transaction and has checked all its fences. A count mismatch
    // rolls back the whole clear, including any matching tickets and the fence write.
    private async Task<bool> ClearJobTrialsAsync(KernelDb db, CommitBatch batch, CancellationToken cancellationToken)
    {
        var keys = JsonSerializer.Serialize(batch.ClearJobTrials.Select(key =>
        {
            var physical = MailKey(key);
            return new { physical.Id, physical.InboxScope };
        }));
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
        var count = await db.Database.ExecuteSqlRawAsync(postgres ? postgresSql : sqliteSql,
            [Kinds.Job, keys, fences], cancellationToken);
        return count == batch.ClearJobTrials.Count;
    }
}
