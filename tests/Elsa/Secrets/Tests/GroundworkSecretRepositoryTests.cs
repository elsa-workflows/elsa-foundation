using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class GroundworkSecretRepositoryTests
{
    [Fact]
    public async Task ListUsesTheDeclaredBoundedQueryIdentityAndPath()
    {
        var documents = new InMemoryDocumentStore(SecretsStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var repository = new GroundworkSecretRepository(documents, queries);

        Assert.Empty((await repository.ListPageAsync(new SecretRepositoryListRequest(10, 25))).Items);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(SecretsStorageManifest.SecretDocumentKind, query.DocumentKind);
        Assert.Equal(SecretsStorageManifest.ListAllQuery, query.QueryIdentity);
        Assert.Equal(10, query.Skip);
        Assert.Equal(25, query.Take);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(SecretsStorageManifest.CollectionField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(SecretsStorageManifest.SecretCollection, Assert.Single(comparison.Values));
    }

    [Fact]
    public async Task RoundTrips_And_Lists_Secrets_By_Collection_Index()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        Assert.True(await repository.TryAddAsync(secret));
        Assert.False(await repository.TryAddAsync(secret));

        var found = await repository.FindAsync("payments.api");
        var all = await repository.ListAsync();

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

        Assert.Equal(SecretStatus.Deleted, Assert.Single(await repository.ListAsync()).Status);
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync("payments.api"))!.Status);
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
        Assert.Equal("v1", (await repository.FindAsync("payments.api"))!.LatestActiveVersion!.Payload.Value);
    }

    [Fact]
    public async Task SaveWithRevision_RejectsStaleRevision()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);

        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisionAware.SaveWithRevisionAsync(Secret("payments.api", "v1"), null)).Status);
        var first = await revisionAware.FindWithRevisionAsync("payments.api");
        var second = await revisionAware.FindWithRevisionAsync("payments.api");

        first!.Secret.DisplayName = "updated";
        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisionAware.SaveWithRevisionAsync(first.Secret, first.Revision)).Status);

        second!.Secret.DisplayName = "stale";
        var stale = await revisionAware.SaveWithRevisionAsync(second.Secret, second.Revision);

        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal("updated", (await repository.FindAsync("payments.api"))!.DisplayName);
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
        Assert.Equal("v1", (await repository.FindAsync("payments.api"))!.LatestActiveVersion!.Payload.Value);
    }

    [Fact]
    public async Task ListPage_ReturnsDeterministicBoundedWindowAndTotalCount()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        await repository.SaveAsync(Secret("a", "v1"));
        await repository.SaveAsync(Secret("b", "v1"));
        await repository.SaveAsync(Secret("c", "v1"));

        var page = await repository.ListPageAsync(new SecretRepositoryListRequest(1, 1));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("b", Assert.Single(page.Items).Name);
    }

    private static Secret Secret(string name, string value) => new()
    {
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
