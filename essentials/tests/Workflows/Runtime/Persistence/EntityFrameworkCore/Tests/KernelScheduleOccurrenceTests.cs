using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class KernelScheduleOccurrenceTests
{
    [Fact]
    public async Task Sqlite_keeps_the_accepted_publication_across_restart_and_republish()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options;
        await using (var context = new RuntimeSqliteDbContext(options))
            await context.Database.MigrateAsync();

        await KernelScheduleOccurrenceContract.RunAsync(() => new RuntimeSqliteDbContext(options));
    }
}
