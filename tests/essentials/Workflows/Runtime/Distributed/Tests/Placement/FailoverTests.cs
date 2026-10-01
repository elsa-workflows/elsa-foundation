using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184's failover (FR-022 to FR-026; User Stories 3, 4, 5 and 7): a departed host id's per-execution leases are
/// reclaimed within one sweep, and its executions made recovery candidates at once; a live, absent, restarted or
/// cache-only-departed host id is never reclaimed from (SC-006); and reclaim never decides a commit (SC-005).
/// </summary>
public sealed class FailoverTests : IAsyncDisposable
{
    private const string RoutedExecutionId = "execution-routed";
    private readonly PlacementCluster _cluster = new();

    [Fact]
    public async Task A_live_host_id_is_never_reclaimed_from()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var owner = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(owner, survivor);

        await survivor.Runtime.Pump.SweepOnceAsync();

        await AssertStillHeldAsync(owner);
    }

    [Fact]
    public async Task A_departed_host_id_is_reclaimed_from_within_one_sweep()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var crashed = await _cluster.StartAsync("host-b");
        var lease = await HoldWorkAsync(crashed, survivor);

        crashed.Member.Expire();
        var sweep = await survivor.Runtime.Pump.SweepOnceAsync();

        // Every lease held under the host id is reclaimed in that sweep: two placement leases, one transport item lease
        // and two executions' execution leases. The routed work then runs on the survivor.
        Assert.Equal(5, sweep.ReclaimedCount);
        Assert.Equal(survivor.HostId, (await _cluster.State.Placement.FindAsync(RoutedExecutionId))?.OwnerId);
        Assert.Equal("routed", Assert.Single(survivor.Runtime.Commands.Committed).EnvelopeId);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        var candidates = await survivor.Runtime.ReclaimedCandidatesAsync();
        Assert.Equal(new[] { ExecutionId, RoutedExecutionId }, candidates.Select(candidate => candidate.WorkflowExecutionId).Order(StringComparer.Ordinal));
        var candidate = candidates.Single(candidate => candidate.WorkflowExecutionId == ExecutionId);
        Assert.Equal(ReclaimedExecutionRegistry.RecoverySource, candidate.Metadata[ReclaimedExecutionRegistry.RecoverySourceMetadataKey]);
        Assert.Equal("departure", candidate.Metadata[ReclaimedExecutionRegistry.ReclaimKindMetadataKey]);
        Assert.Equal(crashed.HostId, candidate.Metadata[ReclaimedExecutionRegistry.ReclaimedHostIdMetadataKey]);
        Assert.True((await _cluster.State.Liveness.FindAsync(ExecutionId, RuntimeExecutionOwnershipStateId.For(ExecutionId)))?.ExecutionLease is { } held && held.FencingToken == lease.FencingToken,
            "Reclaim wrote an execution lease or a fencing token.");
    }

    [Fact]
    public async Task A_host_id_that_left_gracefully_is_reclaimed_from_like_one_that_expired()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var stopped = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(stopped, survivor);

        stopped.Member.Become(MemberStatus.Left);
        await survivor.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(survivor.HostId, (await _cluster.State.Placement.FindAsync(RoutedExecutionId))?.OwnerId);
    }

    [Fact]
    public async Task A_host_id_departed_only_in_a_cached_read_is_never_reclaimed_from()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var owner = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(owner, survivor);

        // The cached view shows it expired, but a fresh read shows it live again: positive evidence only.
        owner.Member.Expire();
        _cluster.Fleet.Snapshot();
        owner.Member.Revive();
        await survivor.Runtime.Pump.SweepOnceAsync();

        await AssertStillHeldAsync(owner);
    }

    [Fact]
    public async Task A_host_id_that_restarted_after_the_cached_read_is_never_reclaimed_from()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var owner = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(owner, survivor);

        owner.Member.Expire();
        _cluster.Fleet.Snapshot();
        _cluster.Fleet.Join(owner.HostId);
        await survivor.Runtime.Pump.SweepOnceAsync();

        await AssertStillHeldAsync(owner);
    }

    [Fact]
    public async Task A_host_id_absent_from_the_view_is_never_reclaimed_from()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var outsider = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(outsider, survivor);

        _cluster.Fleet.Forget(outsider.HostId);
        await survivor.Runtime.Pump.SweepOnceAsync();

        await AssertStillHeldAsync(outsider);
    }

    [Fact]
    public async Task Nothing_is_reclaimed_while_no_fresh_read_succeeds()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var owner = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(owner, survivor);

        owner.Member.Expire();
        _cluster.Fleet.FreshReadsFail = true;
        await survivor.Runtime.Pump.SweepOnceAsync();

        await AssertStillHeldAsync(owner);
    }

    /// <summary>FR-026: a lapsed member judges no departure and reclaims nothing, but keeps claiming work it can run.</summary>
    [Fact]
    public async Task A_lapsed_member_reclaims_nothing_but_keeps_claiming()
    {
        var lapsed = await _cluster.StartAsync("host-a");
        var owner = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(owner, lapsed);

        owner.Member.Expire();
        lapsed.Member.Lapse(_cluster.Now);
        await lapsed.Runtime.Pump.SweepOnceAsync();

        await AssertStillHeldAsync(owner);
        var claimed = await lapsed.Runtime.DispatchAsync(RuntimeWork.Work("execution-other", _cluster.Now, "other"));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, claimed.Status);
    }

    /// <summary>User Story 4, scenario 3: survivors that reclaim concurrently release each lease once and touch nothing
    /// held under any other host id.</summary>
    [Fact]
    public async Task Survivors_that_reclaim_concurrently_release_each_lease_once_and_touch_nothing_else()
    {
        var first = await _cluster.StartAsync("host-a");
        var second = await _cluster.StartAsync("host-c");
        var crashed = await _cluster.StartAsync("host-b");
        await HoldWorkAsync(crashed, first);
        await first.Runtime.DispatchAsync(RuntimeWork.Work("execution-first", _cluster.Now, "first-own"));
        var untouched = await _cluster.State.Placement.FindAsync("execution-first");

        crashed.Member.Expire();
        await Task.WhenAll(first.Runtime.Pump.SweepOnceAsync().AsTask(), second.Runtime.Pump.SweepOnceAsync().AsTask());

        Assert.Single(first.Runtime.Commands.Committed.Concat(second.Runtime.Commands.Committed), commit => commit.EnvelopeId == "routed");
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        var own = await _cluster.State.Placement.FindAsync("execution-first");
        Assert.Equal(first.HostId, own?.OwnerId);
        Assert.True(own!.PlacementToken >= untouched!.PlacementToken);
    }

    /// <summary>
    /// FR-024 and FR-027: the recovery sweep re-drives a reclaimed execution at once, while its execution lease is still
    /// live, through the distributed provider; the re-drive acquires a strictly greater fencing token, the recovery
    /// source says it was reclaimed, and the candidate is settled.
    /// </summary>
    [Fact]
    public async Task The_recovery_sweep_re_drives_a_reclaimed_execution_at_once_under_a_greater_token()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var crashed = await _cluster.StartAsync("host-b");
        var staleLease = await HoldWorkAsync(crashed, survivor);
        crashed.Member.Expire();
        await survivor.Runtime.Pump.SweepOnceAsync();
        await using var scope = survivor.Runtime.Services.CreateAsyncScope();
        var candidates = scope.ServiceProvider.GetServices<IRuntimeRecoveryCandidateSource>().ToArray();
        var resumption = new RuntimeResumptionService(
            new NothingToDeliver(),
            new InMemoryWorkflowSchedulerWorkQueue(),
            new InMemoryRuntimeRecoveryScanner(_cluster.State.Liveness),
            survivor.Runtime.Actors,
            new ShortRuntimeExecutionIdGenerator(_cluster.Clock),
            _cluster.Clock,
            _cluster.State.Executions,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), _cluster.Clock),
            new RuntimeResumptionDiscoveryStateStore(),
            recoveryCandidateSources: candidates);

        var sweep = await resumption.SweepAsync(new RuntimeResumptionSweepRequest());

        var reDrive = sweep.Dispatches.Single(dispatch => dispatch.WorkflowExecutionId == ExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, reDrive.Outcome);
        Assert.True(survivor.Runtime.Commands.Committed.Single(commit => commit.WorkflowExecutionId == ExecutionId).FencingToken > staleLease.FencingToken);
        Assert.DoesNotContain(await survivor.Runtime.ReclaimedCandidatesAsync(), candidate => candidate.WorkflowExecutionId == ExecutionId);
    }

    /// <summary>
    /// User Story 5 and SC-005: a member reclaimed from while alive reaches its commit after the re-drive's, and the fence
    /// refuses it; its next renewal takes nothing back.
    /// </summary>
    [Fact]
    public async Task Reclaim_never_decides_a_commit()
    {
        var survivor = await _cluster.StartAsync("host-a");
        var partitioned = await _cluster.StartAsync("host-b");
        var hold = partitioned.Runtime.Commands.HoldNextDrain();
        var lateDrain = partitioned.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "late")).AsTask();
        var staleLease = await hold.Started.Task;
        await survivor.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "re-drive"));

        partitioned.Member.Expire();
        partitioned.Member.Lapse(_cluster.Now);
        await survivor.Runtime.Pump.SweepOnceAsync();
        hold.Release();
        await lateDrain;

        var committed = Assert.Single(survivor.Runtime.Commands.Committed);
        Assert.True(committed.FencingToken > staleLease.FencingToken);
        Assert.Empty(partitioned.Runtime.Commands.Committed);
        Assert.Equal(staleLease.FencingToken, Assert.Single(partitioned.Runtime.Commands.Fenced).PresentedFencingToken);

        await partitioned.Runtime.Pump.SweepOnceAsync();
        Assert.Equal(survivor.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
    }

    /// <summary>
    /// User Story 3 and SC-003: a process that restarts under the same host id releases every lease its predecessor held
    /// before it claims anything, and makes the predecessor's executions recovery candidates at once; the re-drive holds
    /// a strictly greater fencing token than the crashed drain, which is refused at its commit.
    /// </summary>
    [Fact]
    public async Task A_restarted_process_reclaims_its_predecessors_leases_before_it_claims_anything()
    {
        var crashed = await _cluster.StartAsync("host-a");
        var hold = crashed.Runtime.Commands.HoldNextDrain();
        var crashedDrain = crashed.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "interrupted")).AsTask();
        var staleLease = await hold.Started.Task;
        await _cluster.State.Transport.SendAsync(RoutedExecutionId, RuntimeWork.Work(RoutedExecutionId, _cluster.Now, "routed"), _cluster.Now);
        await _cluster.State.Transport.LeaseAsync(RoutedExecutionId, crashed.HostId, _cluster.Now, TimeSpan.FromMinutes(10), 10);
        await _cluster.State.Placement.TryClaimAsync(new(RoutedExecutionId, crashed.HostId, _cluster.Now, _cluster.Now.AddMinutes(10)), _cluster.Now);

        _cluster.Clock.Advance(TimeSpan.FromSeconds(35));
        var restarted = await _cluster.StartAsync("host-a", sweep: false);

        // Before its join sweep, it claims nothing: a command is forwarded, and the held leases stay.
        var early = await restarted.Runtime.DispatchAsync(RuntimeWork.Work("execution-early", _cluster.Now, "early"));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, early.Status);
        Assert.Equal(nameof(PlacementRefusalKind.JoinSweepPending), early.Metadata[ForwardingWorkflowExecutionActor.PlacementRefusedMetadataKey]);

        var sweep = await restarted.Runtime.Pump.SweepOnceAsync();

        Assert.True(sweep.ReclaimedCount >= 3);
        var candidate = Assert.Single(await restarted.Runtime.ReclaimedCandidatesAsync());
        Assert.Equal(ExecutionId, candidate.WorkflowExecutionId);
        Assert.Equal("join-sweep", candidate.Metadata[ReclaimedExecutionRegistry.ReclaimKindMetadataKey]);
        Assert.Contains(restarted.Runtime.Commands.Committed, commit => commit.EnvelopeId == "routed");
        Assert.Contains(restarted.Runtime.Commands.Committed, commit => commit.EnvelopeId == "early");

        // The recovery sweep's re-drive of the interrupted execution acquires a strictly greater token.
        var reDrive = await restarted.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "re-drive"));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, reDrive.Status);
        Assert.True(restarted.Runtime.Commands.Committed.Single(commit => commit.EnvelopeId == "re-drive").FencingToken > staleLease.FencingToken);
        hold.Release();
        await crashedDrain;
        Assert.Empty(crashed.Runtime.Commands.Committed);
        Assert.Single(crashed.Runtime.Commands.Fenced);
    }

    /// <summary>
    /// FR-012 and FR-022: a joining member claims nothing, and neither renews nor releases what its host id holds, which
    /// may still be its predecessor's; once it is active its join sweep releases those leases and it claims.
    /// </summary>
    [Fact]
    public async Task A_joining_member_claims_nothing_and_leaves_what_its_host_id_holds_until_it_is_active()
    {
        var predecessor = await _cluster.StartAsync("host-a");
        await predecessor.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "held"));
        _cluster.Clock.Advance(TimeSpan.FromSeconds(35));
        var joining = await _cluster.StartAsync("host-a", sweep: false);
        joining.Member.Become(MemberStatus.Joining);

        var refused = await joining.Runtime.DispatchAsync(RuntimeWork.Work("execution-new", _cluster.Now, "new"));
        var sweep = await joining.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(nameof(PlacementRefusalKind.MemberNotActive), refused.Metadata[ForwardingWorkflowExecutionActor.PlacementRefusedMetadataKey]);
        Assert.Equal(0, sweep.ReclaimedCount + sweep.RenewedCount + sweep.HandedOffCount + sweep.ClaimedCount);
        Assert.Equal(joining.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);

        joining.Member.Become(MemberStatus.Active);
        var active = await joining.Runtime.Pump.SweepOnceAsync();

        Assert.True(active.ReclaimedCount > 0);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Equal("new", Assert.Single(joining.Runtime.Commands.Committed).EnvelopeId);
    }

    /// <summary>FR-022: a shell reload in the same process, or a second sweep, is not a first activation, and never
    /// releases the leases the process itself holds.</summary>
    [Fact]
    public async Task A_shell_reload_in_the_same_process_does_not_sweep_its_own_leases()
    {
        var ledger = new JoinSweepLedger();
        var first = await _cluster.StartAsync("host-a", setup => setup.Ledger = ledger);
        await first.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "own"));

        _cluster.Clock.Advance(TimeSpan.FromSeconds(1));
        var reloaded = DistributedRuntimeNode.Create(_cluster.State, first.Member, _cluster.Clock, setup => setup.Ledger = ledger);
        await using (reloaded)
        {
            var sweep = await reloaded.Pump.SweepOnceAsync();
            Assert.Equal(0, sweep.ReclaimedCount);
        }

        Assert.Equal(first.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
    }

    /// <summary>
    /// Edge case "A host with several shells": a second shell whose runtime first activates later in the same process runs
    /// its own join sweep, and releases only what was held under the host id before the process began its first one,
    /// never what another shell of the process has claimed since.
    /// </summary>
    [Fact]
    public async Task A_shell_that_first_activates_later_in_the_process_releases_nothing_the_process_itself_holds()
    {
        var ledger = new JoinSweepLedger();
        var first = await _cluster.StartAsync("host-a", setup => setup.Ledger = ledger);
        _cluster.Clock.Advance(TimeSpan.FromSeconds(1));
        await first.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "own"));

        _cluster.Clock.Advance(TimeSpan.FromSeconds(1));
        await using var second = DistributedRuntimeNode.Create(_cluster.State, first.Member, _cluster.Clock, setup =>
        {
            setup.Ledger = ledger;
            setup.Configure = services => services.Replace(ServiceDescriptor.Singleton(new DistributedRuntimeShell("second-shell", "second-instance")));
        });
        await second.Pump.SweepOnceAsync();

        Assert.Equal(first.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
        Assert.Empty(await second.ReclaimedCandidatesAsync());
    }

    /// <summary>
    /// User Story 7 and SC-008: a single host on the in-process provider reclaims no other host id, however many sweeps
    /// pass, and releases its previous process's leases when it restarts.
    /// </summary>
    [Fact]
    public async Task A_single_host_on_the_in_process_provider_reclaims_no_other_host_id_and_its_restart_releases_its_own()
    {
        var hostId = $"single-{Guid.NewGuid():N}";
        await using var host = DistributedRuntimeNode.Create(_cluster.State, InProcess(hostId), _cluster.Clock);
        await _cluster.State.Placement.TryClaimAsync(new(RoutedExecutionId, "another-host", _cluster.Now, _cluster.Now.AddMinutes(10)), _cluster.Now);
        await host.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "own"));

        for (var sweep = 0; sweep < 3; sweep++)
        {
            Assert.Equal(0, (await host.Pump.SweepOnceAsync()).ReclaimedCount);
            _cluster.Clock.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.Equal("another-host", (await _cluster.State.Placement.FindAsync(RoutedExecutionId))?.OwnerId);
        Assert.Equal(hostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);

        await using var restarted = DistributedRuntimeNode.Create(_cluster.State, InProcess(hostId), _cluster.Clock);
        await restarted.Pump.SweepOnceAsync();

        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Equal("another-host", (await _cluster.State.Placement.FindAsync(RoutedExecutionId))?.OwnerId);
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private sealed class NothingToDeliver : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
    }

    private InProcessClusterMembership InProcess(string hostId) =>
        new(Microsoft.Extensions.Options.Options.Create(new ClusterMembershipOptions { HostId = hostId }), [], _cluster.Clock);

    /// <summary>
    /// Leaves <paramref name="owner"/> holding all three kinds of per-execution lease: the placement and execution lease
    /// of one execution it drained, and the transport item lease of a command routed to it for another it owns.
    /// </summary>
    private async Task<RuntimeExecutionLease> HoldWorkAsync(PlacementMember owner, PlacementMember other)
    {
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
            (await owner.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "drained"))).Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
            (await owner.Runtime.DispatchAsync(RuntimeWork.Work(RoutedExecutionId, _cluster.Now, "owned"))).Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred,
            (await other.Runtime.DispatchAsync(RuntimeWork.Work(RoutedExecutionId, _cluster.Now, "routed"))).Status);
        Assert.Single(await _cluster.State.Transport.LeaseAsync(RoutedExecutionId, owner.HostId, _cluster.Now, TimeSpan.FromMinutes(10), 10));
        var state = await _cluster.State.Liveness.FindAsync(ExecutionId, RuntimeExecutionOwnershipStateId.For(ExecutionId));
        Assert.Equal(owner.HostId, state?.ExecutionLease?.OwnerId);
        return state!.ExecutionLease!;
    }

    private async Task AssertStillHeldAsync(PlacementMember owner)
    {
        Assert.Equal(owner.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
        Assert.Equal(owner.HostId, (await _cluster.State.Placement.FindAsync(RoutedExecutionId))?.OwnerId);
        Assert.Single(await _cluster.State.Transport.ListLeasedAsync(owner.HostId, _cluster.Now, 10));
        foreach (var node in _cluster.Nodes.Where(node => node.HostId != owner.HostId))
            Assert.Empty(await node.ReclaimedCandidatesAsync());
    }
}
