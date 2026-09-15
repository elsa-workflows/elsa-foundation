using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfPublicationSnapshotReviewStoreTests
{
    [Fact]
    public async Task Round_trip_survives_restart_and_duplicate_is_create_only()
    {
        await using var database = await Database.CreateAsync();
        await using (var first = database.Context())
        {
            var store = database.Store(first, "tenant-a");
            var review = Review("token-1", "tenant-a", database.Now.AddMinutes(10));
            Assert.True(await store.TryAddAsync(review));
            Assert.False(await store.TryAddAsync(review));
        }

        await using var restarted = database.Context();
        var loaded = await database.Store(restarted, "tenant-a").FindAsync("token-1");
        Assert.Equal(Review("token-1", "tenant-a", loaded!.ExpiresAt), loaded);
    }

    [Fact]
    public async Task Tenant_scope_is_checked_before_disclosure_and_global_rows_require_global_access()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = database.Store(context, "tenant-a");
        await store.TryAddAsync(Review("tenant-a-token", "tenant-a", database.Now.AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store(context, "tenant-b").FindAsync("tenant-a-token").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryAddAsync(Review("global-token", null, database.Now.AddMinutes(1))).AsTask());
    }

    [Fact]
    public async Task Concurrent_contexts_have_one_consumer()
    {
        await using var database = await Database.CreateAsync();
        await using (var setup = database.Context())
            await database.Store(setup, "tenant-a").TryAddAsync(Review("race", "tenant-a", database.Now.AddMinutes(1)));

        await using var first = database.Context();
        await using var second = database.Context();
        var results = await Task.WhenAll(
            database.Store(first, "tenant-a").TryConsumeAsync("race").AsTask(),
            database.Store(second, "tenant-a").TryConsumeAsync("race").AsTask());
        Assert.Single(results, result => result);
        Assert.Null(await database.Store(first, "tenant-a").FindAsync("race"));
    }

    [Fact]
    public async Task Expiry_cleanup_is_ordered_and_bounded()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = database.Store(context, "tenant-a");
        await store.TryAddAsync(Review("z", "tenant-a", database.Now.AddMinutes(-2)));
        await store.TryAddAsync(Review("a", "tenant-a", database.Now.AddMinutes(-2)));
        await store.TryAddAsync(Review("middle", "tenant-a", database.Now.AddMinutes(-1)));
        await store.TryAddAsync(Review("active", "tenant-a", database.Now.AddMinutes(1)));

        Assert.Equal(2, await store.DeleteExpiredAsync(database.Now, 2));
        Assert.Null(await store.FindAsync("a"));
        Assert.Null(await store.FindAsync("z"));
        Assert.NotNull(await store.FindAsync("middle"));
        Assert.NotNull(await store.FindAsync("active"));
    }

    [Fact]
    public async Task Malformed_persisted_enum_is_refused()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        context.SnapshotReviews.Add(new PublicationSnapshotReviewEntity
        {
            PreflightToken = "malformed",
            CandidateHash = "sha256:x",
            DefinitionId = "definition",
            Action = "NoSuchAction",
            SlotName = "default",
            PolicySource = nameof(PublicationPolicySource.Host),
            SlotRevision = 0,
            ExpiresAt = database.Now.AddMinutes(1)
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store(context, "tenant-a").FindAsync("malformed").AsTask());
    }

    [Fact]
    public void Registration_is_owned_idempotent_and_rejects_unowned_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var options = new PublishingEntityFrameworkCoreOptions { ConnectionString = "Data Source=registration.db" };
        services.AddPublishingEntityFrameworkCore(options);
        var count = services.Count;
        services.AddPublishingEntityFrameworkCore(new PublishingEntityFrameworkCoreOptions { ConnectionString = "Data Source=registration.db" });
        Assert.Equal(count, services.Count);
        Assert.Equal(PublicationSnapshotReviewStoreBackend.EntityFramework, PublicationSnapshotReviewStoreBackend.Find(services)!.Name);

        var foreign = new ServiceCollection();
        foreign.AddScoped<IPublicationSnapshotReviewStore, ForeignStore>();
        Assert.Throws<InvalidOperationException>(() => foreign.AddPublishingEntityFrameworkCore(options));
    }

    private static PublicationSnapshotReview Review(string token, string? tenantId, DateTimeOffset expiresAt) => new(
        token, "sha256:candidate", "definition", PublicationAction.Replace, "default", PublicationPolicySource.Host,
        null, null, null, null, 0, null, tenantId, expiresAt);

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow.AddSeconds(-DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerSecond);
        private Database(SqliteConnection connection) => this.connection = connection;
        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new Database(connection);
            await using var context = database.Context();
            await context.Database.EnsureCreatedAsync();
            return database;
        }
        public PublishingSnapshotReviewSqliteDbContext Context() => new(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite(connection).Options);
        public EfPublicationSnapshotReviewStore Store(PublishingSnapshotReviewDbContext context, string tenant) =>
            new(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));
        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ForeignStore : IPublicationSnapshotReviewStore
    {
        public ValueTask<bool> TryAddAsync(PublicationSnapshotReview review, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PublicationSnapshotReview?> FindAsync(string preflightToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> TryConsumeAsync(string preflightToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> DeleteExpiredAsync(DateTimeOffset expiresAtOrBefore, int maxCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
