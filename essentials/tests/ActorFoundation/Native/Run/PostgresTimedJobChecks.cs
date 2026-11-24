using Kernel.Ef.Conformance;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.Native.Run;

internal static class PostgresTimedJobChecks
{
    internal static async Task<int> RunAsync(bool inBatch, bool owned = true, bool trialWithdrawal = true)
    {
        var open = PostgresMultiRootClient.Database();
        var store = new EfStore(open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stop = timeout.Token;
        await using (var db = open())
        {
            var database = db.GetService<IRelationalDatabaseCreator>();
            if (await database.ExistsAsync(stop) && await database.HasTablesAsync(stop))
            {
                Console.Error.WriteLine("Use a fresh dedicated PostgreSQL database for the timed job Worker check.");
                return 2;
            }
        }
        await store.CreateAsync(1);
        return await RelationalTimedJobChecks.RunAsync(store, open,
            $"PostgreSQL {(owned ? "owned" : "EF")} {(inBatch ? "in-batch" : "checked")}", trialWithdrawal);
    }
}
