using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184's two requirements on spec 181's gate: a member whose writes to a family of the Runtime EF module spec 181
/// refuses claims nothing and hands off what it holds (FR-012), and a shell's runnability entry names the database its
/// Runtime EF module read (FR-008), so a placement query about another database does not count it (FR-009).
/// </summary>
public sealed class SchemaFinalizationPlacementTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();

    [Fact]
    public async Task A_member_whose_runtime_writes_are_refused_claims_nothing_and_forwards_the_command()
    {
        var refused = await _cluster.StartAsync("host-a", Gate(new Finalization { WritesRefusedReason = "schema family 'RuntimeArtifact' is finalized at '2'" }));

        var result = await refused.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "new"));
        var sweep = await refused.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.Equal(nameof(PlacementRefusalKind.RuntimeWritesRefused), result.Metadata[ForwardingWorkflowExecutionActor.PlacementRefusedMetadataKey]);
        Assert.Equal(0, sweep.ClaimedCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Empty(refused.Runtime.Commands.Committed);
    }

    [Fact]
    public async Task A_member_holding_an_execution_hands_it_off_once_its_runtime_writes_are_refused_and_another_claims_it()
    {
        var finalization = new Finalization();
        var partitioned = await _cluster.StartAsync("host-a", Gate(finalization));
        var active = await _cluster.StartAsync("host-b");
        await partitioned.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "held"));
        Assert.Equal(partitioned.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);

        finalization.WritesRefusedReason = "schema family 'RuntimeWorkflowExecution' is finalized at '2', which this member cannot read";
        var handOff = await partitioned.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(1, handOff.HandedOffCount);
        Assert.Equal(0, handOff.RenewedCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        await active.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "after"));
        Assert.Equal(active.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
    }

    [Fact]
    public async Task A_shells_runnability_entry_names_the_database_its_runtime_read_and_a_query_about_another_database_skips_it()
    {
        var member = await _cluster.StartAsync("host-a", setup =>
        {
            Activates("1")(setup);
            Gate(new Finalization { DatabaseIdentity = "database-a" })(setup);
        });

        var view = await member.Member.ReadFleetAsync(FleetReadMode.Fresh);
        var entry = Assert.Single(view.Find(member.Member.Identity)!.Report.Runnability!.Entries);

        Assert.Equal("database-a", entry.DatabaseIdentity);
        Assert.Contains(member.Member.Identity, MemberQuery.Placement(new ActivatesRuntimeConsumer(ApprovalsConsumer, "1", "database-a")).Evaluate(view).Matches.Select(match => match.Identity));
        Assert.DoesNotContain(member.Member.Identity, MemberQuery.Placement(new ActivatesRuntimeConsumer(ApprovalsConsumer, "1", "database-b")).Evaluate(view).Matches.Select(match => match.Identity));
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private static Action<DistributedRuntimeNodeSetup> Gate(Finalization finalization) =>
        setup => setup.Configure += services => services.AddSingleton<IRuntimeSchemaFinalization>(finalization);

    /// <summary>What a Runtime EF module's finalization gate says, set by the test as a refresh would change it.</summary>
    private sealed class Finalization : IRuntimeSchemaFinalization
    {
        public string? DatabaseIdentity { get; set; }

        public string? WritesRefusedReason { get; set; }
    }
}
