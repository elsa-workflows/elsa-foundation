using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1190: JSON Lines runs through the real worker, store and child worker processes.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    // Four records over two partitions: Ada and Bram are accepted, skip is filtered, the broken line is rejected.
    private const string JsonlJourneySource =
        "{\"id\":1,\"name\":\"Ada\"}\n\n{\"id\":2,\"name\":\"skip\"}\nnot json\r\n{\"id\":3,\"name\":\"Bram\"}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_worker_runs_a_jsonl_source_with_lookup_and_routes_bad_lines(bool httpMain)
    {
        await using var files = new RuntimeFiles(JsonlJourneySource);
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(204, "")));
        var delivery = new TestResultDelivery(EtlDeliveryDisposition.Consumed);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, resultDelivery: delivery,
            configureRuntime: runtime => ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: true, httpMain, httpErrors: false));
        // One record per batch: the "skip" batch is fully filtered and the broken-line batch carries only an error.
        var invocation = ConnectorInvocation(files.Scope, "jsonl-worker", JsonlJourneyPipeline(httpMain, maxBatchRecords: 1));

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal("etl.rows-rejected", run.TerminalResult!.Diagnostic!.Code);
        Assert.Equal((2, 1, 1, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords, run.OutstandingRecords, run.OutstandingBytes));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(invocation.RunId, (await delivery.WaitForDeliveryAsync(timeout.Token)).RunId);
        var main = await ReadConnectorRouteAsync(endpoint, files.OutputRoot, "/output", httpMain);
        Assert.Equal(["Ada", "Bram"], main.Select(item => item.GetProperty("name").GetString()!).Order().ToArray());
        Assert.Equal(["one", "three"], main.Select(item => item.GetProperty("category").GetString()!).Order().ToArray());
        var error = Assert.Single(await ReadConnectorRouteAsync(endpoint, files.ErrorRoot, "/errors", http: false));
        Assert.Contains("etl.jsonl.record-invalid", error.GetRawText());
        Assert.DoesNotContain("not json", error.GetRawText());
        await using var scope = host.Services.CreateAsyncScope();
        var retained = Assert.IsType<EtlConnectorRunView>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(files.Scope, invocation.RunId));
        Assert.Equal(EtlConnectorKind.Jsonl, retained.Bindings.Source.Kind);
        Assert.Equal(4, retained.Prepared!.File!.RecordCount);
        // The fully filtered "skip" batch and the error-only batch publish no main effect.
        var inspection = Assert.IsType<EtlConnectorRunInspection>(await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeInspector>()
            .FindConnectorInspectionAsync(files.Scope, invocation.RunId));
        Assert.Equal((2, 1), (inspection.AcceptedMainEffects, inspection.AcceptedErrorEffects));
        Assert.EndsWith(".jsonl", retained.Prepared.File.Source.Key);
    }

    [Theory]
    [InlineData("jsonl", "")]
    [InlineData("jsonl", "\n \r\n")]
    [InlineData("csv", "id,name\n")]
    public async Task Zero_record_file_runs_settle_as_success_through_the_worker(string kind, string content)
    {
        await using var files = new RuntimeFiles(content);
        var delivery = new TestResultDelivery(EtlDeliveryDisposition.Consumed);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, resultDelivery: delivery);
        var pipeline = kind == "jsonl"
            ? JsonlJourneyPipeline(httpMain: false)
            : JsonlJourneyPipeline(httpMain: false) with { Source = new EtlCsvSource("source", EtlTestPipelines.Create().SourceSchema) };
        var invocation = ConnectorInvocation(files.Scope, $"zero-{kind}", pipeline);

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal(0, run.AcceptedRecords);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(invocation.RunId, (await delivery.WaitForDeliveryAsync(timeout.Token)).RunId);
    }

    [Theory]
    [InlineData("AfterReservation")]
    [InlineData("BeforeAcceptance")]
    public async Task Two_jsonl_worker_processes_recover_an_owed_partition_without_duplicate_effects(string boundaryName)
    {
        var boundary = Enum.Parse<EtlConnectorWorkerGate>(boundaryName);
        await using var files = new RuntimeFiles(JsonlJourneySource);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var (host, invocation) = await PrepareJsonlProcessRunAsync(files, $"jsonl-process-{boundaryName}", deadline.Token);

        var predecessor = await files.StartWorkerAsync("jsonl-predecessor", deadline.Token, connectorGate: boundary);
        var stopped = await predecessor.WaitForConnectorCheckpointAsync(deadline.Token);
        Assert.Equal(boundary, stopped.Gate);
        Assert.NotEqual(Environment.ProcessId, stopped.ProcessId);
        var before = await ReadJsonlBatchAsync(host.Services, stopped.BatchId!, deadline.Token);
        Assert.False(before.Settled);
        Assert.Equal(boundary == EtlConnectorWorkerGate.BeforeAcceptance, before.OutputJson is not null);
        Assert.Equal(boundary == EtlConnectorWorkerGate.BeforeAcceptance, before.MainFilePublished);
        // Every route the owed batch produced is physically published before the acceptance boundary.
        if (before.OutputJson is not null)
            Assert.Equal(EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(before.OutputJson).Errors is not null, before.ErrorFilePublished);
        var publishedBefore = PublishedMainFiles(files);
        if (boundary == EtlConnectorWorkerGate.BeforeAcceptance)
            AssertPublishedMatchesRetained(files, before);

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        // The predecessor's heartbeat renewed its lease until the kill, so wait on the stored expiry rather than the
        // checkpointed one; once it passes, owed-batch-first claiming hands the same partition to the replacement.
        await WaitForPartitionLeaseExpiryAsync(host.Services, stopped.Lease.PartitionOrdinal, deadline.Token);
        var survivor = await files.StartWorkerAsync("jsonl-survivor", deadline.Token, connectorGate: EtlConnectorWorkerGate.BeforeAcceptance);
        var recovered = await survivor.WaitForConnectorCheckpointAsync(deadline.Token);

        AssertReplacementWorker(stopped, recovered, survivor);
        await using (var operation = host.Services.CreateAsyncScope())
        {
            // The killed owner's lease cannot accept the batch its replacement now owns.
            var stale = await operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
                .AcceptConnectorBatchAsync(stopped.Lease, stopped.BatchId!, deadline.Token);
            Assert.Equal(EtlStoreStatus.LeaseLost, stale.Status);
        }
        Assert.Equal(stopped.BatchId, recovered.BatchId);
        var pending = await ReadJsonlBatchAsync(host.Services, stopped.BatchId!, deadline.Token);
        Assert.Equal(before.DescriptorJson, pending.DescriptorJson);
        Assert.Equal(before.DescriptorDigest, pending.DescriptorDigest);
        if (before.OutputJson is not null)
            Assert.Equal(before.OutputJson, pending.OutputJson);
        AssertPublishedMatchesRetained(files, pending);
        // Republishing the same effect writes the same create-only file; no second physical file appears.
        if (boundary == EtlConnectorWorkerGate.BeforeAcceptance)
            Assert.Equal(publishedBefore, PublishedMainFiles(files));

        await survivor.ReleaseAsync(deadline.Token);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal((2, 1, 1, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords, run.OutstandingRecords, run.OutstandingBytes));
        var main = await ReadConnectorRouteAsync(null!, files.OutputRoot, "/output", http: false);
        Assert.Equal(["Ada", "Bram"], main.Select(item => item.GetProperty("name").GetString()!).Order().ToArray());
    }

    [Fact]
    public async Task Cancelling_a_jsonl_run_with_owed_work_settles_once_and_stops_progress()
    {
        await using var files = new RuntimeFiles(JsonlJourneySource);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation) = await PrepareJsonlProcessRunAsync(files, "jsonl-cancel", deadline.Token);
        var worker = await files.StartWorkerAsync("jsonl-cancelled", deadline.Token, connectorGate: EtlConnectorWorkerGate.AfterReservation);
        var stopped = await worker.WaitForConnectorCheckpointAsync(deadline.Token);

        await using (var operation = host.Services.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            var cancelled = await store.CancelAsync(invocation, deadline.Token);
            var replay = await store.CancelAsync(invocation, deadline.Token);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
            Assert.Equal(EtlTerminalOutcome.Cancelled, cancelled.Result!.Outcome);
            Assert.NotEqual(EtlStoreStatus.Applied, replay.Status);
        }

        // The stopped owner's lease can no longer accept its owed batch, and no partition can be claimed afterwards.
        await worker.CrashAsync();
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var connector = operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
            Assert.Null(await connector.ClaimConnectorPartitionAsync(files.Scope, invocation.RunId, "after-cancel", deadline.Token));
            var refused = await connector.AcceptConnectorBatchAsync(stopped.Lease, stopped.BatchId!, deadline.Token);
            Assert.NotEqual(EtlStoreStatus.Applied, refused.Status);
        }
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.Cancelled, run.Status);
        Assert.Equal(0, run.AcceptedRecords);
        await using var check = host.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
        Assert.Single(await db.Terminals.AsNoTracking().ToListAsync(deadline.Token));
        Assert.Single(await db.Outbox.AsNoTracking().ToListAsync(deadline.Token));
        Assert.Empty(PublishedMainFiles(files));
    }

    private static EtlPipelineV2 JsonlJourneyPipeline(bool httpMain, int maxBatchRecords = 1_000)
    {
        var baseline = EtlV2TestPipelines.Create();
        return baseline with
        {
            Source = new EtlJsonlSource("source", [new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true),
                new EtlJsonField("name", "/name", EtlColumnType.String, Required: true)]),
            Lookup = new("lookup", EtlTestPipelines.Create().LookupSchema, "id", "id"),
            Routes = new(httpMain ? new EtlHttpDestination("output") : new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Mapping = [new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")],
            Filters = [new("name", EtlComparison.NotEqual, JsonSerializer.SerializeToElement("skip"))],
            Limits = baseline.Limits with { FilePartitionCount = 2, MaxBatchRecords = maxBatchRecords }
        };
    }

    private static async Task<(IHost Host, EtlInvocation Invocation)> PrepareJsonlProcessRunAsync(RuntimeFiles files, string runId,
        CancellationToken cancellationToken)
    {
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(cancellationToken);
        var invocation = ConnectorInvocation(files.Scope, runId, JsonlJourneyPipeline(httpMain: false));
        await files.AdmitAsync(host, invocation);
        return (host, invocation);
    }

    private static async Task WaitForPartitionLeaseExpiryAsync(IServiceProvider services, int partitionOrdinal,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var operation = services.CreateAsyncScope();
            var partition = await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorPartitions.AsNoTracking()
                .SingleAsync(row => row.PartitionOrdinal == partitionOrdinal, cancellationToken);
            if (partition.ExpiresAtUtcTicks is not { } expires || expires < DateTimeOffset.UtcNow.UtcTicks - TimeSpan.TicksPerSecond / 2)
                return;
            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task<EtlConnectorBatchRecord> ReadJsonlBatchAsync(IServiceProvider services, string batchId,
        CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        return await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorBatches.AsNoTracking()
            .SingleAsync(row => row.BatchId == batchId, cancellationToken);
    }

    private static string[] PublishedMainFiles(RuntimeFiles files) => Directory.Exists(files.OutputRoot)
        ? Directory.EnumerateFiles(files.OutputRoot, "*.jsonl").Select(path => Path.GetFileName(path)).Order().ToArray()
        : [];

    private static void AssertPublishedMatchesRetained(RuntimeFiles files, EtlConnectorBatchRecord batch)
    {
        if (batch.OutputJson is null)
            return;
        var main = EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(batch.OutputJson).Main;
        if (main is null || !batch.MainFilePublished)
            return;
        var published = Directory.EnumerateFiles(files.OutputRoot, "*.jsonl")
            .Select(File.ReadAllBytes)
            .Count(bytes => Convert.ToHexStringLower(SHA256.HashData(bytes)) == main.Body.ContentHash);
        Assert.Equal(1, published);
    }
}
