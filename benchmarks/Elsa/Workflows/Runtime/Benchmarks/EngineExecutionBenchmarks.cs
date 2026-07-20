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
/// The hot-loop leaf A/B isolates ADR 0032 R2 / spec 107 (now in base): the contract-level
/// <c>SideEffectProfile { External, ReplaySafe }</c> marker makes the pre-activation claim checkpoint
/// (<c>ActivityAttemptClaimed</c>) a CONDITIONAL flush under Coalesced — <c>ReplaySafe</c> defers the claim into
/// the coalesced segment, <c>External</c>/unmarked keeps the immediate flush. The two hot-loop variants use
/// IDENTICAL graphs (same Flowchart root, same 10-leaf straight-line chain, same node count and nesting) where
/// ONLY the leaf activity class differs: <see cref="NoOpStep"/> (marked
/// <c>[ActivitySideEffectProfile(ReplaySafe)]</c>) vs <c>WriteLine</c> (unmarked ⇒ External). Any commits/run
/// delta between them under the same Coalesced policy is therefore attributable to the marker alone.
///
/// SEGMENT-CAP INTERACTION (why the A/B pins <see cref="HotLoopSegmentCap"/>): the coalescing session enforces
/// <c>MaxSegmentCheckpoints</c> (host default 50). A mandatory claim boundary folds AND RESETS the segment, so
/// with an External leaf the cap never trips (a fresh segment starts at every leaf). Mark the leaf ReplaySafe
/// and nothing resets the segment any more — the full ~66-checkpoint burst runs into the cap mid-chain, the
/// session deactivates, and the drain falls back to per-checkpoint Immediate for the remainder. Measured under
/// the default cap this INVERTS the comparison (ReplaySafe 16 &gt; External 11 commits/run) even though the
/// marker is engaged. The A/B therefore raises the cap above the burst size for both leaves (same options,
/// leaf class still the only variable); a separate single-run diagnostic keeps the default-cap behavior visible.
/// The cap-deactivation fallback is the next optimization target for hot loops longer than the cap — a cap-hit
/// could fold-and-reset (like a claim boundary) instead of deactivating coalescing for the rest of the drain.
///
/// There are deliberately NO hard latency assertions (they would flake in CI); each iteration asserts only that
/// the workflow completed. Commit counts are reported and, being deterministic (stable across every observed
/// iteration), two relationships are softly asserted: Coalesced &lt; Immediate, and ReplaySafe-leaf &lt;
/// External-leaf under Coalesced. A one-off (untimed) diagnostic run per durable hot-loop variant dumps the
/// flushed commit kinds — derived from the persisted commit ids — so the residual mandatory boundaries are
/// named, not guessed. Timings/counts are emitted via <see cref="ITestOutputHelper"/>.
///
/// This project lives under benchmarks/ (not tests/), so neither CI test gate runs it — see the csproj header.
/// Run on demand with: dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj
/// </summary>
public sealed class EngineExecutionBenchmarks(ITestOutputHelper output)
{
    private const int WarmupCount = 2;
    private const int IterationCount = 10;
    private const int HotLoopLength = 10;

    /// <summary>
    /// Coalescing segment cap for the hot-loop A/B, deliberately above the burst's total checkpoint count (~66)
    /// so the cap's deactivation fallback cannot fire and the leaf's side-effect profile is the only variable.
    /// The host default (50) trips mid-burst once ReplaySafe removes the segment-resetting claim boundaries —
    /// that interaction is documented by the default-cap diagnostic run, not baked into the A/B numbers.
    /// </summary>
    private const int HotLoopSegmentCap = 256;
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
        Report($"hot-loop×{HotLoopLength} (ReplaySafe leaf) · durable-sqlite · Immediate", immediate);

        var coalesced = await MeasureAsync(
            () => NewDurableSqliteHarnessAsync(coalesce: true, maxSegmentCheckpoints: HotLoopSegmentCap),
            () => BuildHotLoopFlowchart(NewPureLoopNode),
            AssertHotLoopCompleted);
        Report($"hot-loop×{HotLoopLength} (ReplaySafe leaf) · durable-sqlite · Coalesced (cap {HotLoopSegmentCap})", coalesced);

