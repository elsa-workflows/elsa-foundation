using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Cluster.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Distributed.Tests.Placement;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// A drain that keeps making progress keeps its placement past a lease, and a Cancel sent through another node stops
/// it (#2831). A hand-off makes such a drain yield, so the member releases its placement within a lease and the next
/// owner continues the run (#3678). Node A runs a real workflow runtime behind its distributed provider; node B only
/// routes, or drains on the same runtime as a member sharing node A's database would. Both share the in-memory
/// placement store, command transport and clock, and each pump is driven by hand. A draining member is composed through
/// the feature over a membership instead, with the same real runtime as its local mailbox.
/// </summary>
public sealed class CrossNodeCancelTests
{
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_cancel_sent_through_another_node_stops_the_owners_progressing_drain()
    {
        await using var cluster = await LoopCluster.StartAsync(nodeBDrains: false);
        var (clock, transport, loop, executionId) = (cluster.Clock, cluster.Transport, cluster.Loop, cluster.ExecutionId);

        // For longer than a placement lease, node A's sweeps keep renewing the drain's placement. Node B, sweeping
        // first each time, sees the start once its transport lease lapses, and may neither claim nor run it.
        var sawStart = false;
        for (var elapsed = TimeSpan.Zero; elapsed <= LeaseDuration; elapsed += TimeSpan.FromSeconds(10))
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            sawStart |= (await transport.PeekAsync(executionId, clock.GetUtcNow(), 10)).Count > 0;
            Assert.Equal(0, (await PumpSweeps.AdvancingAsync(cluster.NodeB.Pump, clock)).ClaimedCount);
            await PumpSweeps.AdvancingAsync(cluster.NodeA.Pump, clock);
            Assert.Equal(NodeA, (await cluster.NodeA.PlacementService.FindOwnerAsync(executionId))!.OwnerId);
        }

        Assert.True(sawStart, "The start's transport lease did not lapse while the drain ran.");
        Assert.Empty(cluster.ExecutorB.Processed);
        var steps = loop.Steps;
        await PumpSweeps.UntilAsync(() => Task.FromResult(loop.Steps > steps), "The drain stopped before the Cancel.");

