using Elsa.Activities.Runtime.Core.Models;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, User Story 1 and SC-001: during a rolling upgrade, an execution that needs what only upgraded members can
/// run is never placed on a member without it, and every such execution is placed on one with it. The claimant checks
/// itself (FR-011), a refused claim has no side effect but forwarding (FR-013), and a requirement that cannot be resolved
/// is never treated as met (FR-016).
/// </summary>
public sealed class RollingUpgradePlacementTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();
    private readonly WorkflowExecutable _needsVersionTwo = RuntimeWork.Needing("artifact-approvals-v2", ApprovalsConsumer, "2");

    public RollingUpgradePlacementTests() => _cluster.State.Executables.SaveAsync(_needsVersionTwo).AsTask().GetAwaiter().GetResult();

    [Fact]
    public async Task Work_that_needs_the_new_version_is_never_placed_on_a_member_without_it_and_is_placed_on_one_with_it()
    {
        var upgraded = await _cluster.StartAsync("host-a", Activates("1", "2"));
        var old = await _cluster.StartAsync("host-b", Activates("1"));
        var alsoOld = await _cluster.StartAsync("host-c", Activates("1"));

        var result = await old.Runtime.DispatchAsync(RuntimeWork.Start(_needsVersionTwo, ExecutionId, _cluster.Now, "start"));

        // Accepted for routing, with the reason it did not run here, and no other side effect (FR-013).
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.Equal(nameof(PlacementRefusalKind.RequirementUnmet), result.Metadata[ForwardingWorkflowExecutionActor.PlacementRefusedMetadataKey]);
        Assert.Contains("activates runtime consumer acme.approvals at schema version 2", result.Reason, StringComparison.Ordinal);
        Assert.False(result.Metadata.ContainsKey(ForwardingWorkflowExecutionActor.OwningNodeMetadataKey));
        await AssertUnplacedAndWaitingAsync();

        for (var sweep = 0; sweep < 3; sweep++)
        {
            await old.Runtime.Pump.SweepOnceAsync();
            await alsoOld.Runtime.Pump.SweepOnceAsync();
            _cluster.Clock.Advance(TimeSpan.FromSeconds(10));
        }

        await AssertUnplacedAndWaitingAsync();

        var claimed = await upgraded.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(1, claimed.ClaimedCount);
        Assert.Equal(1, claimed.AckedCount);
        Assert.Equal("start", Assert.Single(upgraded.Runtime.Commands.Committed).EnvelopeId);
        Assert.Equal(upgraded.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
        Assert.Empty(old.Runtime.Commands.Committed);
        Assert.Empty(alsoOld.Runtime.Commands.Committed);
        Assert.Equal(0, await _cluster.State.Transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task An_execution_whose_requirement_the_member_meets_is_claimed_and_drained_locally_exactly_as_before()
    {
        var old = await _cluster.StartAsync("host-b", Activates("1"));
        var needsVersionOne = RuntimeWork.Needing("artifact-approvals-v1", ApprovalsConsumer, "1");
        await _cluster.State.Executables.SaveAsync(needsVersionOne);

        var result = await old.Runtime.DispatchAsync(RuntimeWork.Start(needsVersionOne, ExecutionId, _cluster.Now, "start"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Equal("start", Assert.Single(old.Runtime.Commands.Committed).EnvelopeId);
        Assert.Equal(old.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
        Assert.Equal(0, await _cluster.State.Transport.CountPendingAsync(ExecutionId));
    }

    /// <summary>
    /// Each requirement kind of FR-006 in both directions: the member without it forwards, the member with it claims.
    /// </summary>
    [Theory]
    [InlineData("consumer schema version", "activates runtime consumer acme.approvals at schema version 2")]
    [InlineData("storage driver", "has durable-value storage driver acme.blob")]
    [InlineData("activity type", "resolves activity type Acme.Approve")]
    public async Task Every_requirement_kind_places_work_only_on_a_member_that_satisfies_it(string kind, string unmet)
    {
        var (executable, capable, incapable) = kind switch
        {
            "consumer schema version" => (_needsVersionTwo, Activates("1", "2"), Activates("1")),
            "storage driver" => (
                RuntimeWork.Needing("artifact-blob", ApprovalsConsumer, "1", "acme.blob"),
                (Action<DistributedRuntimeNodeSetup>)(setup => { Activates("1")(setup); setup.StorageDrivers.Add("acme.blob"); }),
                Activates("1")),
            _ => (
                RuntimeWork.NeedingActivityType("artifact-approve", ApproveActivity.Alias),
                (Action<DistributedRuntimeNodeSetup>)(setup => { ClrActivities(setup); setup.TypeRegistry = ApproveActivity.Registry(); }),
                (Action<DistributedRuntimeNodeSetup>)ClrActivities)
        };
        await _cluster.State.Executables.SaveAsync(executable);
        var withIt = await _cluster.StartAsync("host-with", capable);
        var withoutIt = await _cluster.StartAsync("host-without", incapable);

        var refused = await withoutIt.Runtime.DispatchAsync(RuntimeWork.Start(executable, ExecutionId, _cluster.Now, "start"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, refused.Status);
        Assert.Contains(unmet, refused.Reason, StringComparison.Ordinal);
        await withoutIt.Runtime.Pump.SweepOnceAsync();
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));

        await withIt.Runtime.Pump.SweepOnceAsync();
        Assert.Equal("start", Assert.Single(withIt.Runtime.Commands.Committed).EnvelopeId);
        Assert.Empty(withoutIt.Runtime.Commands.Committed);
    }

    /// <summary>User Story 1, scenario 4, and FR-015: a member that loses the requirement stops renewing and hands the
    /// execution off, and no incapable member claims it; while it keeps the requirement, it renews.</summary>
    [Fact]
    public async Task A_member_that_loses_the_requirement_hands_the_execution_off_and_no_incapable_member_claims_it()
    {
        var approvals = new MutableConsumer(ApprovalsConsumer, "1", "2");
        var upgraded = await _cluster.StartAsync("host-a", setup => setup.Consumers.Add(approvals));
        var old = await _cluster.StartAsync("host-b", Activates("1"));
        await upgraded.Runtime.DispatchAsync(RuntimeWork.Start(_needsVersionTwo, ExecutionId, _cluster.Now, "start"));

        var keeps = await upgraded.Runtime.Pump.SweepOnceAsync();
        Assert.Equal(1, keeps.RenewedCount);
        Assert.Equal(0, keeps.HandedOffCount);

        approvals.SupportedSchemaVersions = ["1"];
        var loses = await upgraded.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(0, loses.RenewedCount);
        Assert.Equal(1, loses.HandedOffCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));

        var forwarded = await old.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "work"));
        await old.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, forwarded.Status);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Empty(old.Runtime.Commands.Committed);
        Assert.Equal(1, await _cluster.State.Transport.CountPendingAsync(ExecutionId));
    }

    /// <summary>FR-016: a requirement that cannot be resolved, because the pinned executable cannot be loaded, is never
    /// treated as satisfied, on either claim path.</summary>
    [Fact]
    public async Task A_requirement_that_cannot_be_resolved_is_never_claimed_on_either_claim_path()
    {
        var anyone = await _cluster.StartAsync("host-a", Activates("1", "2"));
        var missing = RuntimeWork.Needing("artifact-never-imported", ApprovalsConsumer, "1");

        var result = await anyone.Runtime.DispatchAsync(RuntimeWork.Start(missing, ExecutionId, _cluster.Now, "start"));
        var sweep = await anyone.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.Equal(nameof(PlacementRefusalKind.RequirementUnresolved), result.Metadata[ForwardingWorkflowExecutionActor.PlacementRefusedMetadataKey]);
        Assert.Contains("artifact-never-imported", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, sweep.ClaimedCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Empty(anyone.Runtime.Commands.Committed);
        var report = Assert.Single(await anyone.Runtime.UnplaceableWorkAsync());
        Assert.Contains("cannot be resolved", report.Requirement, StringComparison.Ordinal);
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private static void ClrActivities(DistributedRuntimeNodeSetup setup) =>
        setup.Consumers.Add(new RuntimeActivityConsumerCapability(WellKnownRuntimeActivityConsumers.ClrActivity, [RuntimeActivityDescriptor.InitialSchemaVersion]));

    private async Task AssertUnplacedAndWaitingAsync()
    {
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Equal(1, await _cluster.State.Transport.CountPendingAsync(ExecutionId));
        Assert.Empty(await _cluster.State.Transport.ListLeasedAsync("host-b", _cluster.Now, 10));
        Assert.Empty(await _cluster.State.Transport.ListLeasedAsync("host-c", _cluster.Now, 10));
    }
}
