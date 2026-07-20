using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Preferences.Persistence.Groundwork.Tests;

public sealed class GroundworkStudioPreferenceStoreTests
{
    [Fact]
    public async Task RoundTripsGlobalDocumentAndEnforcesCas()
    {
        var store = CreateInMemoryStore();
        var key = new StudioPreferenceKey("user-1", "tenant-1", "studio-1", "dashboard");

        var created = await store.WriteAsync(key, new(1, Json("{\"size\":\"wide\"}")), StudioPreferenceWriteCondition.MustNotExist, DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.Equal("rev-1", created.Document!.Revision);

        var stale = await store.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.Matches("rev-0"), DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, stale.Status);

        var updated = await store.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.Matches("rev-1"), DateTimeOffset.UtcNow);
        Assert.Equal("rev-2", updated.Document!.Revision);
        Assert.Equal("rev-2", (await store.FindAsync(key))!.Revision);
    }

    [Fact]
    public async Task CompositeIdentityDoesNotCollideAcrossScopeBoundaries()
    {
        var store = CreateInMemoryStore();
        var first = new StudioPreferenceKey("ab", "c", "host", "dashboard");
        var second = new StudioPreferenceKey("a", "bc", "host", "dashboard");

        await store.WriteAsync(first, new(1, Json("{\"owner\":1}")), StudioPreferenceWriteCondition.MustNotExist, DateTimeOffset.UtcNow);
        Assert.Null(await store.FindAsync(second));
    }

    [Fact]
    public async Task ProductionSqliteCompositionUsesAnExplicitOrdinaryGlobalSessionAndPreservesPreferenceIsolation()
    {
        await using var database = new TemporarySqliteDatabase();
        var endpointContext = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1")));
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<IPersistenceAccessContextAccessor>(_ => endpointContext);
        await using var provider = services
            .AddGroundworkSqliteUnifiedPersistence(database.ConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();
        await using var scope = provider.CreateAsyncScope();

        var ambientStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
        var key = new StudioPreferenceKey("user-1", "tenant-1", "studio-1", "dashboard");

        var ambientFailure = await Assert.ThrowsAsync<InvalidStorageScopeAccessException>(() =>
            ambientStore.LoadAsync(StudioPreferencesStorageManifest.DocumentKind, "unreachable"));
        Assert.Equal(StorageScopeRejectionReason.GlobalAccessRequired, ambientFailure.Rejection.Reason);

        Assert.Null(await store.FindAsync(key));

        var created = await store.WriteAsync(
            key,
            new(1, Json("{\"size\":\"wide\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.Equal("rev-1", created.Document!.Revision);

        var updated = await store.WriteAsync(
            key,
            new(1, Json("{\"size\":\"medium\"}")),
            StudioPreferenceWriteCondition.Matches(created.Document.Revision),
            DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, updated.Status);
        Assert.Equal("rev-2", updated.Document!.Revision);

        var conflict = await store.WriteAsync(
            key,
            new(1, Json("{\"size\":\"small\"}")),
            StudioPreferenceWriteCondition.Matches(created.Document.Revision),
            DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, conflict.Status);
        Assert.Equal("rev-2", (await store.FindAsync(key))!.Revision);

        var otherSubject = key with { SubjectId = "user-2" };
        var otherTenant = key with { TenantId = "tenant-2" };
        var otherHost = key with { StudioHostId = "studio-2" };
        foreach (var isolatedKey in new[] { otherSubject, otherTenant, otherHost })
        {
            var saved = await store.WriteAsync(
                isolatedKey,
                new(1, Json("{\"size\":\"small\"}")),
                StudioPreferenceWriteCondition.MustNotExist,
                DateTimeOffset.UtcNow);
            Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, saved.Status);
            Assert.Equal("small", (await store.FindAsync(isolatedKey))!.Value.GetProperty("size").GetString());
        }

        Assert.Equal("medium", (await store.FindAsync(key))!.Value.GetProperty("size").GetString());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<GroundworkPrivilegedAccessSink>().Snapshot());
    }

    [Fact]
    public void UnifiedManifestIncludesStudioPreferences()
    {
        Assert.Contains(
            new GroundworkAllFeaturesDeploymentSchema().CreateManifest().StorageUnits,
            x => x.Identity.Value == StudioPreferencesStorageManifest.DocumentKind);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static GroundworkStudioPreferenceStore CreateInMemoryStore()
    {
        var source = new TestSessionSource(new InMemoryDocumentStore(
            StudioPreferencesStorageManifest.Create(),
            DocumentStoreAccess.Global));
        var context = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1")));
        return new GroundworkStudioPreferenceStore(new GroundworkStoreSessionFactory(context, source));
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class TestSessionSource(IDocumentStore store) : IGroundworkStoreSessionSource
    {
        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(DocumentStoreAccess.Global, access);
            return ValueTask.FromResult(new GroundworkStoreSessionResources(store, new EmptyBoundedStore()));
        }
    }

    private sealed class EmptyBoundedStore : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-studio-preferences-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete($"{_path}-shm");
            File.Delete($"{_path}-wal");
            return ValueTask.CompletedTask;
        }
    }
}
