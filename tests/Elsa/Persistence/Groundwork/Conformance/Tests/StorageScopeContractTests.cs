using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class StorageScopeContractTests
{
    private const string ScopedDocumentKind = "scope-contract-document";
    private const string GlobalDocumentKind = "global-contract-document";
    private const string ListAllQuery = "list-all";
    private const string ByCollectionIndex = "by-collection";
    private const string CollectionField = "collection";

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Storage_scope_contract_is_enforced_by_every_persistent_provider(string providerKey)
    {
        await using var driver = CreateDriver(providerKey);
        await driver.InitializeAsync(CancellationToken.None);
        await using var source = new ProviderScopeSessionSource(driver);
        var accessor = new MutableAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var privilegedAccessSink = new GroundworkPrivilegedAccessSink();
        var factory = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(privilegedAccessSink));

        await driver.ResetAsync(CancellationToken.None);
        await AssertEqualIdsAndWrongScopeOperationsAsync(factory, accessor);

        await driver.ResetAsync(CancellationToken.None);
        await AssertOrdinaryAccessBoundariesAsync(factory, accessor);

        await driver.ResetAsync(CancellationToken.None);
        await AssertUnitOfWorkBoundariesAsync(factory, accessor);

        await driver.ResetAsync(CancellationToken.None);
        await AssertCancellationDisposalAndReuseAsync(factory, accessor, source);

        Assert.NotEmpty(source.Leases);
        Assert.All(source.Leases, lease => Assert.True(lease.IsDisposed));
        Assert.Contains(
            privilegedAccessSink.Snapshot(),
            record => record.Outcome == GroundworkPrivilegedAccessOutcome.Abandoned);
    }

    private static async Task AssertEqualIdsAndWrongScopeOperationsAsync(
        GroundworkStoreSessionFactory factory,
        MutableAccessContextAccessor accessor)
    {
        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using (var tenantA = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            await SaveAsync(tenantA.DocumentStore, ScopedDocumentKind, "shared-id", "tenant-a");
            await SaveAsync(tenantA.DocumentStore, ScopedDocumentKind, "only-in-a", "tenant-a-only");
        }

        accessor.Current = PersistenceAccessContext.Global;
        await using (var global = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            await SaveAsync(global.DocumentStore, GlobalDocumentKind, "shared-id", "global");
            await SaveAsync(global.DocumentStore, GlobalDocumentKind, "global-only", "global-only");
        }

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        await using (var tenantB = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            await SaveAsync(tenantB.DocumentStore, ScopedDocumentKind, "shared-id", "tenant-b");

            Assert.Equal("tenant-b", await LoadValueAsync(tenantB.DocumentStore, ScopedDocumentKind, "shared-id"));
            Assert.Null(await tenantB.DocumentStore.LoadAsync(ScopedDocumentKind, "only-in-a"));
            Assert.Null(await tenantB.DocumentStore.LoadAsync(ScopedDocumentKind, "missing"));
            Assert.Equal<string>(["shared-id"], await QueryIdsAsync(tenantB.DocumentStore, ScopedDocumentKind));

            var wrongScopeUpdate = await tenantB.DocumentStore.SaveAsync(
                Request(ScopedDocumentKind, "only-in-a", "must-not-write", expectedVersion: 1));
            var missingUpdate = await tenantB.DocumentStore.SaveAsync(
                Request(ScopedDocumentKind, "missing", "must-not-write", expectedVersion: 1));
            var wrongScopeDelete = await tenantB.DocumentStore.DeleteAsync(
                new DeleteDocumentRequest(ScopedDocumentKind, "only-in-a"));
            var missingDelete = await tenantB.DocumentStore.DeleteAsync(
                new DeleteDocumentRequest(ScopedDocumentKind, "missing"));

            Assert.Equal(DocumentStoreWriteStatus.NotFound, wrongScopeUpdate.Status);
            Assert.Equal(missingUpdate.Status, wrongScopeUpdate.Status);
            Assert.Equal(DocumentStoreWriteStatus.NotFound, wrongScopeDelete.Status);
            Assert.Equal(missingDelete.Status, wrongScopeDelete.Status);

            var updated = await tenantB.DocumentStore.SaveAsync(
                Request(ScopedDocumentKind, "shared-id", "tenant-b-updated", expectedVersion: 1));
            Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
            Assert.Equal(
                "tenant-b-updated",
                await LoadValueAsync(tenantB.DocumentStore, ScopedDocumentKind, "shared-id"));

            var deleted = await tenantB.DocumentStore.DeleteAsync(
                new DeleteDocumentRequest(ScopedDocumentKind, "shared-id", ExpectedVersion: 2));
            Assert.Equal(DocumentStoreWriteStatus.Deleted, deleted.Status);
            Assert.Empty(await QueryIdsAsync(tenantB.DocumentStore, ScopedDocumentKind));
        }

        accessor.Current = PersistenceAccessContext.Global;
        await using (var global = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            Assert.Equal("global", await LoadValueAsync(global.DocumentStore, GlobalDocumentKind, "shared-id"));
            Assert.Equal<string>(
                ["global-only", "shared-id"],
                await QueryIdsAsync(global.DocumentStore, GlobalDocumentKind));

            var updated = await global.DocumentStore.SaveAsync(
                Request(GlobalDocumentKind, "shared-id", "global-updated", expectedVersion: 1));
            Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
            var deleted = await global.DocumentStore.DeleteAsync(
                new DeleteDocumentRequest(GlobalDocumentKind, "global-only", ExpectedVersion: 1));
            Assert.Equal(DocumentStoreWriteStatus.Deleted, deleted.Status);
            Assert.Equal<string>(["shared-id"], await QueryIdsAsync(global.DocumentStore, GlobalDocumentKind));
        }

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using var reopenedTenantA = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        Assert.Equal("tenant-a", await LoadValueAsync(reopenedTenantA.DocumentStore, ScopedDocumentKind, "shared-id"));
        Assert.Equal("tenant-a-only", await LoadValueAsync(reopenedTenantA.DocumentStore, ScopedDocumentKind, "only-in-a"));
        Assert.Equal<string>(
            ["only-in-a", "shared-id"],
            await QueryIdsAsync(reopenedTenantA.DocumentStore, ScopedDocumentKind));
    }

    private static async Task AssertOrdinaryAccessBoundariesAsync(
        GroundworkStoreSessionFactory factory,
        MutableAccessContextAccessor accessor)
    {
        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using (var tenantA = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
            await SaveAsync(tenantA.DocumentStore, ScopedDocumentKind, "protected", "tenant-a");

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        await using (var tenantB = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
            await SaveAsync(tenantB.DocumentStore, ScopedDocumentKind, "protected", "tenant-b");

        accessor.Current = PersistenceAccessContext.Global;
        await using (var global = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            await SaveAsync(global.DocumentStore, GlobalDocumentKind, "global", "global");
            var exception = await Assert.ThrowsAsync<InvalidStorageScopeAccessException>(() =>
                global.DocumentStore.LoadAsync(ScopedDocumentKind, "protected"));
            Assert.Equal(StorageScopeRejectionReason.ScopedAccessRequired, exception.Rejection.Reason);
        }

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using (var scoped = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            var exception = await Assert.ThrowsAsync<InvalidStorageScopeAccessException>(() =>
                scoped.DocumentStore.LoadAsync(GlobalDocumentKind, "global"));
            Assert.Equal(StorageScopeRejectionReason.GlobalAccessRequired, exception.Rejection.Reason);
        }

        accessor.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("cross-scope-recovery"));
        await using (var acrossScopes = await factory.CreateAsync(PersistenceAccessPolicy.Privileged))
        {
            var exception = await Assert.ThrowsAsync<InvalidStorageScopeAccessException>(() =>
                acrossScopes.DocumentStore.LoadAsync(ScopedDocumentKind, "protected"));
            Assert.Equal(StorageScopeRejectionReason.TargetScopeRequired, exception.Rejection.Reason);
        }

        var acrossScopeValues = await factory.ExecutePrivilegedAcrossScopesAsync(
            async (session, cancellationToken) =>
            {
                var result = await session.BoundedDocumentStore.QueryAsync(
                    new DocumentQuery(
                        ScopedDocumentKind,
                        ListAllQuery,
                        [DocumentQueryClause.Of(DocumentQueryComparison.Equal(CollectionField, ScopedDocumentKind))]),
                    cancellationToken);
                return result.Documents
                    .Select(document => ReadValue(document.ContentJson)
                        ?? throw new InvalidOperationException("The scope-contract document requires a value."))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            });
        Assert.Equal<string>(["tenant-a", "tenant-b"], acrossScopeValues);

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using var verification = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        Assert.Equal("tenant-a", await LoadValueAsync(verification.DocumentStore, ScopedDocumentKind, "protected"));
    }

    private static async Task AssertUnitOfWorkBoundariesAsync(
        GroundworkStoreSessionFactory factory,
        MutableAccessContextAccessor accessor)
    {
        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using (var tenantA = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            await using (var unitOfWork = await tenantA.DocumentStore.BeginAsync(
                             DocumentCommitScope.Of(ScopedDocumentKind)))
            {
                var saved = await unitOfWork.SaveAsync(
                    Request(ScopedDocumentKind, "unit-of-work", "tenant-a", expectedVersion: 0));
                Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
                await unitOfWork.CommitAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidStorageScopeAccessException>(() =>
                tenantA.DocumentStore.BeginAsync(
                    DocumentCommitScope.Of(ScopedDocumentKind, GlobalDocumentKind)));

            Assert.Equal(StorageScopeOperation.BeginUnitOfWork, exception.Rejection.Operation);
            Assert.Equal(StorageScopeRejectionReason.MixedUnitOfWorkPolicy, exception.Rejection.Reason);
            Assert.Equal("tenant-a", await LoadValueAsync(
                tenantA.DocumentStore,
                ScopedDocumentKind,
                "unit-of-work"));
            Assert.Null(await tenantA.DocumentStore.LoadAsync(ScopedDocumentKind, "mixed-scoped-write"));
        }

        accessor.Current = PersistenceAccessContext.Global;
        await using (var global = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            await using var unitOfWork = await global.DocumentStore.BeginAsync(
                DocumentCommitScope.Of(GlobalDocumentKind));
            var saved = await unitOfWork.SaveAsync(
                Request(GlobalDocumentKind, "unit-of-work", "global", expectedVersion: 0));
            Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
            await unitOfWork.CommitAsync();
            Assert.Equal("global", await LoadValueAsync(global.DocumentStore, GlobalDocumentKind, "unit-of-work"));
            Assert.Null(await global.DocumentStore.LoadAsync(GlobalDocumentKind, "mixed-global-write"));
        }

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        await using var tenantB = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        Assert.Null(await tenantB.DocumentStore.LoadAsync(ScopedDocumentKind, "unit-of-work"));
    }

    private static async Task AssertCancellationDisposalAndReuseAsync(
        GroundworkStoreSessionFactory factory,
        MutableAccessContextAccessor accessor,
        ProviderScopeSessionSource source)
    {
        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        var tenantA = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        var firstStore = tenantA.DocumentStore;
        var lease = source.Leases[^1];

        await using (var unitOfWork = await firstStore.BeginAsync(DocumentCommitScope.Of(ScopedDocumentKind)))
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                unitOfWork.SaveAsync(
                    Request(ScopedDocumentKind, "canceled-write", "must-not-write", expectedVersion: 0),
                    cancellation.Token));
        }

        await tenantA.DisposeAsync();
        Assert.True(lease.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => tenantA.DocumentStore);
        Assert.Throws<ObjectDisposedException>(() => tenantA.BoundedDocumentStore);

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        await using (var tenantB = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary))
        {
            Assert.NotSame(firstStore, tenantB.DocumentStore);
            Assert.Equal("tenant-b", tenantB.Access.Scope?.Value);
            Assert.Null(await tenantB.DocumentStore.LoadAsync(ScopedDocumentKind, "canceled-write"));
            await SaveAsync(tenantB.DocumentStore, ScopedDocumentKind, "after-cancellation", "tenant-b");
            Assert.Equal(
                "tenant-b",
                await LoadValueAsync(tenantB.DocumentStore, ScopedDocumentKind, "after-cancellation"));
        }

        accessor.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        await using var reopenedTenantA = await factory.CreateAsync(PersistenceAccessPolicy.Ordinary);
        Assert.Null(await reopenedTenantA.DocumentStore.LoadAsync(ScopedDocumentKind, "canceled-write"));
        Assert.Null(await reopenedTenantA.DocumentStore.LoadAsync(ScopedDocumentKind, "after-cancellation"));
    }

    private static Task<DocumentStoreWriteResult> SaveAsync(
        IDocumentStore store,
        string documentKind,
        string id,
        string value) =>
        store.SaveAsync(Request(documentKind, id, value, expectedVersion: 0));

    private static SaveDocumentRequest Request(
        string documentKind,
        string id,
        string value,
        long? expectedVersion) =>
        new(
            documentKind,
            id,
            "1.0.0",
            $$"""{"collection":"{{documentKind}}","value":"{{value}}"}""",
            expectedVersion);

    private static async Task<string?> LoadValueAsync(IDocumentStore store, string documentKind, string id)
    {
        var document = await store.LoadAsync(documentKind, id);
        if (document is null)
            return null;

        return ReadValue(document.ContentJson);
    }

    private static string? ReadValue(string contentJson)
    {
        using var json = System.Text.Json.JsonDocument.Parse(contentJson);
        return json.RootElement.GetProperty("value").GetString();
    }

    private static async Task<string[]> QueryIdsAsync(IDocumentStore store, string documentKind)
    {
#pragma warning disable GW0004
        var result = await store.QueryAsync(new PortableDocumentQuery(documentKind, take: 10));
#pragma warning restore GW0004
        return result.Documents.Select(document => document.Id).Order(StringComparer.Ordinal).ToArray();
    }

    private static GroundworkProviderDriver CreateDriver(string providerKey) => providerKey switch
    {
        "sqlite" => new SqliteGroundworkProviderDriver(),
        "sqlserver" => new SqlServerGroundworkProviderDriver(),
        "postgresql" => new PostgreSqlGroundworkProviderDriver(),
        "mongodb" => new MongoDbGroundworkProviderDriver(),
        _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, "Unknown Groundwork provider.")
    };

    private sealed class MutableAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = current;
    }

    private sealed class ProviderScopeSessionSource(GroundworkProviderDriver driver)
        : IGroundworkStoreSessionSource, IAsyncDisposable
    {
        public List<TrackingClientLease> Leases { get; } = [];

        public async ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            var client = await driver.OpenClientAsync(Manifest, access, cancellationToken);
            var lease = new TrackingClientLease(client);
            Leases.Add(lease);
            return new GroundworkStoreSessionResources(
                client.DocumentStore,
                new ProviderTestBoundedDocumentStore(client.DocumentStore),
                lease);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var lease in Leases)
                await lease.DisposeAsync();
        }

        private static StorageManifest Manifest { get; } = CreateManifest();

