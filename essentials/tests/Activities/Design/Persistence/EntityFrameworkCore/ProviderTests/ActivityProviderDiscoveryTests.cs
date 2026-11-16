using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DiscoveryContract;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(ActivitiesDesignPostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlDiscoveryTests(ActivitiesDesignPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_discovers_exact_temporal_provider_membership_before_paging()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        await using var db = new ActivitiesDesignPostgreSqlDbContext(new DbContextOptionsBuilder<ActivitiesDesignPostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await db.Database.EnsureCreatedAsync();
        await ActivityProviderDiscoveryContract.RunAsync(db);
    }
}

[Collection(ActivitiesDesignSqlServerFixture.CollectionName)]
public sealed class SqlServerDiscoveryTests(ActivitiesDesignSqlServerFixture fixture)
{
    [SkippableFact]
    public async Task SqlServer_discovers_exact_temporal_provider_membership_before_paging()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        await using var db = new ActivitiesDesignSqlServerDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>().UseSqlServer(fixture.ConnectionString).Options);
        await db.Database.EnsureCreatedAsync();
        await ActivityProviderDiscoveryContract.RunAsync(db);
    }
}

[Collection(ActivitiesDesignMySqlFixture.CollectionName)]
public sealed class MySqlDiscoveryTests(ActivitiesDesignMySqlFixture fixture)
{
    [SkippableFact]
    public async Task MySql_discovers_exact_temporal_provider_membership_before_paging()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        await using var db = new ActivitiesDesignMySqlDbContext(new DbContextOptionsBuilder<ActivitiesDesignMySqlDbContext>().UseMySQL(fixture.ConnectionString).Options);
        await db.Database.EnsureCreatedAsync();
        await ActivityProviderDiscoveryContract.RunAsync(db);
    }
}
