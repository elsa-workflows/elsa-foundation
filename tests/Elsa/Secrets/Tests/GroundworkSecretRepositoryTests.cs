using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class GroundworkSecretRepositoryTests
{
    private const string TenantId = "tenant-a";
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnfilteredListUsesTheDeclaredBoundedQueryIdentityAndPath()
    {
        var documents = new InMemoryDocumentStore(SecretsStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var repository = new GroundworkSecretRepository(documents, queries);

        Assert.Empty((await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(skip: 10, take: 25))).Items);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(SecretsStorageManifest.SecretDocumentKind, query.DocumentKind);
        Assert.Equal(SecretsStorageManifest.ListUnfilteredQuery, query.QueryIdentity);
        Assert.Equal(10, query.Skip);
        Assert.Equal(25, query.Take);
        Assert.Collection(
            query.Order,
            order => Assert.Equal(SecretsStorageManifest.NormalizedNameField, order.Path));
        Assert.Equal(2, query.Clauses.Count);
        var comparison = Assert.Single(query.Clauses[1].Comparisons);
        Assert.Equal(SecretsStorageManifest.StatusField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.NotEqual, comparison.Operator);
        Assert.Null(Assert.Single(comparison.Values));
    }

    [Fact]
    public async Task StatusFilteredListUsesTheScaleBearingRoute()
    {
        var documents = new InMemoryDocumentStore(SecretsStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var repository = new GroundworkSecretRepository(documents, queries);

        await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(status: SecretStatus.Active, take: 25));

        var query = Assert.Single(queries.Observed);
        Assert.Equal(SecretsStorageManifest.ListFilteredQuery, query.QueryIdentity);
        var comparison = Assert.Single(query.Clauses[1].Comparisons);
        Assert.Equal(SecretsStorageManifest.StatusField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal("active", Assert.Single(comparison.Values));
    }

    [Fact]
    public async Task RoundTrips_And_Lists_Secrets_By_Collection_Index()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        Assert.True(await repository.TryAddAsync(secret));
        Assert.False(await repository.TryAddAsync(secret));

        var found = await repository.FindAsync(TenantId, "payments.api");
        var all = (await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest())).Items;

        Assert.NotNull(found);
        Assert.Equal("payments.api", found!.Name);
        Assert.Equal("v1", found.LatestActiveVersion!.Payload.Value);
        Assert.Equal("payments.api", Assert.Single(all).Name);
    }

    [Fact]
    public async Task List_Includes_Deleted_Tombstones()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        await repository.SaveAsync(secret);
        secret.Status = SecretStatus.Deleted;
        await repository.SaveAsync(secret);

        Assert.Equal(
            SecretStatus.Deleted,
            Assert.Single((await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest())).Items).Status);
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync(TenantId, "payments.api"))!.Status);
    }

    [Fact]
    public async Task TryAdd_UsesCreateOnlySaveWithoutPreload()
    {
        var documents = new InMemoryDocumentStore(SecretsStorageManifest.Create());
        var repository = new GroundworkSecretRepository(documents);
        var secret = Secret("payments.api", "v1");

        Assert.True(await repository.TryAddAsync(secret));
        Assert.False(await repository.TryAddAsync(Secret("payments.api", "v2")));

        Assert.Equal(0, documents.LoadCount);
        Assert.Equal("v1", (await repository.FindAsync(TenantId, "payments.api"))!.LatestActiveVersion!.Payload.Value);
    }

    [Fact]
    public async Task Create_only_identity_does_not_allow_a_tenant_scope_to_bypass_an_existing_name()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var first = Secret("payments.api", "tenant-a");
        first.Scope = "tenant-a";
        var second = Secret("payments.api", "tenant-b");
        second.Scope = "tenant-b";

        Assert.True(await repository.TryAddAsync(first));
        Assert.False(await repository.TryAddAsync(second));

        var secret = await repository.FindAsync(TenantId, "payments.api");
        Assert.Equal("tenant-a", secret!.Scope);
        Assert.Equal("tenant-a", secret.LatestActiveVersion!.Payload.Value);
    }

    [Fact]
    public async Task Revision_and_list_behavior_survive_a_repository_reopen_over_the_same_document_store()
    {
        var documents = new InMemoryDocumentStore(SecretsStorageManifest.Create());
        var firstRepository = new GroundworkSecretRepository(documents);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(firstRepository);
        var secret = Secret("payments.api", "v1");

        var created = await revisionAware.SaveWithRevisionAsync(secret, expectedRevision: null);
        var current = await revisionAware.FindWithRevisionAsync(TenantId, secret.Name);
        Assert.NotNull(current);
        Assert.Equal(SecretRevisionSaveStatus.Saved, created.Status);

        var reopenedRepository = new GroundworkSecretRepository(documents);
        var reopenedRevisionAware = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(reopenedRepository);
        current!.Secret.DisplayName = "Reopened secret";
        var saved = await reopenedRevisionAware.SaveWithRevisionAsync(current.Secret, current.Revision);

        Assert.Equal(SecretRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal("Reopened secret", (await reopenedRepository.FindAsync(TenantId, secret.Name))!.DisplayName);
        Assert.Equal(secret.Name, Assert.Single((await reopenedRepository.ListPageAsync(TenantId, new SecretRepositoryListRequest())).Items).Name);
    }

    [Fact]
    public async Task SaveWithRevision_RejectsStaleRevision()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);

        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisionAware.SaveWithRevisionAsync(Secret("payments.api", "v1"), null)).Status);
        var first = await revisionAware.FindWithRevisionAsync(TenantId, "payments.api");
        var second = await revisionAware.FindWithRevisionAsync(TenantId, "payments.api");

        first!.Secret.DisplayName = "updated";
        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisionAware.SaveWithRevisionAsync(first.Secret, first.Revision)).Status);

        second!.Secret.DisplayName = "stale";
        var stale = await revisionAware.SaveWithRevisionAsync(second.Secret, second.Revision);

        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal("updated", (await repository.FindAsync(TenantId, "payments.api"))!.DisplayName);
    }

    [Fact]
    public async Task SaveWithRevision_NullExpectedRevisionIsCreateOnly()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);

        var created = await revisionAware.SaveWithRevisionAsync(Secret("payments.api", "v1"), null);
        var duplicate = await revisionAware.SaveWithRevisionAsync(Secret("payments.api", "v2"), null);

        Assert.Equal(SecretRevisionSaveStatus.Saved, created.Status);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, duplicate.Status);
        Assert.Equal("v1", (await repository.FindAsync(TenantId, "payments.api"))!.LatestActiveVersion!.Payload.Value);
    }

    [Fact]
    public async Task ListPage_ReturnsDeterministicBoundedWindowAndTotalCount()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        await repository.SaveAsync(Secret("a", "v1"));
        await repository.SaveAsync(Secret("b", "v1"));
        await repository.SaveAsync(Secret("c", "v1"));

        var page = await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(skip: 1, take: 1));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("b", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task ListPage_UsesOrdinaryServerBoundedRouteForSubstringSearch()
    {
        var documents = new InMemoryDocumentStore(SecretsStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var repository = new GroundworkSecretRepository(documents, queries);

        await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(search: "payments", take: 25));

        Assert.Equal(
            SecretsStorageManifest.SearchFilteredQuery,
            Assert.Single(queries.Observed).QueryIdentity);
    }

    [Fact]
    public async Task Save_RecomputesActiveVersionProjectionAfterEveryTransition()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");
        var request = new SecretRepositoryListRequest(activeOnly: true, now: Now);

        await repository.SaveAsync(secret);
        Assert.Single((await repository.ListPageAsync(TenantId, request)).Items);

        Assert.Single(secret.Versions).ExpiresAt = Now;
        await repository.SaveAsync(secret);
        Assert.Empty((await repository.ListPageAsync(TenantId, request)).Items);

        Assert.Single(secret.Versions).ExpiresAt = null;
        await repository.SaveAsync(secret);
        Assert.Single((await repository.ListPageAsync(TenantId, request)).Items);

        Assert.Single(secret.Versions).Status = SecretStatus.Revoked;
        await repository.SaveAsync(secret);
        Assert.Empty((await repository.ListPageAsync(TenantId, request)).Items);
    }

    [Fact]
    public void Manifest_declares_a_scale_bearing_filter_route_over_a_physical_entity_table()
    {
        var unit = Assert.Single(SecretsStorageManifest.Create().StorageUnits);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy);
        Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, policy.Definition.Form);
        Assert.Equal(SecretsStorageManifest.SecretsTable, policy.Definition.FeatureDefaultLogicalName);

        var query = Assert.Single(
            storage.BoundedQueries,
            candidate => candidate.Identity == SecretsStorageManifest.ListFilteredQuery);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, query.ExecutionClass);
        Assert.Equal(SecretsStorageManifest.SecretFilteredListIndex, query.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Offset, query.PagingSupport);
        Assert.True(query.SupportsDisjunction);
        Assert.True(query.SupportsTotalCount);
        Assert.Contains(
            query.PredicateFields,
            field =>
                field.Path == SecretsStorageManifest.TenantIdField &&
                field.Operations.SetEquals([PortableQueryOperation.Equal]));
        Assert.Contains(
            query.PredicateFields,
            field =>
                field.Path == SecretsStorageManifest.StatusField &&
                field.Operations.SetEquals([PortableQueryOperation.Equal]));
        var filteredPhysicalIndex = Assert.Single(
            policy.Definition.Indexes,
            index => index.LogicalName == SecretsStorageManifest.SecretFilteredListIndex);
        Assert.True(storage.LogicalIndexes.Single(index =>
            index.Identity == SecretsStorageManifest.SecretFilteredListIndex).IsUnique);
        Assert.True(filteredPhysicalIndex.IsUnique);
        Assert.Collection(
            filteredPhysicalIndex.Columns,
            scope => Assert.Equal(new DocumentEnvelopeDefinition().StorageScopeColumn, scope.ColumnLogicalName),
            tenant => Assert.Equal(SecretsStorageManifest.TenantIdField, tenant.ColumnLogicalName),
            status => Assert.Equal(SecretsStorageManifest.StatusField, status.ColumnLogicalName),
            name => Assert.Equal(SecretsStorageManifest.NormalizedNameField, name.ColumnLogicalName));
        Assert.Equal(
            SecretNameConstraints.MaximumLength,
            policy.Definition.ProjectedColumns.Single(column =>
                column.Path == SecretsStorageManifest.NormalizedNameField).Length);

        var unfiltered = Assert.Single(
            storage.BoundedQueries,
            candidate => candidate.Identity == SecretsStorageManifest.ListUnfilteredQuery);
        Assert.Equal(BoundedQueryExecutionClass.Ordinary, unfiltered.ExecutionClass);
        Assert.Equal(SecretsStorageManifest.SecretByNameIndex, unfiltered.IndexIdentity);
        Assert.Contains(
            unfiltered.ResidualPredicateFields,
            field =>
                field.Path == SecretsStorageManifest.StatusField &&
                field.IsRequired &&
                field.Operations.Contains(PortableQueryOperation.NotEqual));

        var search = Assert.Single(
            storage.BoundedQueries,
            candidate => candidate.Identity == SecretsStorageManifest.SearchFilteredQuery);
        Assert.Equal(BoundedQueryExecutionClass.Ordinary, search.ExecutionClass);
        Assert.Equal(
            SecretsStorageManifest.TenantIdField,
            Assert.Single(search.PredicateFields).Path);
        Assert.Contains(
            search.ResidualPredicateFields,
            field =>
                field.Path == SecretsStorageManifest.NameSearchKeyField &&
                field.Operations.Contains(PortableQueryOperation.Contains));
        Assert.Contains(
            search.ResidualPredicateFields,
            field =>
                field.Path == SecretsStorageManifest.DisplayNameSearchKeyField &&
                field.Operations.Contains(PortableQueryOperation.Contains));
    }

    private static Secret Secret(string name, string value) => new()
    {
        TenantId = TenantId,
        Name = name,
        DisplayName = name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };

    private sealed class RecordingBoundedDocumentStore : IBoundedDocumentStore
    {
        public List<DocumentQuery> Observed { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Observed.Add(query);
            return Task.FromResult(DocumentQueryResult.Empty);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
