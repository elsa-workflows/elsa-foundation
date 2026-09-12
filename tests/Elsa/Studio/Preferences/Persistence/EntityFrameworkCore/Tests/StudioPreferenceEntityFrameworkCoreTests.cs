using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Core.Services;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Stores;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The EF module is deliberately exercised through its public feature and store contracts.
/// </summary>
public sealed class StudioPreferenceEntityFrameworkCoreTests
{
    [Fact]
    public async Task Store_round_trips_create_and_update_and_honours_every_write_condition()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var key = Key();
        var timestamp = DateTimeOffset.Parse("2026-09-12T09:00:00Z");

        var created = await fixture.Store.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"wide\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            timestamp);

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.NotNull(created.Document);
        Assert.False(string.IsNullOrWhiteSpace(created.Document!.Revision));
        Assert.Equal("dashboard", created.Document.Namespace);
        Assert.Equal(1, created.Document.SchemaVersion);
        Assert.Equal(timestamp, created.Document.UpdatedAt);
        Assert.Equal("wide", created.Document.Value.GetProperty("layout").GetString());
        var loaded = await fixture.Store.FindAsync(key);
        Assert.NotNull(loaded);
        Assert.Equal(created.Document.Revision, loaded!.Revision);
        Assert.Equal(created.Document.Value.GetRawText(), loaded.Value.GetRawText());

        var duplicate = await fixture.Store.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"duplicate\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            timestamp.AddMinutes(1));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, duplicate.Status);
        Assert.Equal("wide", (await fixture.Store.FindAsync(key))!.Value.GetProperty("layout").GetString());

