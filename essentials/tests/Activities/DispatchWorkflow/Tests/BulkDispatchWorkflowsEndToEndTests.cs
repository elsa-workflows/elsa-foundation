using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = Elsa.Activities.DispatchWorkflow.Tests.DispatchWorkflowRuntimeTestFixture;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Runtime behaviour of <c>BulkDispatchWorkflows</c> (spec 1242 FR-004 to FR-009).</summary>
public sealed class BulkDispatchWorkflowsEndToEndTests
{
    [Fact]
    public async Task Waiting_bulk_starts_every_child_at_once_and_routes_each_end_to_its_slot()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-routes";
        object?[] items = [Fixture.BulkItem(1), Fixture.BulkItem(2, fault: true), Fixture.BulkItem(3)];

        await fixture.StartBulkParentAsync("bulk-routes", parentId, items, waitForCompletion: true, completedSlot: true, faultedSlot: true);

        // Three items are below the window, so all three dispatches open before any child runs (FR-005).
        var dispatches = await fixture.ListDispatchesAsync(parentId);
        Assert.Equal(3, dispatches.Count);
        Assert.Equal(3, dispatches.Select(dispatch => dispatch.DispatchId).Distinct(StringComparer.Ordinal).Count());
        await AssertBulkStatusAsync(fixture, parentId, ActivityExecutionStatus.Running, ActivityExecutionStatus.Suspended);

        await fixture.SweepUntilQuietAsync();

