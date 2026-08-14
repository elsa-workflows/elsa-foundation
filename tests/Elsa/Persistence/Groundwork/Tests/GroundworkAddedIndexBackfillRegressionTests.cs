using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
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
// adds an index, the projection for pre-existing documents is backfilled as part of admitting the added
// projection column and index. This test now guards that fixed behavior. See docs/serialization.md.
public sealed class GroundworkAddedIndexBackfillRegressionTests(ITestOutputHelper output)
{
    private const string Kind = "probe";
    private const string StimulusField = "stimulusHash";
    private const string ByStimulus = "by-stimulus";
    private const string ListByStimulus = "list-by-stimulus";

    [Fact]
    public async Task AddedIndex_BackfillsPreexistingDocuments()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-backfill-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            // Phase 1: manifest WITHOUT the by-stimulus index. Write a document carrying a stimulusHash field.
            var (firstStore, _) = await GroundworkPhysicalTestStores.OpenSqliteAsync(
                connectionString, ManifestWithoutIndex(), Provider, DocumentStoreAccess.Global);
            await firstStore.SaveAsync(
                new SaveDocumentRequest(Kind, "doc-1", "1.0.0", """{"stimulusHash":"hash-order","name":"pre-existing"}"""),
                CancellationToken.None);

            // Phase 2: reopen the SAME database with a manifest that ADDS the by-stimulus index. Admitting the
            // added projection column and index on open is what has to carry doc-1 across.
            var (secondStore, queries) = await GroundworkPhysicalTestStores.OpenSqliteAsync(
                connectionString, ManifestWithIndex(), Provider, DocumentStoreAccess.Global);

            // A freshly written document is always visible through the new index (control).
            await secondStore.SaveAsync(
                new SaveDocumentRequest(Kind, "doc-2", "1.0.0", """{"stimulusHash":"hash-order","name":"post-index"}"""),
                CancellationToken.None);

            var results = await queries.QueryAsync(
                new DocumentQuery(
                    Kind,
                    ListByStimulus,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(StimulusField, "hash-order"))],
                    take: 10),
                CancellationToken.None);
            var ids = results.Documents.Select(e => e.Id).OrderBy(x => x).ToArray();

            output.WriteLine($"Documents visible via added index for 'hash-order': [{string.Join(", ", ids)}]");

            // REGRESSION GUARD (Groundwork preview.16, PR #21): adding an index across a manifest version
            // bump now backfills the index's physicalized projection for documents that were written BEFORE
            // the index was declared. So BOTH the pre-existing document (doc-1, written under the no-index
            // manifest) AND the post-index document (doc-2) are visible through the new index — no re-save
            // required. Prior to preview.16 only doc-2 was returned; this test guarded that gap as a probe.
            Assert.Equal(new[] { "doc-1", "doc-2" }, ids);
        }
        finally
        {
            foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static ProviderIdentity Provider => new("groundwork-sqlite", "1.0.0");

    private static StorageManifest ManifestWithoutIndex() => Manifest("1.0.0", indexes: [], queries: []);

    private static StorageManifest ManifestWithIndex() => Manifest(
        "1.1.0",
        indexes: [Keyword(ByStimulus, StimulusField)],
        queries: [Query(ListByStimulus, ByStimulus)]);

    private static StorageManifest Manifest(
        string version,
        SharedDocumentsIndex[] indexes,
        BoundedQueryDeclaration[] queries) => new StorageManifest(
        new StorageManifestIdentity("elsa-probe"),
        new StorageManifestOwner("elsa.probe"),
        new StorageManifestVersion(version),
        [
            StorageUnit.Create(
                new StorageUnitIdentity(Kind),
                "Probe",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.Global,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                SharedDocumentsStorage.Create(Kind, TenancyPolicy.Global, indexes, queries))
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        [])
    {
        SharedDocumentStorages = [SharedDocumentsStorage.Definition]
    };

    private static SharedDocumentsIndex Keyword(string identity, string field) => new(
        new LogicalIndexDeclaration(
            identity,
            [new IndexField(field)],
            IndexValueKind.Keyword,
            isUnique: false,
            missingValueBehavior: MissingValueBehavior.Excluded),
        Projected: true);

    private static BoundedQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);
}
