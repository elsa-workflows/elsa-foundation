using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Secrets.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2SecretRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CrudRevisionTombstoneAndTenantIsolationUsePublicV2Contracts()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var alpha = Secret("tenant-a", "payments.api", "alpha");
        var beta = Secret("tenant-b", "payments.api", "beta");

        Assert.True(await repository.TryAddAsync(alpha));
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "duplicate")));
        Assert.True(await repository.TryAddAsync(beta));
        Assert.Equal("alpha", (await repository.FindAsync("tenant-a", alpha.Name))!.LatestActiveVersion!.Payload.Value);
        Assert.Equal("beta", (await repository.FindAsync("tenant-b", beta.Name))!.LatestActiveVersion!.Payload.Value);

        var current = await revisions.FindWithRevisionAsync("tenant-a", alpha.Name);
        current!.Secret.DisplayName = "updated";
        var updated = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, updated.Status);
        Assert.NotEqual(current.Revision, updated.Revision);

        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);
        var missing = await revisions.SaveWithRevisionAsync(
            Secret("tenant-a", "missing", "value"),
            "gw:00000000000000000001");
        Assert.Equal(SecretRevisionSaveStatus.NotFound, missing.Status);

        current.Secret.Status = SecretStatus.Deleted;
        await repository.SaveAsync(current.Secret);
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync("tenant-a", alpha.Name))!.Status);
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", alpha.Name, "reserved")));
    }

    [Fact]
    public async Task ListCombinesPortableSearchFacetsStatusScopeCountAndPaging()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        await repository.SaveAsync(Secret("tenant-a", "payments.alpha", "a", "Payments Alpha", scope: "Finance"));
        await repository.SaveAsync(Secret("tenant-a", "payments.configuration", "b", "Payments Config", SecretStoreNames.Configuration, "Finance"));
        await repository.SaveAsync(Secret("tenant-a", "payments.other", "c", "Payments Other", scope: "Operations"));
        await repository.SaveAsync(Secret("tenant-a", "orders.alpha", "d", "Orders Alpha", scope: "Finance"));
        var deleted = Secret("tenant-a", "payments.deleted", "e", "Payments Deleted", scope: "Finance");
        deleted.Status = SecretStatus.Deleted;
        await repository.SaveAsync(deleted);

        var filtered = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(
            search: "PAYMENTS",
            typeName: SecretTypeNames.Text,
            typeNames: [SecretTypeNames.Text, SecretTypeNames.RsaKey],
            storeName: SecretStoreNames.Encrypted,
            storeNames: [SecretStoreNames.Encrypted],
            scope: "FINANCE",
            status: SecretStatus.Active,
            excludedStatus: SecretStatus.Deleted));
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("payments.alpha", Assert.Single(filtered.Items).Name);

        var page = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(skip: 1, take: 2));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["payments.alpha", "payments.configuration"], page.Items.Select(secret => secret.Name));
    }

    [Fact]
    public async Task ActiveOnlyUsesStrictExpiryBoundaryAcrossEveryActiveVersion()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        await repository.SaveAsync(Secret("tenant-a", "a.non-expiring", "a"));
        await repository.SaveAsync(Secret("tenant-a", "b.future", "b", expiresAt: Now.AddMinutes(1)));
        await repository.SaveAsync(Secret("tenant-a", "c.boundary", "c", expiresAt: Now));
        await repository.SaveAsync(Secret("tenant-a", "d.expired", "d", expiresAt: Now.AddMinutes(-1)));
        await repository.SaveAsync(Secret(
            "tenant-a",
            "e.mixed",
            "e",
            versions:
            [
                Version("old", expiresAt: Now.AddMinutes(-1)),
                Version("future", expiresAt: Now.AddMinutes(1), version: 2)
            ]));

        var page = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(
            activeOnly: true,
            now: Now,
            take: 20));

        Assert.Equal(["a.non-expiring", "b.future", "e.mixed"], page.Items.Select(secret => secret.Name));
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task SearchQueryCarriesTheReviewedBoundedScanAcceptance()
    {
        var source = new RecordingSessionSource();
        var repository = new GroundworkSecretRepository(source);

        _ = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(search: "pay", take: 25));

        var request = Assert.Single(source.Queries);
        Assert.Equal("GW-SCAN-ELSA-SECRETS-SUBSTRING", request.AcceptedScan!.Id);
        Assert.True(request.AcceptedScan.Allowed);
        Assert.Equal(25, request.Paging.Limit);
        Assert.True(request.Result.IncludesTotalCount);
    }

    [Fact]
    public async Task RegistrationIsScopedAndAdmitsExactlyOneFreshUnit()
    {
        await using var database = new TemporarySqliteDatabase();
        using var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection)
            .AddGroundworkSecretsStore();

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISecretRepository)).Lifetime);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IGroundworkStorageSessionSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ISecretRepository>(),
            second.ServiceProvider.GetRequiredService<ISecretRepository>());
    }

    [Fact]
    public void SchemaAndAssemblyProveTheCleanBreak()
    {
        var unit = SecretsGroundworkStorageSchema.CreateUnit();
        Assert.Equal(ScopePolicy.Scoped, unit.Scope);
        Assert.True(unit.Concurrency.IsOptimistic);
        Assert.Equal(
            [SecretsGroundworkStorageSchema.TenantIdField, SecretsGroundworkStorageSchema.NormalizedNameField],
            unit.Key.Columns);
        Assert.Contains(unit.Indexes, index => index.Name == SecretsGroundworkStorageSchema.FilteredListIndex);

        var references = typeof(GroundworkSecretRepository).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
        Assert.Null(typeof(GroundworkSecretRepository).Assembly.GetType(
            "Elsa.Secrets.Persistence.Groundwork.Stores.LegacySecretTenantBackfill"));
    }

    private static Secret Secret(
        string tenantId,
        string name,
        string value,
        string? displayName = null,
        string storeName = SecretStoreNames.Encrypted,
        string? scope = null,
        DateTimeOffset? expiresAt = null,
        IList<SecretVersion>? versions = null) => new()
        {
            TenantId = tenantId,
            Name = name,
            DisplayName = displayName ?? name,
            TypeName = SecretTypeNames.Text,
            StoreName = storeName,
            Scope = scope,
            Versions = versions ?? [Version(value, expiresAt)]
        };

    private static SecretVersion Version(string value, DateTimeOffset? expiresAt = null, int version = 1) => new()
    {
        Version = version,
        Status = SecretStatus.Active,
        ExpiresAt = expiresAt,
        Payload = SecretPayload.FromValue(value)
    };

    private sealed class SqliteFixture(
        TemporarySqliteDatabase database,
        IStorageProviderConnection connection,
        ISecretRepository repository) : IAsyncDisposable
    {
        public ISecretRepository Repository { get; } = repository;

        public static ValueTask<SqliteFixture> CreateAsync()
        {
            var database = new TemporarySqliteDatabase();
            var connection = new SqliteProviderFactory().Create(database.ConnectionString);
            var unit = SecretsGroundworkStorageSchema.CreateUnit();
            connection.Schema.Apply(unit);
            var source = new DirectSessionSource(connection, unit);
            return ValueTask.FromResult(new SqliteFixture(database, connection, new GroundworkSecretRepository(source)));
        }

        public async ValueTask DisposeAsync()
        {
            connection.Dispose();
            await database.DisposeAsync();
        }
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return connection.OpenSession(unit, access);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unit);

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class RecordingSessionSource : IGroundworkStorageSessionSource
    {
        private readonly RecordingSession session = new();
        public List<QueryRequest> Queries => session.Queries;
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => session;
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null) => session.Unit;
    }

    private sealed class RecordingSession : IStorageSession
    {
        public StorageUnit Unit { get; } = SecretsGroundworkStorageSchema.CreateUnit();
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public List<QueryRequest> Queries { get; } = [];
        public StoredEntry? Read(StorageKey key) => throw new NotSupportedException();
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            Queries.Add(request);
            return new([], 0, null);
        }
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string path = Path.Join(Path.GetTempPath(), $"elsa-secrets-v2-{Guid.NewGuid():N}.db");
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
