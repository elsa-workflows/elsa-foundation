using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

/// <summary>
/// Proves Groundwork's <see cref="DocumentStoreAtomicWriteExtensions.SaveAllAsync"/> enforces all-or-nothing across documents through the
/// Groundwork document unit of work: a clean batch commits every document, and a batch whose later write returns
/// a non-success status aborts before commit so nothing is persisted (the earlier write is rolled back).
/// </summary>
public class GroundworkDocumentStoreAtomicWriteExtensionsTests
{
    private const string Kind = "thing";
    private const string SchemaVersion = "1.0.0";

    [Fact]
    public async Task Commits_every_document_when_all_writes_succeed()
    {
        var store = new InMemoryDocumentStore(BuildManifest());

        await store.SaveAllAsync(
            DocumentCommitScope.Of(Kind),
            [Save("a"), Save("b")]);

        Assert.NotNull(await store.LoadAsync(Kind, "a"));
        Assert.NotNull(await store.LoadAsync(Kind, "b"));
    }

    [Fact]
    public async Task Rolls_back_all_documents_when_a_later_write_fails()
    {
        var store = new InMemoryDocumentStore(BuildManifest());

        // The second write demands version 99 on a document that does not exist -> NotFound (a non-zero
        // expectation can never match an absent document, per the published ExpectedVersion matrix that all
        // real providers implement). Any non-success status aborts the batch before commit.
        var conflicting = new SaveDocumentRequest(Kind, "b", SchemaVersion, "{}", ExpectedVersion: 99);

        var ex = await Assert.ThrowsAsync<DocumentAtomicWriteException>(() =>
            store.SaveAllAsync(DocumentCommitScope.Of(Kind), [Save("a"), conflicting]));

        Assert.Equal(DocumentStoreWriteStatus.NotFound, ex.Status);
        Assert.Equal("b", ex.Id);

        // The first write must not have been committed.
        Assert.Null(await store.LoadAsync(Kind, "a"));
        Assert.Null(await store.LoadAsync(Kind, "b"));
    }

    private static SaveDocumentRequest Save(string id) => new(Kind, id, SchemaVersion, "{}");

    private static StorageManifest BuildManifest() => new(
        new StorageManifestIdentity("elsa-groundwork-atomic-write-tests"),
        new StorageManifestOwner("elsa.persistence.groundwork.querying.tests"),
        new StorageManifestVersion(SchemaVersion),
        [
            new StorageUnit(
                new StorageUnitIdentity(Kind),
                "Thing",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.None,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                [],
                [],
                PhysicalizationPolicy.Portable)
        ],
        new HashSet<string>(),
        []);
}
