using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class KernelScheduleOccurrencePostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_keeps_the_accepted_publication_across_restart_and_republish()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var database = await fixture.CreateEmptyDatabaseAsync("kernel_schedule");
        var options = new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(database).Options;
        await using var context = new RuntimePostgreSqlDbContext(options);
        try
        {
            await context.Database.MigrateAsync();
            await KernelScheduleOccurrenceContract.RunAsync(() => new RuntimePostgreSqlDbContext(options));
        }
        finally { await context.Database.EnsureDeletedAsync(); }
    }
}
