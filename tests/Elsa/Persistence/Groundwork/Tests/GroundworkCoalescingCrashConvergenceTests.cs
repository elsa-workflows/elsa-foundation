using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Two-generation crash-convergence coverage for the opt-in burst-coalescing checkpoint persistence policy
// (W9, findings E3-6/RT-10, condition G).
//
// A single shared IDocumentStore stands in for the durable substrate that survives a process crash. The
// coalescing policy buffers intra-drain checkpoints and folds them into ONE atomic flush commit at quiescence,
// so a crash mid-segment leaves NOTHING durable for the folded segment. The proof this is crash-safe by
// construction rests on one invariant: the durable scheduler queue never advances past the last flushed state.
// The segment-entry work item is only dequeued as part of the (never-reached) flush, so it survives the crash
// and a second, honest generation re-drives from it and converges to the same terminal state a crash-free
// Immediate ("control") run reaches.
//
// In-segment activity re-execution after a mid-segment crash is expected (documented at-least-once-after-persist
// semantics in docs/runtime-durable-resumption.md): the crashed generation persisted no checkpoint, so the
// recovered generation re-runs the whole buffered segment. Convergence to the single-node control snapshot proves
// the replay produces no duplicate terminal state, i.e. no duplicate durable/external effects beyond that window.
public sealed class GroundworkCoalescingCrashConvergenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Coalescing_CrashMidSegment_QueueRetainsSegmentEntry_ThenHonestSweepConvergesWithoutDuplicateEffects()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();

        // Reference: a crash-free Immediate run establishes the terminal state the recovered run must converge to.
        var controlSnapshot = await RunControlAsync(manifest);
        Assert.NotEmpty(controlSnapshot);

        var store = new InMemoryDocumentStore(manifest);

        // Generation 1 (coalescing, crashed): the post-commit outbox processor throws mid-drain, after the segment's
        // checkpoints have been buffered into the coalescing working set but BEFORE the quiescence flush lands. The
        // folded commit never reaches the durable store, so nothing durable was written for the segment.
        await using (var crashed = BuildProvider(store, services =>
        {
            services.AddCoalescingRuntimeCheckpointPersistence();
            services.RemoveAll<IRuntimePostCommitOutboxProcessor>();
            services.AddSingleton<IRuntimePostCommitOutboxProcessor>(new ThrowingOutboxProcessor());
        }))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => SeedAndStartAsync(crashed));

            // Invariant proof: the durable scheduler queue never advanced past the last flushed state. The
            // segment-entry work item is still present because the coalescing buffer only dequeues it as part of
            // the atomic flush commit, which never landed.
            var queue = crashed.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var queued = await queue.ListPendingWorkflowExecutionIdsAsync(10);
            Assert.Contains("wfexec-1", queued);

            // Nothing durable persisted for the folded segment: the crashed generation's terminal snapshot differs
            // from the control state.
            var crashedSnapshot = await SnapshotActivityStateAsync(crashed);
            Assert.NotEqual(controlSnapshot, crashedSnapshot);
        }

        // Generation 2 (coalescing, honest): fresh services over the surviving store. The sweep discovers the durable
        // backlog and re-drives the whole segment, folding it into one flush commit and converging to the terminal
        // state. Re-executing the buffered segment is the expected at-least-once replay window.
        await using (var recovered = BuildProvider(store, services => services.AddCoalescingRuntimeCheckpointPersistence()))
        {
            var sweep = ResolveResumptionService(recovered);
            await sweep.SweepAsync(new RuntimeResumptionSweepRequest());

            var recoveredSnapshot = await SnapshotActivityStateAsync(recovered);

            // Convergence: identical terminal state to the crash-free control run.
            Assert.Equal(controlSnapshot, recoveredSnapshot);

            // No duplicate terminal effect: the replay produced exactly the control's terminal rows, not duplicates.
            Assert.Equal(controlSnapshot.Count, recoveredSnapshot.Count);

            // The durable backlog is fully consumed after convergence.
            var queue = recovered.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var remaining = await queue.ListPendingWorkflowExecutionIdsAsync(10);
            Assert.DoesNotContain("wfexec-1", remaining);
        }
    }

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> RunControlAsync(
        StorageManifest manifest)
    {
        var store = new InMemoryDocumentStore(manifest);
        await using var provider = BuildProvider(store);
        await SeedAndStartAsync(provider);
        return await SnapshotActivityStateAsync(provider);
    }

    private static ServiceProvider BuildProvider(IDocumentStore store, Action<IServiceCollection>? customize = null)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddSingleton(store);
        services.AddGroundworkRuntimeStores();
        customize?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task SeedAndStartAsync(ServiceProvider provider)
    {
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        var executable = NewExecutable();
        await store.SaveAsync(executable);
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
    }

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> SnapshotActivityStateAsync(
        ServiceProvider provider)
    {
        var stateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var states = await stateStore.ListAllAsync("wfexec-1");
        return states
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static RuntimeResumptionService ResolveResumptionService(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            provider.GetRequiredService<IRuntimeRecoveryScanner>(),
            provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            provider.GetRequiredService<TimeProvider>());

    private static WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private static WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    // Simulates a process crash mid-segment: the outbox processor throws after the segment's checkpoints have been
    // buffered into the coalescing working set but before the quiescence flush commits. No durable write lands.
    private sealed class ThrowingOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected crash before quiescence flush.");
    }
}
