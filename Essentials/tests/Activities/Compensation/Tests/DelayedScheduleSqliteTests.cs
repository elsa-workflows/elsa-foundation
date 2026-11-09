using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 261 R-7 on the SQLite runtime store, with a test structural activity that knows nothing of compensation: the
/// delayed child is not scheduled before its due time (D2), and a host stopped after the commit and restarted after the
/// due time schedules it once at the first sweep (D4), in every persistence mode. This host is the one the compensation
/// journeys use, which is why the generic seam is proven here.
/// </summary>
public sealed class DelayedScheduleSqliteTests
{
    private const string RunId = CompensationHost.WorkflowExecutionId;
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(60);

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_D2_the_child_waits_for_its_due_time_on_SQLite(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();
        await using var harness = await host.StartHostAsync(participant);
        await CompensationHost.StartAsync(harness, DelayingParent());
        await harness.SweepUntilQuietAsync();
        Assert.Empty(participant.Calls);

        host.Advance(Delay - TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();
        Assert.Empty(participant.Calls);

        host.Advance(TimeSpan.FromSeconds(1));
        await harness.SweepUntilQuietAsync();

        (await harness.ReadRunAsync(RunId)).AssertWorkflowCompleted();
        Assert.Single(participant.Calls);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DelayedSchedule_D4_a_host_stopped_after_the_commit_schedules_the_child_once_after_restart(PersistenceMode mode)
    {
        using var host = new CompensationHost(mode);
        var participant = new Participant();
        await using (var first = await host.StartHostAsync(participant))
        {
            await CompensationHost.StartAsync(first, DelayingParent());
            await first.SweepUntilQuietAsync();
            Assert.Empty(participant.Calls);
            Assert.Equal(WorkflowExecutionStatus.Running, (await first.ReadRunAsync(RunId)).WorkflowState!.Status);
        }

        host.Advance(Delay + TimeSpan.FromMinutes(5));
        await using var second = await host.StartHostAsync(participant);
        await second.SweepUntilQuietAsync();
        await second.SweepUntilQuietAsync();

        var run = await second.ReadRunAsync(RunId);
        run.AssertWorkflowCompleted();
        Assert.Single(participant.Calls);
        Assert.Single(run.States("node-child"));
    }

    private static ExecutableNode DelayingParent()
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(DelayingParentActivity));
        return new ExecutableNode(
            executableNodeId: "node-parent",
            authoredActivityId: "authored-node-parent",
            activityType: typeof(DelayingParentActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: ClrActivityContractTestBuilder.CompleteInputBindings(contract, new Dictionary<string, RuntimeInputBinding>()),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Test.Child", [CompensationGraph.Participant("node-child")])],
            activityContract: contract);
    }

    /// <summary>Schedules its one child 60 seconds from now and completes when the child completes.</summary>
    public sealed class DelayingParentActivity(TimeProvider clock) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId, null, null, null, clock.GetUtcNow() + Delay);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
