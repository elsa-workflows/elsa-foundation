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
}