        await AssertParentCompletedAsync(fixture, parentId);
        var slots = SlotRuns(fixture, parentId);
        Assert.Equal(["completed:0:item-1", "completed:2:item-3", "faulted:1:fault"], slots);
        await AssertBulkDoneAsync(fixture, parentId);
    }

    [Fact]
    public async Task Waiting_bulk_keeps_at_most_sixteen_passes_open_and_schedules_the_rest_in_item_order()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-window";
        var items = Enumerable.Range(0, 20).Select(index => (object?)Fixture.BulkItem(index)).ToArray();

        await fixture.StartBulkParentAsync("bulk-window", parentId, items, waitForCompletion: true, completedSlot: true);

        Assert.Equal(BulkDispatchWorkflowsConstants.Window, (await fixture.ListDispatchesAsync(parentId)).Count);
        Assert.Equal(BulkDispatchWorkflowsConstants.Window, (await DispatchExecutionsAsync(fixture, parentId)).Length);

        await fixture.SweepUntilQuietAsync();

        await AssertParentCompletedAsync(fixture, parentId);
        Assert.Equal(20, (await fixture.ListDispatchesAsync(parentId)).Count);
        var dispatchRuns = await DispatchExecutionsAsync(fixture, parentId);
        Assert.Equal(20, dispatchRuns.Select(run => run.IterationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, 20).Select(index => $"completed:{index}:item-{index}").Order(StringComparer.Ordinal), SlotRuns(fixture, parentId));
        await AssertBulkDoneAsync(fixture, parentId);

        // The Dispatch passes were committed in item order, and the item after the window's k-th ended pass was committed
        // no earlier than that pass ended (a pass ends when its slot completes): never more than 16 open.
        var dispatchCommits = dispatchRuns
            .OrderBy(run => run.FirstCommitSequence)
            .Select(run => (Index: int.Parse(run.IterationId![(run.IterationId!.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture), Commit: run.FirstCommitSequence!.Value))
            .ToArray();
        Assert.Equal(Enumerable.Range(0, 20), dispatchCommits.Select(run => run.Index));
        var passEnds = (await fixture.ListActivitiesAsync(parentId))
            .Where(state => state.Execution.ExecutableNodeId == Fixture.CompletedSlotNodeId)
            .Select(state => state.LastCommitSequence!.Value)
            .Order()
            .ToArray();
        for (var index = BulkDispatchWorkflowsConstants.Window; index < 20; index++)
            Assert.True(dispatchCommits[index].Commit >= passEnds[index - BulkDispatchWorkflowsConstants.Window],
                $"Item {index} was committed at {dispatchCommits[index].Commit}, before pass end {index - BulkDispatchWorkflowsConstants.Window + 1} at {passEnds[index - BulkDispatchWorkflowsConstants.Window]}.");
    }

    [Fact]
    public async Task Fire_and_forget_bulk_completes_once_every_child_is_dispatched_and_runs_no_slot()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-detached";
        object?[] items = [Fixture.BulkItem(1), Fixture.BulkItem(2, fault: true), Fixture.BulkItem(3)];

        await fixture.StartBulkParentAsync("bulk-detached", parentId, items, waitForCompletion: false, completedSlot: true, faultedSlot: true);
        await fixture.SweepUntilQuietAsync();

        await AssertParentCompletedAsync(fixture, parentId);
        await AssertBulkDoneAsync(fixture, parentId);
        Assert.Empty(SlotRuns(fixture, parentId));
        var dispatchOutcomes = (await DispatchExecutionsAsync(fixture, parentId))
            .Select(state => Assert.Single(JsonSerializer.Deserialize<string[]>(state.Metadata[RuntimeMetadataKeys.CompletionOutcomeNames])!))
            .ToArray();
        Assert.All(dispatchOutcomes, outcome => Assert.Equal(DispatchWorkflowOutcomes.Dispatched, outcome));
        Assert.Equal(3, (await fixture.ListDispatchesAsync(parentId)).Count);
        Assert.Equal(3, fixture.ChildProbe.Observations.Count(observation => !StringComparer.Ordinal.Equals(observation.WorkflowExecutionId, parentId)));
    }

    [Fact]
    public async Task A_child_ending_with_no_matching_slot_only_counts_toward_completion()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-no-slot";
        object?[] items = [Fixture.BulkItem(1, fault: true), Fixture.BulkItem(2)];

        await fixture.StartBulkParentAsync("bulk-no-slot", parentId, items, waitForCompletion: true);
        await fixture.SweepUntilQuietAsync();

        await AssertParentCompletedAsync(fixture, parentId);
        await AssertBulkDoneAsync(fixture, parentId);
        Assert.Empty(SlotRuns(fixture, parentId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_or_null_items_complete_with_Done_at_once(bool empty)
    {
        await using var fixture = await Fixture.CreateAsync();
        var parentId = $"parent-bulk-empty-{empty}";

        await fixture.StartBulkParentAsync($"bulk-empty-{empty}", parentId, empty ? [] : null, waitForCompletion: true, completedSlot: true);

        await AssertParentCompletedAsync(fixture, parentId);
        await AssertBulkDoneAsync(fixture, parentId);
        Assert.Empty(await fixture.ListDispatchesAsync(parentId));
    }

    [Fact]
    public async Task More_items_than_the_cap_fault_before_any_dispatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-cap";
        var items = Enumerable.Range(0, BulkDispatchWorkflowsConstants.ItemCap + 1).Select(index => (object?)index).ToArray();

        await fixture.StartBulkParentAsync("bulk-cap", parentId, items, waitForCompletion: false);

        Assert.Empty(await fixture.ListDispatchesAsync(parentId));
        var bulk = Assert.Single(await fixture.ListActivitiesAsync(parentId), state => state.Execution.ExecutableNodeId == Fixture.BulkNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, bulk.Status);
        var incident = Assert.Single(await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(parentId));
        Assert.Contains("more than the cap of 1000", incident.Message, StringComparison.Ordinal);
        Assert.Equal(BulkDispatchWorkflowsConstants.CapExceededFaultCode, bulk.Fault?.Code);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.FindWorkflowAsync(parentId))?.Status);
    }

    [Fact]
    public async Task Items_that_are_not_a_collection_fault_the_bulk_activity()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-not-collection";

        // A JSON object is not a collection; ForEach refuses it the same way.
        await fixture.StartBulkParentAsync("bulk-not-collection", parentId, items: null, waitForCompletion: false, itemsJson: JsonSerializer.SerializeToElement(new { a = 1 }));

        Assert.Empty(await fixture.ListDispatchesAsync(parentId));
        var bulk = Assert.Single(await fixture.ListActivitiesAsync(parentId), state => state.Execution.ExecutableNodeId == Fixture.BulkNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, bulk.Status);
    }

    [Fact]
    public async Task A_slot_fault_under_the_default_strategy_faults_the_workflow()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-slot-fault";
        object?[] items = [Fixture.BulkItem(0, message: Fixture.FaultingSlotMessage), Fixture.BulkItem(1)];

        await fixture.StartBulkParentAsync("bulk-slot-fault", parentId, items, waitForCompletion: true, completedSlot: true);
        await fixture.SweepUntilQuietAsync();

        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.FindWorkflowAsync(parentId))?.Status);
        Assert.Contains(await fixture.ListActivitiesAsync(parentId), state =>
            state.Execution.ExecutableNodeId == Fixture.CompletedSlotNodeId && state.Status == ActivityExecutionStatus.Faulted);
    }

    [Fact]
    public async Task A_fault_of_the_Dispatch_execution_itself_follows_the_strategy_and_does_not_run_ChildFaulted()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-dispatch-fault";
        // The child declares no input 'undeclared', so the Dispatch execution refuses this item's inputs.
        var bad = Fixture.BulkItem(0);
        bad["undeclared"] = "x";

        await fixture.StartBulkParentAsync("bulk-dispatch-fault", parentId, [bad], waitForCompletion: true, faultedSlot: true);
        await fixture.SweepUntilQuietAsync();

        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.FindWorkflowAsync(parentId))?.Status);
        Assert.Contains(await fixture.ListActivitiesAsync(parentId), state =>
            state.Execution.ExecutableNodeId == Fixture.BulkDispatchNodeId && state.Status == ActivityExecutionStatus.Faulted);
        Assert.Empty(SlotRuns(fixture, parentId));
        Assert.Empty(await fixture.ListDispatchesAsync(parentId));
    }

    [Fact]
    public async Task Under_ContinueWithIncidents_a_slot_fault_keeps_its_pass_open_until_a_reschedule_ends_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-retry";
        object?[] items = [Fixture.BulkItem(0, message: Fixture.FaultingSlotMessage), Fixture.BulkItem(1), Fixture.BulkItem(2)];

        await fixture.StartBulkParentAsync("bulk-retry", parentId, items, waitForCompletion: true, completedSlot: true,
            incidentStrategy: IncidentStrategyBuiltIns.ContinueWithIncidentsReference);
        await fixture.SweepUntilQuietAsync();

        // The other passes went on; the faulted pass keeps the bulk activity open.
        Assert.Equal(["completed:1:item-1", "completed:2:item-2"], SlotRuns(fixture, parentId));
        Assert.NotEqual(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(parentId))?.Status);
        await AssertBulkStatusAsync(fixture, parentId, ActivityExecutionStatus.Running, ActivityExecutionStatus.Suspended);
        var faulted = Assert.Single(await fixture.ListActivitiesAsync(parentId), state =>
            state.Execution.ExecutableNodeId == Fixture.CompletedSlotNodeId && state.Status == ActivityExecutionStatus.Faulted);

        // ContinueWithIncidents leaves the incident open, not waiting for intervention, so RetryFaultedActivity refuses it.
        var refused = await fixture.AlterFaultedActivityAsync(parentId, faulted.Execution.ActivityExecutionId, "RetryFaultedActivity", 1);
        Assert.Equal("NoBlockingIncident", Assert.Single(refused.Outcomes).Code);

        // The bulk activity keeps waiting for the faulted child, so rescheduling it reaches the activity.
        fixture.SlotFaultRepaired = true;
        var job = await fixture.AlterFaultedActivityAsync(parentId, faulted.Execution.ActivityExecutionId, "RescheduleActivity", 1);

        Assert.True(job.Status == WorkflowAlterationJobStatus.Succeeded, JsonSerializer.Serialize(new { job.SafeFailure, job.Outcomes }));
        await AssertParentCompletedAsync(fixture, parentId);
        await AssertBulkDoneAsync(fixture, parentId);
        // The rescheduled slot reads its own pass's item and index.
        Assert.Equal(["completed:0:slot-fault", "completed:1:item-1", "completed:2:item-2"], SlotRuns(fixture, parentId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_fault_inside_a_Sequence_in_a_slot_follows_the_strategy_in_the_same_way(bool continueWithIncidents)
    {
        await using var fixture = await Fixture.CreateAsync();
        var parentId = $"parent-bulk-sequence-fault-{continueWithIncidents}";
        object?[] items = [Fixture.BulkItem(0, message: Fixture.FaultingSlotMessage), Fixture.BulkItem(1)];

        await fixture.StartBulkParentAsync($"bulk-sequence-fault-{continueWithIncidents}", parentId, items, waitForCompletion: true, completedSlot: true,
            incidentStrategy: continueWithIncidents ? IncidentStrategyBuiltIns.ContinueWithIncidentsReference : null,
            completedSlotInSequence: true);
        await fixture.SweepUntilQuietAsync();

        // The ContinueWithIncidents case shows the nested activities read their pass's item, so this fault is the probe's own.
        Assert.Single(await fixture.ListActivitiesAsync(parentId), state =>
            state.Execution.ExecutableNodeId == Fixture.CompletedSlotNodeId && state.Status == ActivityExecutionStatus.Faulted);
        Assert.DoesNotContain(SlotRuns(fixture, parentId), run => run.StartsWith("completed-tail:0:", StringComparison.Ordinal));
        if (!continueWithIncidents)
        {
            Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.FindWorkflowAsync(parentId))?.Status);
            return;
        }

        // Under ContinueWithIncidents the other pass ran its whole Sequence, in order, and the faulted pass keeps the bulk
        // activity open, as a fault directly in the slot does.
        Assert.Equal(["completed:1:item-1", "completed-tail:1:item-1"], fixture.ChildProbe.Observations
            .Where(observation => observation.WorkflowExecutionId == parentId && observation.WorkflowInputs.ContainsKey("slot"))
            .Select(observation => $"{observation.WorkflowInputs["slot"]}:{observation.WorkflowInputs["index"]}:{observation.WorkflowInputs["message"]}"));
        Assert.NotEqual(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(parentId))?.Status);
        await AssertBulkStatusAsync(fixture, parentId, ActivityExecutionStatus.Running, ActivityExecutionStatus.Suspended);
    }

    [Fact]
    public async Task Cancelling_the_parent_cancels_every_dispatch_its_passes_wait_for()
    {
        // The cancel reaches each pass's suspended Dispatch execution under the bulk node. The children have not started
        // yet: had a dispatch survived, the sweep after the cancel would start its child. Cancelling a started child is
        // the shared Dispatch cancellation path (WorkflowDispatchCancellationTests).
        await using var fixture = await Fixture.CreateAsync();
        const string parentId = "parent-bulk-cancel";
        object?[] items = [Fixture.BulkItem(0), Fixture.BulkItem(1), Fixture.BulkItem(2)];

        await fixture.StartBulkParentAsync("bulk-cancel", parentId, items, waitForCompletion: true, completedSlot: true);
        Assert.All(await fixture.ListDispatchesAsync(parentId), dispatch => Assert.Equal(WorkflowDispatchStatus.Pending, dispatch.Status));

        var job = await fixture.CancelWorkflowAsync(parentId);

        Assert.True(job.Status == WorkflowAlterationJobStatus.Succeeded, JsonSerializer.Serialize(new { job.SafeFailure, job.Outcomes }));
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.FindWorkflowAsync(parentId))?.Status);
        var dispatches = await fixture.ListDispatchesAsync(parentId);
        Assert.Equal(3, dispatches.Count);
        Assert.All(dispatches, dispatch => Assert.Equal(WorkflowDispatchStatus.Cancelled, dispatch.Status));
        foreach (var dispatch in dispatches)
            Assert.Null(await fixture.FindWorkflowAsync(dispatch.ChildWorkflowExecutionId));
    }

    private static async Task<ActivityExecutionState[]> DispatchExecutionsAsync(Fixture fixture, string parentId) =>
        (await fixture.ListActivitiesAsync(parentId))
        .Where(state => state.Execution.ExecutableNodeId == Fixture.BulkDispatchNodeId)
        .ToArray();

    // "slot:index:message" for each slot run in the parent, sorted.
    private static string[] SlotRuns(Fixture fixture, string parentId) =>
        fixture.ChildProbe.Observations
            .Where(observation => observation.WorkflowExecutionId == parentId && observation.WorkflowInputs.ContainsKey("slot"))
            .Select(observation => $"{observation.WorkflowInputs["slot"]}:{observation.WorkflowInputs["index"]}:{observation.WorkflowInputs["message"]}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static async Task AssertBulkStatusAsync(Fixture fixture, string parentId, params ActivityExecutionStatus[] allowed)
    {
        var bulk = Assert.Single(await fixture.ListActivitiesAsync(parentId), state => state.Execution.ExecutableNodeId == Fixture.BulkNodeId);
        Assert.Contains(bulk.Status, allowed);
    }

    private static async Task AssertBulkDoneAsync(Fixture fixture, string parentId)
    {
        var bulk = Assert.Single(await fixture.ListActivitiesAsync(parentId), state => state.Execution.ExecutableNodeId == Fixture.BulkNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, bulk.Status);
        Assert.Equal([ActivityOutcomes.Done], JsonSerializer.Deserialize<string[]>(bulk.Metadata[RuntimeMetadataKeys.CompletionOutcomeNames]));
    }

    private static async Task AssertParentCompletedAsync(Fixture fixture, string parentId)
    {
        var parent = await fixture.FindWorkflowAsync(parentId);
        var incidents = await fixture.Services.GetRequiredService<IIncidentStateStore>().ListAsync(parentId);
        Assert.True(
            parent?.Status == WorkflowExecutionStatus.Completed,
            $"Parent ended as {parent?.Status}/{parent?.SubStatus}. " +
            string.Join(" | ", incidents.Select(incident => $"{incident.ActivityExecutionId} {incident.FailureType}: {incident.Message} {string.Join(";", incident.Metadata.Select(m => m.Key + "=" + m.Value))}")));
    }
}
