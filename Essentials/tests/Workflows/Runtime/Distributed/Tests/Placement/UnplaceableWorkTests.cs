using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, User Story 2, FR-017, FR-018 and SC-002: work that no active member can run waits in the durable transport,
/// is never failed, dropped or recorded as an incident, and is reported in Attention for as long as it waits, naming
/// what it needs and never a host. This is the case that looks like success: the sender was told its command was
/// accepted for routing.
/// </summary>
public sealed class UnplaceableWorkTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();
    private readonly WorkflowExecutable _needsVersionTwo = RuntimeWork.Needing("artifact-approvals-v2", ApprovalsConsumer, "2");

    public UnplaceableWorkTests() => _cluster.State.Executables.SaveAsync(_needsVersionTwo).AsTask().GetAwaiter().GetResult();

    [Fact]
    public async Task Work_no_active_member_can_run_waits_is_reported_without_naming_a_host_and_runs_once_a_capable_member_is_active()
    {
        var stopped = await _cluster.StartAsync("host-a", Activates("1", "2"));
        var old = await _cluster.StartAsync("host-b", Activates("1"));
        stopped.Member.Become(MemberStatus.Left);
        await old.Runtime.DispatchAsync(RuntimeWork.Start(_needsVersionTwo, ExecutionId, _cluster.Now, "start"));

        for (var sweep = 0; sweep < 3; sweep++)
            await old.Runtime.Pump.SweepOnceAsync();

        var report = Assert.Single(await old.Runtime.UnplaceableWorkAsync());
        Assert.Equal(1, report.WaitingExecutions);
        Assert.Equal("no active member activates runtime consumer acme.approvals at schema version 2", report.Requirement);
        Assert.DoesNotContain("host-", report.Requirement, StringComparison.Ordinal);
        Assert.Equal(1, await _cluster.State.Transport.CountPendingAsync(ExecutionId));
        Assert.Empty(stopped.Runtime.Commands.Committed);
        Assert.Empty(old.Runtime.Commands.Committed);

        var item = Assert.Single((await AttentionAsync(old)).Items);
        Assert.Equal(AttentionSeverity.Warning, item.Severity);
        Assert.Equal(1, item.Count);
        Assert.Contains("1 workflow execution(s) waiting", item.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("host-", item.Summary, StringComparison.Ordinal);
        Assert.Empty(item.Correlations);

        var capable = await _cluster.StartAsync("host-c", Activates("1", "2"));
        await capable.Runtime.Pump.SweepOnceAsync();
        await old.Runtime.Pump.SweepOnceAsync();

        Assert.Equal("start", Assert.Single(capable.Runtime.Commands.Committed).EnvelopeId);
        Assert.Empty(await old.Runtime.UnplaceableWorkAsync());
        Assert.Empty((await AttentionAsync(old)).Items);
    }

    /// <summary>Work that some active member can run is not unplaceable: that member claims it, so nothing is reported.</summary>
    [Fact]
    public async Task Work_another_active_member_can_run_is_not_reported_as_unplaceable()
    {
        await _cluster.StartAsync("host-a", Activates("1", "2"));
        var old = await _cluster.StartAsync("host-b", Activates("1"));
        await old.Runtime.DispatchAsync(RuntimeWork.Start(_needsVersionTwo, ExecutionId, _cluster.Now, "start"));

        await old.Runtime.Pump.SweepOnceAsync();

        Assert.Empty(await old.Runtime.UnplaceableWorkAsync());
    }

    /// <summary>
    /// Unplaceable work does not starve the work behind it: the pump rotates through the backlog, so runnable work that
    /// sorts after a full page of work it may not run is still claimed.
    /// </summary>
    [Fact]
    public async Task Unplaceable_work_does_not_starve_runnable_work_behind_it()
    {
        var old = await _cluster.StartAsync("host-b", setup =>
        {
            Activates("1")(setup);
            setup.Configure = services => services.Configure<Options.ExecutionPlacementPumpOptions>(options => options.MaxExecutionsPerSweep = 2);
        });
        var needsVersionOne = RuntimeWork.Needing("artifact-approvals-v1", ApprovalsConsumer, "1");
        await _cluster.State.Executables.SaveAsync(needsVersionOne);
        foreach (var executionId in new[] { "a-waits-1", "a-waits-2" })
            await old.Runtime.DispatchAsync(RuntimeWork.Start(_needsVersionTwo, executionId, _cluster.Now, $"start-{executionId}"));
        await _cluster.State.Transport.SendAsync("z-runs", RuntimeWork.Start(needsVersionOne, "z-runs", _cluster.Now, "start-z-runs"), _cluster.Now);

        await old.Runtime.Pump.SweepOnceAsync();
        await old.Runtime.Pump.SweepOnceAsync();

        Assert.Equal("start-z-runs", Assert.Single(old.Runtime.Commands.Committed).EnvelopeId);
        Assert.Equal(2, Assert.Single(await old.Runtime.UnplaceableWorkAsync()).WaitingExecutions);
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private static async Task<AttentionContribution> AttentionAsync(PlacementMember member)
    {
        await using var scope = member.Runtime.Services.CreateAsyncScope();
        var contributor = new WorkflowRuntimeAttentionContributor(
            new NoRuntimeConditions(),
            scope.ServiceProvider.GetServices<IWorkflowRuntimePlacementAttention>());
        return await contributor.EvaluateAsync(new AttentionContributorContext(
            new AttentionQueryContext(new ClaimsPrincipal(), "default"),
            new AttentionExecutionBudget(10),
            new Dictionary<string, string>()));
    }

    private sealed class NoRuntimeConditions : IWorkflowRuntimeAttentionQuery
    {
        public ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(WorkflowRuntimeAttentionQuery request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowRuntimeAttentionSnapshot(0, []));
    }
}
