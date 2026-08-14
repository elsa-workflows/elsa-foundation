using System.Diagnostics;
using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// Per-hop envelope-build stage-timing diagnostic for the runtime-execution-seam phase-4 unit (spec 130). Where
/// <see cref="BufferedCommitStageDiagnostics"/> starts its timers at <c>committer.CommitAsync</c>, this instrument
/// measures the CPU spent <b>upstream</b> of the committer — the three stage-core envelope builds each activity pays
/// per hop in both the discrete and the spec-123 fused paths (<c>BuildScheduledCommitAsync</c>,
/// <c>BuildStartedCommitAsync</c>, <c>BuildCompletedCommitAsync</c>). Spec 123 removed dispatches, not construction,
/// so these costs are invisible to every existing instrument.
///
/// It captures, per cadence mode (Immediate, Coalesced(cap256)) over hot-loop×10:
///   (a) <c>BuildProjectionAsync</c> wall + count via a timing decorator on
///       <see cref="IRuntimeActivityExecutionInspectionAccumulator"/> (registered last so both paths resolve it), of
///       which the durable <c>FindAsync</c> store-read share via a decorator on
///       <see cref="IActivityExecutionInspectionStore"/> — with inspection-read coalescing OFF (the control row) the
///       projection build is a per-hop durable store read; with it ON the coalescing memo absorbs the intermediate
///       builds' reads, leaving one durable read per distinct activity execution per coalesced window;
///   (b) the inter-commit gap (wall between successive committer -> store calls in one drain) = an upper bound on the
///       per-hop envelope build, since construction happens between commits;
///   (c) allocations via process-wide <c>GC.GetTotalAllocatedBytes()</c> across the run plus per-hop
///       <c>GC.GetAllocatedBytesForCurrentThread()</c> bracketed inside the accumulator (thread-affine, best-effort),
///       plus GC collection counts.
///
/// The pinned in-memory hot-loop denominator (research.md) is <c>totalRunWall − durableFlushWall</c>; the durable
/// flush wall is isolated by a timer captured BEFORE the coalescing decorator (ticks only on a real flushed segment),
/// exactly as <see cref="BufferedCommitStageDiagnostics"/> does. Numerator and denominator are captured in the same
/// run under the same fleet load, so the reported CPU share is load-invariant to first order.
///
/// Lives under benchmarks/ (not tests/), so neither CI test gate runs it. Run on demand with:
/// dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj \
///   --filter "FullyQualifiedName~EnvelopeBuildStageDiagnostics" --logger "console;verbosity=detailed"
/// </summary>
public sealed class EnvelopeBuildStageDiagnostics(ITestOutputHelper output)
{
    private const int HotLoopLength = 10;
    private const int SegmentCap = 256;

