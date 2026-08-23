using Groundwork.Kernel;
using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Query.Model;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.Store;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// The one place these benchmarks open their durable SQLite substrate. Every diagnostic and benchmark opens the
/// same runtime units and reads back the same checkpoint-commit ledger, so both live here: the provider open
/// (which also admits the schema a production host admits at startup) and the ledger read-back.
/// </summary>
internal static class GroundworkBenchmarkStore
{
    private static readonly StorageAccess Access = StorageAccess.Scoped(new StorageScope("default"));

    /// <summary>Opens one v2 connection over <paramref name="databasePath"/> with every runtime unit admitted.</summary>
    public static IStorageProviderConnection Open(string databasePath)
    {
        return Admit(new SqliteProviderFactory().Create($"Data Source={databasePath}"));
    }

    private static IStorageProviderConnection Admit(IStorageProviderConnection connection)
    {
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            connection.Schema.Apply(unit);
        return connection;
    }

    /// <summary>Opens one v2 connection over a PostgreSQL server with every runtime unit admitted.</summary>
    public static IStorageProviderConnection OpenPostgreSql(string connectionString) =>
        Admit(new PostgreSqlProviderFactory().Create(connectionString));

    /// <summary>Counts persisted checkpoint-commit markers, provider-side.</summary>
    public static long CountCheckpointCommits(IStorageProviderConnection connection)
    {
        var result = QueryCheckpointCommits(connection, ResultShape.TotalCount.Instance, Paging.Keyset(1));
        return result.TotalCount ?? throw new InvalidOperationException(
            "Groundwork checkpoint-commit count did not return its provider-side total.");
    }

    /// <summary>Reads every persisted checkpoint-commit marker.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ListCheckpointCommits(
        IStorageProviderConnection connection) =>
        QueryCheckpointCommits(connection, ResultShape.Rows.Instance, Paging.Keyset(LedgerPageSize)).Rows;

    private const int LedgerPageSize = 4096;

    private static QueryMaterializedResult QueryCheckpointCommits(
        IStorageProviderConnection connection,
        ResultShape shape,
        Paging paging)
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind);
        var table = new TableId(unit.Name);
        var id = new ColumnRef(table, ElsaRuntimeV2StorageManifest.IdField, QueryType.String, false, 128);
        var collection = new ColumnRef(
            table,
            ElsaRuntimeV2StorageManifest.CollectionField,
            QueryType.String,
            isNullable: true,
            maxLength: 128);
        return connection.OpenSession(unit, Access).Query(new QueryRequest(
            table,
            new Predicate.Equal(
                collection,
                QueryConstant.Of(collection, ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind)),
            [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            paging,
            shape));
    }
}