        var updated = await fixture.Store.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"compact\",\"columns\":2}")),
            StudioPreferenceWriteCondition.Matches(created.Document.Revision),
            timestamp.AddMinutes(2));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, updated.Status);
        Assert.NotNull(updated.Document);
        Assert.NotEqual(created.Document.Revision, updated.Document!.Revision);
        Assert.Equal(timestamp.AddMinutes(2), updated.Document.UpdatedAt);

        var stale = await fixture.Store.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"stale\"}")),
            StudioPreferenceWriteCondition.Matches(created.Document.Revision),
            timestamp.AddMinutes(3));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, stale.Status);
        Assert.Equal("compact", (await fixture.Store.FindAsync(key))!.Value.GetProperty("layout").GetString());

        var missing = await fixture.Store.WriteAsync(
            Key() with { SubjectId = "missing-user" },
            new(1, Json("{}")),
            StudioPreferenceWriteCondition.Matches("rev-1"),
            timestamp.AddMinutes(4));

        Assert.Equal(StudioPreferenceStoreWriteStatus.NotFound, missing.Status);
    }

    [Fact]
    public async Task Composite_scope_isolation_is_injective_across_subject_tenant_host_and_namespace()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var first = new StudioPreferenceKey("ab", "c", "host", StudioPreferenceNamespaces.Dashboard);
        var delimiterCollision = new StudioPreferenceKey("a", "bc", "host", StudioPreferenceNamespaces.Dashboard);

        var firstResult = await fixture.Store.WriteAsync(
            first,
            new(1, Json("{\"owner\":\"first\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.Parse("2026-09-12T09:10:00Z"));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, firstResult.Status);
        Assert.Null(await fixture.Store.FindAsync(delimiterCollision));
        Assert.Null(await fixture.Store.FindAsync(first with { SubjectId = "other" }));
        Assert.Null(await fixture.Store.FindAsync(first with { TenantId = "other" }));
        Assert.Null(await fixture.Store.FindAsync(first with { StudioHostId = "other" }));
        Assert.Null(await fixture.Store.FindAsync(first with { Namespace = StudioPreferenceNamespaces.Attention }));

        var collisionRevisionMatch = await fixture.Store.WriteAsync(
            delimiterCollision,
            new(1, Json("{}")),
            StudioPreferenceWriteCondition.Matches(firstResult.Document!.Revision),
            DateTimeOffset.UtcNow);

        Assert.Equal(StudioPreferenceStoreWriteStatus.NotFound, collisionRevisionMatch.Status);
    }

    [Fact]
    public async Task Ordinal_scope_identity_preserves_case_trailing_space_and_unicode_variants()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var keys = new[]
        {
            new StudioPreferenceKey("User", "Tenant", "Host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceKey("user", "Tenant", "Host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceKey("User ", "Tenant", "Host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceKey("User", "tenant", "Host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceKey("User", "Tenant ", "Host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceKey("U\u0308ser", "Tenant", "Host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceKey("\u00dcser", "Tenant", "Host", StudioPreferenceNamespaces.Dashboard)
        };

        for (var index = 0; index < keys.Length; index++)
        {
            var result = await fixture.Store.WriteAsync(
                keys[index],
                new(1, Json($"{{\"index\":{index}}}")),
                StudioPreferenceWriteCondition.MustNotExist,
                DateTimeOffset.Parse("2026-09-12T09:15:00Z").AddMinutes(index));
            Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, result.Status);
        }

        for (var index = 0; index < keys.Length; index++)
            Assert.Equal(index, (await fixture.Store.FindAsync(keys[index]))!.Value.GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task Unbounded_subject_tenant_and_namespace_identity_round_trips()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var key = new StudioPreferenceKey(
            new string('s', 600),
            new string('t', 600),
            new string('h', 128),
            new string('n', 600));

        var saved = await fixture.Store.WriteAsync(
            key,
            new(1, Json("{\"value\":true}")),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.Parse("2026-09-12T09:15:30Z"));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, saved.Status);
        Assert.True((await fixture.Store.FindAsync(key))!.Value.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_creators_have_exactly_one_winner()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var barrier = new DeterministicSaveBarrier();
            await using var first = await InterceptedSqliteFixture.CreateAsync(
                databasePath,
                new OrderedSaveChangesInterceptor(barrier, isWinner: true));
            await using var second = await InterceptedSqliteFixture.CreateAsync(
                databasePath,
                new OrderedSaveChangesInterceptor(barrier, isWinner: false));

            var writes = await Task.WhenAll(
                first.Store.WriteAsync(
                    Key(),
                    new(1, Json("{\"winner\":\"first\"}")),
                    StudioPreferenceWriteCondition.MustNotExist,
                    DateTimeOffset.Parse("2026-09-12T09:16:00Z")).AsTask(),
                second.Store.WriteAsync(
                    Key(),
                    new(1, Json("{\"winner\":\"second\"}")),
                    StudioPreferenceWriteCondition.MustNotExist,
                    DateTimeOffset.Parse("2026-09-12T09:16:01Z")).AsTask());

            Assert.Equal(1, writes.Count(result => result.Status == StudioPreferenceStoreWriteStatus.Saved));
            Assert.Equal(1, writes.Count(result => result.Status == StudioPreferenceStoreWriteStatus.Conflict));
            Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, writes[0].Status);
            Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, writes[1].Status);
            Assert.Equal("rev-1", (await first.Store.FindAsync(Key()))!.Revision);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_same_revision_updates_have_exactly_one_winner_and_preserve_it()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            StudioPreferenceDocument created;
            await using (var seed = await SqliteFixture.CreateAsync(databasePath))
            {
                created = (await seed.Store.WriteAsync(
                    Key(),
                    new(1, Json("{\"value\":\"original\"}")),
                    StudioPreferenceWriteCondition.MustNotExist,
                    DateTimeOffset.Parse("2026-09-12T09:17:00Z"))).Document!;
            }

            var barrier = new DeterministicSaveBarrier();
            await using var first = await InterceptedSqliteFixture.CreateAsync(
                databasePath,
                new OrderedSaveChangesInterceptor(barrier, isWinner: true));
            await using var second = await InterceptedSqliteFixture.CreateAsync(
                databasePath,
                new OrderedSaveChangesInterceptor(barrier, isWinner: false));

            var writes = await Task.WhenAll(
                first.Store.WriteAsync(
                    Key(),
                    new(1, Json("{\"value\":\"first\"}")),
                    StudioPreferenceWriteCondition.Matches(created.Revision),
                    DateTimeOffset.Parse("2026-09-12T09:17:01Z")).AsTask(),
                second.Store.WriteAsync(
                    Key(),
                    new(1, Json("{\"value\":\"second\"}")),
                    StudioPreferenceWriteCondition.Matches(created.Revision),
                    DateTimeOffset.Parse("2026-09-12T09:17:02Z")).AsTask());

            var saved = Assert.Single(writes, result => result.Status == StudioPreferenceStoreWriteStatus.Saved);
            Assert.Single(writes, result => result.Status == StudioPreferenceStoreWriteStatus.Conflict);
            Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, writes[0].Status);
            Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, writes[1].Status);
            var loaded = await first.Store.FindAsync(Key());
            Assert.Equal("rev-2", loaded!.Revision);
            Assert.Equal(saved.Document!.Value.GetProperty("value").GetString(), loaded.Value.GetProperty("value").GetString());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("rev-0")]
    [InlineData("rev--1")]
    [InlineData("REV-1")]
    [InlineData("rev-01")]
    [InlineData("rev-1 ")]
    [InlineData("rev-9223372036854775808")]
    public async Task Malformed_revisions_are_conflicts_without_mutation(string? revision)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var created = await fixture.Store.WriteAsync(
            Key(),
            new(1, Json("{\"value\":\"original\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.Parse("2026-09-12T09:18:00Z"));

        var result = await fixture.Store.WriteAsync(
            Key(),
            new(1, Json("{\"value\":\"invalid\"}")),
            StudioPreferenceWriteCondition.Matches(revision!),
            DateTimeOffset.Parse("2026-09-12T09:18:01Z"));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, result.Status);
        var loaded = await fixture.Store.FindAsync(Key());
        Assert.Equal(created.Document!.Revision, loaded!.Revision);
        Assert.Equal("original", loaded.Value.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Service_enforces_namespace_shape_and_byte_quota_over_the_EF_store()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = new StudioPreferenceService(
            new StudioPreferenceNamespaceRegistry(
            [
                new DashboardPreferenceNamespace(),
                new AttentionPreferenceNamespace()
            ]),
            fixture.Store,
            TimeProvider.System);

        var key = Key(StudioPreferenceNamespaces.Attention);
        var oversized = Json($"{{\"value\":\"{new string('x', AttentionPreferenceNamespace.DefaultMaxBytes)}\"}}");

        await Assert.ThrowsAsync<StudioPreferenceQuotaExceededException>(() =>
            service.WriteAsync(
                key,
                new(1, oversized),
                StudioPreferenceWriteCondition.MustNotExist).AsTask());

        await Assert.ThrowsAsync<StudioPreferenceValidationException>(() =>
            service.WriteAsync(
                key,
                new(1, Json("[]")),
                StudioPreferenceWriteCondition.MustNotExist).AsTask());

        var saved = await service.WriteAsync(
            Key(),
            new(1, Json("{\"density\":\"comfortable\"}")),
            StudioPreferenceWriteCondition.MustNotExist);

        Assert.NotNull(await fixture.Store.FindAsync(Key()));
        await Assert.ThrowsAsync<StudioPreferenceConflictException>(() =>
            service.WriteAsync(
                Key(),
                new(1, Json("{}")),
                StudioPreferenceWriteCondition.Matches("rev-999")).AsTask());
        Assert.Equal(saved.Revision, (await fixture.Store.FindAsync(Key()))!.Revision);
    }

    [Fact]
    public async Task Unicode_payload_near_the_quota_round_trips_and_oversized_CAS_leaves_the_row_unchanged()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = new StudioPreferenceService(
            new StudioPreferenceNamespaceRegistry([new AttentionPreferenceNamespace()]),
            fixture.Store,
            TimeProvider.System);
        var key = Key(StudioPreferenceNamespaces.Attention);
        var nearLimit = JsonSerializer.SerializeToElement(new
        {
            value = new string('\u00e9', 5_300),
            escaped = "\\\"\\n"
        });
        Assert.InRange(
            JsonSerializer.SerializeToUtf8Bytes(nearLimit).Length,
            31_000,
            AttentionPreferenceNamespace.DefaultMaxBytes);

        var saved = await service.WriteAsync(
            key,
            new(1, nearLimit),
            StudioPreferenceWriteCondition.MustNotExist);
        var oversized = JsonSerializer.SerializeToElement(new { value = new string('\u00e9', 5_500) });

        await Assert.ThrowsAsync<StudioPreferenceQuotaExceededException>(() =>
            service.WriteAsync(
                key,
                new(1, oversized),
                StudioPreferenceWriteCondition.Matches(saved.Revision)).AsTask());

        var loaded = await fixture.Store.FindAsync(key);
        Assert.Equal(saved.Revision, loaded!.Revision);
        Assert.Equal(nearLimit.GetRawText(), loaded.Value.GetRawText());
    }

    [Fact]
    public async Task File_backed_database_reopens_with_the_same_document()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-studio-preferences-{Guid.NewGuid():N}.db");
        try
        {
            StudioPreferenceDocument created;
            await using (var first = await SqliteFixture.CreateAsync(databasePath))
            {
                created = (await first.Store.WriteAsync(
                    Key(),
                    new(1, Json("{\"persisted\":true}")),
                    StudioPreferenceWriteCondition.MustNotExist,
                    DateTimeOffset.Parse("2026-09-12T09:20:00.1234567+05:45"))).Document!;
            }

            await using var reopened = await SqliteFixture.CreateAsync(databasePath);
            var loaded = await reopened.Store.FindAsync(Key());

            Assert.NotNull(loaded);
            Assert.Equal(created.Revision, loaded!.Revision);
            Assert.Equal(created.SchemaVersion, loaded.SchemaVersion);
            Assert.Equal(created.UpdatedAt, loaded.UpdatedAt);
            Assert.Equal(created.Value.GetRawText(), loaded.Value.GetRawText());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void EF_module_registers_a_store_and_never_carries_the_Groundwork_dependency()
    {
        var assembly = typeof(StudioPreferencesEntityFrameworkCoreFeature).Assembly;
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name is not null && reference.Name.StartsWith("Groundwork", StringComparison.OrdinalIgnoreCase));

        var services = ConfigureEf(new ServiceCollection(), "Sqlite", "Data Source=:memory:");
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IStudioPreferenceStore));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ImplementationType == typeof(InMemoryStudioPreferenceStore));
        Assert.Equal(
            "entity-framework",
            services.Single(descriptor => descriptor.ImplementationInstance is StudioPreferenceStoreBackend)
                .ImplementationInstance is StudioPreferenceStoreBackend backend
                ? backend.Name
                : null);
    }

    [Fact]
    public void Feature_builds_the_sqlite_model_without_starting_migrations()
    {
        var services = ConfigureEf(new ServiceCollection(), "Sqlite", "Data Source=:memory:");
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = serviceProvider.CreateScope();
        var contextType = services
            .Select(descriptor => descriptor.ServiceType)
            .Single(type => typeof(DbContext).IsAssignableFrom(type) &&
                            !type.IsAbstract &&
                            type.Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        var context = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService(contextType));

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        var entity = context.Model.FindEntityType(typeof(StudioPreferenceRecord));
        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindPrimaryKey());
        Assert.True(entity.FindProperty(nameof(StudioPreferenceRecord.Revision))!.IsConcurrencyToken);
        Assert.Equal(
            "TEXT",
            entity.FindProperty(nameof(StudioPreferenceRecord.ValueJson))!.GetColumnType(),
            ignoreCase: true);
    }

    [Fact]
    public void Groundwork_then_EF_is_rejected_by_the_backend_marker()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStudioPreferences();

        var error = Assert.Throws<InvalidOperationException>(() => ConfigureEf(services, "Sqlite", "Data Source=:memory:"));
        Assert.Contains("groundwork", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("entity-framework", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EF_then_Groundwork_is_rejected_by_the_backend_marker()
    {
        var services = ConfigureEf(new ServiceCollection(), "Sqlite", "Data Source=:memory:");

        var error = Assert.Throws<InvalidOperationException>(() => services.AddGroundworkStudioPreferences());
        Assert.Contains("entity-framework", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("groundwork", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Model_has_stable_scope_and_document_columns_and_a_primary_key()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var entity = fixture.Context.Model.GetEntityTypes()
            .SingleOrDefault(candidate =>
                candidate.FindPrimaryKey() is not null &&
                candidate.GetProperties().Any(property => property.Name.Contains("Revision", StringComparison.OrdinalIgnoreCase)));

        Assert.NotNull(entity);
        Assert.False(string.IsNullOrWhiteSpace(entity!.GetTableName()));
        Assert.NotEmpty(entity.FindPrimaryKey()!.Properties);

        var names = entity.GetProperties().Select(property => property.Name).ToArray();
        Assert.Contains(names, name => name.Contains("Subject", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Contains("Host", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Contains("Namespace", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Contains("Schema", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Contains("Value", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Contains("Updated", StringComparison.OrdinalIgnoreCase));
    }

    private static IServiceCollection ConfigureEf(IServiceCollection services, string provider, string connectionString)
    {
        new StudioPreferencesEntityFrameworkCoreFeature
        {
            Provider = provider,
            ConnectionString = connectionString
        }.ConfigureServices(services);
        return services;
    }

    private static StudioPreferenceKey Key(string @namespace = StudioPreferenceNamespaces.Dashboard) =>
        new("user-1", "tenant-1", "studio-primary", @namespace);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string TemporaryDatabasePath() =>
        Path.Join(Path.GetTempPath(), $"elsa-studio-preferences-{Guid.NewGuid():N}.db");

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" }.Where(File.Exists))
            File.Delete(path);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly string _databasePath;
        private readonly bool _ownsDatabasePath;

        private SqliteFixture(
            ServiceProvider provider,
            AsyncServiceScope scope,
            DbContext context,
            IStudioPreferenceStore store,
            string databasePath,
            bool ownsDatabasePath)
        {
            _provider = provider;
            _scope = scope;
            _databasePath = databasePath;
            _ownsDatabasePath = ownsDatabasePath;
            Context = context;
            Store = store;
        }

        public DbContext Context { get; }
        public IStudioPreferenceStore Store { get; }

        public static async Task<SqliteFixture> CreateAsync(string? databasePath = null)
        {
            var ownsDatabasePath = databasePath is null;
            databasePath ??= Path.Join(Path.GetTempPath(), $"elsa-studio-preferences-{Guid.NewGuid():N}.db");
            var services = ConfigureEf(
                new ServiceCollection(),
                "Sqlite",
                $"Data Source={databasePath}");
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = provider.CreateAsyncScope();
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>();
                await context.Database.EnsureCreatedAsync();
                var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
                Assert.IsType<EfStudioPreferenceStore>(store);
                return new SqliteFixture(provider, scope, context, store, databasePath, ownsDatabasePath);
            }
            catch
            {
                await scope.DisposeAsync();
                await provider.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            if (_ownsDatabasePath)
            {
                DeleteDatabaseFiles(_databasePath);
            }
        }
    }

    private sealed class InterceptedSqliteFixture(
        StudioPreferencesSqliteDbContext context,
        IStudioPreferenceStore store) : IAsyncDisposable
    {
        public IStudioPreferenceStore Store { get; } = store;

        public static async Task<InterceptedSqliteFixture> CreateAsync(
            string databasePath,
            SaveChangesInterceptor interceptor)
        {
            var options = new DbContextOptionsBuilder<StudioPreferencesSqliteDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .AddInterceptors(interceptor)
                .Options;
            var context = new StudioPreferencesSqliteDbContext(options);
            try
            {
                await context.Database.EnsureCreatedAsync();
                return new InterceptedSqliteFixture(context, new EfStudioPreferenceStore(context));
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class DeterministicSaveBarrier
    {
        private readonly TaskCompletionSource _loserArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _winnerCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SignalLoserArrived() => _loserArrived.TrySetResult();
        public Task WaitForLoserAsync(CancellationToken cancellationToken) => _loserArrived.Task.WaitAsync(cancellationToken);
        public void SignalWinnerCompleted() => _winnerCompleted.TrySetResult();
        public Task WaitForWinnerAsync(CancellationToken cancellationToken) => _winnerCompleted.Task.WaitAsync(cancellationToken);
    }

    private sealed class OrderedSaveChangesInterceptor(
        DeterministicSaveBarrier barrier,
        bool isWinner) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                await barrier.WaitForLoserAsync(cancellationToken);
            else
            {
                barrier.SignalLoserArrived();
                await barrier.WaitForWinnerAsync(cancellationToken);
            }

            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                barrier.SignalWinnerCompleted();

            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                barrier.SignalWinnerCompleted();

            return Task.CompletedTask;
        }
    }
}
