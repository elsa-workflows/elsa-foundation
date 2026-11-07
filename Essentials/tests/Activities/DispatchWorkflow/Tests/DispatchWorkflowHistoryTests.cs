using System.Text.Json;
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

public sealed partial class DispatchWorkflowHistoryTests
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

    [Theory]
    [InlineData("missing")] [InlineData("payload")] [InlineData("scope")]
    [InlineData("expired")] [InlineData("reclaimed")] [InlineData("delivered")]
    [Trait("Feature", "RunHistory")]
    public async Task Rejected_child_source_context_adds_no_observation_and_starts_no_child(string scenario)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true);
        var run = await fixture.StartParentAsync("history-context-" + scenario, "history-context-parent-" + scenario, "correlation");
        await using var scope = fixture.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var claims = services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var claim = Assert.Single(await claims.ClaimAsync(new("source-context-owner", DispatchWorkflowRuntimeTestFixture.Now,
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.StartChildIntentKind)));
        var context = new PostCommitDeliveryContext(claim, new("tenant-42"));
        if (scenario is "missing" or "payload" or "scope") context = TamperSource(context, scenario);
        if (scenario is "expired" or "reclaimed") fixture.AdvanceTime(TimeSpan.FromMinutes(6));
        if (scenario == "reclaimed") Assert.Single(await claims.ClaimAsync(new("replacement-owner", DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(6),
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.StartChildIntentKind)));
        if (scenario == "delivered") await claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId,
            RuntimePostCommitOutboxStatus.Delivered, DispatchWorkflowRuntimeTestFixture.Now));
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var original = (await lookup.FindDurableAsync(claim.OutboxItemId))!;
        var history = services.GetRequiredService<IRuntimeHistoryStore>();
        var before = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        var handler = services.GetRequiredService<ChildStartExecutor>();
        if (scenario is "expired" or "reclaimed" or "delivered")
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(async () => { await handler.HandleAsync(context); });
        else
            Assert.False((await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () => { await handler.HandleAsync(context); })).IsUnavailable);
        Assert.Empty(fixture.ChildProbe.Observations); Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        var after = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        Assert.Equal(before.Head, after.Head); Assert.Equal(before.Items.Select(item => item.HistoryEventId), after.Items.Select(item => item.HistoryEventId));
        Assert.True(original.IsEquivalentTo(await lookup.FindDurableAsync(claim.OutboxItemId)));
    }

    [Theory]
    [InlineData("missing")] [InlineData("payload")] [InlineData("scope")]
    [InlineData("expired")] [InlineData("reclaimed")] [InlineData("delivered")] [InlineData("legacy")]
    [Trait("Feature", "RunHistory")]
    public async Task Rejected_parent_resume_source_context_adds_no_target_observation_and_does_not_resume_parent(string scenario)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true);
        var run = await fixture.StartParentAsync("history-parent-context-" + scenario, "history-parent-context-owner-" + scenario,
            "correlation", waitForCompletion: true);
        await using var scope = fixture.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var claims = services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var startClaim = Assert.Single(await claims.ClaimAsync(new("child-setup-owner", DispatchWorkflowRuntimeTestFixture.Now,
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.StartChildIntentKind)));
        await services.GetRequiredService<ChildStartExecutor>().HandleAsync(new PostCommitDeliveryContext(startClaim, new("tenant-42")));
        Assert.Single(fixture.ChildProbe.Observations);
        var claim = Assert.Single(await claims.ClaimAsync(new("parent-context-owner", DispatchWorkflowRuntimeTestFixture.Now,
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.ResumeParentIntentKind)));
        var context = new PostCommitDeliveryContext(claim, new("tenant-42"));
        if (scenario is "missing" or "payload" or "scope") context = TamperSource(context, scenario);
        if (scenario is "expired" or "reclaimed") fixture.AdvanceTime(TimeSpan.FromMinutes(6));
        if (scenario == "reclaimed") Assert.Single(await claims.ClaimAsync(new("replacement-parent-owner", DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(6),
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.ResumeParentIntentKind)));
        if (scenario == "delivered") await claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId,
            RuntimePostCommitOutboxStatus.Delivered, DispatchWorkflowRuntimeTestFixture.Now));
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var original = (await lookup.FindDurableAsync(claim.OutboxItemId))!;
        var history = services.GetRequiredService<IRuntimeHistoryStore>();
        var before = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        var beforeWorkflow = JsonSerializer.Serialize(await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId));
        var beforeActivity = JsonSerializer.Serialize(await services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(run.Start.WorkflowExecutionId, run.Activity.Execution.ActivityExecutionId));
        var beforeCommands = fixture.Actors.Envelopes.Count;
        var handler = scenario == "legacy" ? new ParentResumeExecutor(services.GetRequiredService<IBookmarkResumeDispatcher>(),
            services.GetRequiredService<IWorkflowExecutionStateStore>(), services.GetRequiredService<IActivityExecutionStateStore>(),
            services.GetRequiredService<IBookmarkStateStore>()) : services.GetRequiredService<ParentResumeExecutor>();
        if (scenario == "legacy") Assert.False(((IRuntimeHistoryDeliverySource)handler).CanRecordHistory(
            services.GetRequiredService<IRuntimeHistoryObservationWriter>(), history));
        if (scenario is "expired" or "reclaimed" or "delivered")
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(async () => { await handler.HandleAsync(context); });
        else
            Assert.Equal(scenario == "legacy", (await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () => { await handler.HandleAsync(context); })).IsUnavailable);
        Assert.Equal(beforeWorkflow, JsonSerializer.Serialize(await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId)));
        Assert.Equal(beforeActivity, JsonSerializer.Serialize(await services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(run.Start.WorkflowExecutionId, run.Activity.Execution.ActivityExecutionId)));
        Assert.Equal(beforeCommands, fixture.Actors.Envelopes.Count);
        var after = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        Assert.Equal(before.Head, after.Head); Assert.Equal(before.Items.Select(item => item.HistoryEventId), after.Items.Select(item => item.HistoryEventId));
        Assert.True(original.IsEquivalentTo(await lookup.FindDurableAsync(claim.OutboxItemId)));
        Assert.Single(fixture.ChildProbe.Observations);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Legacy_parent_receiver_at_child_admission_does_not_gain_settlement_causation_after_receiver_upgrade()
    {
        var legacy = true;
        var logical = new CheckpointRecorder();
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true, configureServices: services =>
        {
            services.AddSingleton<IRuntimeCheckpointCommitEnricher>(new LogicalCheckpointRecorder(logical));
            var actual = services.Last(item => item.ServiceType == typeof(ParentResumeExecutor));
            services.AddScoped<ParentResumeExecutor>(sp => legacy
                ? new(sp.GetRequiredService<IBookmarkResumeDispatcher>(), sp.GetRequiredService<IWorkflowExecutionStateStore>(),
                    sp.GetRequiredService<IActivityExecutionStateStore>(), sp.GetRequiredService<IBookmarkStateStore>())
                : (ParentResumeExecutor)actual.ImplementationFactory!(sp));
        });
        var run = await fixture.StartParentAsync("history-old-parent-source", "history-old-parent-source-owner", "correlation", waitForCompletion: true);
        await fixture.SweepAsync(); await fixture.SweepAsync();
        var child = (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))!;
        Assert.DoesNotContain("ChildWorkflowSettled", child.HistoryCapturePolicy!.SupportedKinds);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))!.Status);
        var terminal = Assert.Single(fixture.Checkpoints.Commits, item => item.WorkflowExecutionId == child.WorkflowExecutionId && item.Checkpoint.Name == "WorkflowCompleted");
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(terminal), item => item.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
        Assert.Null(intent.HistoryCausation);
        // The provider-facing envelope has subsequently materialized the source outbox. Replay starts at
        // the actual logical capture boundary, whose source proof precedes that committer normalization.
        var logicalTerminal = Assert.Single(logical.Commits, item => item.CommitId == terminal.CommitId);
        Assert.Empty(logicalTerminal.StateChanges.PostCommitOutbox);
        Assert.Equal(Assert.Single(logicalTerminal.HistoryBatches!).SourceFingerprint,
            RuntimeCheckpointCommitFingerprint.Compute(logicalTerminal with { HistoryBatches = null }));
        legacy = false;
        await using var scope = fixture.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var native = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>(); var physicalCount = native.ListCommits().Count;
        var history = services.GetRequiredService<IRuntimeHistoryStore>(); var before = await history.ReadPageAsync(new(child.WorkflowExecutionId, 500));
        // The active coalescing session folded preceding logical checkpoints into this physical boundary,
        // retaining their ordered batches and materialized outbox while clearing declared intents. Its marker
        // therefore identifies the whole physical group, not this trailing logical checkpoint in isolation.
        Assert.Empty(terminal.PostCommitIntents);
        Assert.NotEmpty(logicalTerminal.PostCommitIntents);
        Assert.True(terminal.HistoryBatches!.Count > logicalTerminal.HistoryBatches!.Count);
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(logicalTerminal), RuntimeCheckpointCommitFingerprint.Compute(terminal));
        var rebuilt = logicalTerminal with { HistoryBatches = null };
        var enrichers = services.GetServices<IRuntimeCheckpointCommitEnricher>().OrderBy(item => item.Order).ToArray();
        foreach (var enricher in enrichers) rebuilt = await enricher.EnrichAsync(rebuilt);
        foreach (var enricher in enrichers) await enricher.ValidateFinalAsync(rebuilt);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(logicalTerminal), RuntimeCheckpointCommitFingerprint.Compute(rebuilt));
        Assert.Null(Assert.Single(rebuilt.PostCommitIntents, item => item.Kind == DispatchWorkflowConstants.ResumeParentIntentKind).HistoryCausation);
        var originalPhysicalFingerprint = RuntimeCheckpointCommitFingerprint.Compute(terminal);
        await native.CommitAsync(terminal, new(RuntimeCheckpointPersistenceMode.Immediate, "Replay the original physical group."));
        Assert.Equal(originalPhysicalFingerprint, RuntimeCheckpointCommitFingerprint.Compute(terminal));
        Assert.Equal(physicalCount, native.ListCommits().Count);
        var source = await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(RuntimePostCommitOutboxItems.OutboxItemId(terminal.CommitId, intent));
        Assert.Null(source!.Intent.HistoryCausation);
        var after = await history.ReadPageAsync(new(child.WorkflowExecutionId, 500));
        Assert.Equal(before.Head, after.Head); Assert.Equal(before.Items.Select(item => item.HistoryEventId), after.Items.Select(item => item.HistoryEventId));
        Assert.DoesNotContain(after.Items, item => item.Name == "ChildWorkflowSettled");
    }

    private sealed class LogicalCheckpointRecorder(CheckpointRecorder recorder) : IRuntimeCheckpointCommitEnricher
    {
        public int Order => int.MaxValue;
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            recorder.Record(commit);
            return ValueTask.FromResult(commit);
        }
    }

    private static PostCommitDeliveryContext TamperSource(PostCommitDeliveryContext source, string scenario)
    {
        if (scenario == "scope") return new(source.Claim, new("another-tenant"));
        var item = source.Claim.Item;
        var id = scenario == "missing" ? "fabricated-commit:" + item.Intent.IntentId : item.OutboxItemId;
        var intent = new RuntimePostCommitIntent(item.Intent.IntentId, item.Intent.WorkflowExecutionId, item.Intent.Kind,
            item.Intent.RecordedAt, item.Intent.ActivityExecutionId, item.Intent.IdempotencyKey,
            scenario == "payload" ? JsonSerializer.SerializeToElement(new { forged = true }) : item.Intent.Payload,
            item.Intent.Metadata, item.Intent.DependsOnWaitRegistrationId, item.Intent.WaitFailurePolicy,
            item.Intent.MaterializedSchedulerWorkItem, item.Intent.NotBefore)
            { HistoryCausation = item.Intent.HistoryCausation! with { DeliveryId = id } };
        var forged = new RuntimePostCommitOutboxItem(id, intent, item.Status, item.RecordedAt, item.AvailableAt, item.RetryPolicy,
            item.DeliveryAttemptCount, item.DeliveringOwnerId, item.DeliveryStartedAt, item.DeliveredAt, item.LastFailureMessage,
            item.Metadata, item.DeliveryFencingToken, item.DeliveryVisibleAfter, item.StimulusDeliveryProtocolVersion,
            item.StimulusDeliverySourceScope, item.RecipientSnapshot);
        return new(new(forged, source.Claim.OwnerId, source.Claim.FencingToken, source.Claim.ClaimedAt, source.Claim.VisibleAfter), source.Scope);
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
