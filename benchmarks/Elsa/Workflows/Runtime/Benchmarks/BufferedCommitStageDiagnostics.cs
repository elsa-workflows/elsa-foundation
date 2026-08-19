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
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using DiagnosticsActivity = System.Diagnostics.Activity;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// Stage-timing diagnostic for Unit A (engine perf phase 3): decompose the per-commit cost of a Coalesced hot-loop
/// drain into (1) total committer.CommitAsync wall time (captured through the checkpoint-commit span the committer
/// already opens), (2) the store-layer call the committer makes — split into buffered-append calls (deferred, no
/// durable write) versus durable-flush calls, and (3) the durable serialize+persist cost inside the Groundwork
/// checkpoint writer, isolated by a timer captured BEFORE the coalescing decorator so it only ticks on a real flush.
///
/// This is the instrument that confirms or kills the phase-3 hypothesis that the ~137ms of buffered checkpoint.commit
/// spans is deterministic-serializer CPU paid per buffered commit. The decisive number is the durable-writer call
/// COUNT: change-set serialization lives in <c>GroundworkRuntimeCheckpointWriter</c>, and a buffered commit never
/// reaches it (the coalescing store buffers into an in-memory overlay), so serialization cannot be paid 12× per drain.
///
/// Lives under benchmarks/ (not tests/), so neither CI test gate runs it. Run on demand with:
/// dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj \
///   --filter "FullyQualifiedName~BufferedCommitStageDiagnostics" --logger "console;verbosity=detailed"
/// </summary>
public sealed class BufferedCommitStageDiagnostics(ITestOutputHelper output)
{
    private const int HotLoopLength = 10;
    private const int SegmentCap = 256;

