using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Studio.Preferences.Persistence.Groundwork.Tests;

public sealed class GroundworkStudioPreferenceStoreTests
{
    [Fact]
    public async Task PublicV2StoreRoundTripsGlobalRowsAndEnforcesCas()
    {
        await using var database = new TemporarySqliteDatabase();
        using var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection)
            .AddGroundworkStudioPreferences();
        await using var provider = services.BuildServiceProvider();
        await StartAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
        var key = new StudioPreferenceKey("user-1", "tenant-1", "studio-1", "dashboard");

        var created = await store.WriteAsync(
            key,
            new(1, Json("{\"size\":\"wide\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.Parse("2026-08-16T00:00:00Z"));
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.Equal("rev-1", created.Document!.Revision);

        var stale = await store.WriteAsync(
            key,
            new(1, Json("{}")),
            StudioPreferenceWriteCondition.Matches("rev-0"),
            DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, stale.Status);

        var updated = await store.WriteAsync(
            key,
            new(2, Json("{\"size\":\"medium\"}")),
            StudioPreferenceWriteCondition.Matches("rev-1"),
            DateTimeOffset.Parse("2026-08-16T01:00:00Z"));
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, updated.Status);
        Assert.Equal("rev-2", updated.Document!.Revision);

        var loaded = await store.FindAsync(key);
        Assert.Equal("rev-2", loaded!.Revision);
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal("medium", loaded.Value.GetProperty("size").GetString());
    }

    [Fact]
    public async Task CompositeIdentityIsInjectiveAndRevisionMatchOnMissingIsNotFound()
    {
        await using var database = new TemporarySqliteDatabase();
        using var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection)
            .AddGroundworkStudioPreferences();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
        var first = new StudioPreferenceKey("ab", "c", "host", "dashboard");
        var second = new StudioPreferenceKey("a", "bc", "host", "dashboard");

        await store.WriteAsync(
            first,
            new(1, Json("{\"owner\":1}")),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.UtcNow);

        Assert.Null(await store.FindAsync(second));
        var missing = await store.WriteAsync(
            second,
            new(1, Json("{}")),
            StudioPreferenceWriteCondition.Matches("rev-1"),
            DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.NotFound, missing.Status);
    }

    [Fact]
    public async Task NamedTargetNeverFallsBackToDefaultConnection()
    {
        await using var defaultDatabase = new TemporarySqliteDatabase();
        await using var namedDatabase = new TemporarySqliteDatabase();
        using var defaultConnection = new SqliteProviderFactory().Create(defaultDatabase.ConnectionString);
        using var namedConnection = new SqliteProviderFactory().Create(namedDatabase.ConnectionString);
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(defaultConnection)
            .AddGroundworkStorageProviderConnection(namedConnection, "studio")
            .AddGroundworkStudioPreferences("studio");
        await using var provider = services.BuildServiceProvider();
        await StartAsync(provider);

        Assert.NotNull(namedConnection.Catalog.ReadIndexes(
            new StorageUnitId(StudioPreferencesGroundworkStorageSchema.UnitId)));
        Assert.ThrowsAny<Exception>(() =>
            defaultConnection.OpenSession(StudioPreferencesGroundworkStorageSchema.CreateUnit(), StorageAccess.Global)
                .Read(new StorageKey(new Dictionary<string, object?>
                {
                    [StudioPreferencesGroundworkStorageSchema.IdField] = new string('0', 64)
                })));
    }

    [Fact]
    public void ConflictingDeclarationsFailBeforeProviderResolution()
    {
        var registry = new GroundworkStorageUnitRegistry();
        registry.Declare(StudioPreferencesGroundworkStorageSchema.CreateUnit());
        var conflict = StudioPreferencesGroundworkStorageSchema.CreateUnit() with { Name = "other_physical_name" };

        var error = Assert.Throws<InvalidOperationException>(() => registry.Declare(conflict));
        Assert.Contains("declared twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConflictingTargetConnectionsFailDuringComposition()
    {
        await using var firstDatabase = new TemporarySqliteDatabase();
        await using var secondDatabase = new TemporarySqliteDatabase();
        using var first = new SqliteProviderFactory().Create(firstDatabase.ConnectionString);
        using var second = new SqliteProviderFactory().Create(secondDatabase.ConnectionString);
        var services = new ServiceCollection().AddGroundworkStorageProviderConnection(first, "studio");

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddGroundworkStorageProviderConnection(second, "studio"));

        Assert.Contains("already has a v2 provider connection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAssemblyHasNoGroundworkV1DocumentDependency()
    {
        var references = typeof(GroundworkStudioPreferenceStore).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
    }

    private static async Task StartAsync(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string path = Path.Join(Path.GetTempPath(), $"elsa-studio-preferences-v2-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete($"{path}-shm");
            File.Delete($"{path}-wal");
            return ValueTask.CompletedTask;
        }
    }
}
