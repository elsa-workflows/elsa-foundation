using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The caller still owns the transaction and has checked all its fences. A count mismatch
    // rolls back the whole clear, including any matching tickets and the fence write.
    private async Task<bool> ClearSqliteJobTrialsAsync(KernelDb db, CommitBatch batch, CancellationToken cancellationToken)
    {
        if (batch.ClearJobTrials.Any(key => key.Scope <= 0 || key.Match is not null)) return false;
        var keys = JsonSerializer.Serialize(batch.ClearJobTrials.Select(key =>
        {
            var physical = MailKey(key);
            return new { physical.Id, physical.InboxScope };
        }));
        var fences = JsonSerializer.Serialize(batch.Fenced.Select(fence => new { fence.Partition, fence.Epoch }));
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Trial" = 0
            WHERE "Kind" = {Kinds.Job} AND "Done" = 0 AND "Trial" > 0 AND "EventMatch" IS NULL
              AND ("Id", "InboxScope") IN (SELECT value ->> '$.Id', value ->> '$.InboxScope' FROM json_each({keys}))
              AND ("Partition", "Trial") IN (SELECT value ->> '$.Partition', value ->> '$.Epoch' FROM json_each({fences}))
            """, cancellationToken);
        return count == batch.ClearJobTrials.Count;
    }
}
