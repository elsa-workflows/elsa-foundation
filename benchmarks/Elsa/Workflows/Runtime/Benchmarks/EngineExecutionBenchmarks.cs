using System.Diagnostics;
using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Coalescing;
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
/// Engine-level performance benchmark for the real in-process runtime path. Every variant drives the production
/// dispatch/drain path — save executable → activate the workflow-execution actor → enqueue the Start command →
/// drain the scheduler to completion — and reports p50/p95 wall time over N timed iterations after warmups. The
/// durable variants additionally count <c>checkpointCommit</c> documents written per run, which is the durable
/// fsync cost the engine pays.
///
/// Two workflow shapes are measured:
///   * The canonical <b>2-node</b> workflow: a <c>Flowchart</c> root whose single start node is a <c>WriteLine</c>
///     leaf (<see cref="Durable_Sqlite_2Node"/>, <see cref="InMemory_2Node"/>).
///   * A <b>hot loop</b>: a <c>Flowchart</c> whose start node begins a straight-line chain of
///     <see cref="HotLoopLength"/> pure leaf activities (<see cref="Durable_Sqlite_HotLoop"/>). This is the shape
///     ADR 0032's checkpoint-cadence work targets — a burst of pure, replay-safe activities whose intermediate
///     checkpoints are candidates for coalescing/deferral.
///
/// Checkpoint-persistence policy is the durable dial under test:
///   * <b>Immediate</b> (the runtime default; the harness leaves it untouched unless coalescing is enabled) flushes
///     every checkpoint — every <c>ActivityScheduled</c>/<c>ActivityAttemptClaimed</c>/<c>ActivityCompleted</c>
///     and workflow-level transition is its own durable commit.
///   * <b>Coalesced</b> (opt-in via <c>AddCoalescingRuntimeCheckpointPersistence</c>) folds non-mandatory
///     intra-drain checkpoints into one atomic commit at quiescence; only mandatory boundaries flush immediately.
///
/// The hot-loop A/B (<see cref="Durable_Sqlite_HotLoop"/>) measures the same chain under both policies and reports
/// commits/run for each, isolating coalescing's effect. It soft-asserts Coalesced &lt; Immediate on commits/run —
/// deterministic in this harness (the fold is data-driven, not timing-driven).
///
/// A note on ADR 0032 R2 / the spec's "ReplaySafe" A/B: under Coalesced the pre-activation claim checkpoint
/// (<c>ActivityAttemptClaimed</c>) is still an UNCONDITIONAL mandatory flush in this base, so the coalesced floor
/// is ≈ (CLR activity count + terminal) commits — one mandatory claim per leaf. ADR 0032 R2's follow-up (the
/// contract-level <c>SideEffectProfile { External, ReplaySafe }</c> marker that relaxes that one boundary to
/// Deferred for pure activities) is NOT yet in this base, so a "leaf marked ReplaySafe vs. left External" A/B
/// would show no delta here — nothing reads a side-effect marker yet. This benchmark therefore measures what the
/// current base actually exposes (the Immediate→Coalesced drop, and the per-activity claim floor Coalesced leaves
/// behind) so the floor the marker will remove is quantified and re-measurable the moment it lands: swap the
/// hot-loop leaf's contract to <c>ReplaySafe</c> and the Coalesced commits/run should collapse toward ~1–2. The
/// <c>External-leaf</c> reference measurement (a <c>WriteLine</c> chain under Coalesced) is reported alongside the
/// pure-leaf Coalesced run to make that "no delta yet" visible: today the two are equal.
///
/// There are deliberately NO hard latency assertions (they would flake in CI); each iteration asserts only that
/// the workflow completed. Commit counts are reported and, for the Coalesced&lt;Immediate relationship, softly
/// asserted (deterministic). Timings/counts are emitted via <see cref="ITestOutputHelper"/>.
///
/// This project lives under benchmarks/ (not tests/), so neither CI test gate runs it — see the csproj header.
/// Run on demand with: dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj
/// </summary>
public sealed class EngineExecutionBenchmarks(ITestOutputHelper output)
{
    private const int WarmupCount = 2;
    private const int IterationCount = 10;
    private const int HotLoopLength = 10;
    private const string WriteLineNodeId = "node-writeline";
    private const string FlowchartNodeId = "node-flowchart";

