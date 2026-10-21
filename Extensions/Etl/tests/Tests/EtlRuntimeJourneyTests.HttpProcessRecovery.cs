using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    private const string ProcessHttpPage = "{\"records\":[{\"id\":1,\"name\":\"Ada\"},{\"id\":99,\"name\":\"unknown\"}],\"next\":null}";

    [Theory]
    [InlineData("BeforeCapture")]
    [InlineData("AfterReservation")]
    [InlineData("AfterMainAcknowledgement")]
    [InlineData("AfterErrorAcknowledgement")]
    [InlineData("BeforeAcceptance")]
    public async Task Two_http_worker_processes_recover_each_durable_boundary(string boundaryName)
    {
        var boundary = Enum.Parse<EtlConnectorWorkerGate>(boundaryName);
        await using var files = new RuntimeFiles("id,name\n");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(request.Method == "GET" ? 200 : 204,
                request.Method == "GET" ? ProcessHttpPage : "")));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation, bindings) = await PrepareHttpProcessRunAsync(files, endpoint, deadline.Token);
        var predecessor = await files.StartWorkerAsync("http-predecessor", deadline.Token,
            httpBindings: bindings, connectorGate: boundary);
        var stopped = await predecessor.WaitForConnectorCheckpointAsync(deadline.Token);
        Assert.Equal(boundary, stopped.Gate);
        Assert.Equal(predecessor.ProcessId, stopped.ProcessId);
        Assert.NotEqual(Environment.ProcessId, stopped.ProcessId);
        var before = await ReadHttpProcessStateAsync(host.Services, deadline.Token);
        Assert.Equal(0, before.Run.AcceptedRecords);
        Assert.Equal(0, before.Run.RejectedRecords);
        Assert.False(before.Partition.Completed);
        Assert.Equal(boundary == EtlConnectorWorkerGate.BeforeCapture ? 0 : 2, before.Run.OutstandingRecords);
        Assert.Equal(boundary == EtlConnectorWorkerGate.BeforeCapture, before.Batch is null);
        var expectedPosts = boundary switch
        {
            EtlConnectorWorkerGate.AfterMainAcknowledgement => 1,
            EtlConnectorWorkerGate.AfterErrorAcknowledgement or EtlConnectorWorkerGate.BeforeAcceptance => 2,
            _ => 0
        };
        Assert.Equal(expectedPosts, endpoint.Requests.Count(request => request.Method == "POST"));
        var cached = await ReadProcessPageAsync(host.Services, files, invocation, deadline.Token);
        Assert.Equal(Encoding.UTF8.GetBytes(ProcessHttpPage), cached.RawBody.ToArray());
        if (boundary == EtlConnectorWorkerGate.BeforeCapture)
        {
            Assert.Equal(stopped.Page, cached.Descriptor);
            Assert.Null(before.Page);
            Assert.Null(Assert.IsType<EtlConnectorHttpPosition>(stopped.Lease.Position).Page);
            var fetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(before.Request.FetchJson!);
            Assert.Equal(EtlConnectorHttpSourceFetchPhase.InFlight, fetch.Phase);
            Assert.Null(fetch.Capture); // The manifest exists, but no SQL capture receipt has committed.
            Assert.Equal(cached.Provenance.AttemptId, Assert.Single(fetch.Attempts).Attempt.RequestId);
        }
        else
        {
            Assert.NotNull(before.Page);
            Assert.Equal(before.Batch!.BatchId, stopped.BatchId);
            Assert.False(before.Batch.Settled);
            if (boundary == EtlConnectorWorkerGate.AfterReservation)
            {
                Assert.Null(before.Batch.OutputJson);
                Assert.Null(before.Batch.HttpDeliveryJson);
            }
            if (expectedPosts > 0)
            {
                var acknowledgements = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(before.Batch.HttpDeliveryJson!);
                Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, acknowledgements.Main!.Phase);
                if (expectedPosts == 2)
                    Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, acknowledgements.Errors!.Phase);
                else
                    Assert.Null(acknowledgements.Errors);
            }
        }

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        var survivor = await files.StartWorkerAsync("http-survivor", deadline.Token,
            httpBindings: bindings, connectorGate: EtlConnectorWorkerGate.BeforeAcceptance);
        var recovered = await survivor.WaitForConnectorCheckpointAsync(deadline.Token);
        AssertReplacementWorker(stopped, recovered, survivor);
        var pending = await ReadHttpProcessStateAsync(host.Services, deadline.Token);
        Assert.Equal(2, pending.Run.OutstandingRecords);
        Assert.Equal(0, pending.Run.AcceptedRecords);
        Assert.Equal(0, pending.Run.RejectedRecords);
        Assert.False(pending.Batch!.Settled);
        var recoveredPage = await ReadProcessPageAsync(host.Services, files, invocation, deadline.Token);
        Assert.Equal(cached.Descriptor, recoveredPage.Descriptor);
        Assert.Equal(cached.Provenance, recoveredPage.Provenance);
        if (before.Batch is not null)
        {
            Assert.Equal(before.Batch.DescriptorJson, pending.Batch.DescriptorJson);
            Assert.Equal(before.Batch.DescriptorDigest, pending.Batch.DescriptorDigest);
            Assert.Equal(before.Run.OutstandingBytes, pending.Run.OutstandingBytes);
            if (before.Batch.OutputJson is not null)
            {
                Assert.Equal(before.Batch.OutputJson, pending.Batch.OutputJson);
                Assert.Equal(before.Batch.OutputDigest, pending.Batch.OutputDigest);
            }
        }
        // Acknowledged routes are skipped by the survivor; the page is never fetched a second time.
        Assert.Single(endpoint.Requests, request => request.Method == "GET");
        Assert.Single(endpoint.Requests, request => request.Method == "POST" && request.RawTarget == "/output");
        Assert.Single(endpoint.Requests, request => request.Method == "POST" && request.RawTarget == "/errors");
        AssertRecordedRequestsMatchRetainedOutput(endpoint, pending.Batch);
        await survivor.ReleaseAsync(deadline.Token);
        await AssertHttpProcessTerminalAsync(files, host, invocation, deadline.Token);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_http_worker_processes_replay_a_lost_response_with_stable_body_and_effect(bool deduplicatingReceiver)
    {
        await using var files = new RuntimeFiles("id,name\n");
        var observed = new TaskCompletionSource<EtlHttpLoopbackRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedEffects = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var mainRequests = 0;
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(async (request, token) =>
        {
            if (request.Method == "GET") return new(200, ProcessHttpPage);
            var key = request.Headers["Idempotency-Key"];
            appliedEffects.AddOrUpdate(key, 1, (_, count) => deduplicatingReceiver ? count : count + 1);
            if (request.RawTarget == "/output" && Interlocked.Increment(ref mainRequests) == 1)
            {
                observed.TrySetResult(request);
                // The receiver applied the effect, but the worker never receives this response.
                await releaseResponse.Task.WaitAsync(token);
            }
            return new(204, "");
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation, bindings) = await PrepareHttpProcessRunAsync(files, endpoint, deadline.Token);
        var predecessor = await files.StartWorkerAsync("lost-response-predecessor", deadline.Token, httpBindings: bindings);
        var firstRequest = await observed.Task.WaitAsync(deadline.Token);
        var before = await ReadHttpProcessStateAsync(host.Services, deadline.Token);
        var firstDelivery = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(before.Batch!.HttpDeliveryJson!);
        Assert.Equal(EtlHttpDeliveryPhase.InFlight, firstDelivery.Main!.Phase);
        Assert.Null(firstDelivery.Main.Attempts[0].Response);
        Assert.Equal(2, before.Run.OutstandingRecords);
        Assert.False(before.Batch.Settled);
        AssertRecordedRequestsMatchRetainedOutput(endpoint, before.Batch);
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        releaseResponse.TrySetResult();

        var survivor = await files.StartWorkerAsync("lost-response-survivor", deadline.Token,
            httpBindings: bindings, connectorGate: EtlConnectorWorkerGate.BeforeAcceptance);
        var recovered = await survivor.WaitForConnectorCheckpointAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, recovered.ProcessId);
        Assert.NotEqual(predecessor.ProcessId, recovered.ProcessId);
        Assert.NotEqual(Environment.ProcessId, recovered.ProcessId);
        Assert.True(recovered.Lease.Fence > before.Partition.Fence);
        var pending = await ReadHttpProcessStateAsync(host.Services, deadline.Token);
        Assert.Equal(before.Batch.DescriptorJson, pending.Batch!.DescriptorJson);
        Assert.Equal(before.Batch.OutputJson, pending.Batch.OutputJson);
        Assert.Equal(before.Batch.OutputDigest, pending.Batch.OutputDigest);
        Assert.Equal(before.Run.OutstandingRecords, pending.Run.OutstandingRecords);
        Assert.Equal(before.Run.OutstandingBytes, pending.Run.OutstandingBytes);
        Assert.False(pending.Batch.Settled);
        var attempts = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(pending.Batch.HttpDeliveryJson!).Main!;
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, attempts.Phase);
        Assert.Equal(2, attempts.Attempts.Count);
        Assert.Equal(firstDelivery.Main.Attempts[0].Attempt, attempts.Attempts[0].Attempt);
        Assert.Null(attempts.Attempts[0].Response);
        Assert.NotNull(attempts.Attempts[0].CompletedAt);
        Assert.Equal(204, attempts.Attempts[1].Response!.StatusCode);
        Assert.Equal(attempts.Attempts[0].Attempt.EffectId, attempts.Attempts[1].Attempt.EffectId);
        Assert.NotEqual(attempts.Attempts[0].Attempt.RequestId, attempts.Attempts[1].Attempt.RequestId);
        var requests = endpoint.Requests.Where(request => request.RawTarget == "/output").ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(firstRequest.Body, requests[1].Body);
        Assert.Equal(firstRequest.Headers["Idempotency-Key"], requests[1].Headers["Idempotency-Key"]);
        Assert.Equal(deduplicatingReceiver ? 1 : 2, appliedEffects[firstRequest.Headers["Idempotency-Key"]]);
        Assert.Single(endpoint.Requests, request => request.Method == "GET");
        Assert.Single(endpoint.Requests, request => request.RawTarget == "/errors");
        AssertRecordedRequestsMatchRetainedOutput(endpoint, pending.Batch);
        await survivor.ReleaseAsync(deadline.Token);
        await AssertHttpProcessTerminalAsync(files, host, invocation, deadline.Token);
    }

    private static async Task<(IHost Host, EtlInvocation Invocation, List<EtlHostHttpBindingOptions> Bindings)>
        PrepareHttpProcessRunAsync(RuntimeFiles files, EtlHttpLoopbackServer endpoint, CancellationToken cancellationToken)
    {
        List<EtlHostHttpBindingOptions> bindings =
        [
            HttpBinding(files.Scope, endpoint, "source", "Source", "/page", ["/page"],
                [new() { Id = "page", SeedUri = endpoint.CreateUri("/page").AbsoluteUri }]),
            HttpBinding(files.Scope, endpoint, "output", "MainOutput", "/output"),
            HttpBinding(files.Scope, endpoint, "errors", "ErrorOutput", "/errors")
        ];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
        {
            runtime.HttpBindings = bindings;
            runtime.FileBindings.RemoveAll(binding => bindings.Any(http => http.Role == binding.Role));
        });
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(cancellationToken);
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Lookup = new("lookup", EtlTestPipelines.Create().LookupSchema, "id", "id")
        };
        var invocation = ConnectorInvocation(files.Scope, "http-process-recovery", pipeline);
        await files.AdmitAsync(host, invocation);
        return (host, invocation, bindings);
    }

    private static void AssertReplacementWorker(EtlConnectorWorkerCheckpoint previous,
        EtlConnectorWorkerCheckpoint recovered, EtlWorkerProcess survivor)
    {
        Assert.Equal(EtlConnectorWorkerGate.BeforeAcceptance, recovered.Gate);
        Assert.Equal(survivor.ProcessId, recovered.ProcessId);
        Assert.NotEqual(previous.ProcessId, recovered.ProcessId);
        Assert.NotEqual(Environment.ProcessId, recovered.ProcessId);
        Assert.Equal(previous.Lease.Scope, recovered.Lease.Scope);
        Assert.Equal(previous.Lease.RunId, recovered.Lease.RunId);
        Assert.Equal(previous.Lease.PartitionOrdinal, recovered.Lease.PartitionOrdinal);
        Assert.NotEqual(previous.Lease.Owner, recovered.Lease.Owner);
        Assert.True(recovered.Lease.Fence > previous.Lease.Fence);
    }

    private static async Task<EtlCachedHttpPage> ReadProcessPageAsync(IServiceProvider services, RuntimeFiles files,
        EtlInvocation invocation, CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        var run = Assert.IsType<EtlConnectorRunView>(await operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(files.Scope, invocation.RunId, cancellationToken));
        var cache = new EtlHttpPageCache(files.SnapshotRoot, new(invocation, run.Bindings), 0);
        return Assert.IsType<EtlCachedHttpPage>(await cache.ReadAsync(0, new("page", null), cancellationToken));
    }

    private static async Task<HttpProcessState> ReadHttpProcessStateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
        return new(await db.Runs.AsNoTracking().SingleAsync(cancellationToken),
            await db.ConnectorPartitions.AsNoTracking().SingleAsync(cancellationToken),
            await db.ConnectorBatches.AsNoTracking().SingleOrDefaultAsync(cancellationToken),
            await db.ConnectorHttpPages.AsNoTracking().SingleOrDefaultAsync(cancellationToken),
            await db.ConnectorHttpRequests.AsNoTracking().SingleAsync(cancellationToken));
    }

    private static void AssertRecordedRequestsMatchRetainedOutput(EtlHttpLoopbackServer endpoint, EtlConnectorBatchRecord batch)
    {
        var output = EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(batch.OutputJson!);
        foreach (var request in endpoint.Requests.Where(request => request.Method == "POST"))
        {
            Assert.Contains(request.RawTarget, new[] { "/output", "/errors" });
            var route = request.RawTarget == "/output" ? output.Main! : output.Errors!;
            Assert.Equal(route.EffectId, request.Headers["Idempotency-Key"]);
            Assert.Equal(route.Body.ContentHash, Convert.ToHexStringLower(SHA256.HashData(request.Body)));
            Assert.Equal(route.Body.ByteLength, request.Body.LongLength);
        }
    }

    private static async Task AssertHttpProcessTerminalAsync(RuntimeFiles files, IHost host, EtlInvocation invocation,
        CancellationToken cancellationToken)
    {
        var terminal = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, terminal.Status);
        Assert.Equal(1, terminal.AcceptedRecords);
        Assert.Equal(1, terminal.RejectedRecords);
        Assert.Equal(0, terminal.FilteredRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        var state = await ReadHttpProcessStateAsync(host.Services, cancellationToken);
        Assert.True(state.Batch!.Settled);
        Assert.True(state.Partition.Completed);
        Assert.Equal(1, state.Partition.AcceptedBatchCount);
        Assert.Equal(2, state.Page!.AcceptedItemCount);
        var outbox = Assert.IsType<EtlRuntimeOutboxRecord>(await files.ReadOutboxAsync(host.Services, invocation.RunId));
        Assert.Null(outbox.Disposition);
        Assert.Null(outbox.AcknowledgedAtUtcTicks);
        Assert.Equal(0, outbox.Attempts); // No native consumer is installed in this process fixture.
    }

    private sealed record HttpProcessState(EtlRuntimeRunRecord Run, EtlConnectorPartitionRecord Partition,
        EtlConnectorBatchRecord? Batch, EtlConnectorHttpPageRecord? Page, EtlConnectorHttpRequestRecord Request);
}
