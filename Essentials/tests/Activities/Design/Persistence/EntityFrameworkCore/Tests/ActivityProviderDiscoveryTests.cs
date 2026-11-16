using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DiscoveryContract;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivityProviderDiscoveryTests
{
    [Fact]
    public async Task Sqlite_discovers_exact_temporal_provider_membership_before_paging()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await ActivityProviderDiscoveryContract.RunAsync(db);
    }
}
