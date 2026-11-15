using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// <see cref="PostCommitOutboxClaimedListingContract"/> (#2225) under PostgreSQL's own translation of the claimed-item
/// read. The scope is fresh because the contract expects an empty outbox and the provider database is shared.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class PostCommitOutboxClaimedListingPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_store_lists_one_executions_held_items_of_one_kind()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        await PostCommitOutboxClaimedListingContract.ListsOneExecutionsHeldItemsOfOneKindAsync(
            PostCommitOutboxClaimedListingContract.EntityFramework(context, $"native-2225-{Guid.NewGuid():N}"));
    }

    [SkippableFact]
    public async Task PostgreSql_store_also_lists_the_deliveries_a_claim_finished_since_a_time()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        await PostCommitOutboxClaimedListingContract.AlsoListsTheDeliveriesAClaimFinishedSinceAsync(
            PostCommitOutboxClaimedListingContract.EntityFramework(context, $"native-3661-{Guid.NewGuid():N}"));
    }

    [SkippableFact]
    public async Task PostgreSql_store_excludes_observed_deliveries_before_the_provider_page_limit()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        await PostCommitOutboxClaimedListingContract.ExcludesObservedDeliveriesBeforeTheProviderPageLimitAsync(
            PostCommitOutboxClaimedListingContract.EntityFramework(context, $"native-3661-exclude-{Guid.NewGuid():N}"));
    }

}


[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class PostCommitOutboxClaimedListingSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public async Task SqlServer_store_excludes_observed_deliveries_before_the_provider_page_limit()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = new RuntimeSqlServerDbContext(
            new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(fixture.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        await PostCommitOutboxClaimedListingContract.ExcludesObservedDeliveriesBeforeTheProviderPageLimitAsync(
            PostCommitOutboxClaimedListingContract.EntityFramework(context, $"native-3661-exclude-{Guid.NewGuid():N}"));
    }
}


[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class PostCommitOutboxClaimedListingMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public async Task MySql_store_excludes_observed_deliveries_before_the_provider_page_limit()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = new RuntimeMySqlDbContext(
            new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(fixture.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        await PostCommitOutboxClaimedListingContract.ExcludesObservedDeliveriesBeforeTheProviderPageLimitAsync(
            PostCommitOutboxClaimedListingContract.EntityFramework(context, $"native-3661-exclude-{Guid.NewGuid():N}"));
    }
}
