using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1197: keyed SQL delivery through the real worker and store, on the SQLite test engine.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task A_csv_pipeline_upserts_its_accepted_records_into_a_keyed_table_with_one_ledger_row_per_batch()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var target = $"Data Source={Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "warehouse.db")};Pooling=False";
        ExecuteSql(target, "CREATE TABLE people (id INTEGER NOT NULL, name TEXT NOT NULL, category TEXT NULL, CONSTRAINT people_key UNIQUE (id))");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlBinding(files.Scope, target)));

        var first = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "sql-first", SqlPipeline()));
        // A second run over the same input upserts the same keys again: rows are updated in place, never duplicated.
        var second = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "sql-second", SqlPipeline()));

        Assert.Equal((EtlRunStatus.BusinessError, 2L), (first.Status, first.AcceptedRecords));
        Assert.Equal((EtlRunStatus.BusinessError, 2L), (second.Status, second.AcceptedRecords));
        Assert.Equal(["1|Ada|one", "3|Bram|three"], QuerySql(target, "SELECT id, name, category FROM people ORDER BY id"));
        // One batch per accepted record (MaxBatchRecords 1) in each run, each recorded once in the ledger.
        Assert.Equal(["sql-first|2", "sql-second|2"],
            QuerySql(target, "SELECT run_id, COUNT(*) FROM elsa_etl_effect_ledger GROUP BY run_id ORDER BY run_id"));
        // The record whose lookup misses is rejected to the JSON Lines error route once per run.
        Assert.Equal(2, Directory.EnumerateFiles(files.ErrorRoot, "*.jsonl").SelectMany(File.ReadAllLines).Count(line => line.Length > 0));
    }

    [Fact]
    public async Task A_sql_destination_without_its_engine_package_is_refused_at_admission()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlBinding(files.Scope, "Data Source=unused.db")));

        // Admission refuses the run as it records it: the answer is a replay of the refused run.
        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "sql-unavailable", SqlPipeline()), EtlStoreStatus.Replay);

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal("etl.sql-unavailable", run.TerminalResult?.Diagnostic?.Code);
    }

    private static async Task<EtlRunView> AdmitAndWaitAsync(RuntimeFiles files, Microsoft.Extensions.Hosting.IHost host,
        EtlInvocation invocation, EtlStoreStatus expected = EtlStoreStatus.Applied)
    {
        await files.AdmitAsync(host, invocation, expected);
        return await files.WaitForTerminalAsync(host, invocation.RunId);
    }

    private static EtlPipelineV2 SqlPipeline() => CsvRoutePipeline("csv", csvErrors: false) with
    {
        Routes = new(new EtlSqlDestination("warehouse", "people", ["id"]), new EtlJsonlDestination("errors")),
        Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")]
    };

    private static EtlHostSqlBindingOptions SqlBinding(EtlScope scope, string connectionString) => new()
    {
        TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "warehouse", Role = "MainOutput",
        Revision = "v1", Provider = "sqlite", ConnectionString = connectionString, AllowedTables = ["people"]
    };

    private static void ExecuteSql(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string[] QuerySql(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return rows.ToArray();
    }
}