    [Theory]
    [InlineData(false, true, "hotloop10 Immediate")]
    [InlineData(true, true, "hotloop10 Coalesced(cap256)")]
    [InlineData(true, false, "hotloop10 Coalesced(cap256) inspection-read-coalescing OFF (control)")]
    public async Task StageBreakdown(bool coalesce, bool coalesceInspectionReads, string label)
    {
        var metrics = new EnvelopeMetrics();
        var databasePath = Path.Combine(Path.GetTempPath(), $"envelope-stage-{Guid.NewGuid():N}.db");
        var opened = await GroundworkBenchmarkStore.OpenAsync(databasePath);
        IDocumentStore store = opened.Store;

        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IDocumentStore>(store);
                services.AddSingleton<IBoundedDocumentStore>(opened.Queries);
                services.AddGroundworkRuntimeStores();

                // (a) INNER durable-flush timer: replace the Groundwork checkpoint writer BEFORE coalescing captures it
                // as CoalescingInner, so it ticks only on a real flushed segment. Its total wall is the pinned
                // durableFlushWall used to isolate the in-memory hot-loop denominator.
                DecorateCommitStore(services, inner => new TimingCommitStore(inner, metrics.DurableFlush));

                // (a) Inspection-store decorator: isolates the per-hop durable FindAsync store-read share of the
                // projection build. Scoped registration -> decorate the scoped descriptor.
                DecorateInspectionStore(services, inner => new TimingInspectionStore(inner, metrics));

                // (a) Accumulator decorator: total BuildProjectionAsync wall + count, and per-call thread-affine
                // allocation bracket. Registered last so both the discrete and the fused drivers resolve it.
                DecorateAccumulator(services, inner => new TimingInspectionAccumulator(inner, metrics));

                if (coalesce)
                {
                    services.AddCoalescingRuntimeCheckpointPersistence(o =>
                    {
                        o.MaxSegmentCheckpoints = SegmentCap;
                        o.CoalesceInspectionReads = coalesceInspectionReads;
                    });
                }

                // (b) OUTER committer-facing decorator: after coalescing, so the committer resolves THIS one. Records
                // the wall GAP between the end of the previous committer store call and the start of this one = the
                // per-hop non-committer stage work (envelope build + dispatch) upper bound.
                DecorateCommitStore(services, inner => new InterCommitGapStore(inner, metrics));
            })
            .Build(Enumerable.Range(0, 64).Select(i => $"actexec-{i}").ToArray());

        // (c) Allocations + GC counts bracket the whole run. Process-wide GetTotalAllocatedBytes is robust to the
        // async thread-hops the drain performs (unlike the thread-affine per-call bracket inside the accumulator).
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var allocBefore = GC.GetTotalAllocatedBytes();
        var runStart = Stopwatch.GetTimestamp();

        var run = await harness.RunAsync(BuildHotLoop());

        var runWallMs = (Stopwatch.GetTimestamp() - runStart) * 1000.0 / Stopwatch.Frequency;
        var allocTotal = GC.GetTotalAllocatedBytes() - allocBefore;
        var gc0Delta = GC.CollectionCount(0) - gc0;
        var gc1Delta = GC.CollectionCount(1) - gc1;
        var gc2Delta = GC.CollectionCount(2) - gc2;
        run.AssertWorkflowCompleted();

        var dispatchDiagnostics = harness.Services.GetService<RuntimeSchedulerDispatchDiagnostics>();
        var dispatches = dispatchDiagnostics?.Dispatches ?? 0;
        var fusedSpans = dispatchDiagnostics?.FusedSpans ?? 0;

        // Denominators. Hops = BuildProjectionAsync calls (the reliable per-stage-core count); also report dispatches.
        var hops = metrics.BuildProjectionCount;
        var inMemoryHotLoopCpuMs = Math.Max(0.0001, runWallMs - metrics.DurableFlush.Ms);

        double PerHopUs(double totalMs, long count) => count == 0 ? 0 : totalMs / count * 1000.0;
        double Share(double partMs) => partMs / inMemoryHotLoopCpuMs * 100.0;

        var inspectionShare = Share(metrics.BuildProjectionMs);
        var constructionMs = Math.Max(0, metrics.BuildProjectionMs - metrics.FindMs); // build wall minus the durable store read = pure object construction
        var constructionShare = Share(constructionMs);
        var envelopeShare = Share(metrics.InterCommitGapMs);
        var bytesPerHop = hops == 0 ? 0 : allocTotal / hops;

        output.WriteLine($"=== {label} — per-hop envelope-build breakdown (1 run, hot-loop×{HotLoopLength}) ===");
        output.WriteLine($"denominators: BuildProjection hops={hops}  dispatches={dispatches}  fused-spans={fusedSpans}");
        output.WriteLine($"run wall = {runWallMs:F3} ms   durable-flush wall (isolated) = {metrics.DurableFlush.Ms:F3} ms ({metrics.DurableFlush.Count} flush write(s))");
        output.WriteLine($"=> in-memory hot-loop CPU denominator = run − flush = {inMemoryHotLoopCpuMs:F3} ms");
        output.WriteLine("");
        output.WriteLine("(a) inspection projection build (upstream of committer):");
        output.WriteLine($"    BuildProjectionAsync: count={metrics.BuildProjectionCount}  total={metrics.BuildProjectionMs:F3} ms  " +
                         $"per-hop={PerHopUs(metrics.BuildProjectionMs, metrics.BuildProjectionCount):F2} µs");
        output.WriteLine($"      of which durable FindAsync store-read: count={metrics.FindCount}  total={metrics.FindMs:F3} ms  " +
                         $"per-hop={PerHopUs(metrics.FindMs, metrics.FindCount):F2} µs  " +
                         $"({(metrics.BuildProjectionMs <= 0 ? 0 : metrics.FindMs / metrics.BuildProjectionMs * 100.0):F1}% of build)");
        output.WriteLine($"        FindAsync returned null (miss): {metrics.FindNullCount}/{metrics.FindCount}  |  distinct activity executions={metrics.DistinctActivityExecutions}  " +
                         $"=> fold ratio {metrics.BuildProjectionCount}builds:{metrics.DistinctActivityExecutions}activities " +
                         $"({(metrics.DistinctActivityExecutions == 0 ? 0 : (double)metrics.BuildProjectionCount / metrics.DistinctActivityExecutions):F2}× builds/activity, intermediates folded away)");
        output.WriteLine($"      of which pure object CONSTRUCTION (build − find = items 1/4 metadata+ctor, NO I/O): " +
                         $"{constructionMs:F3} ms  per-hop={PerHopUs(constructionMs, metrics.BuildProjectionCount):F2} µs  ({constructionShare:F2}% of in-memory CPU)");
        output.WriteLine($"      per-hop projection-build allocations (thread-affine, best-effort — includes FindAsync deserialize): " +
                         $"{(metrics.BuildProjectionCount == 0 ? 0 : metrics.BuildProjectionAllocBytes / metrics.BuildProjectionCount)} B/hop " +
                         $"(total {metrics.BuildProjectionAllocBytes} B)");
        output.WriteLine("");
        output.WriteLine("(b) inter-commit gap — LOOSE upper bound: contains activity-execute + scheduler dispatch +");
        output.WriteLine("    thread-scheduling latency in ADDITION to envelope build, so it is NOT a clean envelope-build");
        output.WriteLine("    isolator on a loaded box (compare its per-hop µs to the construction µs above to see the gap):");
        output.WriteLine($"    gaps={metrics.InterCommitGapCount}  total={metrics.InterCommitGapMs:F3} ms  " +
                         $"per-hop={PerHopUs(metrics.InterCommitGapMs, hops):F2} µs   committer store calls={metrics.CommitterStoreCalls}");
        output.WriteLine("");
        output.WriteLine("(c) allocations (process-wide, whole run — includes flush serialize + POCO state, NOT just envelope):");
        output.WriteLine($"    total={allocTotal} B  per-hop={bytesPerHop} B/hop  GC gen0/1/2 = {gc0Delta}/{gc1Delta}/{gc2Delta}");
        output.WriteLine("");
        output.WriteLine("CPU shares (numerator/denominator captured in the same run => load-invariant to first order):");
        output.WriteLine($"    inspection projection build   = {inspectionShare:F2}%  (of which durable FindAsync store-read {inspectionShare - constructionShare:F2}pp)");
        output.WriteLine($"    pure envelope CONSTRUCTION CPU = {constructionShare:F2}%  <== the actual 'envelope-building CPU' hypothesis");
        output.WriteLine($"    inter-commit gap (loose)      = {envelopeShare:F2}%  (scheduling-contaminated; not a clean isolator)");
        output.WriteLine("");
        output.WriteLine($"GATE (Coalesced-only, pinned): KILL if envelope-build CPU < 5% AND envelope alloc < ~2 KB/hop; " +
                         $"PROCEED if projection build >= ~10% OR total envelope build >= 15%. The decisive clean number is the");
        output.WriteLine($"    pure-construction CPU share ({constructionShare:F2}%); the inter-commit gap is a contaminated upper bound.");

        await harness.DisposeAsync();
        if (store is IAsyncDisposable ad) await ad.DisposeAsync();
        else if (store is IDisposable d) d.Dispose();
        foreach (var p in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            try { File.Delete(p); } catch (IOException) { }

        // Deterministic guardrail. Without the coalescing inspection-read memo (Immediate cadence, or the OFF control)
        // the projection build is a per-hop durable store read: FindAsync fires once per BuildProjectionAsync. With
        // the memo ON, a coalesced window pays exactly one durable read per distinct activity execution — the
        // intermediate builds the fold discards no longer read the store (this run flushes a single segment, so no
        // mid-run invalidation refreshes the memo).
        Assert.True(metrics.BuildProjectionCount > 0, "Expected the hot loop to build inspection projections.");
        if (coalesce && coalesceInspectionReads)
        {
            Assert.Equal(metrics.DistinctActivityExecutions, metrics.FindCount);
            Assert.True(metrics.BuildProjectionCount > metrics.FindCount,
                "Expected the memo to absorb the intermediate builds' durable reads.");
        }
        else
        {
            Assert.Equal(metrics.BuildProjectionCount, metrics.FindCount);
        }
    }

    // ---- DI decoration helpers --------------------------------------------------------------------------------------

    private static void DecorateCommitStore(
        IServiceCollection services,
        Func<IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore> decorate) =>
        Decorate(services, decorate);

    private static void DecorateInspectionStore(
        IServiceCollection services,
        Func<IActivityExecutionInspectionStore, IActivityExecutionInspectionStore> decorate) =>
        Decorate(services, decorate);

    private static void DecorateAccumulator(
        IServiceCollection services,
        Func<IRuntimeActivityExecutionInspectionAccumulator, IRuntimeActivityExecutionInspectionAccumulator> decorate) =>
        Decorate(services, decorate);

    // Replaces the last registration of TService with a decorator built over the previous one, preserving lifetime.
    private static void Decorate<TService>(IServiceCollection services, Func<TService, TService> decorate)
        where TService : class
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(TService));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(TService),
            sp =>
            {
                var inner = (TService)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
                return decorate(inner);
            },
            descriptor.Lifetime));
    }

    // ---- Metrics ----------------------------------------------------------------------------------------------------

    private sealed class TimerAccumulator
    {
        private long _count;
        private long _ticks;
        public long Count => Interlocked.Read(ref _count);
        public double Ms => Interlocked.Read(ref _ticks) * 1000.0 / Stopwatch.Frequency;
        public void Add(long ticks) { Interlocked.Increment(ref _count); Interlocked.Add(ref _ticks, ticks); }
    }

    private sealed class EnvelopeMetrics
    {
        public readonly TimerAccumulator DurableFlush = new();

        public long BuildProjectionCount;
        private long _buildProjectionTicks;
        public long BuildProjectionAllocBytes;
        public double BuildProjectionMs => Interlocked.Read(ref _buildProjectionTicks) * 1000.0 / Stopwatch.Frequency;
        public void RecordBuildProjection(long ticks, long allocBytes)
        {
            Interlocked.Increment(ref BuildProjectionCount);
            Interlocked.Add(ref _buildProjectionTicks, ticks);
            if (allocBytes > 0) Interlocked.Add(ref BuildProjectionAllocBytes, allocBytes);
        }

        public long FindCount;
        public long FindNullCount;
        private long _findTicks;
        private readonly HashSet<string> _distinctActivityExecutionIds = new(StringComparer.Ordinal);
        public int DistinctActivityExecutions { get { lock (_distinctActivityExecutionIds) return _distinctActivityExecutionIds.Count; } }
        public double FindMs => Interlocked.Read(ref _findTicks) * 1000.0 / Stopwatch.Frequency;
        public void RecordFind(long ticks, string activityExecutionId, bool wasNull)
        {
            Interlocked.Increment(ref FindCount);
            if (wasNull) Interlocked.Increment(ref FindNullCount);
            Interlocked.Add(ref _findTicks, ticks);
            lock (_distinctActivityExecutionIds) _distinctActivityExecutionIds.Add(activityExecutionId);
        }

        public long CommitterStoreCalls;
        public long InterCommitGapCount;
        private long _interCommitGapTicks;
        private long _lastCommitEndTimestamp; // 0 == no prior commit yet
        public double InterCommitGapMs => Interlocked.Read(ref _interCommitGapTicks) * 1000.0 / Stopwatch.Frequency;

        // The drain is single-writer sequential: the previous commit's EndCommit runs before this one's BeginCommit,
        // so the gap between the previous durable-commit call's end and this one's start is the per-hop non-committer
        // stage work (envelope build + dispatch). Called once each per commit.
        public void BeginCommit(long callStart)
        {
            Interlocked.Increment(ref CommitterStoreCalls);
            var prevEnd = Interlocked.Read(ref _lastCommitEndTimestamp);
            if (prevEnd != 0 && callStart > prevEnd)
            {
                Interlocked.Increment(ref InterCommitGapCount);
                Interlocked.Add(ref _interCommitGapTicks, callStart - prevEnd);
            }
        }

        public void EndCommit(long callEnd) => Interlocked.Exchange(ref _lastCommitEndTimestamp, callEnd);
    }

    // ---- Decorators -------------------------------------------------------------------------------------------------

    // Times the durable Groundwork checkpoint writer (registered before coalescing => ticks only on a real flush).
    private sealed class TimingCommitStore(IRuntimeCheckpointCommitStore inner, TimerAccumulator acc) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var start = Stopwatch.GetTimestamp();
            try { return await inner.CommitAsync(commit, decision, cancellationToken); }
            finally { acc.Add(Stopwatch.GetTimestamp() - start); }
        }
    }

    // Records the wall gap between successive committer -> store calls (upper bound on per-hop envelope build).
    private sealed class InterCommitGapStore(IRuntimeCheckpointCommitStore inner, EnvelopeMetrics metrics) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var callStart = Stopwatch.GetTimestamp();
            metrics.BeginCommit(callStart);
            try { return await inner.CommitAsync(commit, decision, cancellationToken); }
            finally { metrics.EndCommit(Stopwatch.GetTimestamp()); }
        }
    }

    // Times the durable FindAsync store read the projection build performs on every hop.
    private sealed class TimingInspectionStore(IActivityExecutionInspectionStore inner, EnvelopeMetrics metrics) : IActivityExecutionInspectionStore
    {
        public async ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            var start = Stopwatch.GetTimestamp();
            ActivityExecutionInspectionProjection? result = null;
            try { return result = await inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken); }
            finally { metrics.RecordFind(Stopwatch.GetTimestamp() - start, activityExecutionId, result is null); }
        }

        public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(ActivityExecutionInspectionSummaryPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListSummariesPageAsync(query, cancellationToken);
    }

    // Times BuildProjectionAsync end-to-end and brackets its thread-affine allocations.
    private sealed class TimingInspectionAccumulator(IRuntimeActivityExecutionInspectionAccumulator inner, EnvelopeMetrics metrics) : IRuntimeActivityExecutionInspectionAccumulator
    {
        public async ValueTask<ActivityExecutionInspectionProjection> BuildProjectionAsync(
            ActivityExecutionState state,
            string checkpointId,
            DateTimeOffset committedAt,
            IReadOnlyCollection<string>? outcomeNames = null,
            IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
            IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
            IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var allocStart = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            try
            {
                return await inner.BuildProjectionAsync(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, valueSnapshots, metadata, cancellationToken);
            }
            finally
            {
                var ticks = Stopwatch.GetTimestamp() - start;
                var alloc = GC.GetAllocatedBytesForCurrentThread() - allocStart; // valid only if no thread hop occurred
                metrics.RecordBuildProjection(ticks, alloc);
            }
        }
    }

    // ---- Graph ------------------------------------------------------------------------------------------------------

    private static WorkflowExecutable BuildHotLoop()
    {
        var leaves = Enumerable.Range(0, HotLoopLength).Select(i => NewNoOp($"node-loop-{i}")).ToArray();
        var connections = Enumerable.Range(0, HotLoopLength - 1)
            .Select(i => new FlowchartConnection(new FlowchartEndpoint(leaves[i].ExecutableNodeId), new FlowchartEndpoint(leaves[i + 1].ExecutableNodeId)))
            .ToArray();
        return NewFlowchart(leaves, connections, leaves[0].ExecutableNodeId);
    }

    private static ExecutableNode NewNoOp(string id) =>
        new(id, $"authored-{id}", typeof(NoOpStep).FullName!, "1.0.0", "clr",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());

    private static WorkflowExecutable NewFlowchart(IReadOnlyCollection<ExecutableNode> leaves, IReadOnlyCollection<FlowchartConnection> connections, string startNodeId)
    {
        var root = new ExecutableNode(
            "node-flowchart", "authored-flowchart", typeof(FlowchartActivity).FullName!, "1.0.0", "elsa.flowchart",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, leaves)],
            structure: new ExecutableActivityStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections: connections, startNodeId: startNodeId))));
        return WorkflowExecutionHarness.NewExecutable(root);
    }
}
