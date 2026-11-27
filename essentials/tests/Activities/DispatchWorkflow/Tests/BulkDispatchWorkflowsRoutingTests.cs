using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BulkDispatchWorkflowsActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Outcome routing (spec 1242 FR-006) and the pass bookkeeping that guards the window (FR-005).</summary>
public sealed class BulkDispatchWorkflowsRoutingTests
{
    [Theory]
    [InlineData(DispatchWorkflowOutcomes.Completed, "completed")]
    [InlineData(DispatchWorkflowOutcomes.Faulted, "faulted")]
    [InlineData(DispatchWorkflowOutcomes.Cancelled, "faulted")]
    [InlineData(DispatchWorkflowOutcomes.DispatchFailed, "faulted")]
    [InlineData(DispatchWorkflowOutcomes.Dispatched, null)]
    public void Each_Dispatch_outcome_selects_its_slot(string outcome, string? expectedSlot)
    {
        var navigator = BulkDispatchWorkflowsNavigator.From(Bulk(completed: true, faulted: true));

        Assert.Equal(expectedSlot, BulkDispatchWorkflowsActivity.SelectSlot(navigator, [outcome])?.ExecutableNodeId);
    }

    [Theory]
    [InlineData(DispatchWorkflowOutcomes.Completed)]
    [InlineData(DispatchWorkflowOutcomes.Cancelled)]
    public void An_outcome_without_its_slot_ends_the_pass(string outcome)
    {
        var navigator = BulkDispatchWorkflowsNavigator.From(Bulk(completed: false, faulted: false));

        Assert.Null(BulkDispatchWorkflowsActivity.SelectSlot(navigator, [outcome]));
    }

    [Fact]
    public void Ending_and_routing_a_pass_are_idempotent_and_completion_needs_every_pass()
    {
        var state = new BulkDispatchWorkflowsState(Count: 3, Next: 3, Ended: [], Routed: []);

        state = state.Route(1).Route(1);
        Assert.True(state.IsRouted(1));
        Assert.Equal([1], state.Routed);

        state = state.End(2).End(2).End(0);
        Assert.Equal([0, 2], state.Ended);
        Assert.Equal(1, state.Open);
        Assert.False(state.AllEnded);

        state = state.End(1);
        Assert.True(state.AllEnded);
        Assert.Equal(0, state.Open);
    }

    [Fact]
    public void Iteration_ids_round_trip_only_for_their_own_bulk_execution()
    {
        var id = BulkDispatchIteration.Id("exec-1", 7);

        Assert.True(BulkDispatchIteration.TryReadIndex("exec-1", id, out var index));
        Assert.Equal(7, index);
        Assert.False(BulkDispatchIteration.TryReadIndex("exec-2", id, out _));
        Assert.False(BulkDispatchIteration.TryReadIndex("exec-1", "exec-1:foreach-iteration:7", out _));
        Assert.False(BulkDispatchIteration.TryReadIndex("exec-1", null, out _));
    }

    private static ExecutableNode Bulk(bool completed, bool faulted)
    {
        var slots = new List<ExecutableChildSlot> { new(BulkDispatchWorkflowsConstants.DispatchSlotName, [Leaf("dispatch")]) };
        if (completed) slots.Add(new(BulkDispatchWorkflowsConstants.ChildCompletedSlotName, [Leaf("completed")]));
        if (faulted) slots.Add(new(BulkDispatchWorkflowsConstants.ChildFaultedSlotName, [Leaf("faulted")]));
        return new ExecutableNode(
            "bulk", "authored-bulk", BulkDispatchWorkflowsConstants.ActivityType, "1.0.0", "test-descriptor",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: slots,
            structure: new ExecutableActivityStructure(
                BulkDispatchWorkflowsConstants.StructureKind,
                BulkDispatchWorkflowsConstants.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BulkDispatchWorkflowsExecutableStructure(
                    "dispatch", completed ? "completed" : null, faulted ? "faulted" : null), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    private static ExecutableNode Leaf(string id) =>
        new(id, $"authored-{id}", "Test.Leaf", "1.0.0", "test-descriptor", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
}
