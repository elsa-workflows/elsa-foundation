using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class KernelEventPublicationScanPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_keeps_Event_publication_passes_finite_across_late_commits()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var connection = await fixture.CreateEmptyDatabaseAsync("kernel_event_scan");
        var builder = new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>();
        EfRelationalProviderBinding.UseNpgsql(builder, connection, RuntimeEfModule.HistoryTableName, schema: "event_scan");
        var options = builder.Options;
        await using var cleanup = new RuntimePostgreSqlDbContext(options);
        try
        {
            await KernelEventPublicationScanContract.RunAsync(() => new RuntimePostgreSqlDbContext(options),
                interceptor => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>(options)
                    .AddInterceptors(interceptor).Options));
        }
        finally { await cleanup.Database.EnsureDeletedAsync(); }
    }
}
