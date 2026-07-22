using System.Diagnostics;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// Concurrency / throughput INSTRUMENT for the in-process runtime (spec 114). The engine-perf campaign (ADR
/// 0031/0032, specs 105–111) optimized SINGLE-run latency; nothing has ever measured N workflow executions running
/// at once. This benchmark drives N concurrent 10-activity hot-loop bursts (the same graph
/// <see cref="EngineExecutionBenchmarks"/> uses) for N ∈ {1, 8, 32, 128} and reports the scaling curve — total wall
/// time, per-run p50/p95 latency, and aggregate durable checkpoint commits — so the next optimization unit can be
/// picked from evidence rather than guessed.
///
/// Three backends isolate WHERE the cost is, by removing one layer at a time:
///   * <b>in-memory</b>: runtime default stores, no fsync — pure CPU + scheduling scaling (the concurrency ceiling).
///   * <b>isolated-sqlite</b>: each execution owns its OWN on-disk SQLite database — adds durable fsync cost but keeps
///     every run on its own writer, so there is no cross-run write contention.
///   * <b>shared-sqlite</b>: all N executions share ONE on-disk SQLite database — the real deployment shape, where the
///     single durable writer is contended. This is the shipping configuration: Coalesced checkpoint persistence +
///     ReplaySafe leaves + the burst-scoped executable cache.
/// The delta in-memory→isolated is the durability/fsync tax; the delta isolated→shared is SQLite single-writer
/// contention. Reading the two deltas against N names the bottleneck.
///
/// WHY N INDEPENDENT HARNESSES SHARING ONE STORE (not one host with N actors): the in-process actor provider
/// (<c>InProcessWorkflowExecutionActorProvider</c>) has NO global drain/concurrency cap — it serializes commands only
/// per workflow-execution id (a per-actor mailbox), and distinct ids drain fully in parallel. So N one-actor engines
/// over one shared store are behaviorally equivalent, for drain concurrency, to one engine hosting N actors, while
/// being far simpler to stand up. Each harness gets a DISTINCT execution id + executable identity so the shared store
/// partitions them cleanly (state/scheduler/executable documents all key on those ids). Per-provider setup — the DI
/// build, the activity-type registry scan (<see cref="WorkflowExecutionHarness.InitializeActivityTypes"/>) — is done
/// BEFORE the timed window, so wall time measures dispatch + drain + store I/O, not engine construction.
///
/// There are deliberately NO hard latency/throughput assertions (they would flake); each run only asserts its
/// workflow completed. Commit counts are the deterministic durable evidence. Emits the curve via
/// <see cref="ITestOutputHelper"/>.
///
/// Lives under benchmarks/ (not tests/), so neither CI gate runs it. Run on demand with:
/// dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj \
///   --filter "FullyQualifiedName~EngineConcurrencyBenchmarks" --logger "console;verbosity=detailed"
/// </summary>
public sealed class EngineConcurrencyBenchmarks(ITestOutputHelper output)
{
    private const int HotLoopLength = 10;
    private const int HotLoopSegmentCap = 256;
    private static readonly int[] ConcurrencyLevels = [1, 8, 32, 128];

    private enum Backend { InMemory, IsolatedSqlite, SharedSqlite }

    // A DISTINCT activity-execution-id pool per execution. This mirrors production, where activity-execution ids are
    // globally unique — the checkpoint-commit marker document is keyed on a CommitId built from the work-item id
    // (which embeds these ids) with NO workflow-execution-id partition, so a shared store needs globally-unique ids
    // or concurrent runs collide on identical commit ids (RuntimeCheckpointReplayConflictException). 64 ids per run
    // comfortably covers the hot loop (11).
    private static string[] NewActivityIdPool(int executionIndex) =>
        Enumerable.Range(0, 64).Select(slot => $"actexec-{executionIndex}-{slot}").ToArray();

    private static WorkflowExecutableIdentity IdentityFor(int index) =>
        new($"artifact-{index}", $"definition-{index}", "version-1", "1.0.0", "sha256:test");

