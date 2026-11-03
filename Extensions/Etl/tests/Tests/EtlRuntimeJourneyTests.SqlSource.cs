using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1198: SQL snapshot reads through the real worker, store and child worker processes, on the SQLite test engine.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    // Four rows over two partitions: Ada, Bram and Cleo are accepted; the NULL name is rejected to the error route.
    private const string SqlSourceRows = "INSERT INTO people VALUES (1, 'Ada'), (2, NULL), (3, 'Bram'), (4, 'Cleo')";

    [Fact]
    public async Task A_sql_source_reads_its_table_once_and_runs_it_across_two_partitions()
    {
        await using var files = new RuntimeFiles("");
        var source = CreateSqlSourceTable(files);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source)));
        var invocation = ConnectorInvocation(files.Scope, "sql-source", SqlSourcePipeline());

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal((EtlRunStatus.BusinessError, "etl.rows-rejected"), (run.Status, run.TerminalResult!.Diagnostic!.Code));
        Assert.Equal((3, 1, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.OutstandingRecords, run.OutstandingBytes));
        Assert.Equal(["Ada", "Bram", "Cleo"], ReadJsonlNames(files.OutputRoot));
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorPartitions
            .CountAsync());
        var retained = Assert.IsType<EtlConnectorRunView>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(files.Scope, invocation.RunId));
        Assert.Equal(EtlConnectorKind.Sql, retained.Bindings.Source.Kind);
        Assert.Null(retained.Bindings.Source.Revision.ExpectedSha256);
        Assert.Equal(4, retained.Prepared!.File!.RecordCount);
    }

    [Fact]
    public async Task A_sql_source_without_its_engine_package_is_refused_at_admission()
    {
        await using var files = new RuntimeFiles("");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, "Data Source=unused.db")));

        await files.AdmitAsync(host, ConnectorInvocation(files.Scope, "sql-source-unavailable", SqlSourcePipeline()), EtlStoreStatus.Replay);
        var run = await files.WaitForTerminalAsync(host, "sql-source-unavailable");

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal("etl.sql-unavailable", run.TerminalResult?.Diagnostic?.Code);
    }

    [Fact]
    public async Task A_worker_killed_after_publishing_its_snapshot_is_replaced_and_the_replacement_reuses_that_snapshot()
    {
        await using var files = new RuntimeFiles("");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var source = CreateSqlSourceTable(files);
        EtlHostSqlBindingOptions[] sql = [SqlSourceBinding(files.Scope, source)];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.AddRange(sql));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var invocation = ConnectorInvocation(files.Scope, "sql-source-process", SqlSourcePipeline());
        await files.AdmitAsync(host, invocation);

        // The predecessor reads and publishes the snapshot, then is killed before the store records the preparation.
        var predecessor = await files.StartWorkerAsync("sql-source-predecessor", deadline.Token,
            connectorGate: EtlConnectorWorkerGate.BeforePreparationCommit, sqlBindings: sql);
        var checkpoint = await predecessor.WaitForConnectorCheckpointAsync(deadline.Token);
        Assert.Equal((EtlConnectorWorkerGate.BeforePreparationCommit, invocation.RunId), (checkpoint.Gate, checkpoint.Lease.RunId));
        var snapshot = Assert.Single(Directory.GetFiles(files.SnapshotRoot, "*.jsonl"));
        var published = await File.ReadAllTextAsync(snapshot, deadline.Token);
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);

        // The table changes before the replacement prepares; a second read would see a different point in time.
        ExecuteSql(source, "UPDATE people SET name = 'changed' WHERE id = 1; INSERT INTO people VALUES (5, 'Dirk')");
        _ = await files.StartWorkerAsync("sql-source-survivor", deadline.Token, sqlBindings: sql);
        var run = await files.WaitForTerminalAsync(host.Services, invocation.RunId);

        Assert.Equal((EtlRunStatus.BusinessError, 3L, 1L), (run.Status, run.AcceptedRecords, run.RejectedRecords));
        Assert.Equal(["Ada", "Bram", "Cleo"], ReadJsonlNames(files.OutputRoot));
        // The recorded preparation is the predecessor's snapshot, byte for byte.
        await using var scope = host.Services.CreateAsyncScope();
        var retained = Assert.IsType<EtlConnectorRunView>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(Path.GetFileName(snapshot), retained.Prepared!.File!.Source.Key);
        Assert.Equal(published, await File.ReadAllTextAsync(snapshot, deadline.Token));
    }

    private static string CreateSqlSourceTable(RuntimeFiles files)
    {
        var source = $"Data Source={Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "source.db")};Pooling=False";
        ExecuteSql(source, "CREATE TABLE people (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL)");
        ExecuteSql(source, SqlSourceRows);
        return source;
    }

    private static EtlPipelineV2 SqlSourcePipeline()
    {
        var baseline = EtlV2TestPipelines.Create();
        return baseline with
        {
            Source = new EtlSqlSource("people-db", "people",
                [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, true)], ["id"]),
            Lookup = null,
            Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name")],
            Filters = [],
            Limits = baseline.Limits with { FilePartitionCount = 2, MaxBatchRecords = 1 }
        };
    }

    private static EtlHostSqlBindingOptions SqlSourceBinding(EtlScope scope, string connectionString) => new()
    {
        TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "people-db", Role = "Source",
        Revision = "v1", Provider = "sqlite", ConnectionString = connectionString, AllowedTables = ["people"]
    };
}
