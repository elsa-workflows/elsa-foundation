using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1196: a continuous run written by the spec 1192 writer migrates, resumes and compacts.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task A_spec_1192_continuous_run_migrates_keeps_its_rows_and_compacts_its_history()
    {
        var fixture = Path.Combine(EtlV1CompatibilityFixture.FindRepositoryRoot(),
            "Extensions", "Etl", "tests", "Shared", "Fixtures", "Continuous1192", "runtime.db");
        await using var files = new RuntimeFiles(CsvJourneySource, new("tenant-fixture", "compaction-1192"));
        var database = Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "runtime.db");
        File.Copy(fixture, database);
        // The host that ran the run had a snapshot root; idle shards need no files in it, but the worker requires the root.
        Directory.CreateDirectory(files.SnapshotRoot);
        var priorRows = await PriorConnectorRowsAsync(database);

        // Migrate alone first: every claim compacts, so the prior rows can only be compared before a host runs.
        await using (var migrating = new EtlRuntimeSqliteDbContext(new DbContextOptionsBuilder<EtlRuntimeSqliteDbContext>()
            .UseSqlite($"Data Source={database};Pooling=False", sqlite => sqlite
                .MigrationsAssembly(typeof(EtlRuntimeSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EtlRuntimeEfModule.HistoryTableName))
            .Options))
        {
            await EfDatabaseMigrator.ApplyAsync(migrating, EtlRuntimeSqliteDbContext.ExpectedProviderName);
            Assert.False(migrating.Database.HasPendingModelChanges());
        }
        Assert.Equal(priorRows, await PriorConnectorRowsAsync(database));
        Assert.Empty(await ForeignKeyViolationsAsync(database));

        // The feed resumes exactly where the fixture's cursors stand.
        var feed = new CursorFeed();
        foreach (var (id, name) in new[] { (1L, "Ada"), (2L, "Bram"), (3L, "Dirk"), (4L, "Eva") }) feed.Append("a", id, name);
        foreach (var (id, name) in new[] { (1L, "Cleo"), (2L, "Finn") }) feed.Append("b", id, name);
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, token) =>
            request.Method == "POST" ? ValueTask.FromResult(new EtlHttpLoopbackResponse(204, "")) : feed.RespondAsync(request, token),
            fixedPort: Continuous1192FixturePort);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: true, httpErrors: true));
        const string runId = "compaction-1192";
        await WaitForIdleAsync(host.Services, files.Scope, runId, accepted: 6);

        feed.Append("a", 5, "Gus");
        await WaitForIdleAsync(host.Services, files.Scope, runId, accepted: 7);
        feed.Append("b", 3, "Hana");
        await WaitForIdleAsync(host.Services, files.Scope, runId, accepted: 8);

        await using (var operation = host.Services.CreateAsyncScope())
        {
            var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            var partitions = await db.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToListAsync();
            // Each idle shard's last claim compacted below its current request's predecessor: shard a (request 4) keeps
            // page 3 and requests 3..4, shard b (request 3) keeps page 2 and requests 2..3. Lifetime counts keep counting.
            Assert.Equal([(4L, 3L, 4L, 3L, 4L, 5L), (3L, 2L, 3L, 2L, 2L, 3L)], partitions.Select(row =>
                (row.CurrentRequestSequence!.Value, row.CompactedPageCount, row.PageCount, row.CompactedBatchCount,
                    row.CompactedAcceptedRecords, row.AcceptedRecords)).ToArray());
            foreach (var partition in partitions)
            {
                var pages = await db.ConnectorHttpPages.AsNoTracking()
                    .Where(row => row.PartitionOrdinal == partition.PartitionOrdinal).Select(row => row.PageSequence).ToListAsync();
                var requests = await db.ConnectorHttpRequests.AsNoTracking()
                    .Where(row => row.PartitionOrdinal == partition.PartitionOrdinal).OrderBy(row => row.PageSequence).ToListAsync();
                var batches = await db.ConnectorBatches.AsNoTracking()
                    .Where(row => row.PartitionOrdinal == partition.PartitionOrdinal).Select(row => row.HttpPageSequence).ToListAsync();
                Assert.Equal([partition.CompactedPageCount], pages);
                Assert.Equal([partition.CompactedPageCount, partition.CompactedPageCount + 1], requests.Select(row => row.PageSequence));
                Assert.Null(requests[0].PreviousPageSequence);
                Assert.All(batches, page => Assert.Equal(partition.CompactedPageCount, page));
            }
        }

        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, runId));
        var run = await files.WaitForTerminalAsync(host, runId);
        Assert.Equal((EtlRunStatus.Succeeded, 8L), (run.Status, run.AcceptedRecords));
        // Only records appended after the resume are delivered here, each once.
        Assert.Equal(["Gus", "Hana"], (await ReadConnectorRouteAsync(endpoint, files.OutputRoot, "/output", http: true))
            .Select(record => record.GetProperty("name").GetString()!).Order().ToArray());
        Assert.Empty(await ForeignKeyViolationsAsync(database));
    }

    private static async Task<IReadOnlyList<string>> ForeignKeyViolationsAsync(string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync();
        var violations = new List<string>();
        while (await reader.ReadAsync())
            violations.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString())));
        return violations;
    }

    /// <summary>The fixture's settled history, read raw so the comparison does not depend on the current entity model.</summary>
    private static async Task<string> PriorConnectorRowsAsync(string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Current requests and partitions are rewritten by each poll, so only settled history is compared.
        command.CommandText = """
            SELECT 'page', PartitionOrdinal, PageSequence, RequestHash, NextRequestHash, HasContinuation, PageJson, ByteLength, PageDigest, MetadataBytes, AcceptedItemCount
            FROM elsa_etl_connector_http_pages
            UNION ALL
            SELECT 'batch', PartitionOrdinal, BatchOrdinal, BatchId, DescriptorJson, OutputJson, HttpDeliveryJson, RecordCount, OutputBytes, MetadataBytes, Settled
            FROM elsa_etl_connector_batches
            UNION ALL
            SELECT 'request', r.PartitionOrdinal, r.PageSequence, r.RequestHash, r.RequestJson, r.FetchJson, r.PreviousPageSequence, r.PreviousHasContinuation, r.MetadataBytes, r.InputReservationBytes, r.SchemaVersion
            FROM elsa_etl_connector_http_requests r
            JOIN elsa_etl_connector_partitions p ON p.ScopeKey = r.ScopeKey AND p.RunKey = r.RunKey AND p.PartitionOrdinal = r.PartitionOrdinal
            WHERE r.PageSequence < p.CurrentRequestSequence
            ORDER BY 1, 2, 3;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString())));
        return string.Join('\n', rows);
    }
}