    [Fact]
    public async Task ConcurrencyScalingCurve()
    {
        var uptime = ReadUptime();
        output.WriteLine($"machine uptime/load at start: {uptime}");
        output.WriteLine($"processor count: {Environment.ProcessorCount}");

        // One warmup pass per backend (JIT + OS page-cache + SQLite file warmup), discarded. Warm at a mid N so the
        // thread pool has already grown before the first measured level.
        foreach (var backend in Enum.GetValues<Backend>())
            await MeasureAsync(backend, concurrency: 8, warmup: true);

        foreach (var backend in Enum.GetValues<Backend>())
        {
            output.WriteLine($"=== backend: {backend} · hot-loop×{HotLoopLength} · Coalesced+ReplaySafe ===");
            output.WriteLine("N | totalWall(ms) | p50(ms) | p95(ms) | min(ms) | max(ms) | aggCommits | commits/run | throughput(runs/s)");
            foreach (var concurrency in ConcurrencyLevels)
            {
                var result = await MeasureAsync(backend, concurrency, warmup: false);
                var throughput = result.TotalWallMs > 0 ? concurrency / (result.TotalWallMs / 1000.0) : double.NaN;
                output.WriteLine(
                    $"{concurrency,4} | {result.TotalWallMs,10:F1} | {result.P50,7:F1} | {result.P95,7:F1} | " +
                    $"{result.Min,7:F1} | {result.Max,7:F1} | {FormatCommits(result.AggregateCommits),10} | " +
                    $"{FormatCommitsPerRun(result.AggregateCommits, concurrency),11} | {throughput,8:F1}");
            }
        }
    }

