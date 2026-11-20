using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1192: continuous HTTP runs through the real worker and store against a loopback cursor feed.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Continuous_run_delivers_appended_records_idles_without_growth_and_drains_on_completion()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed();
        feed.Append("a", 1, "Ada");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        var invocation = ConnectorInvocation(files.Scope, "continuous-drain", ContinuousPipeline(pollIntervalSeconds: 1));
        await files.AdmitAsync(host, invocation);

        var before = await WaitForIdleStateAsync(host.Services, files.Scope, invocation.RunId, accepted: 1);
        var polled = feed.CaughtUpResponses;
        await WaitUntilAsync(() => feed.CaughtUpResponses >= polled + 20, TimeSpan.FromSeconds(30));

        // Twenty or more caught-up polls retain no page, request, batch or byte.
        Assert.Equal(before, await WaitForIdleStateAsync(host.Services, files.Scope, invocation.RunId, accepted: 1));
        Assert.Equal(EtlRunStatus.Running, (await FindRunAsync(host.Services, files.Scope, invocation.RunId))!.Status);

        feed.Append("b", 2, "Bram");
        feed.Append("a", 3, "Cleo");
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 3);
        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        Assert.Equal(EtlRunCompletionOutcome.AlreadyRequested, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal((3, 0, 0, 0), (run.AcceptedRecords, run.RejectedRecords, run.OutstandingRecords, run.OutstandingBytes));
        Assert.Equal(["Ada", "Bram", "Cleo"], ReadJsonlNames(files.OutputRoot));
        Assert.Equal(EtlRunCompletionOutcome.Terminal, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
    }

    [Fact]
    public async Task Completion_wakes_idle_shards_and_rearms_a_poll_that_started_before_the_request()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed();
        feed.Append("a", 1, "Ada");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        // An hour-long interval: the run can only drain if completion wakes idle shards and re-arms the held poll.
        var invocation = ConnectorInvocation(files.Scope, "continuous-race", ContinuousPipeline(pollIntervalSeconds: 3_600));
        // Shard b's first poll computes its empty answer, then is held in flight across the completion request.
        var held = feed.Hold("b");
        await files.AdmitAsync(host, invocation);
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(async () => (await FindRunAsync(host.Services, files.Scope, invocation.RunId))?.AcceptedRecords == 1,
            TimeSpan.FromSeconds(30));

        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        // Appended after b's held poll read the feed: only a poll started after the request can see it.
        feed.Append("b", 2, "Bram");
        held.Release.SetResult();
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal(["Ada", "Bram"], ReadJsonlNames(files.OutputRoot));
    }

    [Fact]
    public async Task Cancellation_ends_a_continuous_run_and_completion_is_refused_for_finite_and_terminal_runs()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed();
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        var continuous = ConnectorInvocation(files.Scope, "continuous-cancel", ContinuousPipeline(pollIntervalSeconds: 1));
        await files.AdmitAsync(host, continuous);
        await WaitForIdleAsync(host.Services, files.Scope, continuous.RunId, accepted: 0);

        await using (var operation = host.Services.CreateAsyncScope())
            Assert.Equal(EtlStoreStatus.Applied, (await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().CancelAsync(continuous)).Status);
        Assert.Equal(EtlRunStatus.Cancelled, (await files.WaitForTerminalAsync(host, continuous.RunId)).Status);
        Assert.Equal(EtlRunCompletionOutcome.Terminal, await RequestCompletionAsync(host.Services, files.Scope, continuous.RunId));

        var finite = ConnectorInvocation(files.Scope, "finite-complete", ContinuousPipeline(1) with { Continuous = null });
        await files.AdmitAsync(host, finite);
        Assert.Equal(EtlRunCompletionOutcome.NotContinuous, await RequestCompletionAsync(host.Services, files.Scope, finite.RunId));
        // A finite run answers notContinuous after it settles too, so the answer does not race its settlement.
        Assert.Equal(EtlRunStatus.Succeeded, (await files.WaitForTerminalAsync(host, finite.RunId)).Status);
        Assert.Equal(EtlRunCompletionOutcome.NotContinuous, await RequestCompletionAsync(host.Services, files.Scope, finite.RunId));
        Assert.Equal(EtlRunCompletionOutcome.NotFound, await RequestCompletionAsync(host.Services, files.Scope, "missing"));
    }

    [Fact]
    public async Task A_cursor_that_moves_without_records_waits_one_interval_between_requests()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed { MoveEmptyCursor = true };
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        var invocation = ConnectorInvocation(files.Scope, "continuous-moving", ContinuousPipeline(pollIntervalSeconds: 2));
        await files.AdmitAsync(host, invocation);

        await WaitUntilAsync(() => feed.RequestTimes("a").Count >= 3 && feed.RequestTimes("b").Count >= 3, TimeSpan.FromSeconds(60));
        var beforeCompletion = new[] { "a", "b" }.ToDictionary(shard => shard, shard => feed.RequestTimes(shard).Count);
        // Completion makes each waiting shard due once; a moving cursor then keeps its interval, since only a caught-up
        // response ends a shard.
        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        var afterCompletion = new[] { "a", "b" }.ToDictionary(shard => shard, shard => feed.RequestTimes(shard).Count);
        await WaitUntilAsync(() => feed.RequestTimes("a").Count >= afterCompletion["a"] + 4 &&
                                   feed.RequestTimes("b").Count >= afterCompletion["b"] + 4, TimeSpan.FromSeconds(60));

        // Each shard waits its two-second interval between requests (a hot loop would be milliseconds apart). Requests
        // around the completion call, including the one wake-up, are excluded from the spacing check.
        foreach (var shard in new[] { "a", "b" })
        {
            var times = feed.RequestTimes(shard);
            foreach (var window in new[] { times.Take(beforeCompletion[shard]).ToArray(), times.Skip(afterCompletion[shard] + 1).ToArray() })
                Assert.All(window.Zip(window.Skip(1)), pair => Assert.True(pair.Second - pair.First >= TimeSpan.FromSeconds(1.5)));
        }
        Assert.Equal(EtlRunStatus.Running, (await FindRunAsync(host.Services, files.Scope, invocation.RunId))!.Status);
    }

    [Fact]
    public async Task A_finite_run_still_refuses_an_empty_page_that_repeats_its_cursor()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed();
        feed.Append("a", 1, "Ada");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        var invocation = ConnectorInvocation(files.Scope, "finite-cycle", ContinuousPipeline(1) with { Continuous = null });

        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        // Shard a's second page is empty and repeats cursor "1": only a continuous run treats that as caught up.
        Assert.NotEqual(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal("etl.http.pagination-cycle", run.TerminalResult!.Diagnostic!.Code);
    }

    [Fact]
    public async Task A_drained_run_with_a_rejected_record_settles_business_error()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed();
        feed.Append("a", 1, "Ada");
        feed.Append("a", 2, null);
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        var invocation = ConnectorInvocation(files.Scope, "continuous-business-error", ContinuousPipeline(pollIntervalSeconds: 1));
        await files.AdmitAsync(host, invocation);
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 1);

        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal((1, 1), (run.AcceptedRecords, run.RejectedRecords));
        Assert.Equal("etl.rows-rejected", run.TerminalResult!.Diagnostic!.Code);
    }

    [Fact]
    public async Task Cancellation_wins_over_a_completion_that_has_not_drained()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var feed = new CursorFeed();
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync(feed.RespondAsync);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: false, httpErrors: false));
        var invocation = ConnectorInvocation(files.Scope, "continuous-cancel-pending", ContinuousPipeline(pollIntervalSeconds: 1));
        // Shard b's poll is held, so the requested completion cannot drain before the cancellation.
        var held = feed.Hold("b");
        await files.AdmitAsync(host, invocation);
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(EtlRunCompletionOutcome.Accepted, await RequestCompletionAsync(host.Services, files.Scope, invocation.RunId));
        await using (var operation = host.Services.CreateAsyncScope())
            Assert.Equal(EtlStoreStatus.Applied, (await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().CancelAsync(invocation)).Status);
        held.Release.SetResult();

        Assert.Equal(EtlRunStatus.Cancelled, (await files.WaitForTerminalAsync(host, invocation.RunId)).Status);
    }

    private static EtlPipelineV2 ContinuousPipeline(int pollIntervalSeconds)
    {
        var baseline = EtlV2TestPipelines.Create();
        return baseline with
        {
            Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Continuous = new EtlContinuousRunPolicy(pollIntervalSeconds)
        };
    }

    private static async Task<EtlRunView?> FindRunAsync(IServiceProvider services, EtlScope scope, string runId)
    {
        await using var operation = services.CreateAsyncScope();
        return await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().FindRunAsync(scope, runId);
    }

    private static async Task<EtlRunCompletionOutcome> RequestCompletionAsync(IServiceProvider services, EtlScope scope, string runId)
    {
        await using var operation = services.CreateAsyncScope();
        return await operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>().RequestConnectorRunCompletionAsync(scope, runId);
    }

    /// <summary>Waits until every shard idles (cleared journal, scheduled poll, no lease) with the given accepted count.</summary>
    private static async Task WaitForIdleAsync(IServiceProvider services, EtlScope scope, string runId, long accepted, TimeSpan? timeout = null)
    {
        try
        {
            await WaitUntilAsync(async () =>
            {
                await using var operation = services.CreateAsyncScope();
                return await IsIdleAsync(operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>(), scope, runId, accepted);
            }, timeout ?? TimeSpan.FromSeconds(30));
        }
        catch (TaskCanceledException exception)
        {
            // Name the state the run stopped in, so a timeout is diagnosable without rerunning.
            var run = await FindRunAsync(services, scope, runId);
            await using var operation = services.CreateAsyncScope();
            var partitions = await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorPartitions.AsNoTracking()
                .Select(row => new { row.PartitionOrdinal, Owned = row.Owner != null, row.NotBeforeUtcTicks, row.CurrentRequestSequence, row.OwedBatchId })
                .ToListAsync();
            throw new TimeoutException($"Run {runId} did not idle with {accepted} accepted: status {run?.Status}, accepted {run?.AcceptedRecords}, " +
                $"diagnostic {run?.TerminalResult?.Diagnostic?.Code}, partitions {string.Join("; ", partitions)}.", exception);
        }
    }

    private static async ValueTask<bool> IsIdleAsync(EtlRuntimeDbContext db, EtlScope scope, string runId, long accepted)
    {
        var scopeKey = EtlRuntimeIdentity.ScopeKey(scope);
        var runKey = EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        var partitions = await db.ConnectorPartitions.AsNoTracking()
            .Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey).ToListAsync();
        // Earlier requests keep their captured journals; only each shard's current request must be cleared.
        var current = await db.ConnectorHttpRequests.AsNoTracking()
            .Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey && row.FetchJson != null)
            .Select(row => new { row.PartitionOrdinal, row.PageSequence }).ToListAsync();
        var journals = current.Count(request => partitions.Any(partition =>
            partition.PartitionOrdinal == request.PartitionOrdinal && partition.CurrentRequestSequence == request.PageSequence));
        return run?.AcceptedRecords == accepted && partitions.Count == 2 && journals == 0 &&
               partitions.All(row => row.NotBeforeUtcTicks is not null && row.Owner is null && row.OwedBatchId is null);
    }

    /// <summary>Waits for an idle snapshot and returns its totals, both read in one transaction so a poll cannot interleave.</summary>
    private static async Task<string> WaitForIdleStateAsync(IServiceProvider services, EtlScope scope, string runId, long accepted)
    {
        string? state = null;
        await WaitUntilAsync(async () =>
        {
            await using var operation = services.CreateAsyncScope();
            var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (!await IsIdleAsync(db, scope, runId, accepted)) return false;
            state = await ContinuousStateAsync(db, scope, runId);
            return true;
        }, TimeSpan.FromSeconds(30));
        return state!;
    }

    private static async Task<string> ContinuousStateAsync(EtlRuntimeDbContext db, EtlScope scope, string runId)
    {
        var scopeKey = EtlRuntimeIdentity.ScopeKey(scope);
        var runKey = EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
        var connector = await db.ConnectorRuns.AsNoTracking().SingleAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        var pages = await db.ConnectorHttpPages.AsNoTracking().CountAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        var requests = await db.ConnectorHttpRequests.AsNoTracking().CountAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        var batches = await db.ConnectorBatches.AsNoTracking().CountAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        var partitionBytes = await db.ConnectorPartitions.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey)
            .Select(row => row.MetadataBytes + row.PageBytes + row.SourceInputReservedBytes).SumAsync();
        return string.Join('|', pages, requests, batches, connector.RetainedInputBytes, connector.RetainedMetadataBytes,
            connector.RetainedOutputBytes, connector.SourceInputReservedBytes, partitionBytes);
    }

    private static async Task WaitUntilAsync(Func<ValueTask<bool>> condition, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!await condition())
            await Task.Delay(50, deadline.Token);
    }


    private static Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout) => WaitUntilAsync(() => ValueTask.FromResult(condition()), timeout);

    private static string[] ReadJsonlNames(string directory) => Directory.EnumerateFiles(directory, "*.jsonl")
        .SelectMany(File.ReadAllLines).Where(line => line.Length > 0)
        .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("name").GetString()!).Order().ToArray();

    /// <summary>A loopback cursor feed: <c>?cursor=n</c> returns the shard's records after position n.</summary>
    private sealed class CursorFeed
    {
        private readonly ConcurrentDictionary<string, List<(long Id, string? Name)>> _shards = new();
        private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _requestTimes = new();
        private readonly ConcurrentDictionary<string, HeldPoll> _held = new();
        private int _caughtUp;
        private int _requests;

        public bool MoveEmptyCursor { get; init; }
        /// <summary>At most this many records per page, so a backlog spans many pages (spec 1196).</summary>
        public int PageSize { get; init; } = int.MaxValue;
        public int CaughtUpResponses => Volatile.Read(ref _caughtUp);
        public int Requests => Volatile.Read(ref _requests);

        public IReadOnlyList<DateTimeOffset> RequestTimes(string shard)
        {
            var times = _requestTimes.GetOrAdd(shard, _ => []);
            lock (times) return times.ToArray();
        }

        public void Append(string shard, long id, string? name)
        {
            var records = _shards.GetOrAdd(shard, _ => []);
            lock (records) records.Add((id, name));
        }

        public HeldPoll Hold(string shard) => _held.GetOrAdd(shard, _ => new HeldPoll());

        public async ValueTask<EtlHttpLoopbackResponse> RespondAsync(EtlHttpLoopbackRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            var target = new Uri(new Uri("http://feed"), request.RawTarget);
            var shard = target.AbsolutePath.TrimStart('/');
            var times = _requestTimes.GetOrAdd(shard, _ => []);
            lock (times) times.Add(DateTimeOffset.UtcNow);
            var query = System.Web.HttpUtility.ParseQueryString(target.Query);
            var cursor = long.TryParse(query["cursor"], out var parsed) ? parsed : 0;
            var records = _shards.GetOrAdd(shard, _ => []);
            (long Id, string? Name)[] page;
            lock (records) page = records.Skip((int)Math.Min(cursor, records.Count)).Take(PageSize).ToArray();
            var response = Page(page, cursor);
            // The answer is fixed before a held poll waits, as a real request reads the feed before replying.
            if (_held.TryRemove(shard, out var held))
            {
                held.Started.TrySetResult();
                await held.Release.Task.WaitAsync(cancellationToken);
            }
            return response;
        }

        private EtlHttpLoopbackResponse Page((long Id, string? Name)[] page, long cursor)
        {
            if (page.Length == 0)
            {
                if (MoveEmptyCursor)
                    return new(200, $"{{\"records\":[],\"next\":\"{cursor + 1}\"}}");
                Interlocked.Increment(ref _caughtUp);
                return new(200, $"{{\"records\":[],\"next\":{(cursor == 0 ? "null" : $"\"{cursor}\"")}}}");
            }
            var body = string.Join(',', page.Select(record =>
                $"{{\"id\":{record.Id},\"name\":{(record.Name is null ? "null" : $"\"{record.Name}\"")}}}"));
            return new(200, $"{{\"records\":[{body}],\"next\":\"{cursor + page.Length}\"}}");
        }
    }

    private sealed class HeldPoll
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
