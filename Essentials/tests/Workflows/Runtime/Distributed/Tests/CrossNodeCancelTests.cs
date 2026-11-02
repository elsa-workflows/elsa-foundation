using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// A drain that keeps making progress keeps its placement past a lease, and a Cancel sent through another node stops
/// it (#2831). Node A runs a real workflow runtime behind its distributed provider; node B only routes. Both share the
/// in-memory placement store, command transport and clock, and each pump is driven by hand.
/// </summary>
public sealed class CrossNodeCancelTests
{
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_cancel_sent_through_another_node_stops_the_owners_progressing_drain()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        var placement = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var loop = new LoopProbe();
        var drains = new ObservedDrains();
        await using var runtime = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(loop);
                services.AddSingleton<IWorkflowSchedulerDrainObserver>(drains);
            })
            .Build(Enumerable.Range(1, 100_000).Select(index => $"actexec-{index}"));
        var nodeA = new NodeHarness(NodeA, placement, transport, clock, runtime.Services.GetRequiredService<InProcessWorkflowExecutionActorProvider>(), LeaseDuration);
        var executorB = new RecordingCommandExecutor();
        var nodeB = new NodeHarness(NodeB, placement, transport, clock, executorB, LeaseDuration);

        var executable = WorkflowExecutionHarness.NewExecutable(WhileTrue(LoopStepNode()));
        await runtime.PublishAsync(executable, "loop");
        var executionId = runtime.ExecutionId;
        await nodeA.PlacementService.TryClaimAsync(executionId);

        try
        {
            // The start arrives on node B, which forwards it; node A's pump starts the drain and leaves it running.
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, (await nodeB.DispatchAsync(executionId, Start(executable.Identity, executionId, clock.GetUtcNow()))).Status);
            await PumpSweeps.AdvancingAsync(nodeA.Pump, clock);
            await PumpSweeps.UntilAsync(() => Task.FromResult(loop.Steps >= 3), "The loop did not keep running after the sweep moved on.");

            // For longer than a placement lease, node A's sweeps keep renewing the drain's placement. Node B, sweeping
            // first each time, sees the start once its transport lease lapses, and may neither claim nor run it.
            var sawStart = false;
            for (var elapsed = TimeSpan.Zero; elapsed <= LeaseDuration; elapsed += TimeSpan.FromSeconds(10))
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                sawStart |= (await transport.PeekAsync(executionId, clock.GetUtcNow(), 10)).Count > 0;
                Assert.Equal(0, (await PumpSweeps.AdvancingAsync(nodeB.Pump, clock)).ClaimedCount);
                await PumpSweeps.AdvancingAsync(nodeA.Pump, clock);
                Assert.Equal(NodeA, (await nodeA.PlacementService.FindOwnerAsync(executionId))!.OwnerId);
            }

            Assert.True(sawStart, "The start's transport lease did not lapse while the drain ran.");
            Assert.Empty(executorB.Processed);
            var steps = loop.Steps;
            await PumpSweeps.UntilAsync(() => Task.FromResult(loop.Steps > steps), "The drain stopped before the Cancel.");

            // A Cancel through node B waits in the transport for node A's next sweep, which delivers it without waiting
            // for the drain and renews the placement.
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, (await nodeB.DispatchAsync(executionId, Cancel(executionId, clock.GetUtcNow()))).Status);
            var delivering = await PumpSweeps.AdvancingAsync(nodeA.Pump, clock);
            Assert.Equal((1, 1), (delivering.RenewedCount, delivering.DispatchedCommandCount));

            await PumpSweeps.UntilAsync(async () =>
                (await runtime.ReadRunAsync(executionId)).WorkflowState?.Status == WorkflowExecutionStatus.Cancelled &&
                await transport.CountPendingAsync(executionId) == 0,
                "The Cancel did not stop the drain, or a command was left unacknowledged.");
            Assert.Contains(drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
            Assert.False(loop.RunawayGuardReached);
            Assert.Equal(NodeA, (await nodeA.PlacementService.FindOwnerAsync(executionId))!.OwnerId);
            Assert.Empty(executorB.Processed);
        }
        finally
        {
            await loop.StopAsync();
        }
    }

    private static ExecutableNode WhileTrue(ExecutableNode body)
    {
        var condition = new ValueTypeDescriptor("Boolean");
        return new(
            executableNodeId: "node-while",
            authoredActivityId: "authored-node-while",
            activityType: typeof(WhileActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(SerializationServices.PayloadSerializer(), typeof(WhileActivity))),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new(
                    "Condition",
                    condition,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(condition, JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));
    }

    private static ExecutableNode LoopStepNode() =>
        new(
            executableNodeId: "node-step",
            authoredActivityId: "authored-node-step",
            activityType: typeof(LoopStep).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(SerializationServices.PayloadSerializer(), typeof(LoopStep))),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutionCommandEnvelope Start(WorkflowExecutableIdentity executable, string executionId, DateTimeOffset now)
    {
        // The artifact id rides on the command and the envelope, as WorkflowStartDispatcher puts it there.
        var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = executable.ArtifactId };
        var payload = JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(executable, executable.ArtifactId));
        return new(
            envelopeId: "env-start",
            workflowExecutionId: executionId,
            command: new WorkflowExecutionCommand("cmd-start", executionId, WorkflowExecutionCommandKind.Start, now, payload, metadata),
            idempotencyKey: "idem-start",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            metadata: metadata);
    }

    private static WorkflowExecutionCommandEnvelope Cancel(string executionId, DateTimeOffset now) =>
        new(
            envelopeId: "env-cancel",
            workflowExecutionId: executionId,
            command: new WorkflowExecutionCommand("cmd-cancel", executionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            idempotencyKey: "idem-cancel",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);

    /// <summary>Counts the loop's steps, and ends a loop the test no longer needs.</summary>
    public sealed class LoopProbe
    {
        private readonly CancellationTokenSource _stopping = new();
        private int _steps;

        public int Steps => Volatile.Read(ref _steps);

        public bool RunawayGuardReached { get; private set; }

        public CancellationToken Stopping => _stopping.Token;

        public void Step()
        {
            if (Interlocked.Increment(ref _steps) <= 20_000)
                return;
            RunawayGuardReached = true;
            throw new InvalidOperationException("The test's runaway guard was reached before the Cancel took effect.");
        }

        public Task StopAsync() => _stopping.CancelAsync();
    }

    /// <summary>One step of an endless loop. It awaits, as a step doing real I/O does, so the drain gives up its thread
    /// between steps.</summary>
    [ActivitySideEffectProfile(SideEffectProfile.External)]
    public sealed class LoopStep(LoopProbe probe) : Activity<ActivityUnit>
    {
        protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            probe.Step();
            await Task.Delay(TimeSpan.FromMilliseconds(1), probe.Stopping);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
    }

    private sealed class ObservedDrains : IWorkflowSchedulerDrainObserver
    {
        private readonly ConcurrentQueue<RuntimeSchedulerDrainResult> _results = new();

        public IReadOnlyCollection<RuntimeSchedulerDrainResult> Results => _results.ToArray();

        public ValueTask OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result, CancellationToken cancellationToken = default)
        {
            _results.Enqueue(result);
            return ValueTask.CompletedTask;
        }
    }
}
