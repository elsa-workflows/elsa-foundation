using System.Text.Json;
using System.Threading;
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
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// Durable round-trip characterization for the drain path (spec 110, STEP 1). Where
/// <see cref="EngineExecutionBenchmarks"/> counts durable checkpoint-commit DOCUMENTS, this diagnostic counts the
/// full set of durable transactions per run — checkpoint commits (== marker documents), durable scheduler-queue
/// transitions, root-write-lease writes, and executable-artifact reads — under both the Immediate and Coalesced
/// cadences, for the canonical 2-node and hot-loop×10 graphs.
///
/// It is the acceptance instrument for spec 110: it demonstrates empirically that Coalesced already folds the
/// per-turn commit + scheduler-queue storm (hot-loop×10: 66 commits + 194 queue ops → 1 commit + 2 queue ops;
/// buffered turns are overlay-only, no fsync), and that the only per-drain-turn durable round-trip coalescing does
/// not remove is the redundant executable-artifact read (~5 per activity, all resolving the same immutable
/// content-addressed pinned artifact). The counting decorators are inserted over the durable stores AFTER
/// <c>AddGroundworkRuntimeStores</c> — the scheduler-queue counter goes in before coalescing captures it as the
/// inner queue, so <c>RuntimeCoalescingSession.AdvanceInnerQueueAsync</c>'s durable advance flows through it.
///
/// Lives under benchmarks/ (not tests/), so neither CI test gate runs it. Run on demand with:
/// dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj \
///   --filter "FullyQualifiedName~DurableRoundTripDiagnostics" --logger "console;verbosity=detailed"
/// </summary>
public sealed class DurableRoundTripDiagnostics(ITestOutputHelper output)
{
    private sealed class Counters
    {
        public int Acquire; public int Release; public int Renew; public int Find; public int Save; public int ListPage;
        public int QEnqueue; public int QDequeue; public int QDelete; public int QConsume; public int QClaim; public int QComplete; public int QRelease; public int QList;
        public override string ToString() =>
            $"lease.acquire={Acquire} lease.release={Release} lease.renew={Renew} exec.find(read)={Find} exec.save={Save} exec.listPage={ListPage}\n" +
            $"    durable-queue: enqueue={QEnqueue} dequeue={QDequeue} delete={QDelete} consume={QConsume} claim={QClaim} complete={QComplete} release={QRelease} list={QList}";
    }

