using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1192: continuous runs recover in real child worker processes over one runtime store.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Theory]
    [InlineData("captured-page")]
    [InlineData("idle-poll")]
    public async Task Continuous_worker_processes_resume_from_the_durable_cursor_after_a_kill(string killPoint)
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var feed = new CursorFeed();
        if (killPoint == "captured-page")
        {
            feed.Append("a", 1, "Ada");
            feed.Append("a", 2, "Bram");
        }
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        List<EtlHostHttpBindingOptions> bindings =
        [
            HttpBinding(files.Scope, endpoint, "source", "Source", "/a", ["/a", "/b"],
                [new() { Id = "a", SeedUri = endpoint.CreateUri("/a").AbsoluteUri },
                 new() { Id = "b", SeedUri = endpoint.CreateUri("/b").AbsoluteUri }])
        ];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
        {
            runtime.HttpBindings = bindings;
            runtime.FileBindings.RemoveAll(binding => binding.Role == "Source");
        });
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        // A short request timeout lets the replacement close the killed worker's uncertain poll quickly.
        var pipeline = ContinuousPipeline(pollIntervalSeconds: 1) is var continuous
            ? continuous with { Http = continuous.Http with { RequestTimeoutSeconds = 2 } }
            : null!;
        var invocation = ConnectorInvocation(files.Scope, $"continuous-process-{killPoint}", pipeline);
        await files.AdmitAsync(host, invocation);

        HeldPoll? held = null;
        EtlWorkerProcess predecessor;
        if (killPoint == "captured-page")
        {
            predecessor = await files.StartWorkerAsync("continuous-predecessor", deadline.Token,
                httpBindings: bindings, connectorGate: EtlConnectorWorkerGate.BeforeAcceptance);
            var stopped = await predecessor.WaitForConnectorCheckpointAsync(deadline.Token);
            Assert.Equal(EtlConnectorWorkerGate.BeforeAcceptance, stopped.Gate);
        }
        else
        {
            held = feed.Hold("b");
            predecessor = await files.StartWorkerAsync("continuous-predecessor", deadline.Token, httpBindings: bindings);
            await held.Started.Task.WaitAsync(deadline.Token);
        }
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        held?.Release.SetResult();

        _ = await files.StartWorkerAsync("continuous-survivor", deadline.Token, httpBindings: bindings);
        if (killPoint == "captured-page")
        {
            await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 2);
            // The replacement replays the captured page from the cache; the seed is never fetched again.
            Assert.Single(endpoint.Requests, request => request.RawTarget == "/a");
        }
        else
        {
            await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 0);
            // The killed worker's seed poll was uncertain, so the replacement polled the same cursor again.
            Assert.True(endpoint.Requests.Count(request => request.RawTarget == "/b") >= 2);
            feed.Append("b", 3, "Cleo");
            await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 1);
        }

        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        var run = await files.WaitForTerminalAsync(host.Services, invocation.RunId);

        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal((0, 0), (run.OutstandingRecords, run.OutstandingBytes));
        Assert.Equal(killPoint == "captured-page" ? ["Ada", "Bram"] : ["Cleo"], ReadJsonlNames(files.OutputRoot));
    }

    /// <summary>Spec 1196: workers killed again and again across compactions lose and duplicate nothing.</summary>
    [Fact]
    public async Task Continuous_worker_processes_killed_repeatedly_keep_a_bounded_window_and_every_record_once()
    {
        const int recordsPerShard = 12;
        await using var files = new RuntimeFiles(CsvJourneySource);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        var feed = new CursorFeed { PageSize = 1 };
        for (var id = 1; id <= recordsPerShard; id++)
        {
            feed.Append("a", id, $"a{id}");
            feed.Append("b", id, $"b{id}");
        }
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        List<EtlHostHttpBindingOptions> bindings =
        [
            HttpBinding(files.Scope, endpoint, "source", "Source", "/a", ["/a", "/b"],
                [new() { Id = "a", SeedUri = endpoint.CreateUri("/a").AbsoluteUri },
                 new() { Id = "b", SeedUri = endpoint.CreateUri("/b").AbsoluteUri }])
        ];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
        {
            runtime.HttpBindings = bindings;
            runtime.FileBindings.RemoveAll(binding => binding.Role == "Source");
        });
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var continuous = ContinuousPipeline(pollIntervalSeconds: 1);
        // Page limit 3: without compaction each shard would fail at its fourth page.
        var invocation = ConnectorInvocation(files.Scope, "continuous-process-kills",
            continuous with { Http = continuous.Http with { RequestTimeoutSeconds = 2, MaxPagesPerPartition = 3 } });
        await files.AdmitAsync(host, invocation);

        // Each worker stops after transforming a batch and before accepting it, then is killed.
        for (var kill = 0; kill < 4; kill++)
        {
            var worker = await files.StartWorkerAsync($"continuous-killed-{kill}", deadline.Token,
                httpBindings: bindings, connectorGate: EtlConnectorWorkerGate.BeforeAcceptance);
            Assert.Equal(EtlConnectorWorkerGate.BeforeAcceptance, (await worker.WaitForConnectorCheckpointAsync(deadline.Token)).Gate);
            await worker.CrashAsync();
            Assert.True(worker.HasExited);
        }
        _ = await files.StartWorkerAsync("continuous-survivor", deadline.Token, httpBindings: bindings);
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 2 * recordsPerShard, TimeSpan.FromSeconds(180));

        await using (var operation = host.Services.CreateAsyncScope())
        {
            var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            var pages = await db.ConnectorHttpPages.AsNoTracking().GroupBy(row => row.PartitionOrdinal).Select(group => group.Count()).ToListAsync();
            Assert.All(pages, count => Assert.InRange(count, 0, 2));
            Assert.All(await db.ConnectorPartitions.AsNoTracking().ToListAsync(), partition =>
                Assert.Equal((long)recordsPerShard, partition.PageCount));
        }
        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        var run = await files.WaitForTerminalAsync(host.Services, invocation.RunId);

        Assert.Equal((EtlRunStatus.Succeeded, 2L * recordsPerShard), (run.Status, run.AcceptedRecords));
        var names = ReadJsonlNames(files.OutputRoot);
        Assert.Equal(2 * recordsPerShard, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
