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
        var engine = new SqliteEtlSqlEngine();
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(engine),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source)));
        var invocation = ConnectorInvocation(files.Scope, "sql-source", SqlSourcePipeline());

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal((EtlRunStatus.BusinessError, "etl.rows-rejected"), (run.Status, run.TerminalResult!.Diagnostic!.Code));
        Assert.Equal((3, 1, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.OutstandingRecords, run.OutstandingBytes));
        // Both partitions read the one retained snapshot; the table is read once.
        Assert.Equal(1, engine.SnapshotReads);
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
    public async Task A_sql_source_delivers_into_a_keyed_sql_table_with_typed_columns()
    {
        await using var files = new RuntimeFiles("");
        var source = CreateSqlSourceTable(files);
        ExecuteSql(source, "UPDATE people SET name = 'Dee' WHERE id = 2");
        ExecuteSql(source, "CREATE TABLE people_copy (id INTEGER NOT NULL, name TEXT NOT NULL, CONSTRAINT people_copy_key UNIQUE (id))");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime =>
            {
                runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source));
                runtime.SqlBindings.Add(new EtlHostSqlBindingOptions
                {
                    TenantId = files.Scope.TenantId, PersistencePartition = files.Scope.PersistencePartition, Alias = "people-copy",
                    Role = "MainOutput", Revision = "v1", Provider = "sqlite", ConnectionString = source, AllowedTables = ["people_copy"]
                });
            });
        var pipeline = SqlSourcePipeline() with
        {
            Routes = new(new EtlSqlDestination("people-copy", "people_copy", ["id"]), new EtlJsonlDestination("errors"))
        };
        var invocation = ConnectorInvocation(files.Scope, "sql-source-to-sql", pipeline);

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        // The SQL route types its parameters from the source columns: id binds as an integer, name as text.
        Assert.Equal((EtlRunStatus.Succeeded, 4L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["1|Ada|integer|text", "2|Dee|integer|text", "3|Bram|integer|text", "4|Cleo|integer|text"],
            QuerySql(source, "SELECT id, name, typeof(id), typeof(name) FROM people_copy ORDER BY id"));
    }

    [Fact]
    public async Task An_empty_sql_table_settles_as_success_with_no_records()
    {
        await using var files = new RuntimeFiles("");
        var source = CreateSqlSourceTable(files);
        ExecuteSql(source, "DELETE FROM people");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source)));
        var invocation = ConnectorInvocation(files.Scope, "sql-source-empty", SqlSourcePipeline());

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal((EtlRunStatus.Succeeded, 0L, 0L), (run.Status, run.AcceptedRecords, run.RejectedRecords));
    }

    [Fact]
    public async Task A_column_the_declaration_cannot_hold_fails_the_run_before_any_partition_exists()
    {
        await using var files = new RuntimeFiles("");
        var source = CreateSqlSourceTable(files);
        ExecuteSql(source, "ALTER TABLE people ADD COLUMN ratio REAL NULL; UPDATE people SET ratio = 0.5");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source)));
        var baseline = SqlSourcePipeline();
        var pipeline = baseline with
        {
            Source = new EtlSqlSource("people-db", "people",
                [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("ratio", EtlColumnType.Decimal, false)], ["id"]),
            Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("ratio", EtlFieldOrigin.Source, "ratio")]
        };
        var invocation = ConnectorInvocation(files.Scope, "sql-source-float", pipeline);

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(("etl.sql-column-type-unsupported", "ratio"), (run.TerminalResult!.Diagnostic!.Code, run.TerminalResult.Diagnostic.Field));
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorPartitions.CountAsync());
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
        await using (var partitions = host.Services.CreateAsyncScope())
            Assert.Equal(2, await partitions.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorPartitions.CountAsync(deadline.Token));
        // The recorded preparation is the predecessor's snapshot, byte for byte.
        await using var scope = host.Services.CreateAsyncScope();
        var retained = Assert.IsType<EtlConnectorRunView>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(Path.GetFileName(snapshot), retained.Prepared!.File!.Source.Key);
        Assert.Equal(published, await File.ReadAllTextAsync(snapshot, deadline.Token));
    }

    [Fact]
    public async Task A_watermarked_sql_source_loads_only_new_and_changed_rows_across_runs()
    {
        await using var files = new RuntimeFiles("");
        var source = CreateVersionedTable(files);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source)));

        var first = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "watermark-first", WatermarkPipeline()));
        Assert.Equal((EtlRunStatus.Succeeded, 3L, 3L), (first.Status, first.AcceptedRecords, await CheckpointAsync(host, files, "watermark-first")));
        // The checkpoint belongs to the definition, the alias and its pinned source, the table and the column: changing any of
        // them, such as an alias pointed at another database, starts again from every row.
        await using (var lookup = host.Services.CreateAsyncScope())
        {
            var store = lookup.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
            var handle = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(files.Scope, "watermark-first")).Bindings.Source.Revision.Handle;
            EtlConnectorSourceWatermarkKey key = new("orders", "people-db", handle, "people", "version");
            Assert.Equal(3L, await store.FindConnectorSourceWatermarkAsync(files.Scope, key));
            foreach (var other in new[]
                     {
                         key with { DefinitionId = "invoices" }, key with { SourceAlias = "other-db" },
                         key with { SourceHandle = new string('0', 64) }, key with { Table = "people_archive" }, key with { Column = "id" }
                     })
                Assert.Null(await store.FindConnectorSourceWatermarkAsync(files.Scope, other));
        }

        ExecuteSql(source, "UPDATE people SET name = 'Bram II', version = 4 WHERE id = 2; INSERT INTO people VALUES (4, 'Dee', 5)");
        var second = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "watermark-second", WatermarkPipeline()));
        Assert.Equal((EtlRunStatus.Succeeded, 2L, 5L), (second.Status, second.AcceptedRecords, await CheckpointAsync(host, files, "watermark-second")));

        var third = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "watermark-third", WatermarkPipeline()));
        Assert.Equal((EtlRunStatus.Succeeded, 0L, 5L), (third.Status, third.AcceptedRecords, await CheckpointAsync(host, files, "watermark-third")));
        Assert.Equal(["Ada", "Bram", "Bram II", "Cleo", "Dee"], ReadJsonlNames(files.OutputRoot));
    }

    [Fact]
    public async Task A_watermarked_run_that_completes_with_rejected_rows_still_advances_the_checkpoint()
    {
        await using var files = new RuntimeFiles("");
        var source = $"Data Source={Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "source.db")};Pooling=False";
        ExecuteSql(source, "CREATE TABLE people (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL, version INTEGER NOT NULL)");
        ExecuteSql(source, "INSERT INTO people VALUES (1, 'Ada', 1), (2, NULL, 2), (3, 'Cleo', 3)");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source)));

        // Every record was routed, one of them to the error route, so the run completes and the checkpoint advances.
        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "watermark-rejected", WatermarkPipeline()));

        Assert.Equal((EtlRunStatus.BusinessError, "etl.rows-rejected", 1L), (run.Status, run.TerminalResult?.Diagnostic?.Code, run.RejectedRecords));
        Assert.Equal(3L, await CheckpointAsync(host, files, "watermark-rejected"));
    }

    [Fact]
    public async Task A_watermarked_run_with_a_rejected_delivery_leaves_the_checkpoint_so_the_next_run_reads_the_rows_again()
    {
        await using var files = new RuntimeFiles("");
        var source = CreateVersionedTable(files);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime =>
            {
                runtime.SqlBindings.Add(SqlSourceBinding(files.Scope, source));
                runtime.SqlBindings.Add(new EtlHostSqlBindingOptions
                {
                    TenantId = files.Scope.TenantId, PersistencePartition = files.Scope.PersistencePartition, Alias = "copy",
                    Role = "MainOutput", Revision = "v1", Provider = "sqlite", ConnectionString = source, AllowedTables = ["people_copy"]
                });
            });
        var toCopy = WatermarkPipeline() with { Routes = new(new EtlSqlDestination("copy", "people_copy", ["id"]), new EtlJsonlDestination("errors")) };

        // The destination table does not exist yet, so delivery is rejected after the snapshot was read.
        var rejected = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "watermark-rejected", toCopy));
        Assert.Equal((EtlRunStatus.BusinessError, "etl.sql-write-rejected"), (rejected.Status, rejected.TerminalResult?.Diagnostic?.Code));
        Assert.Null(await CheckpointAsync(host, files, "watermark-rejected"));

        ExecuteSql(source, "CREATE TABLE people_copy (id INTEGER NOT NULL, name TEXT NOT NULL, version INTEGER NOT NULL, CONSTRAINT people_copy_key UNIQUE (id))");
        var retried = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "watermark-retried", toCopy));
        Assert.Equal((EtlRunStatus.Succeeded, 3L, 3L), (retried.Status, retried.AcceptedRecords, await CheckpointAsync(host, files, "watermark-retried")));
        Assert.Equal(["1|Ada", "2|Bram", "3|Cleo"], QuerySql(source, "SELECT id, name FROM people_copy ORDER BY id"));
    }

    [Fact]
    public async Task A_cancelled_watermarked_run_leaves_the_checkpoint_so_the_next_run_reads_the_rows_again()
    {
        await using var files = new RuntimeFiles("");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var source = CreateVersionedTable(files);
        EtlHostSqlBindingOptions[] sql = [SqlSourceBinding(files.Scope, source)];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.AddRange(sql));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var invocation = ConnectorInvocation(files.Scope, "watermark-cancelled", WatermarkPipeline());
        await files.AdmitAsync(host, invocation);

        // The run is prepared, with its high watermark recorded, when it is cancelled.
        var worker = await files.StartWorkerAsync("watermark-cancelled", deadline.Token,
            connectorGate: EtlConnectorWorkerGate.AfterReservation, sqlBindings: sql);
        await worker.WaitForConnectorCheckpointAsync(deadline.Token);
        await using (var operation = host.Services.CreateAsyncScope())
            Assert.Equal(EtlStoreStatus.Applied,
                (await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().CancelAsync(invocation, deadline.Token)).Status);
        await worker.CrashAsync();
        Assert.Equal(EtlRunStatus.Cancelled, (await files.WaitForTerminalAsync(host.Services, invocation.RunId)).Status);
        Assert.Null(await CheckpointAsync(host, files, invocation.RunId));

        _ = await files.StartWorkerAsync("watermark-after-cancel", deadline.Token, sqlBindings: sql);
        var next = ConnectorInvocation(files.Scope, "watermark-after-cancel", WatermarkPipeline());
        await files.AdmitAsync(host, next);
        var run = await files.WaitForTerminalAsync(host.Services, next.RunId);
        Assert.Equal((EtlRunStatus.Succeeded, 3L), (run.Status, run.AcceptedRecords));
        Assert.Equal(3L, await CheckpointAsync(host, files, next.RunId));
    }

    [Fact]
    public async Task A_watermarked_run_whose_worker_was_killed_after_publishing_records_the_snapshot_high_watermark()
    {
        await using var files = new RuntimeFiles("");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var source = CreateVersionedTable(files);
        EtlHostSqlBindingOptions[] sql = [SqlSourceBinding(files.Scope, source)];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine()),
            configureRuntime: runtime => runtime.SqlBindings.AddRange(sql));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var invocation = ConnectorInvocation(files.Scope, "watermark-process", WatermarkPipeline());
        await files.AdmitAsync(host, invocation);

        var predecessor = await files.StartWorkerAsync("watermark-predecessor", deadline.Token,
            connectorGate: EtlConnectorWorkerGate.BeforePreparationCommit, sqlBindings: sql);
        await predecessor.WaitForConnectorCheckpointAsync(deadline.Token);
        await predecessor.CrashAsync();
        // A row with a higher watermark arrives after the snapshot; the reused snapshot does not contain it.
        ExecuteSql(source, "INSERT INTO people VALUES (9, 'Late', 9)");
        _ = await files.StartWorkerAsync("watermark-survivor", deadline.Token, sqlBindings: sql);
        var run = await files.WaitForTerminalAsync(host.Services, invocation.RunId);

        Assert.Equal((EtlRunStatus.Succeeded, 3L), (run.Status, run.AcceptedRecords));
        Assert.Equal(3L, await CheckpointAsync(host, files, invocation.RunId));
    }

    private static string CreateVersionedTable(RuntimeFiles files)
    {
        var source = $"Data Source={Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "source.db")};Pooling=False";
        ExecuteSql(source, "CREATE TABLE people (id INTEGER NOT NULL PRIMARY KEY, name TEXT NOT NULL, version INTEGER NOT NULL)");
        ExecuteSql(source, "INSERT INTO people VALUES (1, 'Ada', 1), (2, 'Bram', 2), (3, 'Cleo', 3)");
        return source;
    }

    private static EtlPipelineV2 WatermarkPipeline()
    {
        var baseline = SqlSourcePipeline();
        return baseline with
        {
            Source = new EtlSqlSource("people-db", "people", [new EtlColumn("id", EtlColumnType.Int64, true),
                new EtlColumn("name", EtlColumnType.String, true), new EtlColumn("version", EtlColumnType.Int64, true)], ["id"]) { Watermark = "version" },
            Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name"), new("version", EtlFieldOrigin.Source, "version")]
        };
    }

    /// <summary>The checkpoint as of a run, keyed by that run's pinned source handle.</summary>
    private static async Task<long?> CheckpointAsync(Microsoft.Extensions.Hosting.IHost host, RuntimeFiles files, string runId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(files.Scope, runId));
        return await store.FindConnectorSourceWatermarkAsync(files.Scope,
            new("orders", "people-db", run.Bindings.Source.Revision.Handle, "people", "version"));
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
