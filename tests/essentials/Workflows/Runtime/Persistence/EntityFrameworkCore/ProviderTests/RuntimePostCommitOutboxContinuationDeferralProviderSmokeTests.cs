using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimePostCommitOutboxContinuationDeferralPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_sweep_claim_defers_a_continuation_to_its_execution_owner() =>
        RuntimePostCommitOutboxContinuationDeferralSmoke.RunAsync(
            fixture,
            connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimePostCommitOutboxContinuationDeferralSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_sweep_claim_defers_a_continuation_to_its_execution_owner() =>
        RuntimePostCommitOutboxContinuationDeferralSmoke.RunAsync(
            fixture,
            connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options));
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimePostCommitOutboxContinuationDeferralMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_sweep_claim_defers_a_continuation_to_its_execution_owner() =>
        RuntimePostCommitOutboxContinuationDeferralSmoke.RunAsync(
            fixture,
            connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options));
}

/// <summary>
/// #2225 under each provider's own SQL translation: the lease read that decides a deferral runs against the native
/// liveness table. Each scenario gets a fresh scope, because the contract expects an empty outbox and the provider
/// database is shared.
/// </summary>
internal static class RuntimePostCommitOutboxContinuationDeferralSmoke
{
    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        Func<PostCommitOutboxContinuationDeferralContract.Backend, Task>[] scenarios =
        [
            PostCommitOutboxContinuationDeferralContract.ASweepLeavesAContinuationToTheDrainThatOwnsItsExecutionAsync,
            PostCommitOutboxContinuationDeferralContract.ASweepClaimsAContinuationOnceItsOwnerReleasesOrLosesTheLeaseAsync
        ];
        foreach (var scenario in scenarios)
        {
            await using var context = createContext(fixture.ConnectionString);
            await context.Database.EnsureCreatedAsync();
            await scenario(PostCommitOutboxContinuationDeferralContract.EntityFramework(context, $"native-2225-{Guid.NewGuid():N}"));
        }
    }
}
