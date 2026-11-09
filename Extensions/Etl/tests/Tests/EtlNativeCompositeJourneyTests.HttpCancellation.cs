using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Tests.Support;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlNativeCompositeJourneyTests
{
    public enum HttpCancellationBoundary { BeforeWorker, SourceCapture, DestinationAcknowledgement }

    public static TheoryData<CompositeKind, bool, HttpCancellationBoundary> HttpCancellationCases
    {
        get
        {
            var cases = new TheoryData<CompositeKind, bool, HttpCancellationBoundary>();
            foreach (var composite in new[] { CompositeKind.Sequence, CompositeKind.Flowchart })
            foreach (var subtree in new[] { false, true })
            foreach (var boundary in Enum.GetValues<HttpCancellationBoundary>())
                cases.Add(composite, subtree, boundary);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(HttpCancellationCases))]
    public async Task Native_HTTP_cancellation_retires_the_wait_and_refuses_late_receipts(
        CompositeKind composite, bool subtree, HttpCancellationBoundary boundary)
    {
        var effects = 0;
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
        {
            if (request.Method == "GET")
            {
                Assert.Equal("/records", request.RawTarget);
                return ValueTask.FromResult(new EtlHttpLoopbackResponse(200,
                    "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":null}"));
            }
            Assert.Equal("POST", request.Method);
            Assert.Equal("/output", request.RawTarget);
            Interlocked.Increment(ref effects);
            return ValueTask.FromResult(new EtlHttpLoopbackResponse(204, ""));
        });
        var barrier = new HttpCancellationBarrier(boundary);
        await WithJourneyAsync(composite, NativeOutcome.Succeeded, async journey =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var invocation = await journey.StartSuspendedAsync(activityBoundary: subtree);
            try
            {
                if (boundary != HttpCancellationBoundary.BeforeWorker)
                {
                    await journey.StartWorkerAsync();
                    await barrier.Entered.Task.WaitAsync(deadline.Token);
                }
                await journey.CancelNativeAsync(subtree);
                await journey.WaitForNativeCancellationAsync(invocation, subtree);
                var before = await journey.ReadHttpCancellationStateAsync();
                Assert.Equal(boundary == HttpCancellationBoundary.DestinationAcknowledgement ? 1 : 0,
                    Volatile.Read(ref effects));

                if (boundary == HttpCancellationBoundary.BeforeWorker)
                    await journey.StartWorkerAsync();
                else
                {
                    // Real HTTP has completed; submit its receipt after durable native cancellation.
                    // Deliberately omit the cancelled execution token here to test the store's terminal
                    // guard independently of cooperative transport cancellation.
                    barrier.Release.TrySetResult();
                    Assert.Equal(EtlStoreStatus.Terminal, await barrier.Completed.Task.WaitAsync(deadline.Token));
                }
                await journey.WaitForNativeResultAsync(invocation, ActivityExecutionStatus.Cancelled,
                    subtree ? WorkflowExecutionStatus.Running : WorkflowExecutionStatus.Cancelled);
                await journey.StopWorkerAsync(deadline.Token);
                Assert.Equal(before, await journey.ReadHttpCancellationStateAsync());
                AssertCancelledTree(journey, await journey.ReadNativeStateAsync(), subtree);
                Assert.Null(await journey.ReadBookmarkAsync(invocation.ResultAddress.BookmarkId));
                Assert.Empty(await journey.ReadManifestAsync(invocation.Scope, invocation.RunId));
                var run = Assert.IsType<EtlRunView>(await journey.ReadRunAsync(invocation.Scope, invocation.RunId));
                Assert.Equal(EtlRunStatus.Cancelled, run.Status);
                Assert.Equal(EtlTerminalOutcome.Cancelled, run.TerminalResult!.Outcome);
                Assert.Equal(0, run.AcceptedRecords);
                Assert.Equal(0, run.RejectedRecords);
                var outbox = await journey.ReadOutboxAsync(invocation.Scope, invocation.RunId);
                Assert.Equal((int)EtlDeliveryDisposition.ProvenParentTerminal, outbox.Disposition);
                Assert.NotNull(outbox.AcknowledgedAtUtcTicks);
                Assert.Equal(boundary == HttpCancellationBoundary.BeforeWorker ? 0 : 1,
                    endpoint.Requests.Count(request => request.Method == "GET"));
                Assert.Equal(boundary == HttpCancellationBoundary.DestinationAcknowledgement ? 1 : 0,
                    endpoint.Requests.Count(request => request.Method == "POST"));
                Assert.Equal(boundary == HttpCancellationBoundary.DestinationAcknowledgement ? 1 : 0,
                    Volatile.Read(ref effects)); // Cancellation cannot undo the receiver's applied write.
            }
            finally { barrier.Release.TrySetResult(); }
        }, http: new NativeHttpConfiguration(endpoint, barrier));
    }

    private sealed record NativeHttpConfiguration(EtlHttpLoopbackServer Endpoint, HttpCancellationBarrier Barrier)
    {
        public void ConfigureRuntime(EtlRuntimeFeature runtime, EtlScope scope)
        {
            runtime.FileBindings = [];
            runtime.HttpBindings =
            [
                Binding("source", "Source", "/records"),
                Binding("output", "MainOutput", "/output"),
                Binding("errors", "ErrorOutput", "/errors")
            ];
            EtlHostHttpBindingOptions Binding(string alias, string role, string path) => new()
            {
                TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition,
                Alias = alias, Role = role, Revision = "http-v1", AllowHttp = true,
                Endpoint = Endpoint.CreateUri(path).AbsoluteUri, AllowedPaths = [path],
                Shards = role == "Source" ? [new() { Id = "page", SeedUri = Endpoint.CreateUri(path).AbsoluteUri }] : []
            };
        }

        public void ConfigureServices(IServiceCollection services) =>
            services.Replace(ServiceDescriptor.Scoped<IEtlConnectorRuntimeStore>(provider =>
                new NativeHttpCancellationStore(provider.GetRequiredService<IEtlRuntimeStore>(), Barrier)));
    }

    private sealed class HttpCancellationBarrier(HttpCancellationBoundary boundary)
    {
        public HttpCancellationBoundary Boundary { get; } = boundary;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EtlStoreStatus> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class NativeHttpCancellationStore(IEtlRuntimeStore runtime, HttpCancellationBarrier barrier)
        : ForwardingEtlConnectorRuntimeStore(runtime, (IEtlConnectorRuntimeStore)runtime)
    {
        public override async ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(
            EtlConnectorPartitionLease lease, EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page,
            DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default)
        {
            if (barrier.Boundary != HttpCancellationBoundary.SourceCapture)
                return await base.CaptureConnectorHttpSourceAsync(lease, attempt, page, responseReceivedAtUtc, cancellationToken);
            barrier.Entered.TrySetResult();
            await barrier.Release.Task;
            var result = await base.CaptureConnectorHttpSourceAsync(lease, attempt, page, responseReceivedAtUtc, CancellationToken.None);
            barrier.Completed.TrySetResult(result.Status);
            return result;
        }

        public override async ValueTask<EtlConnectorHttpDeliveryResult> CompleteConnectorHttpDeliveryAsync(
            EtlConnectorPartitionLease lease, string batchId, EtlOutputRoute route, EtlHttpDeliveryAttempt attempt,
            EtlHttpDeliveryResponse response, CancellationToken cancellationToken = default)
        {
            if (barrier.Boundary != HttpCancellationBoundary.DestinationAcknowledgement)
                return await base.CompleteConnectorHttpDeliveryAsync(lease, batchId, route, attempt, response, cancellationToken);
            Assert.Equal(EtlOutputRoute.Main, route);
            Assert.Equal(204, response.StatusCode);
            barrier.Entered.TrySetResult();
            await barrier.Release.Task;
            var result = await base.CompleteConnectorHttpDeliveryAsync(lease, batchId, route, attempt, response, CancellationToken.None);
            barrier.Completed.TrySetResult(result.Status);
            return result;
        }
    }

    private sealed partial class NativeEtlJourney
    {
        private readonly NativeHttpConfiguration? _http;

        public async Task StopWorkerAsync(CancellationToken cancellationToken)
        {
            await _worker.StopAsync(cancellationToken);
            _workerStarted = false;
        }

        public async Task<string> ReadHttpCancellationStateAsync()
        {
            await using var operation = await OpenPartitionScopeAsync();
            var context = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            var run = await context.Runs.AsNoTracking().SingleAsync();
            Assert.Single(await context.Terminals.AsNoTracking().ToArrayAsync());
            Assert.Single(await context.Outbox.AsNoTracking().ToArrayAsync());
            // Delivery acknowledgement and lease cleanup may progress independently. Compare the
            // accounting, source/delivery journals, owed batch and terminal payload they cannot change.
            return JsonSerializer.Serialize(new
            {
                run.Status, run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords,
                run.OutstandingRecords, run.OutstandingBytes,
                Connector = await context.ConnectorRuns.AsNoTracking().SingleAsync(),
                Partitions = await context.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal)
                    .Select(row => new { row.PositionJson, row.OwedBatchId, row.AcceptedBatchCount, row.PageCount,
                        row.PageBytes, row.AcceptedRecords, row.RejectedRecords, row.FilteredRecords }).ToArrayAsync(),
                Requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PageSequence).ToArrayAsync(),
                Pages = await context.ConnectorHttpPages.AsNoTracking().OrderBy(row => row.PageSequence).ToArrayAsync(),
                Batches = await context.ConnectorBatches.AsNoTracking().OrderBy(row => row.BatchOrdinal).ToArrayAsync(),
                Terminal = await context.Terminals.AsNoTracking().SingleAsync()
            });
        }
    }
}
