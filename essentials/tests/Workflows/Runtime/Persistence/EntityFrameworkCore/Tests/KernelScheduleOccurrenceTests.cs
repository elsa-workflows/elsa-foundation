using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class KernelScheduleOccurrenceTests
{
    [Fact]
    public async Task Sqlite_keeps_the_accepted_publication_across_restart_and_republish()
    {
        var directory = Directory.CreateTempSubdirectory("runtime-schedule-occurrence-");
        try
        {
            var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory.FullName, "runtime.db"), Pooling = false };
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection.ToString()).Options;
            await using (var context = new RuntimeSqliteDbContext(options))
                await context.Database.MigrateAsync();

            await KernelScheduleOccurrenceContract.RunAsync(() => new RuntimeSqliteDbContext(options));
        }
        finally { directory.Delete(recursive: true); }
    }
}
