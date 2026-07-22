using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Spec 123 guardrail 2 (ADR 0047): a crash <b>inside</b> a ReplaySafe fused span converges to the crash-free terminal
/// on redrive, exactly as a mid-segment coalescing crash does today (research §5 — fusion changes dispatch locality,
/// not durable truth or its redrive ladder). A single shared <see cref="InMemoryDocumentStore"/> stands in for the
/// durable substrate surviving a process crash. Generation 1 runs fusion-ON under coalescing and a
/// <see cref="CancellationException"/>-throwing checkpoint-commit store crashes the drain at a kill point <em>inside</em>
/// a fused schedule→start→invoke span (after N buffered checkpoints, before the quiescence flush). Because the fused
/// span's intermediate StartActivity/InvokeActivity items are never durably enqueued and the flush never landed, the
/// durable scheduler backlog is intact and no partial checkpoint persisted. Generation 2 sweeps and converges to the
/// crash-free control terminal, proving no fusion-specific crash-recovery gap. With the D2 inline completion pump
/// (spec 123 D2, default-ON) the kill points additionally land inside the inline parent-completion pass and on the
/// D2→D1 recursion boundary (a fused successor's first commit) — the cascade's items live only in the coalescing
/// overlay, so the same durable-backlog + fold-forward ladder covers them with no new recovery mechanism.
/// </summary>
public sealed class ReplaySafeFusionCrashConvergenceTests
{
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 32).Select(index => $"actexec-{index}").ToArray();

    // Kill points inside the fused burst. With the D2 inline completion pump (default-ON), the 5-leaf chain runs
    // schedule→start→invoke → completion cascade → successor schedule all inside the first drained dispatch, so the
    // ordinals walk the whole D1+D2 surface: 2–4 land in the first fused span (D1); 7 and 9 land at the leaf's
    // completion commit and inside the inline parent-completion pass (the pumped eval's child-scheduling commit);
    // 10–12 land on and after a fused successor's ActivityScheduled — the D2→D1 recursion boundary — deeper in the
    // pumped chain. Every kill point converges to the same crash-free terminal on redrive.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public async Task CrashInsideFusedSpan_RetainsDurableRedriveSource_ThenSweepConverges(int crashOnCommit)
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();

        // Reference: a crash-free fusion-ON coalesced run over a fresh store establishes the terminal to converge to.
        var control = await RunControlAsync(manifest);
        Assert.NotEmpty(control);
        Assert.All(control, entry => Assert.Equal(ActivityExecutionStatus.Completed, entry.Status));

        var store = new InMemoryDocumentStore(manifest);

        // Generation 1: crash inside a fused span. The checkpoint-commit store (wrapping the coalescing decorator)
        // throws OperationCanceledException on the Nth commit — a process crash, not a handler fault, so the drainer
        // rethrows it and never ack-deletes/poisons the source work item.
        await using (var crashed = BuildHarness(store, services =>
        {
            var descriptor = services.Last(d => d.ServiceType == typeof(IRuntimeCheckpointCommitStore));
            services.Remove(descriptor);
            services.AddScoped<IRuntimeCheckpointCommitStore>(sp => new CrashOnNthCommitStore(
                (IRuntimeCheckpointCommitStore)(descriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)),
                crashOnCommit));
        }))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => crashed.RunAsync(BuildReplaySafeFlowchart()));

            // The durable scheduler backlog survives the crash (the fused span's intermediate items were never durably
            // enqueued, and the coalescing flush never advanced the durable queue), so the execution stays discoverable.
            var queue = crashed.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            Assert.Contains(WorkflowExecutionHarness.WorkflowExecutionId, await queue.ListPendingWorkflowExecutionIdsAsync(10));

            // Nothing terminal persisted: the crashed generation did not converge.
            Assert.NotEqual(control, await SnapshotAsync(crashed.Services));
        }

        // Generation 2: honest fusion-ON services over the surviving store. A sweep re-drives the backlog and folds the
        // replayed segment into the crash-free terminal state.
        await using (var recovered = BuildHarness(store, customize: null))
        {
            recovered.InitializeActivityTypes();
            var sweep = ResolveResumptionService(recovered.Services);
            await sweep.SweepAsync(new RuntimeResumptionSweepRequest());

            Assert.Equal(control, await SnapshotAsync(recovered.Services));
            var queue = recovered.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            Assert.DoesNotContain(WorkflowExecutionHarness.WorkflowExecutionId, await queue.ListPendingWorkflowExecutionIdsAsync(10));
        }
    }

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> RunControlAsync(StorageManifest manifest)
    {
        var store = new InMemoryDocumentStore(manifest);
        await using var harness = BuildHarness(store, customize: null);
        var run = await harness.RunAsync(BuildReplaySafeFlowchart());
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);

        // Non-vacuous: the shipped defaults (D1 fusion + D2 inline completion pump) actually engaged in the control
        // run, so the kill points below land inside fused spans and the inline completion pass, not a discrete drain.
        var diagnostics = harness.Services.GetRequiredService<Elsa.Workflows.Runtime.Core.Diagnostics.RuntimeSchedulerDispatchDiagnostics>();
        Assert.True(diagnostics.FusedSpans > 0, "Control run did not engage D1 fusion.");
        Assert.True(diagnostics.InlineCascadeDispatches > 0, "Control run did not engage the D2 inline completion pump.");

        return await SnapshotAsync(harness.Services);
    }

    private static WorkflowExecutionHarness BuildHarness(IDocumentStore store, Action<IServiceCollection>? customize) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(store);
                services.AddSingleton<IBoundedDocumentStore>(new RuntimeTestBoundedDocumentStore(store));
                services.AddGroundworkRuntimeStores();
                // Coalescing AFTER the Groundwork stores so its decorators wrap the durable stores (fusion only engages
                // inside a live coalescing burst). Fusion is ON by default; leave the toggle at its shipped default.
                services.AddCoalescingRuntimeCheckpointPersistence();
                customize?.Invoke(services);
            })
            .Build(ActivityExecutionIds);

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> SnapshotAsync(IServiceProvider provider)
    {
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        return states
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static RuntimeResumptionService ResolveResumptionService(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            provider.GetRequiredService<IRuntimeRecoveryScanner>(),
            provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>());

    private static WorkflowExecutable BuildReplaySafeFlowchart()
    {
        var leaves = Enumerable.Range(0, 5)
            .Select(index => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-loop-{index}"))
            .ToArray();
        var connections = Enumerable.Range(0, leaves.Length - 1)
            .Select(index => new FlowchartConnection(
                new FlowchartEndpoint(leaves[index].ExecutableNodeId),
                new FlowchartEndpoint(leaves[index + 1].ExecutableNodeId)))
            .ToArray();

        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
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
                    new FlowchartStructure(connections: connections, startNodeId: leaves[0].ExecutableNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    // Throws a process-crash signal on the Nth checkpoint commit; earlier commits pass through so the crash lands
    // inside a fused span after some checkpoints have buffered. OperationCanceledException (not a domain fault) makes
    // the drainer rethrow rather than poison + ack-delete the source item, mirroring GroundworkDurableResumptionCrash.
    private sealed class CrashOnNthCommitStore(IRuntimeCheckpointCommitStore inner, int crashOnCommit) : IRuntimeCheckpointCommitStore
    {
        private int _commits;

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _commits) == crashOnCommit)
                throw new OperationCanceledException($"Simulated crash inside a fused span at commit '{commit.CommitId}'.");

            return inner.CommitAsync(commit, decision, cancellationToken);
        }
    }
}
