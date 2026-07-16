using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Crash-injection coverage for the durable resumption chain (W2 / findings PS-2, RT-3).
//
// A single shared IDocumentStore stands in for the durable substrate that survives a process crash.
// Each scenario runs two provider generations over the SAME store: the first generation is crashed
// mid-continuation (its ServiceProvider is disposed after the durable write but before dispatch
// completes), the second generation rebuilds honest services over the surviving documents and runs
// IRuntimeResumptionService.SweepAsync to drive the execution to the same terminal state a
// crash-free ("control") run reaches.
//
// Three recoverable windows are covered:
//   Window A - crash AFTER checkpoint commit but BEFORE post-commit outbox delivery. The outbox row
//              is durable and Pending; the scheduler work was never enqueued. The sweep's outbox
//              delivery step re-delivers it, enqueues the work, then re-drives.
//   Window B - crash AFTER outbox delivery (work durably queued) but BEFORE the scheduler drain. The
//              work item survives in the durable queue; the sweep discovers the backlog via
//              ListPendingWorkflowExecutionIdsAsync and re-drives.
//   Window C - crash AFTER the drainer picks up a work item but BEFORE the consuming handler's
//              checkpoint commit lands. Closed by the redrive-safe drain (#412 item 3): the drainer no
//              longer destructively dequeues up front — it ack-deletes the source item only AFTER the
//              handler's effect is durable. A crash before the commit therefore leaves the source item
//              durably queued; the sweep discovers it via ListPendingWorkflowExecutionIdsAsync and
//              re-drives the handler idempotently. (Previously this window was residual/unrecoverable
//              at item granularity — the up-front dequeue-delete had already dropped the source item.)
public sealed class GroundworkDurableResumptionCrashTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WindowA_CrashBeforeOutboxDelivery_ResumptionConvergesToControlState()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var controlSnapshot = await RunControlAsync(manifest);
        Assert.NotEmpty(controlSnapshot);

        var store = new InMemoryDocumentStore(manifest);

        // Generation 1: checkpoint commits, but the post-commit outbox never delivers (crash before
        // dispatch). The outbox row is left durable and Pending; no scheduler work is enqueued.
        await using (var crashed = BuildProvider(store, services =>
        {
            services.RemoveAll<IRuntimePostCommitOutboxProcessor>();
            services.AddSingleton<IRuntimePostCommitOutboxProcessor>(new NoDeliveryOutboxProcessor());
        }))
        {
            await SeedAndStartAsync(crashed);

            var outboxStore = crashed.GetRequiredService<IRuntimePostCommitOutboxStore>();
            var pending = await outboxStore.GetDeliverableAsync(
                new RuntimePostCommitOutboxQuery(Now.AddYears(1), limit: 10, workflowExecutionId: "wfexec-1"));
            Assert.NotEmpty(pending);

            var queue = crashed.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var queued = await queue.ListPendingWorkflowExecutionIdsAsync(10);
            Assert.DoesNotContain("wfexec-1", queued);

            var crashedSnapshot = await SnapshotActivityStateAsync(crashed);
            Assert.NotEqual(controlSnapshot, crashedSnapshot);
        }

        // Generation 2: honest services over the surviving store. A single sweep re-delivers the
        // stranded outbox row, enqueues the work, and re-drives to the terminal state.
        await using (var recovered = BuildProvider(store))
        {
            var sweep = ResolveResumptionService(recovered);
            await sweep.SweepAsync(new RuntimeResumptionSweepRequest());

            var recoveredSnapshot = await SnapshotActivityStateAsync(recovered);
            Assert.Equal(controlSnapshot, recoveredSnapshot);
        }
    }

    [Fact]
    public async Task WindowB_CrashAfterOutboxDeliveryBeforeDrain_ResumptionConvergesToControlState()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var controlSnapshot = await RunControlAsync(manifest);
        Assert.NotEmpty(controlSnapshot);

        var store = new InMemoryDocumentStore(manifest);

        // Generation 1: the outbox delivers (work is durably enqueued) but the drain policy refuses
        // to raise a drain request, so the queued work is never consumed before the crash.
        await using (var crashed = BuildProvider(store, services =>
        {
            services.RemoveAll<IWorkflowSchedulerDrainPolicy>();
            services.AddSingleton<IWorkflowSchedulerDrainPolicy>(new NoDrainPolicy());
        }))
        {
            await SeedAndStartAsync(crashed);

            var queue = crashed.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var queued = await queue.ListPendingWorkflowExecutionIdsAsync(10);
            Assert.Contains("wfexec-1", queued);

            var crashedSnapshot = await SnapshotActivityStateAsync(crashed);
            Assert.NotEqual(controlSnapshot, crashedSnapshot);
        }

        // Generation 2: honest services over the surviving store. The sweep discovers the durable
        // backlog and re-drives to the terminal state, draining the queue.
        await using (var recovered = BuildProvider(store))
        {
            var sweep = ResolveResumptionService(recovered);
            await sweep.SweepAsync(new RuntimeResumptionSweepRequest());

            var recoveredSnapshot = await SnapshotActivityStateAsync(recovered);
            Assert.Equal(controlSnapshot, recoveredSnapshot);

            var queue = recovered.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var remaining = await queue.ListPendingWorkflowExecutionIdsAsync(10);
            Assert.DoesNotContain("wfexec-1", remaining);
        }
    }

    [Fact]
    public async Task WindowC_CrashAfterDequeueBeforeCheckpoint_ResumptionConvergesToControlState()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var controlSnapshot = await RunControlAsync(manifest);
        Assert.NotEmpty(controlSnapshot);

        var store = new InMemoryDocumentStore(manifest);

        // Generation 1: the checkpoint commit store throws OperationCanceledException on its first commit — a *process
        // crash* (not a handler fault) landing after the drainer picked up the work item but before its checkpoint
        // committed. Under the redrive-safe drain the source item is NOT ack-deleted until after a successful commit, so
        // the crash leaves it durably queued. (Before the reorder, the up-front dequeue-delete had already dropped it,
        // and this window was unrecoverable at item granularity.)
        await using (var crashed = BuildProvider(store, services =>
        {
            // Manual decoration (Scrutor's Decorate is not referenced here): capture the durable writer type registered
            // by AddGroundworkRuntimeStores and wrap it so the FIRST commit throws OperationCanceledException.
            var innerDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
            var innerImplementationType = innerDescriptor.ImplementationType
                ?? throw new InvalidOperationException("Expected a type-based IRuntimeCheckpointCommitStore registration to decorate.");
            services.Remove(innerDescriptor);
            services.AddSingleton<IRuntimeCheckpointCommitStore>(sp =>
                new CrashOnceCheckpointCommitStore(
                    (IRuntimeCheckpointCommitStore)ActivatorUtilities.CreateInstance(sp, innerImplementationType)));
        }))
        {
            // The crash propagates out of the start dispatch as an OperationCanceledException.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SeedAndStartAsync(crashed));

            // The source scheduler work item survived the crash, so the execution is discoverable for redrive.
            var queue = crashed.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var queued = await queue.ListPendingWorkflowExecutionIdsAsync(10);
            Assert.Contains("wfexec-1", queued);

            var crashedSnapshot = await SnapshotActivityStateAsync(crashed);
            Assert.NotEqual(controlSnapshot, crashedSnapshot);
        }

        // Generation 2: honest services over the surviving store. The sweep discovers the durable backlog and re-drives
        // the handler idempotently to the terminal state, draining the queue.
        await using (var recovered = BuildProvider(store))
        {
            var sweep = ResolveResumptionService(recovered);
            await sweep.SweepAsync(new RuntimeResumptionSweepRequest());

            var recoveredSnapshot = await SnapshotActivityStateAsync(recovered);
            Assert.Equal(controlSnapshot, recoveredSnapshot);

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
        var states = await stateStore.ListAsync("wfexec-1");
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
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    // Simulates a Window C crash: the drainer has picked up the work item, but the very first checkpoint commit throws
    // OperationCanceledException (a process crash, not a handler fault — so the drainer re-throws it rather than parking
    // the item as poison). Under the redrive-safe drain the source item is only ack-deleted after a successful commit,
    // so this crash leaves it durably queued for the resumption sweep to re-drive.
    private sealed class CrashOnceCheckpointCommitStore(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        private int _commitAttempts;

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _commitAttempts) == 1)
                throw new OperationCanceledException($"Simulated crash before checkpoint commit '{commit.CommitId}'.");

            return inner.CommitAsync(commit, decision, cancellationToken);
        }
    }

    // Simulates a crash between checkpoint commit and post-commit outbox delivery: the outbox row is
    // durably persisted (by the checkpoint writer) but never delivered by this generation.
    private sealed class NoDeliveryOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
    }

    // Simulates a crash between outbox delivery and the scheduler drain: work is durably enqueued,
    // but no drain request is raised so the queued work is never consumed by this generation.
    private sealed class NoDrainPolicy : IWorkflowSchedulerDrainPolicy
    {
        public RuntimeSchedulerDrainRequest? CreateDrainRequest(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerWorkItem workItem) => null;
    }
}