        // The A/B counterpart: the IDENTICAL chain (same node count, same composite nesting, same connections,
        // same coalescing options) where only the leaf class differs — WriteLine is unmarked, so its contract
        // compiles to the fail-safe External profile and its pre-activation claim keeps the mandatory flush.
        var coalescedExternalLeaf = await MeasureAsync(
            () => NewDurableSqliteHarnessAsync(coalesce: true, maxSegmentCheckpoints: HotLoopSegmentCap),
            () => BuildHotLoopFlowchart(index => NewWriteLineNode(LoopNodeId(index), $"loop step {index}")),
            AssertHotLoopCompleted);
        Report($"hot-loop×{HotLoopLength} (External leaf: WriteLine) · durable-sqlite · Coalesced (cap {HotLoopSegmentCap})", coalescedExternalLeaf);

        // One-off untimed diagnostic runs: name the commit kinds actually flushed, so the residual mandatory
        // boundaries under each policy/leaf combination are explained rather than guessed. The third run keeps
        // the HOST-DEFAULT segment cap, documenting the cap-deactivation fallback a ReplaySafe hot loop longer
        // than the cap runs into (see the class doc: the tail degrades to per-checkpoint Immediate).
        await DumpCommitBreakdownAsync(
            $"hot-loop×{HotLoopLength} ReplaySafe leaf · Coalesced (cap {HotLoopSegmentCap})",
            () => NewDurableSqliteHarnessAsync(coalesce: true, maxSegmentCheckpoints: HotLoopSegmentCap),
            () => BuildHotLoopFlowchart(NewPureLoopNode));
        await DumpCommitBreakdownAsync(
            $"hot-loop×{HotLoopLength} External leaf · Coalesced (cap {HotLoopSegmentCap})",
            () => NewDurableSqliteHarnessAsync(coalesce: true, maxSegmentCheckpoints: HotLoopSegmentCap),
            () => BuildHotLoopFlowchart(index => NewWriteLineNode(LoopNodeId(index), $"loop step {index}")));
        await DumpCommitBreakdownAsync(
            $"hot-loop×{HotLoopLength} ReplaySafe leaf · Coalesced (HOST-DEFAULT cap 50 — cap trips mid-burst, tail flushes Immediate)",
            () => NewDurableSqliteHarnessAsync(coalesce: true),
            () => BuildHotLoopFlowchart(NewPureLoopNode));

        var immediateCommits = TypicalCommits(immediate);
        var coalescedCommits = TypicalCommits(coalesced);
        var coalescedExternalCommits = TypicalCommits(coalescedExternalLeaf);

        output.WriteLine(
            $"=== hot-loop commit summary === Immediate={immediateCommits}/run  " +
            $"Coalesced(ReplaySafe leaf)={coalescedCommits}/run  Coalesced(External leaf)={coalescedExternalCommits}/run  " +
            $"(segment cap {HotLoopSegmentCap} for both Coalesced variants)");

        // Both relationships are deterministic in this harness (commit counts are stable across iterations):
        // coalescing folds the non-mandatory intra-drain checkpoints, and the spec-107 ReplaySafe marker
        // additionally defers the per-leaf ActivityAttemptClaimed flush the External leaf must keep.
        Assert.True(
            coalescedCommits < immediateCommits,
            $"Expected Coalesced commits/run ({coalescedCommits}) < Immediate commits/run ({immediateCommits}).");
        Assert.True(
            coalescedCommits < coalescedExternalCommits,
            $"Expected ReplaySafe-leaf commits/run ({coalescedCommits}) < External-leaf commits/run ({coalescedExternalCommits}) under Coalesced.");
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

