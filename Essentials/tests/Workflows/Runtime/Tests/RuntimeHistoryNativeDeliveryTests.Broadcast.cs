using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeHistoryNativeDeliveryTests
{
    private const string OtherRun = "other-run";

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public async Task One_publication_has_independent_saved_recipients_receipts_consumptions_and_native_outcomes(
        bool faultOther, bool captureFirst, bool captureOther)
    {
        var activator = new BroadcastActivator(faultOther);
        using var f = await Fixture.CreateAsync("complete", publication: true, captured: captureFirst,
            configure: services => services.AddSingleton<IActivityActivator>(activator));
        await f.SeedOtherRecipientAsync(captureOther); await f.SeedPublisherAsync();
        var source = await f.PublishAsync();
        var published = Assert.Single((await f.History.ReadPageAsync(new("publisher"))).Items, item => item.Name == "StimulusPublicationCommitted");
        Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount);
        var delivered = (await f.PublicationWriter.FindDurableAsync(source.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Empty(delivered.RecipientSnapshot!.Starts);
        Assert.Equal(new[] { OtherRun, Run }, delivered.RecipientSnapshot.Resumes.Select(item => item.WorkflowExecutionId).Order(StringComparer.Ordinal));
        var first = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        var other = Assert.Single(await f.Queue.ListAllAsync(new(OtherRun)));
        Assert.NotEqual(first.WorkItemId, other.WorkItemId); Assert.NotEqual(first.CommandId, other.CommandId);
        var firstReceipt = await AssertBroadcastHandoffAsync(f, Run, captureFirst, first, published, source.OutboxItemId);
        var otherReceipt = await AssertBroadcastHandoffAsync(f, OtherRun, captureOther, other, published, source.OutboxItemId);
        if (firstReceipt is not null && otherReceipt is not null) Assert.NotEqual(firstReceipt.HistoryEventId, otherReceipt.HistoryEventId);
        Assert.Empty(activator.Runs);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(new[] { Run }, activator.Runs);
        Assert.Null(await f.Bookmarks.FindAsync(Run, Wait)); Assert.NotNull(await f.Bookmarks.FindAsync(OtherRun, Wait));
        var notYetRun = (await f.History.ReadPageAsync(new(OtherRun))).Items;
        Assert.DoesNotContain(notYetRun, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted" or "ActivityContinuationFaulted");
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainOtherRecipientAsync()).Items).Status);
        Assert.Equal(new[] { Run, OtherRun }, activator.Runs); Assert.Null(await f.Bookmarks.FindAsync(OtherRun, Wait));
        await AssertBroadcastOutcomeAsync(f, Run, captureFirst, firstReceipt, faulted: false);
        await AssertBroadcastOutcomeAsync(f, OtherRun, captureOther, otherReceipt, faulted: faultOther);
        Assert.Empty(await f.Poison.ListAsync(Run)); Assert.Empty(await f.Poison.ListAsync(OtherRun));
        var publisher = (await f.History.ReadPageAsync(new("publisher"))).Items;
        Assert.Single(publisher, item => item.Name == "StimulusPublicationCommitted");
        Assert.Single(publisher, item => item.Name == "StimulusRoutingAttemptObserved");
        Assert.Single(publisher, item => item.Name == "StimulusRoutingDispositionObserved");
        Assert.Empty((await f.ProcessPublicationsAsync()).Items); // No repeated transport/source occurrence.
    }

    [Fact]
    public async Task One_retired_broadcast_recipient_does_not_erase_another_recipients_consumption_or_invent_its_own()
    {
        var activator = new BroadcastActivator(faultOther: false);
        using var f = await Fixture.CreateAsync("complete", publication: true,
            configure: services => services.AddSingleton<IActivityActivator>(activator));
        await f.SeedOtherRecipientAsync(captured: true); await f.SeedPublisherAsync();
        var source = await f.PublishAsync(); await f.ProcessPublicationsAsync();
        Assert.Equal(2, (await f.PublicationWriter.FindDurableAsync(source.OutboxItemId))!.RecipientSnapshot!.Resumes.Count);
        var other = Assert.Single(await f.Queue.ListAllAsync(new(OtherRun)));
        var cause = other.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!;
        await f.Bookmarks.DeleteAsync(OtherRun, Wait);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainOtherRecipientAsync()).Items).Status);
        Assert.Empty(activator.Runs); Assert.Empty(await f.Queue.ListAllAsync(new(OtherRun)));
        var skipped = Assert.Single((await f.History.ReadPageAsync(new(OtherRun))).Items, IsReceiverObservation);
        Assert.Equal("receiver-bookmark-missing", skipped.Metadata["history.reasonCode"]);
        Assert.Equal(cause.HistoryEventId, skipped.Causation!.HistoryEventId);
        Assert.Equal(OtherRun, skipped.WorkflowExecutionId); Assert.Equal(Wait, skipped.BookmarkId);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(OtherRun))).Items, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(new[] { Run }, activator.Runs);
        var first = (await f.History.ReadPageAsync(new(Run))).Items;
        Assert.Single(first, item => item.Name == "BookmarkConsumed"); Assert.Single(first, item => item.Name == "ActivityContinuationCompleted");
        Assert.DoesNotContain(first, IsReceiverObservation);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(OtherRun))).Items, item => item.Name == "ActivityContinuationCompleted");
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await f.PublicationWriter.FindDurableAsync(source.OutboxItemId))!.Status);
    }

    private static async Task<RuntimeHistoryEvent?> AssertBroadcastHandoffAsync(Fixture f, string run, bool captured,
        RuntimeSchedulerWorkItem work, RuntimeHistoryEvent published, string sourceId)
    {
        var page = await f.History.ReadPageAsync(new(run));
        var cause = work.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!;
        Assert.Equal(run, work.WorkflowExecutionId);
        Assert.DoesNotContain(page.Items, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        if (!captured)
        {
            Assert.Null(page.Head); Assert.Empty(page.Items);
            Assert.Equal(published.HistoryEventId, cause.HistoryEventId); Assert.Equal("publisher", cause.WorkflowExecutionId);
            Assert.Equal(sourceId, cause.DeliveryId); return null;
        }
        var receipt = Assert.Single(page.Items, item => item.Name == "DeliveryReceived");
        Assert.Equal(run, receipt.WorkflowExecutionId); Assert.Equal(ActivityId, receipt.ActivityExecutionId); Assert.Equal(Wait, receipt.BookmarkId);
        Assert.Equal(published.HistoryEventId, receipt.Causation!.HistoryEventId);
        Assert.Equal("publisher", receipt.Causation.WorkflowExecutionId); Assert.Equal(sourceId, receipt.Causation.DeliveryId);
        Assert.Equal(receipt.HistoryEventId, cause.HistoryEventId); Assert.Equal(run, cause.WorkflowExecutionId);
        Assert.Single(page.Items, item => item.Name == "DeliveryDispatched"); return receipt;
    }

    private static async Task AssertBroadcastOutcomeAsync(Fixture f, string run, bool captured, RuntimeHistoryEvent? receipt, bool faulted)
    {
        // Capture controls diagnostic recording, never the authoritative callback outcome. Re-read both exact
        // native run/activity identities after both drains, so the second outcome cannot overwrite the first.
        var native = (await f.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(run, ActivityId))!;
        Assert.Equal(run, native.Execution.WorkflowExecutionId); Assert.Equal(ActivityId, native.Execution.ActivityExecutionId);
        Assert.Equal(faulted ? ActivityExecutionStatus.Faulted : ActivityExecutionStatus.Completed, native.Status);
        var attempt = Assert.Single(native.Attempts!, item => item.Reason == ActivityAttemptReason.Resume);
        Assert.NotNull(attempt.EndedAt);
        Assert.Equal(faulted ? Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault : Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete, attempt.TransitionKind);
        var delivery = Assert.Single(native.TriggerDeliveries!, item => item.DeliveryId == attempt.TriggerDeliveryId);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, delivery.Status); Assert.Empty(native.BookmarkIds);
        var incidents = await f.Services.GetRequiredService<IIncidentStateStore>().ListAsync(run);
        if (faulted) { Assert.Equal("approval.rejected", native.Fault!.Code); Assert.Single(incidents); }
        else { Assert.Null(native.Fault); Assert.NotNull(native.Completion); Assert.Empty(incidents); }
        var page = await f.History.ReadPageAsync(new(run, limit: 200));
        if (!captured)
        {
            Assert.Null(page.Head); Assert.Empty(page.Items);
            var native = (await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(run))!;
            Assert.False(native.HistoryCaptureEnabledAtAdmission); Assert.Null(native.HistoryCapturePolicy); return;
        }
        Assert.All(page.Items, item => Assert.Equal(run, item.WorkflowExecutionId));
        var consumed = Assert.Single(page.Items, item => item.Name == "BookmarkConsumed");
        var entered = Assert.Single(page.Items, item => item.Name == "ActivityContinuationStarted");
        Assert.Equal(receipt!.HistoryEventId, consumed.Causation!.HistoryEventId); Assert.Equal(receipt.HistoryEventId, entered.Causation!.HistoryEventId);
        Assert.True(receipt.Sequence < consumed.Sequence); Assert.True(consumed.Sequence < entered.Sequence);
        var outcome = Assert.Single(page.Items, item => item.Name == (faulted ? "ActivityContinuationFaulted" : "ActivityContinuationCompleted"));
        Assert.Equal(receipt.HistoryEventId, outcome.Causation!.HistoryEventId); Assert.True(entered.Sequence < outcome.Sequence);
        Assert.Equal(faulted ? 1 : 0, page.Items.Count(item => item.Name == "IncidentRecorded"));
        Assert.DoesNotContain(page.Items, item => item.Name is "DeliveryFaulted" || item.Name == (faulted ? "ActivityContinuationCompleted" : "ActivityContinuationFaulted"));
        Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, (await f.History.FindBookmarkAsync(run, Wait))!.Status);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(page.Items)); Assert.False(page.Head!.CapturedFromStart);
    }

    private sealed class BroadcastActivator(bool faultOther) : IActivityActivator
    {
        internal List<string> Runs { get; } = [];
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            Runs.Add(request.WorkflowExecutionId);
            return ValueTask.FromResult(new ActivityActivationLease(new NativeActivity(faultOther && request.WorkflowExecutionId == OtherRun ? "returned-fault" : "complete")));
        }
    }
    private sealed partial class Fixture
    {
        internal async Task SeedOtherRecipientAsync(bool captured)
        {
            var sp = scope.ServiceProvider;
            var workflows = sp.GetRequiredService<IWorkflowExecutionStateStore>();
            var original = (await workflows.FindAsync(Run))!;
            await workflows.SaveAsync(original with { WorkflowExecutionId = OtherRun,
                HistoryCapturePolicy = captured ? RuntimeHistoryRegistration.ResolvePolicy(sp, OtherRun) : null,
                HistoryCaptureEnabledAtAdmission = captured });
            var activities = sp.GetRequiredService<IActivityExecutionStateStore>();
            var state = (await activities.FindAsync(Run, ActivityId))!;
            await activities.SaveAsync(state with { Execution = state.Execution with { WorkflowExecutionId = OtherRun } });
            var payload = new RuntimeCreateBookmarkCommandPayload(Pin, Wait, ActivityId, Node, Resume, PublicationStimulusType,
                PublicationStimulusHash, null, null, RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason);
            var work = new RuntimeSchedulerWorkItem("other-create", OtherRun, "other-create-command", WorkflowExecutionCommandKind.CreateBookmark,
                "other-create-envelope", "other-create-key", clock.GetUtcNow(), clock.GetUtcNow(), payload: JsonSerializer.SerializeToElement(payload));
            await new WorkflowCreateBookmarkSchedulerWorkHandler(sp.GetRequiredService<IWorkflowExecutableStore>(), activities,
                sp.GetRequiredService<RuntimeCheckpointCommitter>(), sp.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>(), clock).HandleAsync(work);
        }
        internal ValueTask<RuntimeSchedulerDrainResult> DrainOtherRecipientAsync() => new WorkflowSchedulerDrainer(Queue,
            [new WorkflowResumeBookmarkSchedulerWorkHandler(provider.GetRequiredService<IServiceScopeFactory>(), clock)],
            scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>(), Poison,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), clock), clock,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = TimeSpan.FromSeconds(5) }).DrainAsync(new(OtherRun, maxWorkItems: 1));
    }
}