    // A comfortably large deterministic activity-execution id pool; the graphs here consume far fewer
    // (2-node: 2; hot loop: HotLoopLength + 1 for the flowchart root).
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 64).Select(index => $"actexec-{index}").ToArray();

    // ---- 2-node baseline ------------------------------------------------------------------------------------

    [Fact]
    public async Task Durable_Sqlite_2Node()
    {
        var measurement = await MeasureAsync(
            () => NewDurableSqliteHarnessAsync(coalesce: false),
            BuildFlowchartWithWriteLine,
            AssertTwoNodeCompleted);
        Report("2-node · durable-sqlite · Immediate (Groundwork over on-disk SQLite, fsync per commit)", measurement);
    }

    [Fact]
    public async Task InMemory_2Node()
    {
        var measurement = await MeasureAsync(
            () => new ValueTask<HarnessLease>(NewInMemoryLease()),
            BuildFlowchartWithWriteLine,
            AssertTwoNodeCompleted);
        Report("2-node · in-memory · Immediate (runtime default stores, no fsync)", measurement);
    }

    // ---- Hot-loop A/B: Immediate vs Coalesced (durable), plus the External-leaf reference -------------------

    [Fact]
    public async Task Durable_Sqlite_HotLoop()
    {
        var immediate = await MeasureAsync(
            () => NewDurableSqliteHarnessAsync(coalesce: false),
            () => BuildHotLoopFlowchart(NewPureLoopNode),
            AssertHotLoopCompleted);
        Report($"hot-loop×{HotLoopLength} (pure leaf) · durable-sqlite · Immediate", immediate);

        var coalesced = await MeasureAsync(
            () => NewDurableSqliteHarnessAsync(coalesce: true),
            () => BuildHotLoopFlowchart(NewPureLoopNode),
            AssertHotLoopCompleted);
        Report($"hot-loop×{HotLoopLength} (pure leaf) · durable-sqlite · Coalesced", coalesced);

        // Reference: the same chain with an External-by-nature leaf (WriteLine) under Coalesced. Because no
        // side-effect marker is read in this base, its commits/run equal the pure-leaf Coalesced run above —
        // that equality is the evidence that ADR 0032 R2's ReplaySafe relaxation is not yet wired.
        var coalescedExternalLeaf = await MeasureAsync(
            () => NewDurableSqliteHarnessAsync(coalesce: true),
            () => BuildHotLoopFlowchart(index => NewWriteLineNode(LoopNodeId(index), $"loop step {index}")),
            AssertHotLoopCompleted);
        Report($"hot-loop×{HotLoopLength} (External leaf: WriteLine) · durable-sqlite · Coalesced", coalescedExternalLeaf);

        var immediateCommits = TypicalCommits(immediate);
        var coalescedCommits = TypicalCommits(coalesced);
        var coalescedExternalCommits = TypicalCommits(coalescedExternalLeaf);

        output.WriteLine(
            $"=== hot-loop commit summary === Immediate={immediateCommits}/run  " +
            $"Coalesced(pure)={coalescedCommits}/run  Coalesced(External leaf)={coalescedExternalCommits}/run  " +
            $"(ADR 0032 R2 marker not in base ⇒ pure≈External under Coalesced; per-activity claim floor remains)");

        // Deterministic in this harness: coalescing folds the non-mandatory intra-drain checkpoints of a
        // HotLoopLength-activity burst into the quiescence flush, so it strictly beats per-checkpoint Immediate.
        Assert.True(
            coalescedCommits < immediateCommits,
            $"Expected Coalesced commits/run ({coalescedCommits}) < Immediate commits/run ({immediateCommits}).");
    }

    // ---- Measurement engine ---------------------------------------------------------------------------------

    /// <summary>
    /// Runs warmups (discarded) then <see cref="IterationCount"/> timed runs, each on a fresh harness. Collects
    /// wall time and (for durable harnesses) durable checkpoint-commit counts, counted after the timed window.
    /// </summary>
    private async Task<Measurement> MeasureAsync(
        Func<ValueTask<HarnessLease>> newLease,
        Func<WorkflowExecutable> executableFactory,
        Action<WorkflowExecutionRun> assert)
    {
        for (var warmup = 0; warmup < WarmupCount; warmup++)
            await RunOnceAsync(newLease, executableFactory, stopwatch: null, assert);

        var walls = new List<double>(IterationCount);
        var commits = new List<long>(IterationCount);
        for (var iteration = 0; iteration < IterationCount; iteration++)
        {
            var stopwatch = new Stopwatch();
            var commitCount = await RunOnceAsync(newLease, executableFactory, stopwatch, assert);
            walls.Add(stopwatch.Elapsed.TotalMilliseconds);
            if (commitCount is { } value)
                commits.Add(value);
        }

        return new Measurement(walls, commits);
    }

    /// <summary>
    /// Builds a fresh harness (fixed workflow-execution id ⇒ one run per harness), times only the execution
    /// (dispatch + scheduler drain), then — outside the timed window — counts durable checkpoint commits.
    /// Returns the commit count for durable harnesses, or <c>null</c> for the in-memory harness.
    /// </summary>
    private async Task<long?> RunOnceAsync(
        Func<ValueTask<HarnessLease>> newLease,
        Func<WorkflowExecutable> executableFactory,
        Stopwatch? stopwatch,
        Action<WorkflowExecutionRun> assert)
    {
        await using var lease = await newLease();
        var executable = executableFactory();

        stopwatch?.Start();
        var run = await lease.Harness.RunAsync(executable);
        stopwatch?.Stop();

        assert(run);

        return lease.Store is null ? null : await CountCheckpointCommitsAsync(lease.Store);
    }

    private static async Task<long> CountCheckpointCommitsAsync(IDocumentStore store)
    {
        // Provider-agnostic count of durable checkpoint-commit documents (the fsync unit under measurement),
        // equivalent to `SELECT COUNT(*) FROM groundwork_documents WHERE document_kind='checkpointCommit'`.
#pragma warning disable GW0004
        var result = await store.QueryAsync(
            new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004
        return result.TotalCount;
    }

    private static void AssertTwoNodeCompleted(WorkflowExecutionRun run)
    {
        run.AssertWorkflowCompleted();
        run.AssertCompleted(WriteLineNodeId);
    }

    private static void AssertHotLoopCompleted(WorkflowExecutionRun run)
    {
        run.AssertWorkflowCompleted();
        run.AssertCompleted(LoopNodeId(0));
        run.AssertCompleted(LoopNodeId(HotLoopLength - 1));
    }

    // ---- Reporting ------------------------------------------------------------------------------------------

    private void Report(string variant, Measurement measurement)
    {
        var ordered = measurement.Walls.OrderBy(value => value).ToArray();
        output.WriteLine($"=== engine benchmark: {variant} ===");
        output.WriteLine($"warmups={WarmupCount}  timed runs={ordered.Length}");
        output.WriteLine($"p50={Percentile(ordered, 50):F2} ms  p95={Percentile(ordered, 95):F2} ms  " +
                         $"min={ordered[0]:F2} ms  max={ordered[^1]:F2} ms  mean={ordered.Average():F2} ms");
        if (measurement.Commits.Count > 0)
        {
            var commits = measurement.Commits;
            output.WriteLine($"durable checkpoint commits/run: typical={TypicalCommits(measurement)}  " +
                             $"min={commits.Min()}  max={commits.Max()}  " +
                             $"(stable={(commits.Min() == commits.Max() ? "yes" : "no")})");
        }
        output.WriteLine("per-run wall (ms): " + string.Join(", ", measurement.Walls.Select(value => value.ToString("F2"))));
    }

    /// <summary>The representative (modal) commit count; commit counts are deterministic so this is the stable value.</summary>
    private static long TypicalCommits(Measurement measurement) =>
        measurement.Commits
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First().Key;

    private static double Percentile(IReadOnlyList<double> orderedAscending, double percentile)
    {
        if (orderedAscending.Count == 0)
            return double.NaN;
        var rank = (int)Math.Ceiling(percentile / 100.0 * orderedAscending.Count);
        return orderedAscending[Math.Clamp(rank - 1, 0, orderedAscending.Count - 1)];
    }

    // ---- Harness construction -------------------------------------------------------------------------------

    private static HarnessLease NewInMemoryLease()
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .Build(ActivityExecutionIds);
        return new HarnessLease(harness, store: null, databasePath: null);
    }

    private static async ValueTask<HarnessLease> NewDurableSqliteHarnessAsync(bool coalesce)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-engine-bench-{Guid.NewGuid():N}.db");
        var store = await SqliteDocumentStoreFactory.CreateAsync(
            $"Data Source={databasePath}",
            ElsaRuntimeStorageManifest.CreatePhysicalized(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            GroundworkTestAccess.DefaultScoped);

        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // Swap the runtime's in-memory store defaults for the durable Groundwork bridges, backed by the
                // raw SQLite document store above plus the shared bounded-query adapter. This is the exact wiring
                // the ActivityDraftTestRun suite uses to drive real end-to-end execution over SQLite.
                services.AddSingleton<IDocumentStore>(store);
                services.AddSingleton<IBoundedDocumentStore>(new RuntimeTestBoundedDocumentStore(store));
                services.AddGroundworkRuntimeStores();

                // Opt into burst-coalescing checkpoint persistence AFTER the durable stores are registered — the
                // decorator captures the last (durable) registration of each runtime store. Without this call the
                // runtime keeps its default Immediate policy (every checkpoint is its own commit).
                if (coalesce)
                    services.AddCoalescingRuntimeCheckpointPersistence();
            })
            .Build(ActivityExecutionIds);

        return new HarnessLease(harness, store, databasePath);
    }

    // ---- Workflow graphs ------------------------------------------------------------------------------------

    /// <summary>Builds the 2-node executable: a Flowchart root whose single start node is a WriteLine leaf.</summary>
    private static WorkflowExecutable BuildFlowchartWithWriteLine()
    {
        var writeLine = NewWriteLineNode(WriteLineNodeId, "Hello from the Elsa engine benchmark.");
        return NewFlowchart([writeLine], connections: [], startNodeId: WriteLineNodeId);
    }

    /// <summary>
    /// Builds the hot-loop executable: a Flowchart whose start node begins a straight-line chain of
    /// <see cref="HotLoopLength"/> leaf activities (leaf shape chosen by <paramref name="makeLeaf"/>), each
    /// wired to the next by a default-outcome connection.
    /// </summary>
    private static WorkflowExecutable BuildHotLoopFlowchart(Func<int, ExecutableNode> makeLeaf)
    {
        var leaves = Enumerable.Range(0, HotLoopLength).Select(makeLeaf).ToArray();
        var connections = Enumerable.Range(0, HotLoopLength - 1)
            .Select(index => new FlowchartConnection(
                new FlowchartEndpoint(leaves[index].ExecutableNodeId),
                new FlowchartEndpoint(leaves[index + 1].ExecutableNodeId)))
            .ToArray();
        return NewFlowchart(leaves, connections, startNodeId: leaves[0].ExecutableNodeId);
    }

    private static WorkflowExecutable NewFlowchart(
        IReadOnlyCollection<ExecutableNode> leaves,
        IReadOnlyCollection<FlowchartConnection> connections,
        string startNodeId)
    {
        var root = new ExecutableNode(
            executableNodeId: FlowchartNodeId,
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, leaves)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new FlowchartStructure(connections: connections, startNodeId: startNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static string LoopNodeId(int index) => $"node-loop-{index}";

    /// <summary>A pure benchmark-local leaf (<see cref="NoOpStep"/>) for the hot-loop body: no external effect.</summary>
    private static ExecutableNode NewPureLoopNode(int index) =>
        new(
            executableNodeId: LoopNodeId(index),
            authoredActivityId: $"authored-{LoopNodeId(index)}",
            activityType: typeof(NoOpStep).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    /// <summary>
    /// A real <see cref="WriteLine"/> CLR leaf with a literal <c>text</c> input. The activity contract is left
    /// unset so the harness reflects it from the CLR type (its established path for non-probe CLR nodes).
    /// </summary>
    private static ExecutableNode NewWriteLineNode(string nodeId, string text)
    {
        var stringType = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding(
            inputKey: "text",
            targetType: stringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                stringType,
                JsonSerializer.SerializeToElement(text),
                ValueProtectionPolicy.InstanceInline));

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteLine).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = binding },
            metadata: new Dictionary<string, string>());
    }

    /// <summary>A run measurement: per-iteration wall times, and (durable harnesses only) per-iteration commit counts.</summary>
    private sealed record Measurement(IReadOnlyList<double> Walls, IReadOnlyList<long> Commits);

    /// <summary>Owns one harness run plus (for the durable variant) its SQLite store and temp database file.</summary>
    private sealed class HarnessLease(WorkflowExecutionHarness harness, IDocumentStore? store, string? databasePath)
        : IAsyncDisposable
    {
        public WorkflowExecutionHarness Harness { get; } = harness;

        /// <summary>The durable document store (for commit counting), or <c>null</c> for the in-memory harness.</summary>
        public IDocumentStore? Store { get; } = store;

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            if (Store is IAsyncDisposable asyncStore)
                await asyncStore.DisposeAsync();
            else if (Store is IDisposable disposableStore)
                disposableStore.Dispose();

            if (databasePath is null)
                return;
            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the temp database; a leftover file must not fail the benchmark.
                }
            }
        }
    }
}

/// <summary>
/// A pure, benchmark-local CLR leaf: it completes immediately with the single <c>Done</c> outcome and performs
/// no externally observable side effect (it touches only in-workflow control flow). This is the "hot-loop body"
/// class ADR 0032 R1 classifies as <c>SideEffectProfile.ReplaySafe</c>. The marker itself is not in this base,
/// so nothing reads it yet; when ADR 0032 R2's WU-marker lands, declaring this contract <c>ReplaySafe</c> is the
/// one change that lets the Coalesced hot-loop drop its per-activity <c>ActivityAttemptClaimed</c> flush.
/// Discovered by the runtime type registrar via the AppDomain assembly scan (the benchmark assembly is loaded),
/// exactly as the shipped primitive activities are.
/// </summary>
[ActivityOutcome("Done")]
public sealed class NoOpStep : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
