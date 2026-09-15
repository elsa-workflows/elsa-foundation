using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

[Collection(PublishingPostgreSqlContainerFixture.CollectionName)]
public sealed class PublishingPostgreSqlSmokeTests(PublishingPostgreSqlContainerFixture fixture)
{
    [SkippableFact]
    public Task Native_provider_round_trip_consume_and_cleanup()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "PostgreSQL is unavailable.");
        return PublishingEfNativeProviderSmoke.RunAsync(fixture.ConnectionString, connectionString =>
            new PublishingSnapshotReviewPostgreSqlDbContext(
                new DbContextOptionsBuilder<PublishingSnapshotReviewPostgreSqlDbContext>()
                    .UseNpgsql(connectionString)
                    .Options));
    }
}

[Collection(PublishingSqlServerContainerFixture.CollectionName)]
public sealed class PublishingSqlServerSmokeTests(PublishingSqlServerContainerFixture fixture)
{
    [SkippableFact]
    public Task Native_provider_round_trip_consume_and_cleanup()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "SQL Server is unavailable.");
        return PublishingEfNativeProviderSmoke.RunAsync(fixture.ConnectionString, connectionString =>
            new PublishingSnapshotReviewSqlServerDbContext(
                new DbContextOptionsBuilder<PublishingSnapshotReviewSqlServerDbContext>()
                    .UseSqlServer(connectionString)
                    .Options));
    }
}

[Collection(PublishingMySqlContainerFixture.CollectionName)]
public sealed class PublishingMySqlSmokeTests(PublishingMySqlContainerFixture fixture)
{
    [SkippableFact]
    public Task Native_provider_round_trip_consume_and_cleanup()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "MySQL is unavailable.");
        return PublishingEfNativeProviderSmoke.RunAsync(fixture.ConnectionString, connectionString =>
            new PublishingSnapshotReviewMySqlDbContext(
                new DbContextOptionsBuilder<PublishingSnapshotReviewMySqlDbContext>()
                    .UseMySQL(connectionString)
                    .Options));
    }
}

internal static class PublishingEfNativeProviderSmoke
{
    public static async Task RunAsync(
        string connectionString,
        Func<string, PublishingSnapshotReviewDbContext> createContext)
    {
        var prefix = $"native-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await using (var setup = createContext(connectionString))
        {
            await setup.Database.EnsureCreatedAsync();
            var store = Store(setup, "tenant-a");
            var review = Review($"{prefix}-roundtrip", "tenant-a", now.AddMinutes(10));

            Assert.True(await store.TryAddAsync(review));
            Assert.False(await store.TryAddAsync(review));

            var policy = new PublicationPolicy(
                $"{prefix}-definition",
                PublicationPolicyDefaultAction.ReplaceDefaultSlot,
                $"{prefix}-slot",
                0,
                now);
            var policyStore = new EfPublicationPolicyStore(setup, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            var savedPolicy = await policyStore.TrySaveAsync(policy, 0);
            Assert.True(savedPolicy.Succeeded);

            var intent = new PublicationProjectionIntent(
                $"{prefix}-intent",
                $"{prefix}-publication",
                PublicationProjectionKinds.TriggerBindings,
                PublicationProjectionOperation.Prepare,
                PublicationProjectionIntentStatus.Pending,
                0,
                null,
                null);
            var intentStore = new EfPublicationProjectionIntentStore(setup, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            await intentStore.SaveAsync(intent);

            await using var restarted = createContext(connectionString);
            var loaded = await Store(restarted, "tenant-a").FindAsync(review.PreflightToken);
            Assert.Equal(review, loaded);
            Assert.Equal(policy with { Revision = 1 }, await new EfPublicationPolicyStore(restarted, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))).FindAsync(policy.WorkflowDefinitionId));
            Assert.Equal(intent, await new EfPublicationProjectionIntentStore(restarted, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))).FindAsync(intent.IntentId));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Store(restarted, "tenant-b").FindAsync(review.PreflightToken).AsTask());
        }

        var raceToken = $"{prefix}-race";
        await using (var setup = createContext(connectionString))
        {
            var store = Store(setup, "tenant-a");
            await store.TryAddAsync(Review(raceToken, "tenant-a", now.AddMinutes(10)));
        }

        await using (var first = createContext(connectionString))
        await using (var second = createContext(connectionString))
        {
            var results = await Task.WhenAll(
                Store(first, "tenant-a").TryConsumeAsync(raceToken).AsTask(),
                Store(second, "tenant-a").TryConsumeAsync(raceToken).AsTask());
            Assert.Single(results, result => result);
        }

        var cleanupA = $"{prefix}-a";
        var cleanupM = $"{prefix}-m";
        var cleanupZ = $"{prefix}-z";
        var foreign = $"{prefix}-foreign";
        await using (var setup = createContext(connectionString))
        {
            var store = Store(setup, "tenant-a");
            Assert.True(await store.TryAddAsync(Review(cleanupZ, "tenant-a", now.AddMinutes(-1))));
            Assert.True(await store.TryAddAsync(Review(cleanupA, "tenant-a", now.AddMinutes(-1))));
            Assert.True(await store.TryAddAsync(Review(cleanupM, "tenant-a", now.AddMinutes(-1))));
            Assert.True(await Store(setup, "tenant-b").TryAddAsync(Review(foreign, "tenant-b", now.AddMinutes(-1))));

            Assert.Equal(2, await store.DeleteExpiredAsync(now, 2));
            Assert.Null(await store.FindAsync(cleanupA));
            Assert.Null(await store.FindAsync(cleanupM));
            Assert.NotNull(await store.FindAsync(cleanupZ));
        }

        await using var tenantB = createContext(connectionString);
        Assert.NotNull(await Store(tenantB, "tenant-b").FindAsync(foreign));
    }

    private static EfPublicationSnapshotReviewStore Store(PublishingSnapshotReviewDbContext context, string tenant) =>
        new(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

    private static PublicationSnapshotReview Review(string token, string tenant, DateTimeOffset expiresAt) => new(
        token, "sha256:native", "native-definition", PublicationAction.Replace, "default",
        PublicationPolicySource.Host, null, null, null, null, 0, null, tenant, expiresAt);

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
