using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Persistence.Groundwork.Tests;

// Condition 7 probe (W7): when a by-stimulus index is added to an already-populated unit, are documents
// that were written BEFORE the index existed visible to the new index without being re-saved? This tests
// the Groundwork SQLite provider directly with a minimal two-phase manifest so the answer is verified,
// not assumed. The finding is documented in docs/serialization.md.
public sealed class GroundworkAddedIndexVisibilityProbeTests(ITestOutputHelper output)
{
    private const string Kind = "probe";
    private const string StimulusField = "stimulusHash";
    private const string ByStimulus = "by-stimulus";

    [Fact]
    public async Task AddedIndex_PreexistingDocumentVisibility_IsVerified()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-probe-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            // Phase 1: manifest WITHOUT the by-stimulus index. Write a document carrying a stimulusHash field.
            await using (var handle = await SqliteDocumentStoreFactory.CreateAsync(connectionString, ManifestWithoutIndex(), Provider))
            {
                await handle.Store.SaveAsync(
                    new SaveDocumentRequest(Kind, "doc-1", "1.0.0", """{"stimulusHash":"hash-order","name":"pre-existing"}"""),
                    CancellationToken.None);
            }

            // Phase 2: reopen the SAME database with a manifest that ADDS the by-stimulus index, then query it.
            await using (var handle = await SqliteDocumentStoreFactory.CreateAsync(connectionString, ManifestWithIndex(), Provider))
            {
                // A freshly written document is always visible through the new index (control).
                await handle.Store.SaveAsync(
                    new SaveDocumentRequest(Kind, "doc-2", "1.0.0", """{"stimulusHash":"hash-order","name":"post-index"}"""),
                    CancellationToken.None);

                var results = await handle.Store.QueryAsync(new DocumentStoreQuery(Kind, ByStimulus, "hash-order"), CancellationToken.None);
                var ids = results.Select(e => e.Id).OrderBy(x => x).ToArray();

                output.WriteLine($"Documents visible via added index for 'hash-order': [{string.Join(", ", ids)}]");

                // VERIFIED BEHAVIOR (Condition 7): the Groundwork SQLite provider populates an index's
                // physicalized projection only when a document is written. A document written BEFORE the
                // index was declared is NOT retroactively backfilled — even across a manifest version bump —
                // so only the post-index document is visible through the new index. Re-saving the document
                // makes it visible. Documented in docs/serialization.md; the bookmark by-stimulus gap this
                // implies is bounded because bookmarks are short-lived.
                Assert.Equal(new[] { "doc-2" }, ids);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static ProviderIdentity Provider => new("groundwork-sqlite", "1.0.0");

    private static StorageManifest ManifestWithoutIndex() => Manifest("1.0.0", indexes: [], queries: []);

    private static StorageManifest ManifestWithIndex() => Manifest(
        "1.1.0",
        indexes: [Keyword(ByStimulus, StimulusField)],
        queries: [Query("list-by-stimulus", ByStimulus)]);

    private static StorageManifest Manifest(string version, IndexDeclaration[] indexes, PortableQueryDeclaration[] queries) => new(
        new StorageManifestIdentity("elsa-probe"),
        new StorageManifestOwner("elsa.probe"),
        new StorageManifestVersion(version),
        [
            new StorageUnit(
                new StorageUnitIdentity(Kind),
                "Probe",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.None,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                indexes,
                queries,
                PhysicalizationPolicy.Portable)
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

    private static PortableQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);
}
