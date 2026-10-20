using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// FR-009: a completion the scope has already journaled changes nothing. The runtime may deliver a child's completion
/// to its parent again after a crash between the child's commit and the parent's evaluation, so these tests drive the
/// scope's callbacks directly and replay a delivery with the state the first delivery committed.
/// </summary>
public sealed class CompensationScopeRepeatDeliveryTests
{
    private const string ScopeExecutionId = "actexec-scope";

    private static readonly ExecutableNode Journey = ReservationJourney();

    [Fact]
    public async Task A_repeated_step_completion_neither_journals_a_second_entry_nor_schedules_the_next_step_again()
    {
        var started = await ExecuteAsync();
        var firstStep = Assert.Single(started.Requests);

        var first = await CompletedAsync(started.State, firstStep, "actexec-reserve-inventory");
        Assert.Equal("reserve-delivery", Assert.Single(first.Requests).ExecutableNodeId);

        var repeated = await CompletedAsync(first.State, firstStep, "actexec-reserve-inventory");

        Assert.True(repeated.Continuation.IsDeferred);
        Assert.Null(repeated.Continuation.StateUpdate);
        Assert.Empty(repeated.Requests);
    }

    [Fact]
    public async Task A_repeated_handler_completion_does_not_schedule_the_next_handler_again()
    {
        var started = await ExecuteAsync();
        var inventory = await CompletedAsync(started.State, Assert.Single(started.Requests), "actexec-reserve-inventory");
        var delivery = await CompletedAsync(inventory.State, Assert.Single(inventory.Requests), "actexec-reserve-delivery");
        var confirm = Assert.Single(delivery.Requests);

        var faulted = await FaultedAsync(delivery.State, confirm, "actexec-confirm-order", "incident-confirm");
        var cancelDelivery = Assert.Single(faulted.Requests);
        Assert.Equal("cancel-delivery", cancelDelivery.ExecutableNodeId);

        var handled = await CompletedAsync(faulted.State, cancelDelivery, "actexec-cancel-delivery");
        Assert.Equal("release-inventory", Assert.Single(handled.Requests).ExecutableNodeId);

        var repeated = await CompletedAsync(handled.State, cancelDelivery, "actexec-cancel-delivery");

        Assert.True(repeated.Continuation.IsDeferred);
        Assert.Null(repeated.Continuation.StateUpdate);
        Assert.Empty(repeated.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Child_callback_metadata_is_an_immutable_snapshot_independent_of_the_delivery_command(bool faulted)
    {
        var started = await ExecuteAsync();
        var child = Assert.Single(started.Requests);
        var metadata = child.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        var context = NewContext(started.State, new Dictionary<string, string>());
        IReadOnlyDictionary<string, string> snapshot = faulted
            ? new ActivityChildFaultedContext(context, "child", child.ExecutableNodeId, "incident", null, metadata).ChildSchedulingMetadata
            : new ActivityChildCompletedContext(context, "child", child.ExecutableNodeId, [ActivityOutcomes.Done], null, metadata).ChildSchedulingMetadata;

        metadata[CompensationScope.MetadataKeys.Scope] = "different-scope";

        Assert.Equal(ScopeExecutionId, snapshot[CompensationScope.MetadataKeys.Scope]);
        Assert.Empty(context.SchedulerWorkItem.CommandMetadata);
    }

    private static async Task<Evaluation> ExecuteAsync()
    {
        var context = NewContext(state: null, commandMetadata: new Dictionary<string, string>());
        var continuation = await ((IRuntimeStructuralActivity)new CompensationScope()).ExecuteStructureAsync(context);
        return new(continuation, context.GetChildActivityScheduleRequests().ToArray());
    }

    private static async Task<Evaluation> CompletedAsync(RuntimeStructuralStateUpdate? state, RuntimeChildActivityScheduleRequest child, string childExecutionId)
    {
        var context = NewContext(state, new Dictionary<string, string>());
        var continuation = await new CompensationScope().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, childExecutionId, child.ExecutableNodeId, [ActivityOutcomes.Done], null, child.Metadata));
        return new(continuation, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> FaultedAsync(RuntimeStructuralStateUpdate? state, RuntimeChildActivityScheduleRequest child, string childExecutionId, string incidentId)
    {
        var context = NewContext(state, new Dictionary<string, string>());
        var continuation = await new CompensationScope().OnChildFaultedAsync(
            new ActivityChildFaultedContext(context, childExecutionId, child.ExecutableNodeId, incidentId, null, child.Metadata));
        return new(continuation, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static SimpleActivityExecutionContext NewContext(RuntimeStructuralStateUpdate? state, IReadOnlyDictionary<string, string> commandMetadata) =>
        new(
            new CompensationScope(),
            CancellationToken.None,
            "wfexec-1",
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new RuntimeSchedulerWorkItem(
                workItemId: "work-scope",
                workflowExecutionId: "wfexec-1",
                commandId: "command-scope",
                commandKind: WorkflowExecutionCommandKind.InvokeActivity,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-1:scope",
                enqueuedAt: DateTimeOffset.UnixEpoch,
                recordedAt: DateTimeOffset.UnixEpoch,
                sequence: 1,
                payload: null,
                commandMetadata: commandMetadata,
                envelopeMetadata: new Dictionary<string, string>()),
            Journey,
            new ActivityExecutionState(
                Execution: new ActivityExecution(ScopeExecutionId, "wfexec-1", ScopeNodeId, $"authored-{ScopeNodeId}", typeof(CompensationScope).FullName!, "1.0.0"),
                Status: ActivityExecutionStatus.Running,
                SubStatus: null,
                ScheduledAt: DateTimeOffset.UnixEpoch,
                StartedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: null,
                SchedulingActivityExecutionId: null,
                ParentActivityExecutionId: null,
                BranchId: null,
                IterationId: null,
                CallStackDepth: 0,
                BookmarkIds: [],
                IncidentIds: [],
                FaultCount: 0,
                AggregateFaultCount: 0,
                Metadata: new Dictionary<string, string>())
            {
                PrivateState = state is null ? null : new ActivityPrivateState(ScopeExecutionId, state.StateVersion, state.Value, "attempt-1", DateTimeOffset.UnixEpoch)
            });

    // The state the evaluation committed: its staged update, or the state it was given when it staged none.
    private sealed record Evaluation(RuntimeStructuralContinuation Continuation, RuntimeChildActivityScheduleRequest[] Requests, RuntimeStructuralStateUpdate? Previous = null)
    {
        public RuntimeStructuralStateUpdate? State => Continuation.StateUpdate ?? Previous;
    }
}
