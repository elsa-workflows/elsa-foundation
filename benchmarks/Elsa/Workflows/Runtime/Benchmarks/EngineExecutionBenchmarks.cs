using System.Diagnostics;
using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Models;
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
/// Engine-level performance benchmark for the canonical 2-node workflow (a <c>Flowchart</c> root whose single
/// start node is a <c>WriteLine</c> leaf). It drives the real in-process runtime path — save executable →
/// activate the workflow-execution actor → enqueue the Start command → drain the scheduler to completion —
/// exactly as the production dispatcher/actor/drainer does, and reports p50/p95 wall time over N timed
/// iterations after warmups.
///
/// Two variants isolate engine overhead from durable-write (fsync) cost:
///   * <see cref="Durable_Sqlite_2Node"/> backs every runtime persistence seam with Groundwork over a real
///     on-disk SQLite database (the demo-container shape: checkpoint commits, post-commit outbox, scheduler
///     work queue, execution/activity state all round-trip through SQLite transactions).
///   * <see cref="InMemory_2Node"/> keeps the runtime's in-memory store defaults, measuring pure engine cost
///     with no fsync.
///
/// There are deliberately NO hard latency assertions (they would flake in CI). Each iteration only asserts the
/// workflow reached Completed and the WriteLine leaf ran; timings are emitted via <see cref="ITestOutputHelper"/>.
///
/// This project lives under benchmarks/ (not tests/), so neither CI test gate runs it — see the csproj header.
/// Run on demand with: dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj
/// </summary>
public sealed class EngineExecutionBenchmarks(ITestOutputHelper output)
{
    private const int WarmupCount = 2;
    private const int IterationCount = 10;
    private const string WriteLineNodeId = "node-writeline";
    private const string FlowchartNodeId = "node-flowchart";

    // A comfortably large deterministic activity-execution id pool; the 2-node graph consumes far fewer.
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 32).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task Durable_Sqlite_2Node()
    {
        var samples = await MeasureAsync(NewDurableSqliteHarnessAsync);
        Report("durable-sqlite (Groundwork over on-disk SQLite, fsync per transaction)", samples);
    }

    [Fact]
    public async Task InMemory_2Node()
    {
        var samples = await MeasureAsync(() => new ValueTask<HarnessLease>(NewInMemoryLease()));
        Report("in-memory (runtime default stores, no fsync)", samples);
    }

    /// <summary>Runs warmups (discarded) then <see cref="IterationCount"/> timed runs, each on a fresh harness.</summary>
    private async Task<IReadOnlyList<double>> MeasureAsync(Func<ValueTask<HarnessLease>> newLease)
    {
        for (var warmup = 0; warmup < WarmupCount; warmup++)
            await RunOnceAsync(newLease, stopwatch: null);

        var samples = new List<double>(IterationCount);
        for (var iteration = 0; iteration < IterationCount; iteration++)
        {
            var stopwatch = new Stopwatch();
            await RunOnceAsync(newLease, stopwatch);
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return samples;
    }

    /// <summary>
    /// Builds a fresh harness (fixed workflow-execution id ⇒ one run per harness), then times only the
    /// execution (dispatch + scheduler drain), leaving harness/store construction outside the measured window.
    /// </summary>
    private async Task RunOnceAsync(Func<ValueTask<HarnessLease>> newLease, Stopwatch? stopwatch)
    {
        await using var lease = await newLease();
        var executable = BuildFlowchartWithWriteLine();

        stopwatch?.Start();
        var run = await lease.Harness.RunAsync(executable);
        stopwatch?.Stop();

        run.AssertWorkflowCompleted();
        run.AssertCompleted(WriteLineNodeId);
    }

    private void Report(string variant, IReadOnlyList<double> samples)
    {
        var ordered = samples.OrderBy(value => value).ToArray();
        output.WriteLine($"=== 2-node engine benchmark: {variant} ===");
        output.WriteLine($"warmups={WarmupCount}  timed runs={ordered.Length}");
        output.WriteLine($"p50={Percentile(ordered, 50):F2} ms  p95={Percentile(ordered, 95):F2} ms  " +
                         $"min={ordered[0]:F2} ms  max={ordered[^1]:F2} ms  mean={ordered.Average():F2} ms");
        output.WriteLine("per-run (ms): " + string.Join(", ", samples.Select(value => value.ToString("F2"))));
    }

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

    private static async ValueTask<HarnessLease> NewDurableSqliteHarnessAsync()
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
            })
            .Build(ActivityExecutionIds);

        return new HarnessLease(harness, store, databasePath);
    }

    // ---- Workflow graph -------------------------------------------------------------------------------------

    /// <summary>Builds the 2-node executable: a Flowchart root whose single start node is a WriteLine leaf.</summary>
    private static WorkflowExecutable BuildFlowchartWithWriteLine()
    {
        var writeLine = NewWriteLineNode(WriteLineNodeId, "Hello from the Elsa engine benchmark.");

        var root = new ExecutableNode(
            executableNodeId: FlowchartNodeId,
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [writeLine])],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new FlowchartStructure(connections: [], startNodeId: WriteLineNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

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

    /// <summary>Owns one harness run plus (for the durable variant) its SQLite store and temp database file.</summary>
    private sealed class HarnessLease(WorkflowExecutionHarness harness, IDocumentStore? store, string? databasePath)
        : IAsyncDisposable
    {
        public WorkflowExecutionHarness Harness { get; } = harness;

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            if (store is IAsyncDisposable asyncStore)
                await asyncStore.DisposeAsync();
            else if (store is IDisposable disposableStore)
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
