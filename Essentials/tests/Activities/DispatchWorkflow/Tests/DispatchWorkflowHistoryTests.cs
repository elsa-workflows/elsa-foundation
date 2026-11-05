using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Microsoft.Extensions.Options;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowHistoryTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_child_history_distinguishes_requested_observed_admission_and_native_settlement(bool waited)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true);
        var run = await fixture.StartParentAsync("history-" + waited, "history-parent-" + waited, "correlation", waitForCompletion: waited);
        await using var scope = fixture.Services.CreateAsyncScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var before = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        var requested = Assert.Single(before.Items, e => e.Name == "ChildWorkflowDispatchRequested");
        Assert.Equal(run.Activity.Execution.ActivityExecutionId, requested.ActivityExecutionId);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, requested.Metadata["history.relatedWorkflowExecutionId"]);
        Assert.Null((await history.ReadPageAsync(new(run.Identity.ChildWorkflowExecutionId))).Head);
        fixture.AdvanceTime(TimeSpan.FromSeconds(2)); await fixture.SweepAsync();
        var parent = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        var observed = Assert.Single(parent.Items, e => e.Name == "ChildStartDispositionObserved");
        Assert.Equal(requested.HistoryEventId, observed.Causation!.HistoryEventId);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, observed.Metadata["history.relatedWorkflowExecutionId"]);
        Assert.True(observed.OccurredAt >= DispatchWorkflowRuntimeTestFixture.Now.AddSeconds(2));
        var child = await history.ReadPageAsync(new(run.Identity.ChildWorkflowExecutionId, 500));
        var started = Assert.Single(child.Items, e => e.Name == "WorkflowStarted");
        Assert.Equal(requested.HistoryEventId, started.Causation!.HistoryEventId); Assert.Equal(run.Start.WorkflowExecutionId, started.Causation.WorkflowExecutionId);
        var settled = Assert.Single(child.Items, e => e.Name == "ChildWorkflowSettled");
        Assert.Equal(run.Start.WorkflowExecutionId, settled.Metadata["history.relatedWorkflowExecutionId"]);
        Assert.NotEqual(run.Activity.Execution.ActivityExecutionId, settled.ActivityExecutionId);
        Assert.All(child.Items, e => Assert.Equal(run.Identity.ChildWorkflowExecutionId, e.WorkflowExecutionId));
        if (waited)
        {
            await fixture.SweepAsync();
            parent = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
            var delivered = Assert.Single(parent.Items, e => e.Name == "DeliveryReceived" && e.Causation?.HistoryEventId == settled.HistoryEventId);
            Assert.Equal(run.Identity.ChildWorkflowExecutionId, delivered.Causation!.WorkflowExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))!.Status);
        }
        Assert.Single(fixture.ChildProbe.Observations);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Captured_parent_does_not_create_history_for_independently_uncaptured_child()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true);
        var run = await fixture.StartParentAsync("history-independent", "history-independent-parent", "correlation", waitForCompletion: true);
        fixture.Services.GetRequiredService<RuntimeHistoryOptions>().Enabled = false;
        await fixture.SweepAsync(); await fixture.SweepAsync();
        await using var scope = fixture.Services.CreateAsyncScope(); var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var parent = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        Assert.Contains(parent.Items, e => e.Name == "ChildWorkflowDispatchRequested" && e.Metadata["history.relatedWorkflowExecutionId"] == run.Identity.ChildWorkflowExecutionId);
        Assert.Contains(parent.Items, e => e.Name == "ChildStartDispositionObserved");
        var child = await history.ReadPageAsync(new(run.Identity.ChildWorkflowExecutionId)); Assert.Null(child.Head); Assert.Empty(child.Items);
        Assert.Null((await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))!.HistoryCapturePolicy);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))!.Status);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Observed_result_ack_loss_keeps_child_source_owed_then_reconciles_saved_child_without_reexecution()
    {
        var fault = new ObservationFailureProbe();
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true, configureServices: services =>
        {
            var descriptor = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter)); services.Remove(descriptor);
            services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new LosingObservationWriter(
                (IRuntimeHistoryObservationWriter)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!), fault));
        });
        var run = await fixture.StartParentAsync("history-ack-loss", "history-ack-loss-parent", "correlation", waitForCompletion: true);
        var start = Assert.Single(CheckpointCommitIntents.IntentsOf(run.CompletionCommit), intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        var sourceId = RuntimePostCommitOutboxItems.OutboxItemId(run.CompletionCommit.CommitId, start);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () => { await fixture.SweepAsync(); });
        Assert.Single(fixture.ChildProbe.Observations);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var source = await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(sourceId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, source!.Status); Assert.Equal(0, source.DeliveryAttemptCount);
        }
        Assert.Equal(2, fault.DispositionWrites.Count); Assert.Same(fault.DispositionWrites[0], fault.DispositionWrites[1]);
        fixture.AdvanceTime(TimeSpan.FromMinutes(10)); await fixture.SweepAsync();
        Assert.Single(fixture.ChildProbe.Observations);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var source = await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(sourceId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source!.Status);
            var events = (await scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new(run.Start.WorkflowExecutionId, 500))).Items;
            var results = events.Where(item => item.Name == "ChildStartDispositionObserved").ToArray();
            Assert.Equal(2, results.Length); Assert.NotEqual(results[0].HistoryEventId, results[1].HistoryEventId);
            Assert.Equal("source-terminal-or-retired-observed", results[1].Metadata["history.reasonCode"]);
            Assert.All(results, item => Assert.Equal(sourceId, item.Causation!.DeliveryId));
        }
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Legacy_selected_child_receiver_omits_observations_and_later_upgrade_does_not_retrofit_pinned_source()
    {
        var selection = new ChildReceiverSelection { Legacy = true };
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true,
            configureServices: services => SelectChildReceiver(services, selection));
        var run = await fixture.StartParentAsync("history-old-receiver", "history-old-receiver-parent", "correlation");
        var owner = (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))!;
        Assert.DoesNotContain("ChildStartAttemptObserved", owner.HistoryCapturePolicy!.SupportedKinds);
        Assert.DoesNotContain("ChildStartDispositionObserved", owner.HistoryCapturePolicy.SupportedKinds);
        var start = Assert.Single(CheckpointCommitIntents.IntentsOf(run.CompletionCommit), intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        Assert.Null(start.HistoryCausation);
        selection.Legacy = false;
        await fixture.SweepAsync();
        Assert.Single(fixture.ChildProbe.Observations);
        await using var scope = fixture.Services.CreateAsyncScope();
        var history = await scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        Assert.Contains(history.Items, item => item.Name == "ChildWorkflowDispatchRequested");
        Assert.DoesNotContain(history.Items, item => item.Name is "ChildStartAttemptObserved" or "ChildStartDispositionObserved");
        Assert.Null(Assert.Single(CheckpointCommitIntents.IntentsOf(run.CompletionCommit), intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind).HistoryCausation);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Replacing_captured_child_receiver_with_legacy_receiver_refuses_before_effect_and_keeps_source_owed()
    {
        var selection = new ChildReceiverSelection();
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true,
            configureServices: services => SelectChildReceiver(services, selection));
        var run = await fixture.StartParentAsync("history-replaced-receiver", "history-replaced-receiver-parent", "correlation");
        var start = Assert.Single(CheckpointCommitIntents.IntentsOf(run.CompletionCommit), intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        Assert.NotNull(start.HistoryCausation);
        selection.Legacy = true;
        var unavailable = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () => { await fixture.SweepAsync(); });
        Assert.True(unavailable.IsUnavailable);
        Assert.Empty(fixture.ChildProbe.Observations);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var row = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>()
                .FindAsync(RuntimePostCommitOutboxItems.OutboxItemId(run.CompletionCommit.CommitId, start)))!;
            Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, row.Status); Assert.Equal(0, row.DeliveryAttemptCount);
        }
        selection.Legacy = false; fixture.AdvanceTime(TimeSpan.FromMinutes(10)); await fixture.SweepAsync();
        Assert.Single(fixture.ChildProbe.Observations);
    }

    private sealed class ChildReceiverSelection { public bool Legacy; }
    private static void SelectChildReceiver(IServiceCollection services, ChildReceiverSelection selection) =>
        services.AddScoped<ChildStartExecutor>(sp => selection.Legacy
            ? new(sp.GetRequiredService<IWorkflowStartDispatcher>(), sp.GetRequiredService<IOptions<DispatchWorkflowOptions>>(),
                sp.GetRequiredService<IWorkflowDispatchStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IWorkflowExecutionStateStore>())
            : new(sp.GetRequiredService<IWorkflowStartDispatcher>(), sp.GetRequiredService<IOptions<DispatchWorkflowOptions>>(),
                sp.GetRequiredService<IWorkflowDispatchStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IWorkflowExecutionStateStore>(), sp));

    private sealed class ObservationFailureProbe
    {
        public int RemainingFailures = 2;
        public List<RuntimeHistoryObservation> DispositionWrites { get; } = [];
    }
    private sealed class LosingObservationWriter(IRuntimeHistoryObservationWriter inner, ObservationFailureProbe fault) : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore reader) => inner.CanRecordHistory(reader);
        public async ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            var result = await inner.RecordAsync(observation, cancellationToken);
            if (observation.Event.Name == "ChildStartDispositionObserved")
            {
                fault.DispositionWrites.Add(observation);
                if (Interlocked.Decrement(ref fault.RemainingFailures) >= 0) throw new IOException("Constructed observed-result acknowledgement loss.");
            }
            return result;
        }
    }
}
