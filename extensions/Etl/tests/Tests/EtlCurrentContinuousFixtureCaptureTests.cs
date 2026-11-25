#if ETL_V1_FIXTURE_CAPTURE
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task CaptureCurrentWriterContinuousHistoryFixture()
    {
        var destination = Environment.GetEnvironmentVariable("ETL_S9_CONTINUOUS_CAPTURE_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(destination));
        Assert.False(Directory.Exists(destination));
        var source = await CurrentEtlCaptureSourceAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var files = new RuntimeFiles(CsvJourneySource, new("tenant-fixture", "compaction-1192"));
        var feed = new CursorFeed();
        var deliveries = new ConcurrentQueue<EtlHttpLoopbackRequest>();
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, cancellation) =>
        {
            if (request.Method != "POST") return feed.RespondAsync(request, cancellation);
            deliveries.Enqueue(request);
            return ValueTask.FromResult(new EtlHttpLoopbackResponse(204, ""));
        }, fixedPort: Continuous1192FixturePort);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: true, httpErrors: true));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(token);
        var pipeline = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(1) };
        var invocation = ConnectorInvocation(files.Scope, "compaction-1192", pipeline);
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlConnectorRuntimeStore>();
            Assert.Equal(EtlStoreStatus.Applied, (await services.GetRequiredService<EtlExecution>().AdmitAsync(invocation, token)).Status);
            var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(files.Scope, "s9-continuous-preparer", token));
            await services.GetRequiredService<EtlConnectorExecution>().PrepareAsync(preparation, token);
            var view = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(files.Scope, invocation.RunId, token));
            Assert.Equal(EtlRunStatus.Running, view.Run.Status);
            Assert.NotNull(view.Prepared);
            var admission = new EtlConnectorRunAdmission(view.Run.Invocation, view.Bindings);
            // Claim both peers before any history exists. Carry the actual returned leases;
            // another claim would legitimately compact the history this fixture must retain.
            var leases = new EtlConnectorPartitionLease[2];
            for (var index = 0; index < leases.Length; index++)
            {
                var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(files.Scope, invocation.RunId, "s9-history-capture", token));
                leases[lease.PartitionOrdinal] = lease;
            }
            var http = services.GetRequiredService<HostHttpBindings>();
            var transport = services.GetRequiredService<EtlHttpTransport>();
            var acquirer = new EtlHttpSourceAcquirer(store, http, transport, files.SnapshotRoot, TimeProvider.System);
            var output = new EtlConnectorOutputProcessor(store, services.GetRequiredService<HostFileBindings>(), http,
                services.GetRequiredService<IFileChunkPublisherFactory>(), transport, files.SnapshotRoot);
            feed.Append("a", 1, "Ada");
            leases[0] = await PageAsync(leases[0]);
            feed.Append("a", 2, "Bram"); feed.Append("b", 1, "Cleo");
            leases[0] = await PageAsync(leases[0]); leases[1] = await PageAsync(leases[1]);
            feed.Append("a", 3, "Dirk"); feed.Append("a", 4, "Eva"); feed.Append("b", 2, "Finn");
            leases[0] = await PageAsync(leases[0]); leases[1] = await PageAsync(leases[1]);
            foreach (var lease in leases)
            {
                Assert.Equal(EtlStoreStatus.Applied, await store.RenewConnectorPartitionAsync(lease, token));
                var caughtUp = await acquirer.AcquireAsync(admission, lease, token);
                Assert.Equal(EtlStoreStatus.Applied, caughtUp.Status);
                Assert.Null(caughtUp.FailureCode); Assert.Null(caughtUp.Page); Assert.Null(caughtUp.Lease);
                Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease, token));
            }
            Assert.Equal(7, feed.Requests); Assert.Equal(2, feed.CaughtUpResponses); Assert.Equal(5, deliveries.Count);
            var database = services.GetRequiredService<EtlRuntimeDbContext>();
            database.ChangeTracker.Clear();
            Assert.True(await IsIdleAsync(database, files.Scope, invocation.RunId, 6));
            Assert.Equal(7, await database.ConnectorHttpRequests.CountAsync(token));
            Assert.Equal(5, await database.ConnectorHttpPages.CountAsync(token));
            Assert.Equal(5, await database.ConnectorBatches.CountAsync(token));
            var peers = await database.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToArrayAsync(token);
            Assert.Equal([3L, 2L], peers.Select(row => row.CurrentRequestSequence));
            Assert.All(peers, row => Assert.Equal(0, row.CompactedPageCount));

            async Task<EtlConnectorPartitionLease> PageAsync(EtlConnectorPartitionLease initial)
            {
                Assert.Equal(EtlStoreStatus.Applied, await store.RenewConnectorPartitionAsync(initial, token));
                var acquired = await acquirer.AcquireAsync(admission, initial, token);
                Assert.Null(acquired.FailureCode);
                var lease = Assert.IsType<EtlConnectorPartitionLease>(acquired.Lease);
                Assert.NotNull(acquired.Page);
                await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, view.Prepared!, lease.PartitionOrdinal, token);
                var built = await builder.BuildNextAsync(lease.Position, acquired.Page, token);
                Assert.Null(built.Diagnostic); Assert.NotNull(built.Batch);
                var reserved = await store.ReserveConnectorBatchAsync(lease, built.Batch!.Descriptor, token);
                Assert.Equal(EtlStoreStatus.Applied, reserved.Status); Assert.NotNull(reserved.Reservation);
                lease = lease with { OwedBatch = reserved.Reservation };
                var retained = await output.TransformAndRetainAsync(lease, admission, pipeline, built.Batch, token);
                Assert.Null(retained.FailureCode); Assert.NotNull(retained.Output);
                lease = lease with { OwedBatch = reserved.Reservation! with { Output = retained.Output } };
                var sent = await output.DispatchAsync(lease, admission, pipeline, retained.Output!, token);
                Assert.Null(sent.FailureCode); Assert.True(sent.ReadyToAccept);
                var accepted = await store.AcceptConnectorBatchAsync(lease, built.Batch.Descriptor.BatchId, token);
                Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
                var next = Assert.IsType<EtlConnectorPartitionLease>(accepted.Lease);
                output.DeleteRetainedRouteBodies(retained.Output!);
                if (((EtlConnectorHttpPosition)next.Position).PageSequence > ((EtlConnectorHttpPosition)lease.Position).PageSequence)
                    await acquirer.DeleteLeftPageAsync(admission, lease, token);
                return next;
            }
        }
        await files.StopHostAsync(host);
        Directory.CreateDirectory(destination!);
        await BackupCurrentEtlDatabaseAsync(Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "runtime.db"), Path.Combine(destination!, "runtime.db"));
        EtlV1CompatibilityFixture.CopyDirectory(files.SnapshotRoot, Path.Combine(destination!, "snapshots"));
        File.WriteAllBytes(Path.Combine(destination!, "deliveries.json"), JsonSerializer.SerializeToUtf8Bytes(deliveries, Elsa.Serialization.Core.JsonBaseline.Options));
        Assert.Equal(source, await CurrentEtlCaptureSourceAsync());
        File.WriteAllBytes(Path.Combine(destination!, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, kind = "current-store-continuous-peer-history", sourceSha = source, sourceDirty = false,
            capturedAtUtc = DateTimeOffset.UtcNow, actualWriters = CurrentEtlCaptureWriters(AppContext.BaseDirectory, source),
            fixturePort = Continuous1192FixturePort, expectedRequests = 7, expectedPages = 5, expectedBatches = 5, expectedAcceptedRecords = 6,
            proof = "Current public store APIs and real HTTP acquisition/output under continuing original leases; historical writer and migration evidence remain separate.",
            files = EtlV1CompatibilityFixture.DescribeFiles(destination!)
        }, Elsa.Serialization.Core.JsonBaseline.Options));
    }
}
#endif