    /// <summary>
    /// The shared-Postgres counterfactual to shared-sqlite: all N executions share ONE Postgres database, whose engine
    /// has REAL concurrent writers (MVCC) instead of SQLite's single writer. If single-writer serialization is the
    /// SQLite bottleneck at high N, Postgres should hold throughput better. Reuses the Testcontainers Groundwork
    /// Postgres driver (Docker required — acceptable here, benchmarks never run in CI). Skips gracefully with a
    /// recorded reason if Docker/container is unavailable. Commits are counted as the per-level delta on the shared DB.
    /// </summary>
    [Fact]
    public async Task ConcurrencyScalingCurve_SharedPostgres()
    {
        output.WriteLine($"machine uptime/load at start: {ReadUptime()}");

        var driver = new PostgreSqlGroundworkProviderDriver();
        try
        {
            await driver.InitializeAsync();
        }
        catch (Exception exception)
        {
            output.WriteLine($"SKIPPED shared-postgres: driver/container unavailable — {exception.GetType().Name}: {exception.Message}");
            await driver.DisposeAsync();
            return;
        }

        await using var client = await driver.OpenClientAsync();
        var store = client.DocumentStore;

        try
        {
            // Warmup pass (discarded), then the measured curve. Commits accumulate on the one shared DB, so each level
            // reports the DELTA in checkpoint-commit documents (== that level's runs; deterministically 1/run).
            await RunPostgresLevelAsync(store, concurrency: 8, warmup: true);
            var commitsSoFar = await CountCheckpointCommitsAsync(store);

            output.WriteLine($"=== backend: SharedPostgres · hot-loop×{HotLoopLength} · Coalesced+ReplaySafe ===");
            output.WriteLine("N | totalWall(ms) | p50(ms) | p95(ms) | min(ms) | max(ms) | aggCommits | commits/run | throughput(runs/s)");
            foreach (var concurrency in ConcurrencyLevels)
            {
                // The container ships PostgreSQL's default max_connections (~100). At high N the N independent
                // providers each pool connections and the level can exceed it (53300: too many clients). That is a
                // driver-reuse infrastructure ceiling, NOT an engine finding, so degrade gracefully rather than fail
                // the benchmark: record the level that could not complete and stop.
                try
                {
                    var (totalWallMs, latencies) = await RunPostgresLevelAsync(store, concurrency, warmup: false);
                    var after = await CountCheckpointCommitsAsync(store);
                    var levelCommits = after - commitsSoFar;
                    commitsSoFar = after;
                    var throughput = totalWallMs > 0 ? concurrency / (totalWallMs / 1000.0) : double.NaN;
                    output.WriteLine(
                        $"{concurrency,4} | {totalWallMs,10:F1} | {Percentile(latencies, 50),7:F1} | {Percentile(latencies, 95),7:F1} | " +
                        $"{latencies[0],7:F1} | {latencies[^1],7:F1} | {levelCommits,10} | " +
                        $"{levelCommits / (double)concurrency,11:F1} | {throughput,8:F1}");
                }
                catch (Exception exception)
                {
                    output.WriteLine($"{concurrency,4} | INFEASIBLE at this N on the reused container — {exception.GetType().Name}: {exception.Message}");
                    break;
                }
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }
    }

    /// <summary>
    /// A/B for spec 115 group commit on the shared-SQLite single writer — the bottleneck spec 114 isolated (N=128
    /// shared = 1.5 runs/s vs isolated = 4.5 runs/s, pure write serialization at 1 commit/run). Each level is measured
    /// twice, back-to-back: group commit OFF (today's one-transaction-per-run path) then ON (concurrent commits fold
    /// onto one shared unit-of-work / one fsync). Run order is swapped between adjacent levels so a monotonic load
    /// drift does not systematically favor one variant. Per-run durable checkpoint-commit markers stay 1/run in both
    /// (the deterministic correctness evidence); the coordinator's batch counters are the deterministic evidence that
    /// folding actually happened. Walls carry the usual load caveat; read the OFF/ON ratio within a level, not absolutes.
    /// </summary>
    [Fact]
    public async Task ConcurrencyScalingCurve_GroupCommit()
    {
        output.WriteLine($"machine uptime/load at start: {ReadUptime()}");
        output.WriteLine($"processor count: {Environment.ProcessorCount}");

        await MeasureSharedSqliteAsync(concurrency: 8, groupCommit: false, warmup: true);
        await MeasureSharedSqliteAsync(concurrency: 8, groupCommit: true, warmup: true);

        output.WriteLine($"=== shared-sqlite · hot-loop×{HotLoopLength} · Coalesced+ReplaySafe · group-commit A/B ===");
        output.WriteLine("Each level: paired OFF/ON measured back-to-back, order alternated across repeats so load drift cancels.");
        output.WriteLine("N | rep | order | OFF wall(ms) | ON wall(ms) | ON/OFF | OFF thr | ON thr | batchFlushes | batchedMembers | soloFlushes | degraded | aggCommits");
        const int repeats = 3;
        // Focus on the saturated regime where a single writer is contended (N=1 no-regression is a one-shot check
        // below); each level is repeated with alternating order to average out ambient machine load. Mid-N levels
        // (16, 64) probe where along the curve batching starts to pay, which is what decides the default.
        int[] levels = [8, 16, 32, 64, 128];
        var soloProbe = await MeasureSharedSqliteAsync(concurrency: 1, groupCommit: true, warmup: false);
        var soloBaseline = await MeasureSharedSqliteAsync(concurrency: 1, groupCommit: false, warmup: false);
        output.WriteLine($"N=1 solo no-regression check: OFF {soloBaseline.TotalWallMs:F1} ms vs ON {soloProbe.TotalWallMs:F1} ms (ON soloFlushes={soloProbe.SoloFlushes}, batchFlushes={soloProbe.BatchFlushes})");
        foreach (var concurrency in levels)
        {
            for (var rep = 0; rep < repeats; rep++)
            {
                // Alternate which variant runs first each repeat so a monotonic load drift within a pair does not
                // systematically favor one variant; the paired ratio is the load-robust signal.
                var onFirst = rep % 2 == 1;
                GroupCommitResult off, on;
                if (onFirst)
                {
                    on = await MeasureSharedSqliteAsync(concurrency, groupCommit: true, warmup: false);
                    off = await MeasureSharedSqliteAsync(concurrency, groupCommit: false, warmup: false);
                }
                else
                {
                    off = await MeasureSharedSqliteAsync(concurrency, groupCommit: false, warmup: false);
                    on = await MeasureSharedSqliteAsync(concurrency, groupCommit: true, warmup: false);
                }

                var ratio = off.TotalWallMs > 0 ? on.TotalWallMs / off.TotalWallMs : double.NaN;
                var offThr = off.TotalWallMs > 0 ? concurrency / (off.TotalWallMs / 1000.0) : double.NaN;
                var onThr = on.TotalWallMs > 0 ? concurrency / (on.TotalWallMs / 1000.0) : double.NaN;
                output.WriteLine(
                    $"{concurrency,4} | {rep,3} | {(onFirst ? "ON,OFF" : "OFF,ON"),6} | {off.TotalWallMs,11:F1} | {on.TotalWallMs,10:F1} | {ratio,6:F2} | " +
                    $"{offThr,7:F1} | {onThr,6:F1} | {on.BatchFlushes,12} | {on.BatchedMembers,14} | {on.SoloFlushes,11} | {on.DegradedBatches,8} | {on.AggregateCommits,10}");
            }
        }
    }

    private async Task<GroupCommitResult> MeasureSharedSqliteAsync(int concurrency, bool groupCommit, bool warmup)
    {
        var shared = await NewSqliteStoreAsync();
        var coordinator = groupCommit ? new RuntimeGroupCommitCoordinator(shared.Store, new RuntimeGroupCommitOptions()) : null;

        var harnesses = new List<WorkflowExecutionHarness>(concurrency);
        var executables = new List<WorkflowExecutable>(concurrency);
        for (var index = 0; index < concurrency; index++)
        {
            var identity = IdentityFor(index);
            var harness = NewDurableHarness(shared.Store, identity, $"wfexec-{index}", NewActivityIdPool(index), coordinator);
            harness.InitializeActivityTypes();
            harnesses.Add(harness);
            executables.Add(BenchmarkWorkflows.HotLoop(HotLoopLength, BenchmarkWorkflows.NoOpLeaf, identity));
        }

        var (totalWallMs, latencies) = await TimeConcurrentRunsAsync(harnesses, executables);
        var aggregateCommits = await CountCheckpointCommitsAsync(shared.Store);

        foreach (var harness in harnesses)
            await harness.DisposeAsync();
        await DisposeStoreAsync(shared.Store, shared.Path);

        return new GroupCommitResult(
            totalWallMs,
            Percentile(latencies, 50),
            Percentile(latencies, 95),
            aggregateCommits,
            coordinator?.BatchFlushCount ?? 0,
            coordinator?.BatchedMemberCount ?? 0,
            coordinator?.SoloFlushCount ?? 0,
            coordinator?.DegradedBatchCount ?? 0);
    }

    private readonly record struct GroupCommitResult(
        double TotalWallMs,
        double P50,
        double P95,
        long AggregateCommits,
        long BatchFlushes,
        long BatchedMembers,
        long SoloFlushes,
        long DegradedBatches);

    private async Task<(double TotalWallMs, double[] SortedLatencies)> RunPostgresLevelAsync(IDocumentStore store, int concurrency, bool warmup)
    {
        var harnesses = new List<WorkflowExecutionHarness>(concurrency);
        var executables = new List<WorkflowExecutable>(concurrency);
        for (var index = 0; index < concurrency; index++)
        {
            // Warmup and each measured level share one DB; give every run a globally-unique id space (level tag +
            // index) so their work-item-derived checkpoint CommitIds never collide across levels on the shared store.
            var tag = warmup ? $"w{index}" : $"n{concurrency}-{index}";
            var identity = new WorkflowExecutableIdentity($"artifact-{tag}", $"definition-{tag}", "version-1", "1.0.0", "sha256:test");
            var executionId = $"wfexec-{tag}";
            var activityIds = Enumerable.Range(0, 64).Select(slot => $"actexec-{tag}-{slot}").ToArray();
            var harness = NewDurableHarness(store, identity, executionId, activityIds);
            harness.InitializeActivityTypes();
            harnesses.Add(harness);
            executables.Add(BenchmarkWorkflows.HotLoop(HotLoopLength, BenchmarkWorkflows.NoOpLeaf, identity));
        }

        var result = await TimeConcurrentRunsAsync(harnesses, executables);
        foreach (var harness in harnesses)
            await harness.DisposeAsync();
        return result;
    }

    private async Task<ConcurrencyResult> MeasureAsync(Backend backend, int concurrency, bool warmup)
    {
        var trackedStores = new List<(IDocumentStore Store, string Path)>();
        IDocumentStore? sharedStore = null;

        if (backend == Backend.SharedSqlite)
        {
            var shared = await NewSqliteStoreAsync();
            sharedStore = shared.Store;
            trackedStores.Add(shared);
        }

        var harnesses = new List<WorkflowExecutionHarness>(concurrency);
        var executables = new List<WorkflowExecutable>(concurrency);

        for (var index = 0; index < concurrency; index++)
        {
            var identity = IdentityFor(index);
            var executionId = $"wfexec-{index}";

            var activityIds = NewActivityIdPool(index);
            WorkflowExecutionHarness harness;
            switch (backend)
            {
                case Backend.InMemory:
                    harness = NewInMemoryHarness(identity, executionId, activityIds);
                    break;
                case Backend.IsolatedSqlite:
                    var isolated = await NewSqliteStoreAsync();
                    trackedStores.Add(isolated);
                    harness = NewDurableHarness(isolated.Store, identity, executionId, activityIds);
                    break;
                case Backend.SharedSqlite:
                    harness = NewDurableHarness(sharedStore!, identity, executionId, activityIds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(backend), backend, null);
            }

            // Pay per-provider setup (registry scan) BEFORE the timed window so wall time is execution, not construction.
            harness.InitializeActivityTypes();
            harnesses.Add(harness);
            executables.Add(BenchmarkWorkflows.HotLoop(HotLoopLength, BenchmarkWorkflows.NoOpLeaf, identity));
        }

        var (totalWallMs, latencies) = await TimeConcurrentRunsAsync(harnesses, executables);

        // Aggregate durable checkpoint commits (deterministic evidence). Shared store: one query over all N runs;
        // isolated: sum per-run stores; in-memory: none.
        long? aggregateCommits = backend == Backend.InMemory ? null : 0;
        if (backend == Backend.SharedSqlite)
            aggregateCommits = await CountCheckpointCommitsAsync(sharedStore!);
        else if (backend == Backend.IsolatedSqlite)
            foreach (var (store, _) in trackedStores)
                aggregateCommits += await CountCheckpointCommitsAsync(store);

        foreach (var harness in harnesses)
            await harness.DisposeAsync();
        foreach (var (store, path) in trackedStores)
            await DisposeStoreAsync(store, path);

        return new ConcurrencyResult(totalWallMs, Percentile(latencies, 50), Percentile(latencies, 95),
            latencies.Length > 0 ? latencies[0] : double.NaN,
            latencies.Length > 0 ? latencies[^1] : double.NaN,
            aggregateCommits);
    }

    /// <summary>
    /// Times N concurrent runs (each harness runs its own executable under its own stopwatch) and returns the total
    /// wall clock plus the per-run latencies sorted ascending. Shared by every backend, including Postgres.
    /// </summary>
    private static async Task<(double TotalWallMs, double[] SortedLatencies)> TimeConcurrentRunsAsync(
        IReadOnlyList<WorkflowExecutionHarness> harnesses,
        IReadOnlyList<WorkflowExecutable> executables)
    {
        var latencies = new double[harnesses.Count];
        var wall = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, harnesses.Count).Select(index => Task.Run(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            var run = await harnesses[index].RunAsync(executables[index]);
            stopwatch.Stop();
            run.AssertWorkflowCompleted();
            latencies[index] = stopwatch.Elapsed.TotalMilliseconds;
        })));
        wall.Stop();
        Array.Sort(latencies);
        return (wall.Elapsed.TotalMilliseconds, latencies);
    }

    // ---- Harness construction --------------------------------------------------------------------------------

    private static WorkflowExecutionHarness NewInMemoryHarness(WorkflowExecutableIdentity identity, string executionId, IEnumerable<string> activityIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .Build(identity, executionId, activityIds);

    private static WorkflowExecutionHarness NewDurableHarness(IDocumentStore store, WorkflowExecutableIdentity identity, string executionId, IEnumerable<string> activityIds) =>
        NewDurableHarness(store, identity, executionId, activityIds, groupCommit: null);

    private static WorkflowExecutionHarness NewDurableHarness(
        IDocumentStore store,
        WorkflowExecutableIdentity identity,
        string executionId,
        IEnumerable<string> activityIds,
        RuntimeGroupCommitCoordinator? groupCommit) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // The shipping durable configuration: durable Groundwork bridges over the (possibly shared) SQLite
                // store, Coalesced checkpoint persistence (cap above the burst so it never trips), burst-scoped
                // executable cache on.
                services.AddSingleton(store);
                services.AddSingleton<IBoundedDocumentStore>(new RuntimeTestBoundedDocumentStore(store));
                services.AddGroundworkRuntimeStores();
                services.AddCoalescingRuntimeCheckpointPersistence(options => options.MaxSegmentCheckpoints = HotLoopSegmentCap);
                services.RemoveAll<RuntimeBurstCacheOptions>();
                services.AddSingleton(new RuntimeBurstCacheOptions { Enabled = true });

                // Group commit (spec 115): each concurrent harness owns its own DI container but shares ONE store, so a
                // per-container coordinator would only ever see its single execution and always flush solo. Injecting one
                // shared coordinator instance across every harness models production's single host-wide coordinator that
                // all concurrent drains resolve, which is what lets cross-drain commits fold onto one fsync.
                if (groupCommit is not null)
                    services.AddSingleton(groupCommit);
            })
            .Build(identity, executionId, activityIds);

    private static async Task<(IDocumentStore Store, string Path)> NewSqliteStoreAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-conc-bench-{Guid.NewGuid():N}.db");
        var store = await SqliteDocumentStoreFactory.CreateAsync(
            $"Data Source={databasePath}",
            ElsaRuntimeStorageManifest.CreatePhysicalized(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            GroundworkTestAccess.DefaultScoped);
        return (store, databasePath);
    }

    private static async Task<long> CountCheckpointCommitsAsync(IDocumentStore store)
    {
#pragma warning disable GW0004
        var result = await store.QueryAsync(
            new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004
        return result.TotalCount;
    }

    private static async Task DisposeStoreAsync(IDocumentStore store, string databasePath)
    {
        if (store is IAsyncDisposable asyncStore)
            await asyncStore.DisposeAsync();
        else if (store is IDisposable disposableStore)
            disposableStore.Dispose();

        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a leftover file must not fail the benchmark.
            }
        }
    }

    // ---- Reporting helpers -----------------------------------------------------------------------------------

    private static string FormatCommits(long? aggregate) => aggregate?.ToString() ?? "-";

    private static string FormatCommitsPerRun(long? aggregate, int concurrency) =>
        aggregate is { } value && concurrency > 0 ? (value / (double)concurrency).ToString("F1") : "-";

    private static double Percentile(IReadOnlyList<double> orderedAscending, double percentile)
    {
        if (orderedAscending.Count == 0)
            return double.NaN;
        var rank = (int)Math.Ceiling(percentile / 100.0 * orderedAscending.Count);
        return orderedAscending[Math.Clamp(rank - 1, 0, orderedAscending.Count - 1)];
    }

    private static string ReadUptime()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("uptime") { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null)
                return "unavailable";
            var text = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return text.Length == 0 ? "unavailable" : text;
        }
        catch
        {
            return "unavailable";
        }
    }

    private readonly record struct ConcurrencyResult(
        double TotalWallMs,
        double P50,
        double P95,
        double Min,
        double Max,
        long? AggregateCommits);
}
