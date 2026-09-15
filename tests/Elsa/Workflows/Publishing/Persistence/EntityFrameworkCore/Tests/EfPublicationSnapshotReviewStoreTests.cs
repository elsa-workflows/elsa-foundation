using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
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
    public async Task Reinserted_logical_token_gets_a_new_incarnation()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = database.Store(context, "tenant-a");
        var review = Review("reinsert", "tenant-a", database.Now.AddMinutes(1));
        Assert.True(await store.TryAddAsync(review));
        var firstIncarnation = (await context.SnapshotReviews.SingleAsync()).Incarnation;
        Assert.True(await store.TryConsumeAsync(review.PreflightToken));
        context.ChangeTracker.Clear();
        Assert.True(await store.TryAddAsync(review));
        Assert.NotEqual(firstIncarnation, (await context.SnapshotReviews.SingleAsync()).Incarnation);
        Assert.True(await store.TryConsumeAsync(review.PreflightToken));
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
            Incarnation = Guid.NewGuid().ToString("N"),
            CandidateHash = "sha256:x",
            DefinitionId = "definition",
            Action = "NoSuchAction",
            SlotName = "default",
            PolicySource = nameof(PublicationPolicySource.Host),
            TenantId = "tenant-a",
            SlotRevision = 0,
            ExpiresAt = database.Now.AddMinutes(1)
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store(context, "tenant-a").FindAsync("malformed").AsTask());
    }

    [Fact]
    public async Task Foreign_malformed_row_is_rejected_by_scope_before_decoding()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        context.SnapshotReviews.Add(new PublicationSnapshotReviewEntity
        {
            PreflightToken = "foreign-malformed",
            Incarnation = Guid.NewGuid().ToString("N"),
            CandidateHash = "sha256:x",
            DefinitionId = "definition",
            Action = "NoSuchAction",
            SlotName = "default",
            PolicySource = nameof(PublicationPolicySource.Host),
            TenantId = "tenant-b",
            ExpiresAt = database.Now.AddMinutes(1)
        });
        await context.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store(context, "tenant-a").FindAsync("foreign-malformed").AsTask());
        Assert.Contains("does not belong", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Explicit_EF_selection_wins_when_Groundwork_is_composed_before_or_after_it()
    {
        foreach (var compose in new[] { "ef-gw-ef", "gw-ef-gw" })
        {
            var services = new ServiceCollection();
            new WorkflowsPublishingFeature().ConfigureServices(services);
            if (compose == "ef-gw-ef")
            {
                services.AddPublishingEntityFrameworkCore(new PublishingEntityFrameworkCoreOptions { ConnectionString = "Data Source=registration.db" });
                services.AddGroundworkPublishingStores();
                services.AddPublishingEntityFrameworkCore(new PublishingEntityFrameworkCoreOptions { ConnectionString = "Data Source=registration.db" });
            }
            else
            {
                services.AddGroundworkPublishingStores();
                services.AddPublishingEntityFrameworkCore(new PublishingEntityFrameworkCoreOptions { ConnectionString = "Data Source=registration.db" });
                services.AddGroundworkPublishingStores();
            }

            Assert.Equal(PublicationSnapshotReviewStoreBackend.EntityFramework, PublicationSnapshotReviewStoreBackend.Find(services)!.Name);
            Assert.Single(services, service => service.ServiceType == typeof(IPublicationSnapshotReviewStore));
            Assert.Single(services, service => service.ServiceType == typeof(PublishingEntityFrameworkCoreOptions));
            Assert.Single(services, service => service.ServiceType == typeof(EfPublicationSnapshotReviewStore));
            Assert.Single(services, service => service.ServiceType == typeof(PublishingSnapshotReviewDbContext));
            Assert.Single(services, service => service.ServiceType == typeof(IPublicationRecordStore));
            Assert.Equal(typeof(GroundworkPublicationRecordStore), services.Last(service => service.ServiceType == typeof(IPublicationRecordStore)).ImplementationType);
        }
    }

    [Fact]
    public void Groundwork_registration_rolls_back_when_late_target_validation_fails()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        var registry = new GroundworkStorageUnitRegistry();
        registry.Declare(StorageUnit.Declare(PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind, "conflicting")
            .String("id", 32, column => column.Required()).Key("id").Build(), "conflicting");
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(typeof(PublishingGroundworkStorageManifestSource), null);
        services.AddSingleton(bindings);
        services.AddSingleton(registry);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkPublishingStores("conflicting"));

        Assert.Equal(before.Length, services.Count);
        Assert.Equal(before, services);
        Assert.Single(registry.Registrations);
        Assert.Equal(GroundworkTargetNames.Default, bindings.TargetFor(typeof(PublishingGroundworkStorageManifestSource)));
        Assert.Equal(PublicationSnapshotReviewStoreBackend.InMemory, PublicationSnapshotReviewStoreBackend.Find(services)!.Name);
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
