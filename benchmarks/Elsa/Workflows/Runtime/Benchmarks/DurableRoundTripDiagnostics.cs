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

    private readonly record struct RunCounts(long CheckpointCommits, int QueueOps, int ExecutableReads, long DispatchesPerRun, long FusedSpansPerRun);

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

    private async Task<RunCounts> RunAsync(bool coalesce, string label, WorkflowExecutable executable, int? cap = null)
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
            })
            .Build(Enumerable.Range(0, 64).Select(i => $"actexec-{i}").ToArray());

        var run = await harness.RunAsync(executable);
        run.AssertWorkflowCompleted();

        var dispatchDiagnostics = harness.Services.GetService<RuntimeSchedulerDispatchDiagnostics>();
        var dispatchesPerRun = dispatchDiagnostics?.Dispatches ?? 0;
        var fusedSpansPerRun = dispatchDiagnostics?.FusedSpans ?? 0;

#pragma warning disable GW0004
        var docs = await store.QueryAsync(new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004

        output.WriteLine($"=== {label} ===");
        output.WriteLine($"durable checkpoint-commit documents/run = {docs.TotalCount}");
        output.WriteLine($"dispatches/run = {dispatchesPerRun}   fused-spans/run = {fusedSpansPerRun}");
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
        return new RunCounts(docs.TotalCount, queueOps, counters.Find, dispatchesPerRun, fusedSpansPerRun);
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
