using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Constants;
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

    // Segment-cap follow-up (ADR 0032): a cap-hit fold-and-flush lands mid-drain and STARTS A FRESH SEGMENT. A crash
    // mid-second-segment must replay from that folded flush, not from the original segment entry: the flush already
    // advanced the durable queue past the segment-entry item, and the crash-redrive source is the continuation intent
    // the flush persisted durably as Pending (delivered in-overlay only, so the crash loses the delivery but never
    // the intent). The honest second generation sweeps the pending intent back into durable scheduler work and
    // converges to the crash-free control state.
    [Fact]
    public async Task Coalescing_CrashMidSecondSegment_ReplaysFromLastFoldedFlush_AndConverges()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();

        // Reference: a crash-free Immediate run over the same executable establishes the terminal state.
        var controlSnapshot = await RunCompletingControlAsync(manifest);
        Assert.NotEmpty(controlSnapshot);

        var store = new InMemoryDocumentStore(manifest);
        var crashSwitch = new CrashOnNthCallSwitch(callNumber: 2);

        // Generation 1 (coalescing, cap 1, crashed): cycle 1 buffers the WorkflowStarted checkpoint and delivers its
        // continuation from the overlay (outbox call #1 passes through). Cycle 2's ActivityScheduled checkpoint trips
        // the cap: the segment folds into one durable commit that also persists the still-undelivered activity-start
        // intent as Pending, and a fresh segment begins. Outbox call #2 then crashes the drain mid-second-segment,
        // before that intent's overlay delivery lands anywhere durable.
        await using (var crashed = BuildProvider(store, services =>
        {
            services.AddCoalescingRuntimeCheckpointPersistence(options => options.MaxSegmentCheckpoints = 1);
            WrapOutboxProcessor(services, crashSwitch);
        }))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => SeedAndStartCompletingAsync(crashed));

            // The folded flush landed: durable state exists (unlike a pre-flush crash) but is not terminal.
            var execution = await crashed.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
            Assert.NotNull(execution);
            Assert.NotEqual(WorkflowExecutionStatus.Completed, execution!.Status);
            Assert.NotEqual(controlSnapshot, await SnapshotActivityStateAsync(crashed));

            // The flush advanced the durable queue past the consumed segment entry — replay cannot come from there.
            var queue = crashed.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            Assert.DoesNotContain("wfexec-1", await queue.ListPendingWorkflowExecutionIdsAsync(10));

            // The crash-redrive source: the fold durably persisted the second segment's continuation intent as Pending.
            var outbox = crashed.GetRequiredService<IRuntimePostCommitOutboxStore>();
            // The runtime stamps AvailableAt from the ambient clock, so query with the real clock, not the fixed Now.
            var deliverable = await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                now: DateTimeOffset.UtcNow.AddMinutes(5),
                limit: 10,
                workflowExecutionId: "wfexec-1"));
            var pending = Assert.Single(deliverable);
            Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, pending.Intent.Kind);
        }

        // Generation 2 (coalescing, honest): the sweep delivers the durably pending intent back into durable scheduler
        // work and re-drives the execution from the folded flush to the crash-free terminal state.
        await using (var recovered = BuildProvider(store, services =>
            services.AddCoalescingRuntimeCheckpointPersistence(options => options.MaxSegmentCheckpoints = 1)))
        {
            var sweep = ResolveResumptionService(recovered);
            await sweep.SweepAsync(new RuntimeResumptionSweepRequest());

            Assert.Equal(controlSnapshot, await SnapshotActivityStateAsync(recovered));
            var execution = await recovered.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
            Assert.Equal(WorkflowExecutionStatus.Completed, execution!.Status);

            // No duplicate redelivery source survives: the intent the fold persisted durably was marked Delivered by
            // the sweep's durable delivery, so nothing in the outbox can re-run the replayed segment.
            var outbox = recovered.GetRequiredService<IRuntimePostCommitOutboxStore>();
            Assert.Empty(await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                now: DateTimeOffset.UtcNow.AddMinutes(10),
                limit: 10,
                workflowExecutionId: "wfexec-1")));

            // The sweep's own RunSchedulerWork redrive envelope lands behind the completing dispatch and is stranded
            // by the drainer's terminal-status guard (#293) — never dispatched. A further sweep now finds the execution
            // terminal and purges that residue instead of re-driving it (spec 113): the converged terminal state stays
            // untouched, and the stranded item no longer keeps the completed execution discoverable.
            var secondSweep = await sweep.SweepAsync(new RuntimeResumptionSweepRequest());
            Assert.Equal(1, secondSweep.TerminalExecutionsPurged);
            Assert.Empty(secondSweep.Dispatches);
            Assert.Empty(await recovered.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListPendingWorkflowExecutionIdsAsync(10));
            Assert.Equal(controlSnapshot, await SnapshotActivityStateAsync(recovered));
            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await recovered.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"))!.Status);
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
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>());

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

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> RunCompletingControlAsync(
        StorageManifest manifest)
    {
        var store = new InMemoryDocumentStore(manifest);
        await using var provider = BuildProvider(store);
        await SeedAndStartCompletingAsync(provider);
        return await SnapshotActivityStateAsync(provider);
    }

    private static async Task SeedAndStartCompletingAsync(ServiceProvider provider)
    {
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        var executable = NewCompletingExecutable();
        await store.SaveAsync(executable);
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
    }

    // Replaces the registered outbox processor with a wrapper that passes calls through to the real processor until
    // the configured call number, then throws — modelling a crash at a drain-cycle boundary after real deliveries.
    private static void WrapOutboxProcessor(IServiceCollection services, CrashOnNthCallSwitch crashSwitch)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimePostCommitOutboxProcessor),
            serviceProvider => new CrashOnNthCallOutboxProcessor(
                (IRuntimePostCommitOutboxProcessor)(descriptor.ImplementationFactory?.Invoke(serviceProvider)
                    ?? ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType!)),
                crashSwitch),
            descriptor.Lifetime));
    }

    // A workflow that genuinely runs to completion (a Finish intrinsic root needs no CLR activity contract), so the
    // crash-free control run establishes a Completed terminal state the recovered generation must converge to.
    private static WorkflowExecutable NewCompletingExecutable()
    {
        var outcomeType = new ValueTypeDescriptor("String");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "elsa.intrinsic.finish",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = nameof(WorkflowIntrinsicKind.Finish), schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Outcome] = new(
                    WorkflowIntrinsicInputKeys.Outcome,
                    outcomeType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(outcomeType, JsonSerializer.SerializeToElement("Done"), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Finish);

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
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

    // Shared across the scoped processor instances of one generation so the call count spans drain cycles.
    private sealed class CrashOnNthCallSwitch(int callNumber)
    {
        private int _calls;

        public bool ShouldCrash() => Interlocked.Increment(ref _calls) >= callNumber;
    }

    private sealed class CrashOnNthCallOutboxProcessor(
        IRuntimePostCommitOutboxProcessor inner,
        CrashOnNthCallSwitch crashSwitch) : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (crashSwitch.ShouldCrash())
                throw new InvalidOperationException("Injected crash mid-second-segment.");

            return inner.ProcessAsync(request, cancellationToken);
        }
    }
}
