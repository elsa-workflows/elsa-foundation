using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1196: a continuous run outlives its page limit with bounded rows, bytes and files.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task A_continuous_run_accepts_far_more_pages_than_its_page_limit_with_a_bounded_window_per_shard()
    {
        const int pagesPerShard = 40;
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed { PageSize = 1 };
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, token) =>
            request.Method == "POST" ? ValueTask.FromResult(new EtlHttpLoopbackResponse(204, "")) : feed.RespondAsync(request, token));
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: true, httpErrors: true));
        var baseline = EtlV2TestPipelines.Create();
        // Three pages would end a finite shard; the window, not the page sequence, bounds a continuous one.
        var invocation = ConnectorInvocation(files.Scope, "continuous-window", baseline with
        {
            Http = baseline.Http with { MaxPagesPerPartition = 3 },
            Continuous = new EtlContinuousRunPolicy(1)
        });
        for (var id = 1; id <= pagesPerShard; id++)
        {
            feed.Append("a", id, $"a{id}");
            feed.Append("b", id, $"b{id}");
        }

        using var sampling = new CancellationTokenSource();
        var peak = (Pages: 0, Requests: 0);
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                await using var operation = host.Services.CreateAsyncScope();
                var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
                var pages = await db.ConnectorHttpPages.AsNoTracking().GroupBy(row => row.PartitionOrdinal).Select(group => group.Count()).ToListAsync();
                var requests = await db.ConnectorHttpRequests.AsNoTracking().GroupBy(row => row.PartitionOrdinal).Select(group => group.Count()).ToListAsync();
                peak = (Math.Max(peak.Pages, pages.DefaultIfEmpty().Max()), Math.Max(peak.Requests, requests.DefaultIfEmpty().Max()));
                await Task.Delay(20);
            }
        });
        await files.AdmitAsync(host, invocation);
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 2 * pagesPerShard, TimeSpan.FromSeconds(120));
        await sampling.CancelAsync();
        await sampler;

        Assert.InRange(peak.Pages, 1, 2);
        Assert.InRange(peak.Requests, 1, 3);
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            var partitions = await db.ConnectorPartitions.AsNoTracking().ToListAsync();
            Assert.All(partitions, partition =>
            {
                Assert.Equal((long)pagesPerShard, partition.PageCount);
                Assert.Equal((long)pagesPerShard, partition.AcceptedRecords);
                Assert.True(partition.CompactedPageCount >= pagesPerShard - 2);
            });
            var pageBytes = await db.ConnectorHttpPages.AsNoTracking().SumAsync(row => row.ByteLength);
            var connector = await db.ConnectorRuns.AsNoTracking().SingleAsync();
            Assert.Equal(pageBytes, connector.RetainedInputBytes);
            Assert.Equal(pageBytes, partitions.Sum(partition => partition.PageBytes));
            // Settled route bodies and left pages were deleted after their transitions committed.
            Assert.Empty(Directory.GetFiles(files.SnapshotRoot, "*", SearchOption.AllDirectories));
        }
        var inspection = await FindInspectionAsync(host.Services, files.Scope, invocation.RunId);
        Assert.Equal(2L * pagesPerShard, inspection!.AcceptedMainEffects);

        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal((EtlRunStatus.Succeeded, 2L * pagesPerShard), (run.Status, run.AcceptedRecords));
        var delivered = (await ReadConnectorRouteAsync(endpoint, files.OutputRoot, "/output", http: true))
            .Select(record => record.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(2 * pagesPerShard, delivered.Length);
        Assert.Equal(delivered.Length, delivered.Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task<EtlConnectorRunInspection?> FindInspectionAsync(IServiceProvider services, EtlScope scope, string runId)
    {
        await using var operation = services.CreateAsyncScope();
        return await operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeInspector>().FindConnectorInspectionAsync(scope, runId);
    }
}