    private sealed class CountingQueue(IWorkflowSchedulerWorkQueue inner, Counters c) : IWorkflowSchedulerWorkQueue
    {
        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem w, CancellationToken ct = default) { Interlocked.Increment(ref c.QEnqueue); return inner.EnqueueAsync(w, ct); }
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery q, CancellationToken ct = default) { Interlocked.Increment(ref c.QList); return inner.ListAsync(q, ct); }
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string id, CancellationToken ct = default) { Interlocked.Increment(ref c.QDequeue); return inner.DequeueAsync(id, ct); }
        public ValueTask<bool> DeleteAsync(string id, string wid, CancellationToken ct = default) { Interlocked.Increment(ref c.QDelete); return inner.DeleteAsync(id, wid, ct); }
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken ct = default) => inner.ListPendingWorkflowExecutionIdsAsync(limit, ct);
        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest r, CancellationToken ct = default) { Interlocked.Increment(ref c.QClaim); return inner.ClaimAsync(r, ct); }
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim cl, DateTimeOffset now, TimeSpan vt, CancellationToken ct = default) => inner.RenewClaimAsync(cl, now, vt, ct);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim cl, CancellationToken ct = default) { Interlocked.Increment(ref c.QComplete); return inner.CompleteClaimAsync(cl, ct); }
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim cl, DateTimeOffset va, CancellationToken ct = default) { Interlocked.Increment(ref c.QRelease); return inner.ReleaseClaimAsync(cl, va, ct); }
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken ct = default) { Interlocked.Increment(ref c.QConsume); return inner.ConsumeClaimedAsync(consumed, ct); }
    }

    private sealed class CountingExecutableStore(IWorkflowExecutableStore inner, Counters c) : IWorkflowExecutableStore
    {
        public ValueTask SaveAsync(WorkflowExecutable e, CancellationToken ct = default) { Interlocked.Increment(ref c.Save); return inner.SaveAsync(e, ct); }
        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string id, string leaseId, DateTimeOffset ex, DateTimeOffset now, CancellationToken ct = default) { Interlocked.Increment(ref c.Acquire); return inner.TryAcquireRootWriteLeaseAsync(id, leaseId, ex, now, ct); }
        public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease l, DateTimeOffset ex, DateTimeOffset now, CancellationToken ct = default) { Interlocked.Increment(ref c.Renew); return inner.RenewRootWriteLeaseAsync(l, ex, now, ct); }
        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease l, CancellationToken ct = default) { Interlocked.Increment(ref c.Release); return inner.ReleaseRootWriteLeaseAsync(l, ct); }
        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string id, string op, DateTimeOffset ex, DateTimeOffset now, CancellationToken ct = default) => inner.TryBeginDeletionAsync(id, op, ex, now, ct);
        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard g, CancellationToken ct = default) => inner.CancelDeletionAsync(g, ct);
        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard g, DateTimeOffset now, CancellationToken ct = default) => inner.DeleteAsync(g, now, ct);
        public ValueTask<WorkflowExecutable?> FindAsync(string id, CancellationToken ct = default) { Interlocked.Increment(ref c.Find); return inner.FindAsync(id, ct); }
        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest r, CancellationToken ct = default) { Interlocked.Increment(ref c.ListPage); return inner.ListPageAsync(r, ct); }
    }

    private readonly record struct RunCounts(long CheckpointCommits, int QueueOps, int ExecutableReads, long DispatchesPerRun, long FusedSpansPerRun, long InlineCascadePerRun = 0, long WallMs = 0);

    private static RuntimeReplaySafeFusionOptions FusionNone => new() { Enabled = false };
    private static RuntimeReplaySafeFusionOptions FusionD1Only => new() { Enabled = true, FuseCompletionCascade = false };
    private static RuntimeReplaySafeFusionOptions FusionD1D2 => new();

    [Theory]
    [InlineData(false, "2-node Immediate")]
    [InlineData(true, "2-node Coalesced")]
    public async Task TwoNode(bool coalesce, string label) => await RunAsync(coalesce, label, BuildTwoNode());

    [Theory]
    [InlineData(false, "hotloop10 Immediate")]
    [InlineData(true, "hotloop10 Coalesced(cap256)")]
    public async Task HotLoop(bool coalesce, string label) => await RunAsync(coalesce, label, BuildHotLoop(), cap: 256);

    /// <summary>FR-001 guardrail: Coalesced strictly folds the per-turn commit + scheduler-queue storm.</summary>
    [Fact]
    public async Task CoalescedFoldsTheCommitAndQueueStorm()
    {
        var immediate = await RunAsync(false, "hotloop10 Immediate", BuildHotLoop());
        var coalesced = await RunAsync(true, "hotloop10 Coalesced(cap256)", BuildHotLoop(), cap: 256);

        Assert.True(coalesced.CheckpointCommits < immediate.CheckpointCommits,
            $"Coalesced checkpoint commits ({coalesced.CheckpointCommits}) must be < Immediate ({immediate.CheckpointCommits}).");
        Assert.True(coalesced.QueueOps < immediate.QueueOps,
            $"Coalesced durable queue ops ({coalesced.QueueOps}) must be < Immediate ({immediate.QueueOps}).");
        // The executable-read residual survives coalescing unchanged — the re-aimed target of spec 110.
        Assert.Equal(immediate.ExecutableReads, coalesced.ExecutableReads);
    }

    /// <summary>
    /// Spec 123 / ADR 0047 A/B acceptance instrument: ReplaySafe hot-loop×10 under the Coalesced cadence across all
    /// three fusion configurations — <b>none</b> (toggle off), <b>D1</b> (fused schedule→start→invoke, completion
    /// cascade discrete: <c>FuseCompletionCascade = false</c>), and <b>D1+D2</b> (the shipped default, which also
    /// pumps the completion cascade inline). Counters are the deterministic evidence (parallel fleet sessions share
    /// the machine, so walls are indicative only and reported with a run-order swap). Durable (Groundwork/Sqlite) and
    /// in-memory substrates are both exercised. Run on demand with:
    /// dotnet test benchmarks/.../Elsa.Workflows.Runtime.Benchmarks.csproj \
    ///   --filter "FullyQualifiedName~ReplaySafeFusionDispatchAb" --logger "console;verbosity=detailed"
    /// </summary>
    [Fact]
    public async Task ReplaySafeFusionDispatchAb()
    {
        // Run-order swap for the wall caveat: none→D1→D1+D2 then the reverse, on the durable substrate.
        var durableOff = await RunAsync(true, "durable: fusion OFF (none)", BuildReplaySafeHotLoop(), cap: 256, fusion: FusionNone);
        var durableD1 = await RunAsync(true, "durable: fusion ON (D1 only)", BuildReplaySafeHotLoop(), cap: 256, fusion: FusionD1Only);
        var durableD1D2 = await RunAsync(true, "durable: fusion ON (D1+D2)", BuildReplaySafeHotLoop(), cap: 256, fusion: FusionD1D2);
        var durableD1D2Swap = await RunAsync(true, "durable: fusion ON (D1+D2) [swap]", BuildReplaySafeHotLoop(), cap: 256, fusion: FusionD1D2);
        var durableD1Swap = await RunAsync(true, "durable: fusion ON (D1 only) [swap]", BuildReplaySafeHotLoop(), cap: 256, fusion: FusionD1Only);
        var durableOffSwap = await RunAsync(true, "durable: fusion OFF (none) [swap]", BuildReplaySafeHotLoop(), cap: 256, fusion: FusionNone);

        var memOff = await RunInMemoryAsync("in-memory: fusion OFF (none)", BuildReplaySafeHotLoop(), fusion: FusionNone);
        var memD1 = await RunInMemoryAsync("in-memory: fusion ON (D1 only)", BuildReplaySafeHotLoop(), fusion: FusionD1Only);
        var memD1D2 = await RunInMemoryAsync("in-memory: fusion ON (D1+D2)", BuildReplaySafeHotLoop(), fusion: FusionD1D2);

        output.WriteLine("=== spec 123 A/B: ReplaySafe hot-loop×10 — none vs D1 vs D1+D2 ===");
        output.WriteLine($"durable none  : dispatches={durableOff.DispatchesPerRun} fused={durableOff.FusedSpansPerRun} inline-cascade={durableOff.InlineCascadePerRun} commits={durableOff.CheckpointCommits} reads={durableOff.ExecutableReads} wall={durableOff.WallMs}ms (swap {durableOffSwap.WallMs}ms)");
        output.WriteLine($"durable D1    : dispatches={durableD1.DispatchesPerRun} fused={durableD1.FusedSpansPerRun} inline-cascade={durableD1.InlineCascadePerRun} commits={durableD1.CheckpointCommits} reads={durableD1.ExecutableReads} wall={durableD1.WallMs}ms (swap {durableD1Swap.WallMs}ms)");
        output.WriteLine($"durable D1+D2 : dispatches={durableD1D2.DispatchesPerRun} fused={durableD1D2.FusedSpansPerRun} inline-cascade={durableD1D2.InlineCascadePerRun} commits={durableD1D2.CheckpointCommits} reads={durableD1D2.ExecutableReads} wall={durableD1D2.WallMs}ms (swap {durableD1D2Swap.WallMs}ms)");
        output.WriteLine($"in-mem  none  : dispatches={memOff.DispatchesPerRun} fused={memOff.FusedSpansPerRun} inline-cascade={memOff.InlineCascadePerRun} commits={memOff.CheckpointCommits} wall={memOff.WallMs}ms");
        output.WriteLine($"in-mem  D1    : dispatches={memD1.DispatchesPerRun} fused={memD1.FusedSpansPerRun} inline-cascade={memD1.InlineCascadePerRun} commits={memD1.CheckpointCommits} wall={memD1.WallMs}ms");
        output.WriteLine($"in-mem  D1+D2 : dispatches={memD1D2.DispatchesPerRun} fused={memD1D2.FusedSpansPerRun} inline-cascade={memD1D2.InlineCascadePerRun} commits={memD1D2.CheckpointCommits} wall={memD1D2.WallMs}ms");

        // Deterministic evidence: D1 strictly cuts scheduler dispatches vs none (58→36 baseline), and D1+D2 strictly
        // cuts them again by folding the completion-cascade share into the fused dispatch; OFF never fuses and only
        // the D1+D2 configuration pumps the cascade inline.
        Assert.True(durableD1.DispatchesPerRun < durableOff.DispatchesPerRun,
            $"Durable D1 dispatches ({durableD1.DispatchesPerRun}) must be < none ({durableOff.DispatchesPerRun}).");
        Assert.True(durableD1D2.DispatchesPerRun < durableD1.DispatchesPerRun,
            $"Durable D1+D2 dispatches ({durableD1D2.DispatchesPerRun}) must be < D1-only ({durableD1.DispatchesPerRun}).");
        Assert.True(memD1.DispatchesPerRun < memOff.DispatchesPerRun,
            $"In-memory D1 dispatches ({memD1.DispatchesPerRun}) must be < none ({memOff.DispatchesPerRun}).");
        Assert.True(memD1D2.DispatchesPerRun < memD1.DispatchesPerRun,
            $"In-memory D1+D2 dispatches ({memD1D2.DispatchesPerRun}) must be < D1-only ({memD1.DispatchesPerRun}).");
        Assert.True(durableD1.FusedSpansPerRun > 0 && memD1.FusedSpansPerRun > 0);
        Assert.True(durableD1D2.InlineCascadePerRun > 0 && memD1D2.InlineCascadePerRun > 0);
        Assert.Equal(0, durableD1.InlineCascadePerRun);
        Assert.Equal(0, memD1.InlineCascadePerRun);
        Assert.Equal(0, durableOff.FusedSpansPerRun);
        Assert.Equal(0, memOff.FusedSpansPerRun);
    }

    private async Task<RunCounts> RunAsync(bool coalesce, string label, WorkflowExecutable executable, int? cap = null, RuntimeReplaySafeFusionOptions? fusion = null)
    {
        var counters = new Counters();
        var databasePath = Path.Combine(Path.GetTempPath(), $"scratch-{Guid.NewGuid():N}.db");
        IDocumentStore store = await SqliteDocumentStoreFactory.CreateAsync(
            $"Data Source={databasePath}",
            ElsaRuntimeStorageManifest.CreatePhysicalized(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            GroundworkTestAccess.DefaultScoped);

        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IDocumentStore>(store);
                services.AddSingleton<IBoundedDocumentStore>(new RuntimeTestBoundedDocumentStore(store));
                services.AddGroundworkRuntimeStores();

                var descriptor = services.Last(d => d.ServiceType == typeof(IWorkflowExecutableStore));
                services.Remove(descriptor);
                services.AddSingleton<IWorkflowExecutableStore>(sp =>
                {
                    var innerInstance = descriptor.ImplementationInstance
                        ?? descriptor.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
                    return new CountingExecutableStore((IWorkflowExecutableStore)innerInstance, counters);
                });

                // Insert a counting decorator over the durable scheduler queue BEFORE coalescing captures it as
                // CoalescingInner (LastOrDefault), so AdvanceInnerQueueAsync's post-flush durable dequeue/enqueue
                // round-trips flow through the counter.
                var qDescriptor = services.Last(d => d.ServiceType == typeof(IWorkflowSchedulerWorkQueue));
                services.Remove(qDescriptor);
                services.AddSingleton<IWorkflowSchedulerWorkQueue>(sp =>
                {
                    var innerInstance = qDescriptor.ImplementationInstance
                        ?? qDescriptor.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, qDescriptor.ImplementationType!);
                    return new CountingQueue((IWorkflowSchedulerWorkQueue)innerInstance, counters);
                });

                if (coalesce)
                    services.AddCoalescingRuntimeCheckpointPersistence(o => { if (cap is { } v) o.MaxSegmentCheckpoints = v; });

                // spec 123 A/B lever: supersede the runtime default so the same graph runs none / D1 / D1+D2.
                services.AddSingleton(fusion ?? new RuntimeReplaySafeFusionOptions());
            })
            .Build(Enumerable.Range(0, 64).Select(i => $"actexec-{i}").ToArray());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var run = await harness.RunAsync(executable);
        stopwatch.Stop();
        run.AssertWorkflowCompleted();

        var dispatchDiagnostics = harness.Services.GetService<RuntimeSchedulerDispatchDiagnostics>();
        var dispatchesPerRun = dispatchDiagnostics?.Dispatches ?? 0;
        var fusedSpansPerRun = dispatchDiagnostics?.FusedSpans ?? 0;
        var inlineCascadePerRun = dispatchDiagnostics?.InlineCascadeDispatches ?? 0;