    [Theory]
    [InlineData(false, "hotloop10 Immediate")]
    [InlineData(true, "hotloop10 Coalesced(cap256)")]
    public async Task StageBreakdown(bool coalesce, string label)
    {
        var metrics = new StageMetrics();
        var databasePath = Path.Combine(Path.GetTempPath(), $"buffered-stage-{Guid.NewGuid():N}.db");
        var opened = await GroundworkBenchmarkStore.OpenAsync(databasePath);
        IDocumentStore store = opened.Store;

        using var tracer = new StageTracer(metrics);

        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartRuntimeFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IDocumentStore>(store);
                services.AddSingleton<IBoundedDocumentStore>(opened.Queries);
                services.AddGroundworkRuntimeStores();

                // The committer opens the checkpoint-commit span through IWorkflowEngineTracer; our tracer records the
                // full committer.CommitAsync duration per commit and the persistence-mode tag it stamps.
                services.AddSingleton<IWorkflowEngineTracer>(tracer);

                // INNER durable timer: replace the Groundwork writer registration with a timing decorator BEFORE the
                // coalescing wiring captures it as CoalescingInner. It ticks only when a real durable serialize+persist
                // happens — i.e. once per flushed segment — so its call count is the serialization count for the run.
                DecorateCommitStore(services, inner => new TimingCommitStore(inner, metrics.Durable));

                if (coalesce)
                    services.AddCoalescingRuntimeCheckpointPersistence(o => o.MaxSegmentCheckpoints = SegmentCap);

                // OUTER committer-facing timer: after coalescing, so the committer resolves THIS decorator. It measures
                // every committer -> store call and classifies it as a buffered-append (no durable tick) or a flush
                // (durable timer ticked during the call).
                DecorateCommitStore(services, inner => new ClassifyingCommitStore(inner, metrics));
            })
            .Build(Enumerable.Range(0, 64).Select(i => $"actexec-{i}").ToArray());

        var run = await harness.RunAsync(BuildHotLoop());
        run.AssertWorkflowCompleted();

        var commitCount = await GroundworkBenchmarkStore.CountCheckpointCommitsAsync(opened.Queries);

        output.WriteLine($"=== {label} — stage breakdown (1 run, hot-loop×{HotLoopLength}) ===");
        output.WriteLine($"durable checkpoint-commit documents/run = {commitCount}");
        output.WriteLine("");
        output.WriteLine($"committer.CommitAsync invocations: total={metrics.CommitterCount} (reliable count)");
        output.WriteLine($"  span durations captured (best-effort listener): spans={metrics.SpanCount}  " +
                         $"deferred={metrics.DeferredCommitterCount}  immediate={metrics.ImmediateCommitterCount}  " +
                         $"total={metrics.CommitterMs:F3} ms  avg={Avg(metrics.CommitterMs, metrics.SpanCount):F4} ms/span");
        output.WriteLine("");
        output.WriteLine($"store-call classification (committer -> IRuntimeCheckpointCommitStore):");
        output.WriteLine($"  BUFFERED-append calls (no durable write): count={metrics.BufferedCalls}  " +
                         $"total={metrics.BufferedMs:F3} ms  avg={Avg(metrics.BufferedMs, metrics.BufferedCalls):F4} ms/call");
        output.WriteLine($"  FLUSH calls (durable write triggered):    count={metrics.FlushCalls}  " +
                         $"total={metrics.FlushMs:F3} ms  avg={Avg(metrics.FlushMs, metrics.FlushCalls):F4} ms/call");
        output.WriteLine("");
        output.WriteLine($"durable serialize+persist (GroundworkRuntimeCheckpointWriter.CommitAsync):");
        output.WriteLine($"  count={metrics.Durable.Count}  total={metrics.Durable.Ms:F3} ms  " +
                         $"avg={Avg(metrics.Durable.Ms, metrics.Durable.Count):F4} ms/write");
        var totalStoreCalls = metrics.BufferedCalls + metrics.FlushCalls;
        output.WriteLine("");
        output.WriteLine($"=> durable serialize invoked {metrics.Durable.Count}× across {totalStoreCalls} committer store call(s) " +
                         $"=> {(totalStoreCalls == 0 ? 0d : (double)metrics.Durable.Count / totalStoreCalls):F3} serialize calls per committed checkpoint " +
                         $"({metrics.BufferedCalls} of them buffered at {Avg(metrics.BufferedMs, metrics.BufferedCalls):F4} ms each, no serialization).");

        await harness.DisposeAsync();
        if (store is IAsyncDisposable ad) await ad.DisposeAsync();
        else if (store is IDisposable d) d.Dispose();
        foreach (var p in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            try { File.Delete(p); } catch (IOException) { }

        // Guardrail assertions (deterministic): a buffered-append call never performs a durable serialize.
        if (coalesce)
        {
            Assert.True(metrics.BufferedCalls > 0, "Expected the Coalesced hot loop to buffer intra-drain checkpoints.");
            Assert.True(metrics.Durable.Count < metrics.BufferedCalls + metrics.FlushCalls,
                $"Expected durable serialize count ({metrics.Durable.Count}) < total committer store calls ({metrics.BufferedCalls + metrics.FlushCalls}).");
        }
    }

    private static double Avg(double totalMs, long count) => count == 0 ? 0 : totalMs / count;

    // Replaces the current IRuntimeCheckpointCommitStore registration with a decorator built over the previous one.
    private static void DecorateCommitStore(
        IServiceCollection services,
        Func<IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore> decorate)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            sp =>
            {
                var inner = (IRuntimeCheckpointCommitStore)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
                return decorate(inner);
            },
            descriptor.Lifetime));
    }

    // ---- Metrics + decorators ---------------------------------------------------------------------------------------

    private sealed class TimerAccumulator
    {
        private long _count;
        private long _ticks;
        public long Count => Interlocked.Read(ref _count);
        public double Ms => Interlocked.Read(ref _ticks) * 1000.0 / Stopwatch.Frequency;
        public void Add(long ticks) { Interlocked.Increment(ref _count); Interlocked.Add(ref _ticks, ticks); }
    }

    private sealed class StageMetrics
    {
        public readonly TimerAccumulator Durable = new();

        public long CommitterCount;
        public long DeferredCommitterCount;
        public long ImmediateCommitterCount;
        private long _committerTicks;
        public double CommitterMs => Interlocked.Read(ref _committerTicks) * 1000.0 / Stopwatch.Frequency;

        public long BufferedCalls;
        public long FlushCalls;
        private long _bufferedTicks;
        private long _flushTicks;
        public double BufferedMs => Interlocked.Read(ref _bufferedTicks) * 1000.0 / Stopwatch.Frequency;
        public double FlushMs => Interlocked.Read(ref _flushTicks) * 1000.0 / Stopwatch.Frequency;

        public long SpanCount;

        public void RecordCommitter(long durationTicks, string? mode)
        {
            // CommitterCount is incremented reliably in StartCheckpointCommit; this listener path only adds duration
            // and the persistence-mode split (best-effort — the flush committer's span is not always captured).
            Interlocked.Increment(ref SpanCount);
            Interlocked.Add(ref _committerTicks, durationTicks);
            if (string.Equals(mode, "Deferred", StringComparison.Ordinal)) Interlocked.Increment(ref DeferredCommitterCount);
            else if (string.Equals(mode, "Immediate", StringComparison.Ordinal)) Interlocked.Increment(ref ImmediateCommitterCount);
        }

        public void RecordStoreCall(long ticks, bool durableTicked)
        {
            if (durableTicked) { Interlocked.Increment(ref FlushCalls); Interlocked.Add(ref _flushTicks, ticks); }
            else { Interlocked.Increment(ref BufferedCalls); Interlocked.Add(ref _bufferedTicks, ticks); }
        }
    }

    // Times the wrapped store's CommitAsync (used for the durable Groundwork writer).
    private sealed class TimingCommitStore(IRuntimeCheckpointCommitStore inner, TimerAccumulator acc) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var start = Stopwatch.GetTimestamp();
            try { return await inner.CommitAsync(commit, decision, cancellationToken); }
            finally { acc.Add(Stopwatch.GetTimestamp() - start); }
        }
    }

    // Times the committer -> coalescing-store call and classifies it as buffered vs flush by whether the durable timer
    // ticked during the call.
    private sealed class ClassifyingCommitStore(IRuntimeCheckpointCommitStore inner, StageMetrics metrics) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var durableBefore = metrics.Durable.Count;
            var start = Stopwatch.GetTimestamp();
            try { return await inner.CommitAsync(commit, decision, cancellationToken); }
            finally { metrics.RecordStoreCall(Stopwatch.GetTimestamp() - start, metrics.Durable.Count > durableBefore); }
        }
    }

    // Captures committer.CommitAsync duration via the checkpoint-commit span (Activity) the committer opens and disposes.
    private sealed class StageTracer : IWorkflowEngineTracer, IDisposable
    {
        private static readonly ActivitySource Source = new("Elsa.Benchmarks.BufferedCommitStage");
        private readonly ActivityListener _listener;
        private readonly StageMetrics _metrics;

        public StageTracer(StageMetrics metrics)
        {
            _metrics = metrics;
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source == Source,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (!string.Equals(activity.OperationName, "checkpoint.commit", StringComparison.Ordinal))
                        return;
                    var mode = activity.GetTagItem(WorkflowEngineTelemetry.CheckpointPersistenceModeTag) as string;
                    _metrics.RecordCommitter((long)(activity.Duration.Ticks * (Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond)), mode);
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public DiagnosticsActivity? StartDrainCycle(RuntimeSchedulerDrainRequest request) => null;
        public DiagnosticsActivity? StartDispatch(RuntimeSchedulerWorkItem workItem) => null;
        public DiagnosticsActivity? StartActivityExecution(RuntimeSchedulerWorkItem workItem) => null;
        public DiagnosticsActivity? StartCheckpointCommit(RuntimeCheckpointCommit commit)
        {
            // Reliable committer-invocation count independent of the (best-effort) listener duration capture.
            Interlocked.Increment(ref _metrics.CommitterCount);
            return Source.StartActivity("checkpoint.commit");
        }

        public void Dispose() => _listener.Dispose();
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
