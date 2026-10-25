using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = Elsa.Activities.DispatchWorkflow.Tests.WorkflowControlBoundaryTests.Fixture;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Terminal checkpoint projection controls; transport and upgraded-host recovery remain separate gates.</summary>
public sealed class WorkflowControlProducerTests
{
    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task Ordinary_terminal_without_authored_control_gets_a_fresh_empty_barrier(WorkflowExecutionStatus status)
    {
        await using var fixture = await Fixture.CreateAsync("valid");
        var root = await SetRootAsync(fixture, status);
        var commit = await TerminalAsync(fixture, status);
        Assert.Empty(commit.StateChanges.ActivityExecutions);

        var enriched = await Producer(fixture).EnrichAsync(commit);

        var projected = Assert.Single(enriched.StateChanges.ActivityExecutions).State;
        var barrier = Assert.IsType<RuntimeChildControlBatch>(projected.ProducedControl);
        Assert.Equal(0, barrier.Ordinal);
        Assert.True(barrier.IsTerminal);
        Assert.Empty(barrier.Notifications);
        Assert.Null(barrier.AuthoredFailureCode);
        Assert.Equal("child-root", barrier.RootActivityExecutionId);
        Assert.Equal(fixture.Identity.ChildWorkflowExecutionId, barrier.ChildWorkflowExecutionId);
        Assert.Equal("terminal-commit:" + commit.CommitId, barrier.SourceWorkItemId);
        Assert.Equal(JsonSerializer.Serialize(root.Fault), JsonSerializer.Serialize(projected.Fault));
        Assert.Contains("child-root", enriched.Checkpoint.ActivityExecutionIds);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    public async Task Terminal_after_a_nonterminal_emission_uses_the_next_ordinal(WorkflowExecutionStatus status)
    {
        await using var fixture = await Fixture.CreateAsync("valid");
        var prior = Batch(fixture, 0, terminal: false);
        await SetRootAsync(fixture, status, prior);

        var enriched = await Producer(fixture).EnrichAsync(await TerminalAsync(fixture, status));

        var barrier = Assert.Single(enriched.StateChanges.ActivityExecutions).State.ProducedControl!;
        Assert.Equal(1, barrier.Ordinal);
        Assert.True(barrier.IsTerminal);
        Assert.Empty(barrier.Notifications);
        Assert.Null(barrier.AuthoredFailureCode);
    }

    [Fact]
    public async Task A_previously_staged_terminal_batch_is_retained_as_one_whole_payload()
    {
        await using var fixture = await Fixture.CreateAsync("valid");
        var produced = Batch(fixture, 1, terminal: true, code: "authored-error");
        await SetRootAsync(fixture, WorkflowExecutionStatus.Faulted, produced);

        var enriched = await Producer(fixture).EnrichAsync(await TerminalAsync(fixture, WorkflowExecutionStatus.Faulted));

        var retained = Assert.Single(enriched.StateChanges.ActivityExecutions).State.ProducedControl;
        Assert.Same(produced, retained);
        Assert.Equal(["first", "second"], retained!.Notifications.Select(item => item.Code));
        Assert.Equal("authored-error", retained.AuthoredFailureCode);
    }

    [Fact]
    public async Task Current_checkpoint_root_projection_overlays_the_retained_store()
    {
        await using var fixture = await Fixture.CreateAsync("valid");
        var prior = Batch(fixture, 0, terminal: false);
        var root = await SetRootAsync(fixture, WorkflowExecutionStatus.Faulted, prior);
        var changed = root with { Metadata = new Dictionary<string, string>(root.Metadata) { ["test.current-change"] = "retained" } };
        var commit = await TerminalAsync(fixture, WorkflowExecutionStatus.Faulted);
        commit = commit with { StateChanges = commit.StateChanges.WithActivityExecutions([Change(changed)]) };

        var enriched = await Producer(fixture).EnrichAsync(commit);

        var projected = Assert.Single(enriched.StateChanges.ActivityExecutions).State;
        Assert.Equal("retained", projected.Metadata["test.current-change"]);
        Assert.Equal(1, projected.ProducedControl!.Ordinal);
    }

    [Fact]
    public async Task Re_enrichment_of_a_committed_root_less_terminal_checkpoint_is_identical()
    {
        await using var fixture = await Fixture.CreateAsync("valid");
        await SetRootAsync(fixture, WorkflowExecutionStatus.Faulted, Batch(fixture, 0, terminal: false));
        var original = await TerminalAsync(fixture, WorkflowExecutionStatus.Faulted);
        var enriched = await Producer(fixture).EnrichAsync(original);
        var root = Assert.Single(enriched.StateChanges.ActivityExecutions).State;
        await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(root);

        var repeated = await Producer(fixture).EnrichAsync(original);

        Assert.Equal(JsonSerializer.Serialize(enriched), JsonSerializer.Serialize(repeated));
    }

    [Fact]
    public async Task Legacy_dispatch_without_capability_keeps_ordinary_terminal_projection()
    {
        await using var fixture = await Fixture.CreateAsync("legacy");
        await SetRootAsync(fixture, WorkflowExecutionStatus.Completed);
        var original = await TerminalAsync(fixture, WorkflowExecutionStatus.Completed);
        Assert.Same(original, await Producer(fixture).EnrichAsync(original));
    }

    [Fact]
    public async Task Actual_host_commits_the_empty_terminal_barrier_with_its_workflow_terminal_checkpoint()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync("terminal-barrier", "terminal-barrier-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var checkpoint = Assert.Single(fixture.Checkpoints.Commits, commit =>
            commit.WorkflowExecutionId == run.Dispatch.ChildWorkflowExecutionId &&
            commit.StateChanges.WorkflowExecution?.State.Status == WorkflowExecutionStatus.Completed);
        var staged = Assert.Single(checkpoint.StateChanges.ActivityExecutions).State;
        var persisted = Assert.Single(await fixture.ListActivitiesAsync(run.Dispatch.ChildWorkflowExecutionId));
        Assert.True(staged.ProducedControl!.IsTerminal);
        Assert.Equal(0, staged.ProducedControl.Ordinal);
        Assert.Empty(staged.ProducedControl.Notifications);
        Assert.Equal(JsonSerializer.Serialize(staged.ProducedControl), JsonSerializer.Serialize(persisted.ProducedControl));
    }