    /// <summary>
    /// One-off untimed diagnostic: runs the workflow once on a fresh durable harness, then dumps the persisted
    /// checkpoint-commit documents grouped by commit kind. The persisted marker does not carry the checkpoint
    /// name, but every emitter builds its <c>CommitId</c> as <c>commit:{workItemId}:{kind-slug}[:{ids…}]</c>
    /// (e.g. <c>activity-attempt-claimed</c>, <c>activity-completed</c>, <c>workflow-completed</c>), so the
    /// kebab-case slug segments identify the flushed boundary. This names the residual mandatory flushes under a
    /// given policy/leaf combination instead of leaving the commit count unexplained.
    /// </summary>
    private async Task DumpCommitBreakdownAsync(
        string label,
        Func<ValueTask<HarnessLease>> newLease,
        Func<WorkflowExecutable> executableFactory)
    {
        await using var lease = await newLease();
        var run = await lease.Harness.RunAsync(executableFactory());
        run.AssertWorkflowCompleted();

#pragma warning disable GW0004
        var result = await lease.Store!.QueryAsync(
            new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004

        var kinds = result.Documents
            .Select(document =>
            {
                using var content = JsonDocument.Parse(document.ContentJson);
                var root = content.RootElement;
                var commitId =
                    root.TryGetProperty("commitId", out var camel) ? camel.GetString() :
                    root.TryGetProperty("CommitId", out var pascal) ? pascal.GetString() : null;
                return CommitKindSlug(commitId ?? document.Id);
            })
            .GroupBy(kind => kind, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"=== flushed-commit breakdown (1 diagnostic run): {label} — total={result.TotalCount} ===");
        foreach (var group in kinds)
            output.WriteLine($"  {group.Count(),3} × {group.Key}");
    }

    /// <summary>
    /// The commit-kind slugs the runtime's emitters put into their <c>CommitId</c>s (grep <c>CommitId: $"commit:</c>
    /// under src/Elsa). Work-item ids also contain kebab-case hops (<c>schedule-child</c>, <c>invoke</c>, …), so a
    /// naive "alphabetic segments" heuristic drowns the signal; matching this closed set keeps the dump readable.
    /// </summary>
    private static readonly string[] KnownCommitKindSlugs =
    [
        "activity-attempt-claimed", "activity-scheduled", "activity-started", "activity-completed",
        "activity-inspection-captured", "parent-activity-completed", "activity-suspended", "activity-cancelled",
        "bookmark-created", "incident-recorded", "workflow-faulted", "scheduler-poison", "intrinsic", "cancel"
    ];

    /// <summary>
    /// Extracts the commit-kind slug from a <c>commit:{workItemId}:{kind-slug}[:{ids…}]</c> id by matching the
    /// runtime's known kind slugs; unmatched ids (e.g. the terminal continuation-checkpoint flush) fall back to
    /// their last two segments.
    /// </summary>
    private static string CommitKindSlug(string commitId)
    {
        foreach (var slug in KnownCommitKindSlugs)
            if (commitId.Contains($":{slug}", StringComparison.Ordinal))
                return slug;

        var segments = commitId.Split(':');
        return segments.Length >= 2 ? $"…{segments[^2]}:{segments[^1]}" : commitId;
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

    private static async ValueTask<HarnessLease> NewDurableSqliteHarnessAsync(bool coalesce, int? maxSegmentCheckpoints = null)
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
                // runtime keeps its default Immediate policy (every checkpoint is its own commit). A caller may
                // pin the segment cap; omitted, the shipped default (50) applies.
                if (coalesce)
                    services.AddCoalescingRuntimeCheckpointPersistence(options =>
                    {
                        if (maxSegmentCheckpoints is { } cap)
                            options.MaxSegmentCheckpoints = cap;
                    });
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
/// no externally observable side effect (it touches only in-workflow control flow). Declared
/// <c>[ActivitySideEffectProfile(ReplaySafe)]</c> per ADR 0032 R1 / spec 107, so under the Coalesced policy its
/// pre-activation <c>ActivityAttemptClaimed</c> checkpoint defers into the coalesced segment instead of flushing
/// per activity — the exact relaxation the hot-loop A/B measures against the unmarked (External) WriteLine leaf.
/// Discovered by the runtime type registrar via the AppDomain assembly scan (the benchmark assembly is loaded),
/// exactly as the shipped primitive activities are.
/// </summary>
[ActivityOutcome("Done")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class NoOpStep : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
