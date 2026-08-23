using Groundwork.Store;
using System.Diagnostics;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Capabilities;
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
/// The curve is swept over THREE LEAF SHAPES (issue #1225). It originally fixed the leaf at <c>NoOpStep</c>, a
/// ReplaySafe leaf that exists only in this assembly and commits once per run; the marking pass behind ADR 0047 found
/// no SHIPPED leaf activity is ReplaySafe, so that curve described the fusable floor, not production traffic. The
/// External CLR leaf (an unmarked <c>WriteLine</c>) pays ~11 commits and ~56 dispatches per run against the fusable
/// shapes' 1 and 5, so it is the shape an admission-control limiter must be sized against. Alongside it the <c>Set</c>
/// intrinsic — where Elsa 4's pure value work actually happens, fusable only since the blanket intrinsic exclusion
/// became per-kind — and the original ReplaySafe leaf, kept as the reference bound.
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
/// (<c>InProcessWorkflowExecutionActorProvider</c>) serializes commands only per workflow-execution id (a per-actor
/// mailbox), and distinct ids drain fully in parallel. So N one-actor engines over one shared store are behaviorally
/// equivalent, for drain concurrency, to one engine hosting N actors, while being far simpler to stand up. The one
/// thing this arrangement does NOT reproduce is a host-wide limiter: admission control (RB1, #1235) is a singleton
/// per container, so these curves each run with N private controllers that never see each other. Only
/// <see cref="ConcurrencyScalingCurve_AdmissionControl"/> injects one shared controller across the harnesses, which
/// is why it is the arm that measures the limiter. Each harness gets a DISTINCT execution id + executable identity so the shared store
/// partitions them cleanly (state/scheduler/executable documents all key on those ids). Per-provider setup — the DI
/// build, the activity-type registry scan (<see cref="WorkflowExecutionHarness.InitializeActivityTypes"/>) — is done
/// BEFORE the timed window, so wall time measures dispatch + drain + store I/O, not engine construction.
///
/// A level may also FAIL outright rather than merely slow down, and that outcome is part of the curve: the
/// <c>WorkflowDrainOrchestrator</c> renews its ownership lease (<c>RuntimeExecutionOwnershipOptions.LeaseDuration</c>,
/// 1 minute by default) on a duration/3 cadence, and that renewal is itself a store write behind the same connection
/// gate the drains are queued on. Past a queue depth the renewal starves and the execution faults, which is congestion
/// collapse becoming correctness-visible. Such a level is recorded as FAULTED and the sweep continues, so the rest of
/// the curve is not lost with it.
///
/// The fault surfaces as a DIFFERENT exception type per backend, all of them lease loss under load:
/// <c>RuntimeSchedulerWorkClaimLostException</c> (in-memory), <c>RuntimeStaleFencingTokenException</c> (isolated
/// SQLite), <c>RuntimeExecutionOwnershipLostException</c> (shared SQLite). Which one surfaces is not fixed either: the
/// threshold moves with ambient host load. That is why the catch below is deliberately broad and records the exception
/// TYPE rather than filtering on one of them. See issue #1249, which is scoped to reproducing this on a quiet machine
/// before anyone concludes what the mechanism is.
///
/// There are deliberately NO hard latency/throughput assertions (they would flake); each run only asserts its
/// workflow completed. Commit AND scheduler-dispatch counts are the deterministic, load-proof evidence — a curve
/// reporting only wall time cannot falsify its own claim, because it cannot separate "the engine did more work" from
/// "the shared writer congested". Dispatches come from <see cref="RuntimeSchedulerDispatchDiagnostics"/> (spec 123
/// FR-008), the same counter the single-run engine benchmark uses. Emits the curve via
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

    /// <summary>
    /// The hot-loop leaf shapes the curve is measured over (issue #1225). The original spec-114 curve fixed the leaf
    /// at <see cref="BenchmarkWorkflows.NoOpLeaf"/>, a ReplaySafe leaf that exists only in this assembly and commits
    /// once per run — the fusable FLOOR, not a shape any user can compose. Sweeping the leaf makes the leaf class the
    /// only variable, so the shape of the collapse can be read against per-run durable work rather than assumed.
    /// </summary>
    private readonly record struct LeafShape(string Name, Func<int, ExecutableNode> MakeLeaf);

    private static readonly LeafShape[] LeafShapes =
    [
        new("External CLR leaf (WriteLine)", BenchmarkWorkflows.ExternalLeaf),
        new("Set intrinsic", BenchmarkWorkflows.SetIntrinsicLeaf),
        new("ReplaySafe CLR leaf (NoOpStep)", BenchmarkWorkflows.NoOpLeaf)
    ];

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

        // The External leaf is a real WriteLine, so N×HotLoopLength Console writes would serialize on Console's own
        // global lock and be charged to the store writer. Park stdout for the whole sweep so the only shared writer in
        // the measurement is the one under study; ITestOutputHelper does not go through Console.Out, so the curve is
        // still emitted. Restored in the finally.
        var stdout = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            // One warmup pass per backend (JIT + OS page-cache + SQLite file warmup), discarded. Warm at a mid N so the
            // thread pool has already grown before the first measured level. Warm on the External shape: it exercises
            // strictly the most code (no fusion, per-leaf attempt-claim flush), so it subsumes the other two shapes' JIT.
            foreach (var backend in Enum.GetValues<Backend>())
                await MeasureAsync(backend, LeafShapes[0], concurrency: 8, warmup: true);

            // Sweep order is backend → N → leaf shape, so the three shapes at a given (backend, N) are measured
            // BACK-TO-BACK. The comparison this instrument exists to make is across leaf shapes at a fixed N, and the
            // machine's ambient load drifts over a sweep this long; pairing the shapes tightly is what keeps that
            // comparison readable when the absolute walls are not (the same pairing discipline the group-commit A/B uses).
            foreach (var backend in Enum.GetValues<Backend>())
            {
                // Only the durable backends configure Coalesced persistence. Fusion is a burst-only locality
                // optimization — ReplaySafeFusionDriver.ShouldFuse requires a live coalescing session — so the
                // in-memory backend runs every hop discretely and is the UNFUSED baseline, not a Coalesced one. Label
                // it as such: its 58 dispatches/run are exactly ADR 0047's pre-fusion figure, and calling that column
                // "Coalesced" (as this instrument originally did for all three backends) invites reading a 58-vs-5
                // difference as a leaf-shape effect when it is a policy effect.
                var policy = backend == Backend.InMemory ? "runtime defaults (Immediate, unfused)" : "Coalesced";
                output.WriteLine($"=== backend: {backend} · hot-loop×{HotLoopLength} · {policy} · leaf shapes measured back-to-back per N ===");
                output.WriteLine("N | leaf shape | totalWall(ms) | p50(ms) | p95(ms) | min(ms) | max(ms) | aggCommits | commits/run | aggDispatches | dispatches/run | throughput(runs/s)");
                foreach (var concurrency in ConcurrencyLevels)
                foreach (var shape in LeafShapes)
                {
                    // A level can FAIL rather than merely slow down. On the shared writer the External shape queues so
                    // deep behind the connection gate that the drain's own ownership heartbeat, itself a store write
                    // behind that same gate, misses its renewal and the execution faults. That outcome IS the
                    // measurement (it is the congestion collapse turning into a correctness-visible fault), so record
                    // it and carry on to the remaining cells instead of aborting the sweep and losing them.
                    //
                    // The catch is deliberately BROAD and must stay that way. The observed fault is a different
                    // exception type per backend -- RuntimeSchedulerWorkClaimLostException (in-memory),
                    // RuntimeStaleFencingTokenException (isolated), RuntimeExecutionOwnershipLostException (shared) --
                    // so narrowing it to any one of them aborts the sweep on the other two and loses every remaining
                    // cell, which is the exact failure this arm was added to prevent. The threshold is not
                    // deterministic (it moves with host load), so which type surfaces is not fixed either. The
                    // diagnosability cost is paid by recording exception.GetType().Name below: an unexpected exception
                    // is visible as itself rather than silently absorbed, so a harness bug stays distinguishable from
                    // a lease fault.
                    ConcurrencyResult result;
                    try
                    {
                        result = await MeasureAsync(backend, shape, concurrency, warmup: false);
                    }
                    catch (Exception exception)
                    {
                        output.WriteLine($"{concurrency,4} | {shape.Name,-31} | FAULTED — {exception.GetType().Name}: {exception.Message}");
                        continue;
                    }

                    var throughput = result.TotalWallMs > 0 ? concurrency / (result.TotalWallMs / 1000.0) : double.NaN;
                    output.WriteLine(
                        $"{concurrency,4} | {shape.Name,-31} | {result.TotalWallMs,10:F1} | {result.P50,7:F1} | {result.P95,7:F1} | " +
                        $"{result.Min,7:F1} | {result.Max,7:F1} | {FormatCommits(result.AggregateCommits),10} | " +
                        $"{FormatCommitsPerRun(result.AggregateCommits, concurrency),11} | {result.AggregateDispatches,13} | " +
                        $"{result.AggregateDispatches / (double)concurrency,15:F1} | {throughput,8:F1}");
                }
            }
        }
        finally
        {
            Console.SetOut(stdout);
        }

        // Load at the END as well as the start: a sweep this long outlives any single load reading, and a reader
        // comparing rows across backends needs to see whether the host drifted underneath them.
        output.WriteLine($"machine uptime/load at end: {ReadUptime()}");
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

        // The v1 container driver went with the document substrate. This level now runs against whatever
        // PostgreSQL the environment points at, and skips when it points at none.
        var connectionString = Environment.GetEnvironmentVariable("ELSA_TEST_POSTGRESQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("SKIPPED shared-postgres: set ELSA_TEST_POSTGRESQL to run this level.");
            return;
        }

        using var store = GroundworkBenchmarkStore.OpenPostgreSql(connectionString);

        {
            // Warmup pass (discarded), then the measured curve. Commits accumulate on the one shared DB, so each level
            // reports the DELTA in checkpoint-commit documents (== that level's runs; deterministically 1/run).
            await RunPostgresLevelAsync(store, concurrency: 8, warmup: true);
            var commitsSoFar = GroundworkBenchmarkStore.CountCheckpointCommits(store);

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
                    var after = GroundworkBenchmarkStore.CountCheckpointCommits(store);
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
    }

    // ConcurrencyScalingCurve_GroupCommit measured RuntimeGroupCommitCoordinator, which batches flushes on
    // the v1 document store. Group commit has no Groundwork v2 counterpart, so the benchmark went with the
    // substrate it measured rather than being pointed at something that does not exist.

    private async Task<(double TotalWallMs, double[] SortedLatencies)> RunPostgresLevelAsync(IStorageProviderConnection store, int concurrency, bool warmup)
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

    private async Task<ConcurrencyResult> MeasureAsync(Backend backend, LeafShape shape, int concurrency, bool warmup)
    {
        var trackedStores = new List<(IStorageProviderConnection Store, string Path)>();
        IStorageProviderConnection? sharedStore = null;

        if (backend == Backend.SharedSqlite)
        {
            var shared = NewSqliteStore();
            sharedStore = shared.Store;
            trackedStores.Add(shared);
        }

        var harnesses = new List<WorkflowExecutionHarness>(concurrency);
        var executables = new List<WorkflowExecutable>(concurrency);

        try
        {
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
                        var isolated = NewSqliteStore();
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
                executables.Add(BenchmarkWorkflows.HotLoop(HotLoopLength, shape.MakeLeaf, identity));
            }

            var (totalWallMs, latencies) = await TimeConcurrentRunsAsync(harnesses, executables);
            var aggregateDispatches = SumDispatches(harnesses);

            // Aggregate durable checkpoint commits (deterministic evidence). Shared store: one query over all N runs;
            // isolated: sum per-run stores; in-memory: none.
            long? aggregateCommits = backend == Backend.InMemory ? null : 0;
            if (backend == Backend.SharedSqlite)
                aggregateCommits = GroundworkBenchmarkStore.CountCheckpointCommits(sharedStore!);
            else if (backend == Backend.IsolatedSqlite)
                foreach (var (tracked, _) in trackedStores)
                    aggregateCommits += GroundworkBenchmarkStore.CountCheckpointCommits(tracked);

            return new ConcurrencyResult(totalWallMs, Percentile(latencies, 50), Percentile(latencies, 95),
                latencies.Length > 0 ? latencies[0] : double.NaN,
                latencies.Length > 0 ? latencies[^1] : double.NaN,
                aggregateCommits,
                aggregateDispatches);
        }
        finally
        {
            // Cleanup must survive a level that FAILS rather than merely slows: on the shared writer the External shape
            // can starve its own ownership heartbeat and throw (see ConcurrencyScalingCurve), and without this the
            // failing level would leak N providers and a temp database on the way out.
            foreach (var harness in harnesses)
                await harness.DisposeAsync();
            foreach (var (tracked, path) in trackedStores)
                await DisposeStoreAsync(tracked, path);
        }
    }

    /// <summary>
    /// Aggregate scheduler dispatches over the N harnesses of one level (spec 123 FR-008 / PR #1214's counter). Each
    /// concurrent harness owns its own DI container, so each resolves its OWN singleton counter and the level total is
    /// the sum. Unlike wall time this is deterministic and load-proof, so it is the part of the curve that can falsify
    /// its own claim: it separates "the engine is doing more work" from "the shared writer is congested".
    /// </summary>
    private static long SumDispatches(IEnumerable<WorkflowExecutionHarness> harnesses) =>
        harnesses.Sum(harness => harness.Services.GetService<RuntimeSchedulerDispatchDiagnostics>()?.Dispatches ?? 0);

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

    private static WorkflowExecutionHarness NewDurableHarness(
        IStorageProviderConnection store,
        WorkflowExecutableIdentity identity,
        string executionId,
        IEnumerable<string> activityIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // The shipping durable configuration: durable Groundwork bridges over the (possibly shared) SQLite
                // store, Coalesced checkpoint persistence (cap above the burst so it never trips), burst-scoped
                // executable cache on.
                services.AddGroundworkStorageProviderConnection(_ => store);
                services.AddGroundworkV2RuntimeStores();
                services.AddCoalescingRuntimeCheckpointPersistence(options => options.MaxSegmentCheckpoints = HotLoopSegmentCap);
                services.RemoveAll<RuntimeBurstCacheOptions>();
                services.AddSingleton(new RuntimeBurstCacheOptions { Enabled = true });

                // Group commit (spec 115): each concurrent harness owns its own DI container but shares ONE store, so a
                // per-container coordinator would only ever see its single execution and always flush solo. Injecting one
                // shared coordinator instance across every harness models production's single host-wide coordinator that
                // all concurrent drains resolve, which is what lets cross-drain commits fold onto one fsync.
            })
            .Build(identity, executionId, activityIds);

    private static (IStorageProviderConnection Store, string Path) NewSqliteStore()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-conc-bench-{Guid.NewGuid():N}.db");
        var opened = GroundworkBenchmarkStore.Open(databasePath);
        return (opened, databasePath);
    }

    private static async Task DisposeStoreAsync(IStorageProviderConnection store, string databasePath)
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
        long? AggregateCommits,
        long AggregateDispatches);
}