    [Fact]
    public async Task Actual_root_authored_fault_is_preserved_when_workflow_cancellation_wins_before_fault_observation()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredFaultChild: true,
            configureServices: services =>
            {
                // Hold only workflow fault observation; root execution, incident writing and cancellation use the real runtime.
                foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerDrainObserver) &&
                             descriptor.ImplementationType is { } type && (type == typeof(BlockingIncidentWorkflowFaultObserver) ||
                                 type == typeof(IncidentStrategyResolutionDrainObserver))).ToArray())
                    services.Remove(descriptor);
            });
        var run = await fixture.StartParentAsync("fault-cancel-control", "fault-cancel-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var childId = run.Dispatch.ChildWorkflowExecutionId;
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(childId))!.Status);
        var faultedRoot = Assert.Single(await fixture.ListActivitiesAsync(childId));
        Assert.Equal(ActivityExecutionStatus.Faulted, faultedRoot.Status);
        Assert.Equal("authored-error", faultedRoot.ProducedControl!.AuthoredFailureCode);
        Assert.Contains(fixture.Checkpoints.Commits, commit => commit.WorkflowExecutionId == childId &&
            commit.StateChanges.ActivityExecutions.Any(change => change.State.ProducedControl?.AuthoredFailureCode == "authored-error"));

        var now = DispatchWorkflowRuntimeTestFixture.Now.AddSeconds(1);
        var command = new WorkflowExecutionCommand("cancel-faulted-root", childId, WorkflowExecutionCommandKind.Cancel, now,
            null, new Dictionary<string, string>());
        await fixture.Services.GetRequiredService<IWorkflowExecutionCommandExecutor>().ProcessAsync(
            new WorkflowExecutionCommandEnvelope("cancel-envelope", childId, command, "cancel-control-race",
                WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, partition: run.Dispatch.Partition));

        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.FindWorkflowAsync(childId))!.Status);
        var retained = Assert.Single(await fixture.ListActivitiesAsync(childId));
        Assert.Equal(ActivityExecutionStatus.Faulted, retained.Status);
        Assert.Equal(JsonSerializer.Serialize(faultedRoot.Fault), JsonSerializer.Serialize(retained.Fault));
        Assert.Equal(JsonSerializer.Serialize(faultedRoot.ProducedControl), JsonSerializer.Serialize(retained.ProducedControl));
        var terminal = Assert.Single(fixture.Checkpoints.Commits, commit => commit.WorkflowExecutionId == childId &&
            commit.StateChanges.WorkflowExecution?.State.Status == WorkflowExecutionStatus.Cancelled);
        Assert.Equal(JsonSerializer.Serialize(retained.ProducedControl),
            JsonSerializer.Serialize(Assert.Single(terminal.StateChanges.ActivityExecutions).State.ProducedControl));
        var parent = Assert.Single(await fixture.ListActivitiesAsync(run.Dispatch.ParentWorkflowExecutionId),
            state => state.ParentActivityExecutionId is null);
        var sourceIntent = "terminal-result";
        var carried = new RuntimeChildControlTransport(run.Dispatch.DispatchId, run.Dispatch.ParentWorkflowExecutionId,
            run.Dispatch.ParentActivityExecutionId, parent.InvocationId, (await fixture.FindWorkflowAsync(run.Dispatch.ParentWorkflowExecutionId))!.PinnedExecutable,
            terminal.CommitId, sourceIntent, RuntimePostCommitOutboxIdentity.CreateLogicalValue(terminal.CommitId, sourceIntent), retained.ProducedControl!);
        var result = new DispatchWorkflowResult(childId, WorkflowDispatchStatus.Cancelled, [],
            new Dictionary<string, string> { ["code"] = DispatchWorkflowDiagnostics.CancelledCode,
                ["category"] = DispatchWorkflowDiagnostics.Category, ["summary"] = DispatchWorkflowDiagnostics.CancelledSummary }, carried);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, result.Status);
        Assert.Equal("authored-error", result.Control!.Batch.AuthoredFailureCode);
        Assert.Equal(["before-error", "alongside-error"], result.Control.Batch.Notifications.Select(notification => notification.Code));
    }

    private static IRuntimeCheckpointCommitEnricher Producer(Fixture fixture) =>
        fixture.Services.GetServices<IRuntimeCheckpointCommitEnricher>().Single(enricher => enricher.Order == 75);

    private static RuntimeChildControlBatch Batch(Fixture fixture, long ordinal, bool terminal, string? code = null) =>
        new(1, "test.control", 1, fixture.Identity.ChildWorkflowExecutionId, "child-root", "source-control", ordinal,
            terminal, [new("first"), new("second")], code);

    private static async Task<ActivityExecutionState> SetRootAsync(Fixture fixture, WorkflowExecutionStatus status, RuntimeChildControlBatch? control = null)
    {
        var store = fixture.Services.GetRequiredService<IActivityExecutionStateStore>();
        var root = await store.FindAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root");
        var changed = root! with
        {
            Status = status switch
            {
                WorkflowExecutionStatus.Completed => ActivityExecutionStatus.Completed,
                WorkflowExecutionStatus.Cancelled => ActivityExecutionStatus.Cancelled,
                _ => ActivityExecutionStatus.Faulted
            },
            ProducedControl = control,
            Fault = status == WorkflowExecutionStatus.Faulted ? new ActivityFault("original-runtime-code", "Original runtime failure.").ToNormalized() : null
        };
        return await store.SaveAsync(changed);
    }

    private static RuntimeStateChange<ActivityExecutionState> Change(ActivityExecutionState state) =>
        new(state.InvocationId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>());

    private static async Task<RuntimeCheckpointCommit> TerminalAsync(Fixture fixture, WorkflowExecutionStatus status)
    {
        var workflow = (await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(fixture.Identity.ChildWorkflowExecutionId))! with { Status = status };
        var record = (await fixture.Services.GetRequiredService<IWorkflowDispatchStore>().FindAsync(fixture.Identity.DispatchId))!;
        var target = status switch
        {
            WorkflowExecutionStatus.Completed => WorkflowDispatchStatus.Completed,
            WorkflowExecutionStatus.Cancelled => WorkflowDispatchStatus.Cancelled,
            _ => WorkflowDispatchStatus.Faulted
        };
        var at = DateTimeOffset.UnixEpoch.AddDays(2);
        var terminal = record.TransitionTo(target, at);
        return new RuntimeCheckpointCommit("terminal-commit", new RuntimeCheckpoint("terminal-checkpoint", "WorkflowTerminal",
            workflow.WorkflowExecutionId, at, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(new RuntimeStateChange<WorkflowExecutionState>(workflow.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], [],
                [new RuntimeStateChange<WorkflowDispatchRecord>(terminal.DispatchId, RuntimeStateChangeOperation.Upsert, terminal,
                    new Dictionary<string, string>())]), [], new Dictionary<string, string>());
    }
}