        // A Cancel through node B waits in the transport for node A's next sweep, which delivers it without waiting
        // for the drain and renews the placement.
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, (await cluster.NodeB.DispatchAsync(executionId, Cancel(executionId, clock.GetUtcNow()))).Status);
        var delivering = await PumpSweeps.AdvancingAsync(cluster.NodeA.Pump, clock);
        Assert.Equal((1, 1), (delivering.RenewedCount, delivering.DispatchedCommandCount));

        await cluster.UntilCancelledAsync();
        Assert.Contains(cluster.Drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
        Assert.False(loop.RunawayGuardReached);
        Assert.Equal(NodeA, (await cluster.NodeA.PlacementService.FindOwnerAsync(executionId))!.OwnerId);
        Assert.Empty(cluster.ExecutorB.Processed);
    }

    [Fact]
    public async Task A_hand_off_makes_a_progressing_drain_yield_and_the_next_owner_continues_the_run()
    {
        await using var cluster = await LoopCluster.StartAsync(nodeBDrains: true);
        var (clock, loop, executionId) = (cluster.Clock, cluster.Loop, cluster.ExecutionId);

        // Node A hands the execution off, as a member that drains, stops or no longer meets its requirement does. The
        // drain yields at its next step, so the hand-off finishes and releases the placement while node A holds it.
        Assert.Equal(1, await cluster.NodeA.Pump.HandOffAllAsync("the member is draining").AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains(cluster.Drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
        Assert.Null(await cluster.NodeA.PlacementService.FindOwnerAsync(executionId));
        var steps = loop.Steps;
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Equal(steps, loop.Steps);

        // The yielded segment flushed its next step durably. As the resumption sweep would, deliver that step and re-drive
        // the execution through placement: node B claims it and continues the loop from there.
        await using (var scope = cluster.Runtime.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 100, workflowExecutionId: executionId));
        await cluster.Transport.SendAsync(executionId, NodeHarness.Envelope(executionId, "env-redrive", clock.GetUtcNow()), clock.GetUtcNow());
        Assert.Equal(1, (await PumpSweeps.AdvancingAsync(cluster.NodeB.Pump, clock)).ClaimedCount);
        await PumpSweeps.UntilAsync(() => Task.FromResult(loop.Steps >= steps + 3), "The next owner did not continue the run.");
        Assert.Equal(NodeB, (await cluster.NodeB.PlacementService.FindOwnerAsync(executionId))!.OwnerId);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await cluster.NodeB.DispatchAsync(executionId, Cancel(executionId, clock.GetUtcNow()))).Status);
        await cluster.UntilCancelledAsync();
        Assert.False(loop.RunawayGuardReached);
    }

    [Fact]
    public async Task A_cancel_sent_while_the_owner_hands_off_takes_effect_on_the_next_owner()
    {
        await using var cluster = await LoopCluster.StartAsync(nodeBDrains: true);
        var (clock, loop, executionId) = (cluster.Clock, cluster.Loop, cluster.ExecutionId);

        // A step that is still running holds the hand-off at the drain's next step.
        var held = loop.HoldNextStep();
        await held.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var handOff = cluster.NodeA.Pump.HandOffAllAsync("the member is draining").AsTask();

        // Node A still holds the placement, so node B forwards the Cancel to the transport.
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, (await cluster.NodeB.DispatchAsync(executionId, Cancel(executionId, clock.GetUtcNow()))).Status);
        Assert.False(handOff.IsCompleted);
        held.Release();
        Assert.Equal(1, await handOff.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains(cluster.Drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
        Assert.Null(await cluster.NodeA.PlacementService.FindOwnerAsync(executionId));

        // The next owner claims the execution for the Cancel and runs it.
        Assert.Equal(1, (await PumpSweeps.AdvancingAsync(cluster.NodeB.Pump, clock)).ClaimedCount);
        await cluster.UntilCancelledAsync();
        Assert.False(loop.RunawayGuardReached);
    }

    /// <summary>
    /// A member that starts draining hands off, in its own sweep, an execution whose drain keeps making progress (#3678).
    /// The member runs the loop inline, as it runs an execute request it accepted. Its sweeps run every sweep interval
    /// until its placement lease would expire, and one of them releases the placement before that; without the yield the
    /// hand-off waits for a turn that never ends, and the lease lapses with the drain still running.
    /// </summary>
    [Fact]
    public async Task A_draining_member_hands_off_a_progressing_execution_within_one_lease()
    {
        var loop = new LoopProbe();
        var drains = new ObservedDrains();
        await using var runtime = BuildRuntime(loop, drains);
        await using var cluster = new PlacementCluster();
        try
        {
            var local = runtime.Services.GetRequiredService<InProcessWorkflowExecutionActorProvider>();
            var member = await cluster.StartAsync("host-a", setup =>
            {
                setup.LeaseDuration = LeaseDuration;
                setup.Configure = services => services.Replace(ServiceDescriptor.Singleton(local));
            });
            var executionId = runtime.ExecutionId;
            var executable = WorkflowExecutionHarness.NewExecutable(WhileTrue(LoopStepNode()));
            await runtime.PublishAsync(executable, "loop");
            await using (var scope = member.Runtime.Services.CreateAsyncScope())
                Assert.True((await scope.ServiceProvider.GetRequiredService<IExecutionPlacementService>().TryClaimAsync(executionId)).IsOwnedByClaimant);
            var lease = (await cluster.State.Placement.FindAsync(executionId))!;
            var actor = await local.GetAgentAsync(new WorkflowExecutionActorActivationRequest(
                executionId, WorkflowExecutionActorActivationReason.Start, cluster.Now, member.HostId, WorkflowExecutionActorCapabilities.None));
            var run = actor.EnqueueAsync(Start(executable.Identity, executionId, cluster.Now)).AsTask();
            await PumpSweeps.UntilAsync(() => Task.FromResult(loop.Steps >= 3), "The loop did not start.");

            member.Member.Become(MemberStatus.Draining);
            var handedOff = 0;
            var released = false;
            while (!released && cluster.Now + SweepInterval < lease.ExpiresAt)
            {
                cluster.Clock.Advance(SweepInterval);
                handedOff += (await PumpSweeps.AdvancingAsync(member.Runtime.Pump, cluster.Clock)).HandedOffCount;
                released = await WithinAsync(async () => await cluster.State.Placement.FindAsync(executionId) is null, TimeSpan.FromSeconds(2));
            }

            Assert.True(released, "The draining member did not release the placement before its lease expired.");
            Assert.True(cluster.Now < lease.ExpiresAt);
            Assert.Equal(1, handedOff);
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await run.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            Assert.Contains(drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
            var steps = loop.Steps;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.Equal(steps, loop.Steps);
            Assert.False(loop.RunawayGuardReached);
        }
        finally
        {
            await loop.StopAsync();
        }
    }

    /// <summary>
    /// A Cancel the owner's own pump dispatches while the owner hands the execution off does not reach the passivating
    /// mailbox: resolving the mailbox waits for the passivation, which holds the execution's activation. Once the drain
    /// yields and the passivation ends, the Cancel runs on a fresh mailbox on this node and stops the run, and its
    /// transport item is acknowledged. A sweep only overlaps a hand-off that the stop path started; one a sweep started
    /// keeps later sweeps away from the execution.
    /// </summary>
    [Fact]
    public async Task A_cancel_the_owners_pump_dispatches_during_its_hand_off_runs_once_the_hand_off_ends()
    {
        await using var cluster = await LoopCluster.StartAsync(nodeBDrains: false);
        var (clock, loop, executionId) = (cluster.Clock, cluster.Loop, cluster.ExecutionId);
        var local = cluster.Runtime.Services.GetRequiredService<InProcessWorkflowExecutionActorProvider>();
        var actor = await local.GetAgentAsync(new WorkflowExecutionActorActivationRequest(
            executionId, WorkflowExecutionActorActivationReason.Recovery, clock.GetUtcNow(), NodeA, WorkflowExecutionActorCapabilities.None));

        var held = loop.HoldNextStep();
        await held.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var handOff = cluster.NodeA.Pump.HandOffAllAsync("the shell is stopping").AsTask();
        await PumpSweeps.UntilAsync(() => Task.FromResult(actor.Descriptor.Status == WorkflowExecutionActorStatus.Passivating), "The hand-off did not start passivating.");

        // Node A still holds the placement, so its sweep leases the Cancel and dispatches it without waiting for the drain.
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, (await cluster.NodeB.DispatchAsync(executionId, Cancel(executionId, clock.GetUtcNow()))).Status);
        Assert.Equal(1, (await PumpSweeps.AdvancingAsync(cluster.NodeA.Pump, clock)).DispatchedCommandCount);
        Assert.False(handOff.IsCompleted);
        Assert.NotEqual(WorkflowExecutionStatus.Cancelled, (await cluster.Runtime.ReadRunAsync(executionId)).WorkflowState?.Status);

        held.Release();
        Assert.Equal(1, await handOff.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains(cluster.Drains.Results, result => result.StopReason == RuntimeSchedulerDrainStopReason.Yielded);
        await cluster.UntilCancelledAsync();
        Assert.Equal(WorkflowExecutionActorStatus.Passivated, actor.Descriptor.Status);
        Assert.Empty(cluster.ExecutorB.Processed);
        Assert.False(loop.RunawayGuardReached);
    }

    private static async Task<bool> WithinAsync(Func<Task<bool>> condition, TimeSpan wait)
    {
        using var timeout = new CancellationTokenSource(wait);
        while (!await condition())
        {
            if (timeout.IsCancellationRequested)
                return false;
            await Task.Delay(10);
        }

        return true;
    }

    private static WorkflowExecutionHarness BuildRuntime(LoopProbe loop, ObservedDrains drains) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(loop);
                services.AddSingleton<IWorkflowSchedulerDrainObserver>(drains);
            })
            .Build(Enumerable.Range(1, 100_000).Select(index => $"actexec-{index}"));

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
        private StepHold? _hold;
        private int _steps;

        public int Steps => Volatile.Read(ref _steps);

        public bool RunawayGuardReached { get; private set; }

        public CancellationToken Stopping => _stopping.Token;

        /// <summary>Holds the loop inside its next step until the hold is released.</summary>
        public StepHold HoldNextStep()
        {
            var hold = new StepHold();
            Volatile.Write(ref _hold, hold);
            return hold;
        }

        internal Task PassAsync() => Interlocked.Exchange(ref _hold, null)?.EnterAsync() ?? Task.CompletedTask;

        public void Step()
        {
            if (Interlocked.Increment(ref _steps) <= 20_000)
                return;
            RunawayGuardReached = true;
            throw new InvalidOperationException("The test's runaway guard was reached before the Cancel took effect.");
        }

        public Task StopAsync() => _stopping.CancelAsync();
    }

    public sealed class StepHold
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        internal Task EnterAsync()
        {
            _entered.TrySetResult();
            return _released.Task;
        }
    }

    /// <summary>One step of an endless loop. It awaits, as a step doing real I/O does, so the drain gives up its thread
    /// between steps.</summary>
    [ActivitySideEffectProfile(SideEffectProfile.External)]
    public sealed class LoopStep(LoopProbe probe) : Activity<ActivityUnit>
    {
        protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            probe.Step();
            await probe.PassAsync().WaitAsync(probe.Stopping);
            await Task.Delay(TimeSpan.FromMilliseconds(1), probe.Stopping);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
    }

    /// <summary>The two nodes, with node A's drain of an endless loop started through node B and left running by node A's
    /// sweep. Node B drains on node A's runtime when <c>nodeBDrains</c> is set, and records what it would run otherwise.</summary>
    private sealed class LoopCluster : IAsyncDisposable
    {
        private LoopCluster(bool nodeBDrains)
        {
            Runtime = BuildRuntime(Loop, Drains);
            var local = Runtime.Services.GetRequiredService<InProcessWorkflowExecutionActorProvider>();
            NodeA = new NodeHarness(CrossNodeCancelTests.NodeA, Placement, Transport, Clock, local, LeaseDuration);
            NodeB = nodeBDrains
                ? new NodeHarness(CrossNodeCancelTests.NodeB, Placement, Transport, Clock, local, LeaseDuration)
                : new NodeHarness(CrossNodeCancelTests.NodeB, Placement, Transport, Clock, ExecutorB, LeaseDuration);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        public InMemoryExecutionPlacementStore Placement { get; } = new();
        public InMemoryExecutionCommandTransport Transport { get; } = new();
        public LoopProbe Loop { get; } = new();
        public ObservedDrains Drains { get; } = new();
        public RecordingCommandExecutor ExecutorB { get; } = new();
        public WorkflowExecutionHarness Runtime { get; }
        public NodeHarness NodeA { get; }
        public NodeHarness NodeB { get; }
        public string ExecutionId => Runtime.ExecutionId;

        public static async Task<LoopCluster> StartAsync(bool nodeBDrains)
        {
            var cluster = new LoopCluster(nodeBDrains);
            try
            {
                var executable = WorkflowExecutionHarness.NewExecutable(WhileTrue(LoopStepNode()));
                await cluster.Runtime.PublishAsync(executable, "loop");
                await cluster.NodeA.PlacementService.TryClaimAsync(cluster.ExecutionId);

                // The start arrives on node B, which forwards it; node A's pump starts the drain and leaves it running.
                Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred,
                    (await cluster.NodeB.DispatchAsync(cluster.ExecutionId, Start(executable.Identity, cluster.ExecutionId, cluster.Clock.GetUtcNow()))).Status);
                await PumpSweeps.AdvancingAsync(cluster.NodeA.Pump, cluster.Clock);
                await PumpSweeps.UntilAsync(() => Task.FromResult(cluster.Loop.Steps >= 3), "The loop did not keep running after the sweep moved on.");
                return cluster;
            }
            catch
            {
                await cluster.DisposeAsync();
                throw;
            }
        }

        public Task UntilCancelledAsync() =>
            PumpSweeps.UntilAsync(async () =>
                (await Runtime.ReadRunAsync(ExecutionId)).WorkflowState?.Status == WorkflowExecutionStatus.Cancelled &&
                await Transport.CountPendingAsync(ExecutionId) == 0,
                "The Cancel did not stop the drain, or a command was left unacknowledged.");

        public async ValueTask DisposeAsync()
        {
            await Loop.StopAsync();
            await Runtime.DisposeAsync();
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
