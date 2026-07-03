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

// Condition 7 regression (W7 probe → GW-BUMP): when a by-stimulus index is added to an already-populated
// unit, documents written BEFORE the index existed must become visible to the new index WITHOUT being
// re-saved. This drives the Groundwork SQLite provider directly with a minimal two-phase manifest so the
// backfill behavior is verified, not assumed.
//
// History: previously (Groundwork ≤ preview.10) added indexes were NOT backfilled — a document written
// before a new index was declared stayed invisible to that index until its next save. That gap was guarded
// by this test as a probe. Groundwork preview.16 (PR #21) added index-projection backfill: when a manifest
// adds a portable index, RelationalMaterializerBase backfills the projection for pre-existing documents
// inside the materialization transaction. This test now guards that fixed behavior. See docs/serialization.md.
public sealed class GroundworkAddedIndexBackfillRegressionTests(ITestOutputHelper output)
{
    private const string Kind = "probe";
    private const string StimulusField = "stimulusHash";
    private const string ByStimulus = "by-stimulus";

    [Fact]
    public async Task AddedIndex_BackfillsPreexistingDocuments()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-backfill-{Guid.NewGuid():N}.db");
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

                // REGRESSION GUARD (Groundwork preview.16, PR #21): adding an index across a manifest version
                // bump now backfills the index's physicalized projection for documents that were written BEFORE
                // the index was declared. So BOTH the pre-existing document (doc-1, written under the no-index
                // manifest) AND the post-index document (doc-2) are visible through the new index — no re-save
                // required. Prior to preview.16 only doc-2 was returned; this test guarded that gap as a probe.
                // Backfill runs inside the materialization transaction (RelationalMaterializerBase), sharing
                // single-field index semantics with save-time via RelationalIndexValues.TryGetIndexValue.
                Assert.Equal(new[] { "doc-1", "doc-2" }, ids);
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
