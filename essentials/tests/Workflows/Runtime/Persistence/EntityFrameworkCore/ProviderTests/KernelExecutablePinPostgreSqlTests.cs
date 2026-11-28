using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class KernelExecutablePinPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_keeps_selected_material_until_every_exact_pin_is_released()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var database = await fixture.CreateEmptyDatabaseAsync("kernel_executable_pins");
        var options = new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(database).Options;
        await using var context = new RuntimePostgreSqlDbContext(options);
        try { await KernelExecutablePinContract.RunAsync("PostgreSql", database); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }
}
