using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Http_attempt_replacement_keeps_capacity_through_cancellation_and_reuses_the_published_page_once()
    {
        const string originalBody = "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":null}";
        var responseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(async (request, token) =>
        {
            Assert.Equal("GET", request.Method);
            Assert.Equal("/records", request.RawTarget);
            responseEntered.TrySetResult();
            await releaseResponse.Task.WaitAsync(token);
            return new EtlHttpLoopbackResponse(200, originalBody);
        });

        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var clock = new AttemptClock();
        var barrier = new HttpAttemptReplacementBarrier();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Lookup = new("lookup", EtlTestPipelines.Create().LookupSchema, "id", "id"),
            Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Http = defaults.Http with { RequestTimeoutSeconds = 60, DeadlineSeconds = 180 }
        };
        var capacity = ConnectorAttemptCapacity(pipeline);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureRuntime: runtime =>
            {
                runtime.MaxConcurrentAttempts = 2;
                runtime.FileBindings.RemoveAll(binding => binding.Role == "Source");
                runtime.HttpBindings.Add(new EtlHostHttpBindingOptions
                {
                    TenantId = files.Scope.TenantId,
                    PersistencePartition = files.Scope.PersistencePartition,
                    Alias = "source",
                    Role = "Source",
                    Revision = "http-v1",
                    Endpoint = endpoint.CreateUri("/records").AbsoluteUri,
                    AllowHttp = true,
                    AllowedPaths = ["/records"],
                    Shards = [new() { Id = "page", SeedUri = endpoint.CreateUri("/records").AbsoluteUri }]
                });
            },
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton(new EtlAttemptBudget(2, capacity)));
                services.Replace(ServiceDescriptor.Scoped<IEtlConnectorRuntimeStore>(provider =>
                    new HttpAttemptReplacementStore(
                        provider.GetRequiredService<IEtlRuntimeStore>(), barrier)));
            });

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var invocation = ConnectorInvocation(files.Scope, "http-attempt-replacement", pipeline);
        await files.AdmitAsync(host, invocation);

        await using var control = host.Services.CreateAsyncScope();
        await using var oldScope = host.Services.CreateAsyncScope();
        await using var successorScope = host.Services.CreateAsyncScope();
        var commonStore = control.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var connectorStore = control.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
        var preparation = Assert.IsType<EtlPreparationLease>(await connectorStore.ClaimConnectorPreparationAsync(
            files.Scope, "http-prepare", deadline.Token));
        await control.ServiceProvider.GetRequiredService<EtlConnectorExecution>().PrepareAsync(preparation, deadline.Token);

        var oldLease = Assert.IsType<EtlConnectorPartitionLease>(await connectorStore.ClaimConnectorPartitionAsync(
            files.Scope, invocation.RunId, "http-old", deadline.Token));
        var oldTask = oldScope.ServiceProvider.GetRequiredService<EtlConnectorExecution>()
            .ExecutePartitionAsync(oldLease, deadline.Token).AsTask();
        Task? successorTask = null;
        try
        {
            await barrier.OldRenewalEntered.Task.WaitAsync(deadline.Token);
            await responseEntered.Task.WaitAsync(deadline.Token);
            await AssertAttemptCapacityHeldAsync(host.Services.GetRequiredService<EtlAttemptBudget>(), capacity, deadline.Token);

            // The short-lived partition lease expires while the real loopback GET is held. Its
            // source-attempt window remains open, so the late response can still be retained.
            clock.Advance(TimeSpan.FromSeconds(31));
            var successorLease = Assert.IsType<EtlConnectorPartitionLease>(await connectorStore.ClaimConnectorPartitionAsync(
                files.Scope, invocation.RunId, "http-successor", deadline.Token));
            Assert.True(successorLease.Fence > oldLease.Fence);
            await AssertAttemptCapacityHeldAsync(host.Services.GetRequiredService<EtlAttemptBudget>(), capacity, deadline.Token);

            releaseResponse.TrySetResult();
            await barrier.OldCaptureEntered.Task.WaitAsync(deadline.Token);
            var publishedPage = Assert.IsType<EtlRetainedHttpPage>(barrier.PublishedPage);
            Assert.Equal(Encoding.UTF8.GetBytes(originalBody),
                await File.ReadAllBytesAsync(Path.Combine(files.SnapshotRoot, publishedPage.Body.Key), deadline.Token));
            Assert.Single(endpoint.Requests);
            var cached = await ReadProcessPageAsync(host.Services, files, invocation, deadline.Token);
            Assert.Equal(publishedPage, cached.Descriptor);
            Assert.Equal(oldLease.Fence, cached.Provenance.Fence);

            // Renewal now observes the replacement fence and cancels the old execution after its
            // physical GET and immutable cache publication. Hold asynchronous cleanup to prove the
            // actual execution still owns all of its shared capacity until it completely unwinds.
            barrier.ReleaseOldRenewal.TrySetResult();
            await barrier.OldCleanupEntered.Task.WaitAsync(deadline.Token);
            Assert.Equal(EtlStoreStatus.LeaseLost, barrier.OldRenewalStatus);
            Assert.False(oldTask.IsCompleted);
            await AssertAttemptCapacityHeldAsync(host.Services.GetRequiredService<EtlAttemptBudget>(), capacity, deadline.Token);

            barrier.ReleaseOldCleanup.TrySetResult();
            await oldTask.WaitAsync(deadline.Token);
            Assert.Equal(EtlStoreStatus.LeaseLost, barrier.OldCleanupStatus);
            var beforeSuccessor = await ReadHttpProcessStateAsync(host.Services, deadline.Token);
            Assert.Null(beforeSuccessor.Page);
            Assert.Null(beforeSuccessor.Batch);
            var pendingFetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(beforeSuccessor.Request.FetchJson!);
            Assert.Null(pendingFetch.Capture);
            Assert.Equal(cached.Provenance.AttemptId, Assert.Single(pendingFetch.Attempts).Attempt.RequestId);

            await using (var available = await host.Services.GetRequiredService<EtlAttemptBudget>()
                             .AcquireAsync(capacity, deadline.Token)) { }

            successorTask = successorScope.ServiceProvider.GetRequiredService<EtlConnectorExecution>()
                .ExecutePartitionAsync(successorLease, deadline.Token).AsTask();
            await successorTask.WaitAsync(deadline.Token);

            var run = Assert.IsType<EtlRunView>(await commonStore.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
            Assert.Equal(EtlRunStatus.Succeeded, run.Status);
            Assert.Equal(1, run.AcceptedRecords);
            Assert.Equal(0, run.RejectedRecords);
            Assert.Equal(0, run.FilteredRecords);
            Assert.Equal(0, run.OutstandingRecords);
            Assert.Equal(0, run.OutstandingBytes);
            Assert.Single(endpoint.Requests); // The successor consumed the published original page.

            var completed = Assert.IsType<EtlConnectorRunView>(await connectorStore.FindConnectorRunAsync(
                files.Scope, invocation.RunId, deadline.Token));
            Assert.NotNull(completed.Prepared?.Lookup);
            Assert.Equal(publishedPage.Body.ByteLength + completed.Prepared.Lookup.ByteLength, completed.RetainedInputBytes);
            var final = await ReadHttpProcessStateAsync(host.Services, deadline.Token);
            var captured = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(final.Request.FetchJson!);
            Assert.Equal(EtlConnectorHttpSourceFetchPhase.Captured, captured.Phase);
            Assert.Equal(cached.Provenance.AttemptId, captured.Capture!.RequestId);
            Assert.Equal(cached.Provenance.Fence, captured.Capture.Fence);
            Assert.Equal(cached.Provenance.ResponseReceivedAtUtc, captured.Capture.ResponseReceivedAtUtc);
            Assert.Single(captured.Attempts);
            Assert.True(final.Batch!.Settled);
            Assert.Equal(1, final.Partition.AcceptedBatchCount);
            Assert.Equal(1, final.Page!.AcceptedItemCount);
            Assert.Equal("Ada", Assert.Single(await ReadConnectorRouteAsync(endpoint, files.OutputRoot, "/output", false))
                .GetProperty("name").GetString());
            Assert.Empty(Directory.Exists(files.ErrorRoot) ? Directory.EnumerateFiles(files.ErrorRoot, "*.jsonl") : []);
        }
        finally
        {
            releaseResponse.TrySetResult();
            barrier.ReleaseOldRenewal.TrySetResult();
            barrier.ReleaseOldCapture.TrySetResult();
            barrier.ReleaseOldCleanup.TrySetResult();
            await Task.WhenAll(new[] { oldTask, successorTask }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static EtlAttemptCharge ConnectorAttemptCapacity(EtlPipelineV2 pipeline) => new(
        Math.Max(pipeline.Limits.MaxBatchRecords, pipeline.Http.MaxPageRecords),
        Math.Max(pipeline.Limits.MaxBatchBytes, pipeline.Http.MaxPageBytes),
        pipeline.Limits.MaxLookupBytes,
        pipeline.Limits.MaxOutputBatchBytes);

    private sealed class HttpAttemptReplacementBarrier
    {
        public readonly TaskCompletionSource OldRenewalEntered = Signal();
        public readonly TaskCompletionSource ReleaseOldRenewal = Signal();
        public readonly TaskCompletionSource OldCaptureEntered = Signal();
        public readonly TaskCompletionSource ReleaseOldCapture = Signal();
        public readonly TaskCompletionSource OldCleanupEntered = Signal();
        public readonly TaskCompletionSource ReleaseOldCleanup = Signal();
        public EtlRetainedHttpPage? PublishedPage;
        public EtlStoreStatus? OldRenewalStatus;
        public EtlStoreStatus? OldCleanupStatus;
        public int OldRenewals;

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Preserves the connector store capability while gating only stale capture and cleanup.</summary>
    private sealed class HttpAttemptReplacementStore(IEtlRuntimeStore runtime,
        HttpAttemptReplacementBarrier barrier) : ForwardingEtlConnectorRuntimeStore(runtime, (IEtlConnectorRuntimeStore)runtime)
    {
        public override async ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
            EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page, DateTimeOffset responseReceivedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (lease.Owner == "http-old")
            {
                barrier.PublishedPage = page;
                barrier.OldCaptureEntered.TrySetResult();
                await barrier.ReleaseOldCapture.Task.WaitAsync(cancellationToken);
            }
            return await Connector.CaptureConnectorHttpSourceAsync(lease, attempt, page, responseReceivedAtUtc, cancellationToken);
        }

        public override async ValueTask<EtlStoreStatus> RenewConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default)
        {
            if (lease.Owner == "http-old" && Interlocked.Increment(ref barrier.OldRenewals) == 1)
            {
                barrier.OldRenewalEntered.TrySetResult();
                await barrier.ReleaseOldRenewal.Task;
                var status = await Connector.RenewConnectorPartitionAsync(lease, CancellationToken.None);
                barrier.OldRenewalStatus = status;
                return status;
            }
            return await Connector.RenewConnectorPartitionAsync(lease, cancellationToken);
        }

        public override async ValueTask<EtlStoreStatus> ReleaseConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default)
        {
            if (lease.Owner == "http-old")
            {
                barrier.OldCleanupEntered.TrySetResult();
                await barrier.ReleaseOldCleanup.Task.WaitAsync(cancellationToken);
                var status = await Connector.ReleaseConnectorPartitionAsync(lease, cancellationToken);
                barrier.OldCleanupStatus = status;
                return status;
            }
            return await Connector.ReleaseConnectorPartitionAsync(lease, cancellationToken);
        }
    }
}