#pragma warning disable GW0001, GW0002, GW0003
        private static StorageManifest CreateManifest() => new(
            new StorageManifestIdentity("elsa-storage-scope-contract"),
            new StorageManifestOwner("elsa.tests"),
            new StorageManifestVersion("1.0.0"),
            [
                CreateUnit(ScopedDocumentKind, TenancyPolicy.Scoped),
                CreateUnit(GlobalDocumentKind, TenancyPolicy.Global)
            ],
            new HashSet<string>(),
            []);

        private static StorageUnit CreateUnit(string identity, TenancyPolicy tenancy) => new(
            new StorageUnitIdentity(identity),
            identity,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            tenancy,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [Keyword(ByCollectionIndex, CollectionField)],
            [Query(ListAllQuery, ByCollectionIndex)],
            PhysicalizationPolicy.Portable);

        private static PortableQueryDeclaration Query(string identity, string indexIdentity) => new(
            identity,
            indexIdentity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.None,
            QueryPagingSupport.Offset);

        private static IndexDeclaration Keyword(string identity, string field) => new(
            identity,
            [new IndexField(field)],
            IndexValueKind.Keyword,
            false,
            true,
            MissingValueBehavior.Excluded,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            IndexPhysicalizationPolicy.Optimized);
#pragma warning restore GW0001, GW0002, GW0003
    }

    /// <summary>
    /// Adapts the standalone provider-driver surface to the route-bound query contract. Production
    /// hosts obtain an admitted provider query runtime from their provider initializer instead.
    /// </summary>
    private sealed class ProviderTestBoundedDocumentStore(IDocumentStore documents) : IBoundedDocumentStore
    {
        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ListAllQuery, query.QueryIdentity);
            var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
            Assert.Equal(CollectionField, comparison.Path);
            Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);

#pragma warning disable GW0004
            var result = await documents.QueryAsync(
                new DocumentStoreQuery(
                    query.DocumentKind,
                    ByCollectionIndex,
                    Assert.Single(comparison.Values)!,
                    query.Skip,
                    query.Take),
                cancellationToken);
#pragma warning restore GW0004
            return new DocumentQueryResult(result, result.Count);
        }

        public async Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            (await QueryAsync(query, cancellationToken)).TotalCount;

        public async Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            (await QueryAsync(query.Page(query.Skip, 1), cancellationToken)).Documents.FirstOrDefault();

        public async Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            await FirstOrDefaultAsync(query, cancellationToken) is not null;
    }

    private sealed class TrackingClientLease(GroundworkProviderClient client) : IAsyncDisposable
    {
        private GroundworkProviderClient? _client = client;

        public bool IsDisposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _client, null);
            if (current is null)
                return;
            try
            {
                await current.DisposeAsync();
            }
            finally
            {
                IsDisposed = true;
            }
        }
    }

}
