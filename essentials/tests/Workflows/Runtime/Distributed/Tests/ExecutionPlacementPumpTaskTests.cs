using System.Collections.Concurrent;
using System.Threading.Channels;
using Elsa.Cluster.Core.Options;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class ExecutionPlacementPumpTaskTests
{
    private const string ExecutionId = "wf-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_ClaimsUnownedBacklog_DispatchesLocally_AndAcks()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new RecordingCommandExecutor();
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);

        // A command lands in the transport inbox for an execution nobody owns yet.
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        var result = await node.Pump.SweepOnceAsync();

        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(1, result.DispatchedCommandCount);
        Assert.Equal(1, result.AckedCount);
        Assert.Equal(new[] { "env-1" }, executor.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task Sweep_SkipsExecutionOwnedByAnotherLiveNode()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);

        var nodeA = new NodeHarness(NodeA, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);
        var executorB = new RecordingCommandExecutor();
        var nodeB = new NodeHarness(NodeB, store, transport, clock, executorB, LeaseDuration);

        // Node A owns the execution; a command arrives in the shared inbox.
        await nodeA.PlacementService.TryClaimAsync(ExecutionId);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        // Node B's sweep must not steal a live-owned execution.
        var result = await nodeB.Pump.SweepOnceAsync();

        Assert.Equal(0, result.ClaimedCount);
        Assert.Empty(executorB.Processed);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task Sweep_RenewsPlacementThisNodeHolds()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var node = new NodeHarness(NodeA, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);

        var initial = await node.PlacementService.TryClaimAsync(ExecutionId);

        // Advance within the lease window and sweep; the held lease should be renewed (expiry pushed out, token bumped).
        clock.Advance(TimeSpan.FromSeconds(10));
        var result = await node.Pump.SweepOnceAsync();

        Assert.Equal(1, result.RenewedCount);
        var renewed = await node.PlacementService.FindOwnerAsync(ExecutionId);
        Assert.NotNull(renewed);
        Assert.True(renewed!.PlacementToken > initial.Lease.PlacementToken);
        Assert.Equal(_now.AddSeconds(10) + LeaseDuration, renewed.ExpiresAt);
    }

    [Fact]
    public async Task Sweep_AfterOwnerPlacementExpires_ClaimsAndReDrivesOnFailover()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);

        var executorA = new RecordingCommandExecutor();
        var nodeA = new NodeHarness(NodeA, store, transport, clock, executorA, LeaseDuration);
        var executorB = new RecordingCommandExecutor();
        var nodeB = new NodeHarness(NodeB, store, transport, clock, executorB, LeaseDuration);

        // Node A owns the execution and has leased the command (in-flight) but dies before acking.
        await nodeA.PlacementService.TryClaimAsync(ExecutionId);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        var leasedByA = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);

        // Time passes beyond both the placement lease and the transport visibility lease: A is gone.
        clock.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

        var result = await nodeB.Pump.SweepOnceAsync();

        // Node B claims placement, re-leases the stranded command, dispatches it locally, and acks.
        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(1, result.DispatchedCommandCount);
        Assert.Equal(1, result.AckedCount);
        Assert.Equal(new[] { "env-1" }, executorB.Processed);
        Assert.Empty(executorA.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));

        var owner = await nodeB.PlacementService.FindOwnerAsync(ExecutionId);
        Assert.Equal(NodeB, owner!.OwnerId);
    }

    [Fact]
    public async Task Sweep_DoesNotAckWhenDispatchIsDeferred()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);

        // Node A owns placement so node B's local dispatch would forward (Deferred) instead of draining.
        var nodeA = new NodeHarness(NodeA, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);
        await nodeA.PlacementService.TryClaimAsync(ExecutionId);

        // A command is in the inbox. Node B cannot own it (A is live), so its sweep claims nothing and acks nothing.
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        var nodeB = new NodeHarness(NodeB, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);

        var result = await nodeB.Pump.SweepOnceAsync();

        Assert.Equal(0, result.AckedCount);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));
    }

    /// <summary>
    /// A command whose drain keeps running must not hold the sweep (#2831). The sweep stops waiting after
    /// <c>InlineDispatchWait</c>. Its next sweep renews the node's placement and delivers a Cancel to the busy mailbox
    /// at once, while a further non-Cancel command waits until the drain ends. A command whose transport lease lapses
    /// mid-drain is not queued again, and every command is acknowledged.
    /// </summary>
    [Fact]
    public async Task A_running_drain_does_not_hold_the_sweep_from_renewing_or_delivering_a_cancel()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1");
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        // The first sweep claims the execution and starts env-1's drain, which keeps running.
        var first = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 1, 0), (first.ClaimedCount, first.DispatchedCommandCount, first.AckedCount));
        Assert.True(executor.Holding.Task.IsCompleted);

        // Within the placement lease, the next sweep renews it and delivers the Cancel without waiting; env-3 waits.
        clock.Advance(TimeSpan.FromSeconds(10));
        await transport.SendAsync(ExecutionId, CancelEnvelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-3", clock.GetUtcNow()), clock.GetUtcNow());
        var beforeSecond = clock.GetUtcNow();
        var second = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 1, 0), (second.RenewedCount, second.DispatchedCommandCount, second.AckedCount));
        Assert.Equal(beforeSecond, clock.GetUtcNow());

        // env-1's transport lease lapses while the placement is still live: it is leased again but not dispatched twice.
        clock.Advance(TimeSpan.FromSeconds(25));
        var third = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 0), (third.RenewedCount, third.DispatchedCommandCount));
        Assert.Equal(NodeA, (await node.PlacementService.FindOwnerAsync(ExecutionId))!.OwnerId);

        // The drain ends: env-1 and the Cancel are acknowledged under their latest leases, and the held env-3 is released.
        executor.Release.SetResult();
        await WaitUntilAsync(async () => (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count == 0);
        Assert.Equal(["env-1", "env-2"], executor.Processed);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));

        // The next sweep runs env-3 inline, without waiting for its lease to lapse.
        var fourth = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 1), (fourth.DispatchedCommandCount, fourth.AckedCount));
        Assert.Equal(["env-1", "env-2", "env-3"], executor.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
        Assert.Empty(await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10));
    }

    /// <summary>
    /// A renewal that finds the placement taken passivates the local actor. The drain yields to it at its next step (#3678),
    /// but the step it is in does not end here, as one blocked on I/O would not. The sweep must not wait that out (#2831).
    /// Until the passivation ends, later sweeps leave the execution alone: a command leased meanwhile would wait for the
    /// passivation, then run on a fresh actor.
    /// </summary>
    [Fact]
    public async Task Passivating_a_busy_actor_after_a_lost_renewal_does_not_hold_the_sweep()
    {
        var store = new RenewalRefusingStore(new InMemoryExecutionPlacementStore());
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1");
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        await SweepAdvancingAsync(node, clock);
        Assert.True(executor.Holding.Task.IsCompleted);

        // The lease is still listed as this node's, but its renewal loses (as to a reclaim racing it).
        store.RefuseRenewals = true;
        clock.Advance(TimeSpan.FromSeconds(10));
        var result = await SweepAdvancingAsync(node, clock);
        Assert.Equal(0, result.RenewedCount);

        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        var next = await SweepAdvancingAsync(node, clock);
        Assert.Equal((0, 0), (next.ClaimedCount, next.DispatchedCommandCount));
        Assert.Equal(["env-1"], (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Select(item => item.Envelope.EnvelopeId));
        executor.Release.SetResult();
    }

    /// <summary>Forwards to a placement store, but can make every renewal lose.</summary>
    private sealed class RenewalRefusingStore(IExecutionPlacementStore inner) : IExecutionPlacementStore
    {
        public bool RefuseRenewals { get; set; }

        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, cancellationToken);

        public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.TryClaimAsync(claim, now, cancellationToken);

        public ValueTask<ExecutionPlacementLease?> TryRenewAsync(ExecutionPlacementLease held, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
            RefuseRenewals ? ValueTask.FromResult<ExecutionPlacementLease?>(null) : inner.TryRenewAsync(held, now, expiresAt, cancellationToken);

        public ValueTask<bool> ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) =>
            inner.ListOwnedAsync(request, cancellationToken);
    }

    /// <summary>A drain that fails still releases the commands held behind it; the failed command keeps its lease.</summary>
    [Fact]
    public async Task A_failed_drain_releases_the_commands_held_behind_it()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1") { FailHeld = true };
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        await SweepAdvancingAsync(node, clock);

        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        var held = await SweepAdvancingAsync(node, clock);
        Assert.Equal(0, held.DispatchedCommandCount);
        Assert.Equal(2, (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count);

        executor.Release.SetResult();
        await WaitUntilAsync(async () => (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count == 1);
        Assert.Equal(["env-1"], (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Select(item => item.Envelope.EnvelopeId));
    }

    /// <summary>
    /// A drain that ends releases only the commands held back behind it. A command this node dispatched inline earlier,
    /// which came back Deferred, keeps its lease until the lease lapses, as it would without a drain.
    /// </summary>
    [Fact]
    public async Task A_drain_that_ends_releases_only_the_commands_held_behind_it()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1") { Shed = ["env-0"] };
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-0", _now), _now);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        // env-0 is deferred and stays leased; env-1 starts a drain that keeps running, and env-2 is held behind it.
        var first = await SweepAdvancingAsync(node, clock);
        Assert.Equal((2, 0), (first.DispatchedCommandCount, first.AckedCount));
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        Assert.Equal(0, (await SweepAdvancingAsync(node, clock)).DispatchedCommandCount);

        executor.Release.SetResult();
        await WaitUntilAsync(async () => await transport.CountPendingAsync(ExecutionId) == 2);
        await WaitUntilAsync(async () => (await transport.PeekAsync(ExecutionId, clock.GetUtcNow(), 10)).Count > 0);
        Assert.Equal(["env-2"], (await transport.PeekAsync(ExecutionId, clock.GetUtcNow(), 10)).Select(item => item.Envelope.EnvelopeId));
        Assert.Equal(["env-0"], (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Select(item => item.Envelope.EnvelopeId));
    }

    /// <summary>
    /// A detached dispatch acknowledges under the item's latest lease, however often a sweep leases the item again while
    /// an ack is in flight. A delivered command left leased would wait out its lease and run again.
    /// </summary>
    [Fact]
    public async Task A_detached_dispatch_acks_under_every_lease_taken_while_its_acks_ran()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new AckGatingTransport(new InMemoryExecutionCommandTransport(), gatedAcks: 2);
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1");
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        await SweepAdvancingAsync(node, clock);

        // The drain ends. While each of its acks runs, sweeps renew the placement and lease env-1 again once its lease
        // lapses, without dispatching it.
        executor.Release.SetResult();
        for (var ack = 0; ack < 2; ack++)
        {
            var running = await transport.NextGatedAckAsync();
            clock.Advance(TimeSpan.FromSeconds(10));
            await SweepAdvancingAsync(node, clock);
            clock.Advance(TimeSpan.FromSeconds(25));
            Assert.Equal(0, (await SweepAdvancingAsync(node, clock)).DispatchedCommandCount);
            running.SetResult();
        }

        await WaitUntilAsync(async () => await transport.CountPendingAsync(ExecutionId) == 0);
        Assert.Equal(["env-1"], executor.Processed);
    }

    /// <summary>
    /// A hand-off that runs once a detached dispatch was delivered, but before its ack, leaves that item to the dispatch.
    /// Released instead, the ack would fail and another member would run the delivered command again.
    /// </summary>
    [Fact]
    public async Task A_hand_off_leaves_a_delivered_detached_dispatch_to_acknowledge_its_command()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new AckGatingTransport(new InMemoryExecutionCommandTransport(), gatedAcks: 1);
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1");
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        var executorB = new RecordingCommandExecutor();
        var nodeB = new NodeHarness(NodeB, store, transport, clock, executorB, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        await SweepAdvancingAsync(node, clock);

        // The drain ends and its ack is held while node A hands the execution off.
        executor.Release.SetResult();
        var ack = await transport.NextGatedAckAsync();
        Assert.Equal(1, await node.Pump.HandOffAllAsync("the test hands it off"));
        Assert.Null(await store.FindAsync(ExecutionId));

        ack.SetResult();
        await WaitUntilAsync(async () => (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count == 0);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
        Assert.Equal(0, (await SweepAdvancingAsync(nodeB, clock)).DispatchedCommandCount);
        Assert.Empty(executorB.Processed);
        Assert.Equal(["env-1"], executor.Processed);
    }

    /// <summary>
    /// Through the composition the feature uses, the waits of every persistence scope come out of one budget per sweep,
    /// a third of the placement lease. Once it is spent, a busy execution, here in the next scope, is detached without a
    /// wait (#2831). A detached dispatch acknowledges through an operation scope of its own, the sweep's having ended.
    /// </summary>
    [Fact]
    public async Task Every_scope_shares_one_wait_budget_and_a_detached_dispatch_acks_through_its_own_scope()
    {
        var clock = new FakeTimeProvider(_now);
        var waits = new WaitRecordingClock(clock);
        var executor = new HoldingCommandExecutor("env-1", "env-2", "env-3");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(waits);
        services.AddSingleton<IWorkflowExecutionCommandExecutor>(executor);
        services.Configure<ClusterMembershipOptions>(options => options.HostId = NodeA);
        services.AddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.AddSingleton(new JoinSweepLedger());
        services.AddSingleton<IPersistenceScopeSource>(new ScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeDistributedFeature { LeaseDurationSeconds = 6 }.ConfigureServices(services);
        services.Configure<ExecutionPlacementPumpOptions>(options => options.InlineDispatchWait = TimeSpan.FromSeconds(1.2));
        var transport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        services.Replace(ServiceDescriptor.Scoped<IExecutionCommandTransport>(sp =>
            new ScopeBoundTransport((IExecutionCommandTransport)transport.ImplementationFactory!(sp))));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>());
        var scopes = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        await pump.SweepOnceAsync();
        await SendAsync(scopes, "tenant-a", "wf-1", "env-1");
        await SendAsync(scopes, "tenant-a", "wf-2", "env-2");
        await SendAsync(scopes, "tenant-b", "wf-3", "env-3");

        // wf-1 waits its 1.2 s, wf-2 what is left of the 2 s, and wf-3 nothing.
        waits.Waits.Clear();
        var sweep = await PumpSweeps.AdvancingAsync(pump, clock);
        Assert.Equal((3, 3, 0), (sweep.ClaimedCount, sweep.DispatchedCommandCount, sweep.AckedCount));
        Assert.Equal(2, waits.Waits.Count);
        Assert.True(waits.Waits.Sum(wait => wait.Ticks) <= TimeSpan.FromSeconds(2).Ticks, $"The sweep waited {string.Join(", ", waits.Waits)}.");

        executor.Release.SetResult();
        await WaitUntilAsync(async () =>
            await CountPendingAsync(scopes, "tenant-a", "wf-1") + await CountPendingAsync(scopes, "tenant-a", "wf-2") + await CountPendingAsync(scopes, "tenant-b", "wf-3") == 0);
        Assert.Equal(["env-1", "env-2", "env-3"], executor.Processed.Order());
    }

    private static async Task SendAsync(IPersistenceOperationScopeFactory scopes, string scope, string executionId, string envelopeId)
    {
        await using var operation = await scopes.CreateAsync(new PersistenceScope(scope));
        var now = operation.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var envelope = NodeHarness.Envelope(executionId, envelopeId, now);
        await operation.ServiceProvider.GetRequiredService<IExecutionCommandTransport>().SendAsync(
            executionId,
            new WorkflowExecutionCommandEnvelope(envelope.EnvelopeId, executionId, envelope.Command, envelope.IdempotencyKey, envelope.DeliveryMode, now, partition: new WorkflowExecutionPartition(scope)),
            now);
    }

    private static async Task<int> CountPendingAsync(IPersistenceOperationScopeFactory scopes, string scope, string executionId)
    {
        await using var operation = await scopes.CreateAsync(new PersistenceScope(scope));
        return await operation.ServiceProvider.GetRequiredService<IExecutionCommandTransport>().CountPendingAsync(executionId);
    }

    private sealed class ScopeSource(params string[] scopes) : IPersistenceScopeSource
    {
        public ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default) =>
            new(scopes.Select(scope => new PersistenceScope(scope)).ToArray());
    }

    /// <summary>A scoped transport that refuses every call once its operation scope has ended.</summary>
    private sealed class ScopeBoundTransport(IExecutionCommandTransport inner) : ForwardingCommandTransport(inner), IDisposable
    {
        private bool _disposed;

        public override ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return base.AckAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);
        }

        public override ValueTask<bool> ReleaseLeaseAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return base.ReleaseLeaseAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);
        }

        public void Dispose() => _disposed = true;
    }

    /// <summary>Holds each of the first acks until the test lets it run.</summary>
    private sealed class AckGatingTransport(IExecutionCommandTransport inner, int gatedAcks) : ForwardingCommandTransport(inner)
    {
        private readonly Channel<TaskCompletionSource> _gated = Channel.CreateUnbounded<TaskCompletionSource>();
        private int _remaining = gatedAcks;

        public async Task<TaskCompletionSource> NextGatedAckAsync() =>
            await _gated.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public override async ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref _remaining) >= 0)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _gated.Writer.TryWrite(gate);
                await gate.Task;
            }
            return await base.AckAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);
        }
    }

    /// <summary>Records every wait for local work the pump starts, on a fake clock the test advances.</summary>
    private sealed class WaitRecordingClock(FakeTimeProvider clock) : TimeProvider
    {
        public ConcurrentQueue<TimeSpan> Waits { get; } = new();

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

        public override long GetTimestamp() => clock.GetTimestamp();

        public override long TimestampFrequency => clock.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Enqueue(dueTime);
            return clock.CreateTimer(callback, state, dueTime, period);
        }
    }

    private static WorkflowExecutionCommandEnvelope CancelEnvelope(string executionId, string envelopeId, DateTimeOffset now) =>
        new(
            envelopeId: envelopeId,
            workflowExecutionId: executionId,
            command: new WorkflowExecutionCommand($"cmd-{envelopeId}", executionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            idempotencyKey: $"idem-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);

    private static Task WaitUntilAsync(Func<Task<bool>> condition) =>
        PumpSweeps.UntilAsync(condition, "The detached dispatches were not acknowledged.");

    private static Task<ExecutionPlacementSweepResult> SweepAdvancingAsync(NodeHarness node, FakeTimeProvider clock) =>
        PumpSweeps.AdvancingAsync(node.Pump, clock);

    /// <summary>Holds the given envelopes' drains until released, and sheds those in <see cref="Shed"/>; records every
    /// envelope it runs.</summary>
    private sealed class HoldingCommandExecutor(params string[] heldEnvelopeIds) : IWorkflowExecutionCommandExecutor
    {
        private readonly ConcurrentQueue<string> _processed = new();

        public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> Processed => _processed.ToArray();
        public bool FailHeld { get; init; }
        public string[] Shed { get; init; } = [];

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _processed.Enqueue(envelope.EnvelopeId);
            if (Shed.Contains(envelope.EnvelopeId))
                return WorkflowExecutionCommandProcessResult.FromShed("The test sheds this command.", null, workQueued: false);
            if (heldEnvelopeIds.Contains(envelope.EnvelopeId))
            {
                Holding.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                if (FailHeld)
                    throw new InvalidOperationException("The held drain failed.");
            }
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }
}
