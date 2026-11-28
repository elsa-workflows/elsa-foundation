using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing;
using Elsa.Cluster.Tests.Reference;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 183's legacy distributed-runtime cases over the shared-store reference membership provider:
/// each member runs <see cref="DistributedRuntimeNode"/> over its own membership, and the members share one
/// durable runtime state. Leases outlive the membership liveness window here, so what frees a member's work is the
/// provider's departure verdict and placement's reclaim, before the execution lease can expire.
/// </summary>
public sealed class LegacyDistributedMembershipTests : IAsyncDisposable
{
    private const string RuntimeExecutionId = "conformance-execution";

    private readonly SharedStoreConformanceFixture _fixture = new();

    /// <summary>
    /// The commit fence, with placement's reclaim interleaving: a member cut off from the membership
    /// store while it drains is judged departed by the others, which reclaim its leases and re-drive the execution; the
    /// cut-off member then reaches its commit after the re-drive's, and the fence refuses it. Exactly one commit lands.
    /// </summary>
    [Fact]
    public async Task FR057_a_member_reclaimed_from_while_alive_reaches_its_commit_after_the_re_drive_and_exactly_one_commit_succeeds()
    {
        var survivor = await _fixture.StartMemberAsync(Setup("survivor"));
        var partitioned = await _fixture.StartMemberAsync(Setup("partitioned"));
        var shared = new SharedRuntimeState();
        await using var survivorNode = DistributedRuntimeNode.Create(shared, survivor.Membership, survivor.Clock);
        await using var partitionedNode = DistributedRuntimeNode.Create(shared, partitioned.Membership, partitioned.Clock);
        await survivorNode.Pump.SweepOnceAsync();
        await partitionedNode.Pump.SweepOnceAsync();

        // The partitioned member claims the execution and is mid-drain, holding the first fencing token.
        var hold = partitionedNode.Commands.HoldNextDrain();
        var lateDrain = partitionedNode.DispatchAsync(RuntimeWork.Work(RuntimeExecutionId, partitioned.Clock.GetUtcNow(), "partitioned-drain")).AsTask();
        var staleLease = await hold.Started.Task;

        // More work for the execution arrives on the survivor, which forwards it to the owner through the durable queue.
        var forwarded = await survivorNode.DispatchAsync(RuntimeWork.Work(RuntimeExecutionId, survivor.Clock.GetUtcNow(), "re-drive"));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, forwarded.Status);

        // The partitioned member loses the membership store but not the runtime database, so the survivor's view and
        // its own disagree: it still believes it owns the execution.
        await partitioned.IsolateAsync();
        await AdvancePastLivenessWindowAsync();

        var sweep = await survivorNode.Pump.SweepOnceAsync();

        Assert.True(sweep.ReclaimedCount > 0, "The survivor did not reclaim the departed member's leases.");
        var reDrive = Assert.Single(survivorNode.Commands.Committed);
        Assert.Equal("re-drive", reDrive.EnvelopeId);
        Assert.True(reDrive.FencingToken > staleLease.FencingToken);

        hold.Release();
        await lateDrain;

        Assert.Empty(partitionedNode.Commands.Committed);
        var refused = Assert.Single(partitionedNode.Commands.Fenced);
        Assert.Equal(staleLease.FencingToken, refused.PresentedFencingToken);
        Assert.Single(survivorNode.Commands.Committed.Concat(partitionedNode.Commands.Committed));

        // The cut-off member's own sweep takes nothing back: its lease was released, and a renewal never re-grants.
        await partitionedNode.Pump.SweepOnceAsync();
        Assert.Equal(Identity(survivor).HostId, (await shared.Placement.FindAsync(RuntimeExecutionId))?.OwnerId);
    }

    /// <summary>
    /// Durable delivery: work routed to a member that then dies is still delivered from the durable queue, by a
    /// survivor, once the member's departure lets it reclaim the dead member's leases.
    /// </summary>
    [Fact]
    public async Task FR059_work_routed_to_a_member_that_then_dies_is_still_delivered_from_the_durable_queue()
    {
        var survivor = await _fixture.StartMemberAsync(Setup("survivor"));
        var doomed = await _fixture.StartMemberAsync(Setup("doomed"));
        var shared = new SharedRuntimeState();
        await using var survivorNode = DistributedRuntimeNode.Create(shared, survivor.Membership, survivor.Clock);
        await using var doomedNode = DistributedRuntimeNode.Create(shared, doomed.Membership, doomed.Clock);
        await survivorNode.Pump.SweepOnceAsync();
        await doomedNode.Pump.SweepOnceAsync();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
            (await doomedNode.DispatchAsync(RuntimeWork.Work(RuntimeExecutionId, doomed.Clock.GetUtcNow(), "first"))).Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred,
            (await survivorNode.DispatchAsync(RuntimeWork.Work(RuntimeExecutionId, survivor.Clock.GetUtcNow(), "routed"))).Status);

        // The owner leases the routed command, then dies before delivering it.
        var routed = Assert.Single(await shared.Transport.LeaseAsync(RuntimeExecutionId, Identity(doomed).HostId, doomed.Clock.GetUtcNow(), TimeSpan.FromMinutes(10), 10));
        await doomed.KillAsync();
        await AdvancePastLivenessWindowAsync();

        await survivorNode.Pump.SweepOnceAsync();

        Assert.Equal("routed", Assert.Single(survivorNode.Commands.Committed).EnvelopeId);
        Assert.Equal(routed.Envelope.EnvelopeId, survivorNode.Commands.Committed.Single().EnvelopeId);
        Assert.Equal(0, await shared.Transport.CountPendingAsync(RuntimeExecutionId));
        Assert.Contains(await survivorNode.ReclaimedCandidatesAsync(), candidate =>
            candidate.WorkflowExecutionId == RuntimeExecutionId &&
            candidate.Metadata[ReclaimedExecutionRegistry.RecoverySourceMetadataKey] == ReclaimedExecutionRegistry.RecoverySource);
    }

    private ValueTask AdvancePastLivenessWindowAsync()
    {
        var timings = _fixture.Timings;
        return _fixture.AdvanceAsync(timings.ExpiryPeriod + timings.SkewAllowance + timings.HeartbeatInterval);
    }

    private static ConformanceMemberSetup Setup(string hostId) => new(hostId, Readability: []);

    private static ClusterMemberIdentity Identity(IConformanceMember member) => member.Membership.GetLocalStanding().Identity;

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();
}
