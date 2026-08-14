using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// The one place these benchmarks open their durable SQLite substrate. Every diagnostic and benchmark opens the
/// same runtime manifest and reads back the same checkpoint-commit ledger, so both live here: the physical open
/// (which also yields the route-bound query runtime a production host obtains from its provider initializer) and
/// the ledger read-back through that unit's declared bounded route.
/// </summary>
internal static class GroundworkBenchmarkStore
{
    private static readonly ProviderIdentity Provider = new("groundwork-sqlite", "1.0.0");

    /// <summary>
    /// The ledger's declared list-all route is offset-paged and claims no total-count support, so the read-back
    /// walks pages rather than asking for a count. The page is far larger than any single benchmark run produces.
    /// </summary>
    private const int LedgerPageSize = 4096;

    public static Task<GroundworkPhysicalTestStore<SqlitePhysicalDocumentStore>> OpenAsync(string databasePath) =>
        GroundworkPhysicalTestStores.OpenSqliteAsync(
            $"Data Source={databasePath}",
            ElsaRuntimeStorageManifest.CreatePhysicalized(),
            Provider,
            GroundworkTestAccess.DefaultScoped);

    /// <summary>Counts persisted checkpoint-commit markers through the ledger's declared bounded route.</summary>
    public static async Task<long> CountCheckpointCommitsAsync(IBoundedDocumentStore queries) =>
        (await ListCheckpointCommitsAsync(queries)).Count;

    /// <summary>Reads every persisted checkpoint-commit marker through the ledger's declared bounded route.</summary>
    public static async Task<IReadOnlyList<DocumentEnvelope>> ListCheckpointCommitsAsync(IBoundedDocumentStore queries)
    {
        var all = new List<DocumentEnvelope>();
        while (true)
        {
            var page = await queries.QueryAsync(new DocumentQuery(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                ElsaRuntimeStorageManifest.ListCheckpointCommitsQuery,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.CheckpointCommitCollection))
                ],
                skip: all.Count,
                take: LedgerPageSize));
            all.AddRange(page.Documents);
            if (page.Documents.Count < LedgerPageSize)
                return all;
        }
    }
}