#pragma warning disable GW0004
        var docs = await store.QueryAsync(new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004

        output.WriteLine($"=== {label} ===");
        output.WriteLine($"durable checkpoint-commit documents/run = {docs.TotalCount}");
        output.WriteLine($"dispatches/run = {dispatchesPerRun}   fused-spans/run = {fusedSpansPerRun}   inline-cascade/run = {inlineCascadePerRun}");
        output.WriteLine($"root-write-lease + executable-store ops/run: {counters}");
        output.WriteLine($"=> per durable checkpoint flush there is 1 acquire(read+write) + 1 release(read+write) + closure find(s); " +
                         $"lease writes/run ≈ {counters.Acquire + counters.Release + counters.Renew} fsync-scale, vs {docs.TotalCount} checkpoint-marker fsyncs.");

        await harness.DisposeAsync();
        if (store is IAsyncDisposable ad) await ad.DisposeAsync();
        else if (store is IDisposable d) d.Dispose();
        foreach (var p in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            try { File.Delete(p); } catch (IOException) { }

        var queueOps = counters.QEnqueue + counters.QDequeue + counters.QDelete + counters.QConsume +
                       counters.QClaim + counters.QComplete + counters.QRelease;
        return new RunCounts(docs.TotalCount, queueOps, counters.Find, dispatchesPerRun, fusedSpansPerRun, inlineCascadePerRun, stopwatch.ElapsedMilliseconds);
    }

    // In-memory substrate variant (no Groundwork/Sqlite): coalesced, none / D1 / D1+D2. Commits counted from the
    // in-memory commit store; dispatch/fused-span/inline-cascade counters from the shared diagnostics singleton.
    private async Task<RunCounts> RunInMemoryAsync(string label, WorkflowExecutable executable, RuntimeReplaySafeFusionOptions fusion)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithCoalescing()
            .ConfigureServices(services => services.AddSingleton(fusion))
            .Build(Enumerable.Range(0, 64).Select(i => $"actexec-{i}").ToArray());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var run = await harness.RunAsync(executable);
        stopwatch.Stop();
        run.AssertWorkflowCompleted();

        var dispatchDiagnostics = harness.Services.GetService<RuntimeSchedulerDispatchDiagnostics>();
        var commitStore = harness.Services.GetService<InMemoryRuntimeCheckpointCommitStore>();
        var commits = commitStore?.ListCommits().Count ?? 0;

        output.WriteLine($"=== {label} ===");
        output.WriteLine($"commits/run = {commits}   dispatches/run = {dispatchDiagnostics?.Dispatches ?? 0}   fused-spans/run = {dispatchDiagnostics?.FusedSpans ?? 0}   inline-cascade/run = {dispatchDiagnostics?.InlineCascadeDispatches ?? 0}   wall = {stopwatch.ElapsedMilliseconds}ms");

        return new RunCounts(commits, 0, 0, dispatchDiagnostics?.Dispatches ?? 0, dispatchDiagnostics?.FusedSpans ?? 0, dispatchDiagnostics?.InlineCascadeDispatches ?? 0, stopwatch.ElapsedMilliseconds);
    }

    private static WorkflowExecutable BuildTwoNode()
    {
        var writeLine = NewWriteLine("node-writeline", "hi");
        return NewFlowchart([writeLine], [], "node-writeline");
    }

    private static WorkflowExecutable BuildHotLoop()
    {
        var leaves = Enumerable.Range(0, 10).Select(i => NewNoOp($"node-loop-{i}")).ToArray();
        var connections = Enumerable.Range(0, 9)
            .Select(i => new FlowchartConnection(new FlowchartEndpoint(leaves[i].ExecutableNodeId), new FlowchartEndpoint(leaves[i + 1].ExecutableNodeId)))
            .ToArray();
        return NewFlowchart(leaves, connections, leaves[0].ExecutableNodeId);
    }

    private static WorkflowExecutable BuildReplaySafeHotLoop()
    {
        var leaves = Enumerable.Range(0, 10).Select(i => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-loop-{i}")).ToArray();
        var connections = Enumerable.Range(0, 9)
            .Select(i => new FlowchartConnection(new FlowchartEndpoint(leaves[i].ExecutableNodeId), new FlowchartEndpoint(leaves[i + 1].ExecutableNodeId)))
            .ToArray();
        return NewFlowchart(leaves, connections, leaves[0].ExecutableNodeId);
    }

    private static ExecutableNode NewNoOp(string id) =>
        new(id, $"authored-{id}", typeof(NoOpStep).FullName!, "1.0.0", "clr",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());

    private static ExecutableNode NewWriteLine(string id, string text)
    {
        var stringType = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding("text", stringType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, ValueEnvelope.Inline(stringType, JsonSerializer.SerializeToElement(text), ValueProtectionPolicy.InstanceInline));
        return new ExecutableNode(id, $"authored-{id}", typeof(WriteLine).FullName!, "1.0.0", "clr",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["text"] = binding }, new Dictionary<string, string>());
    }

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
