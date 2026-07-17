using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    [Fact]
    public async Task HandleAsync_TypedTriggerValidatesThenResumesFreshActivationAndCommitsOneCompletion()
    {
        var executable = NewTypedExecutable(currentLiteral: "changed-after-snapshot");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var state = NewTypedTriggerState(contract, triggerType);
        var activator = new StatefulResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(state);
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        var activation = Assert.Single(activator.Requests);
        Assert.Equal("delivery-42", activation.Trigger!.DeliveryId);
        Assert.Equal("actexec-1:attempt:2", activation.Attempt.AttemptId);
        Assert.Equal(ActivityAttemptReason.Resume, activation.Attempt.Reason);
        var activity = Assert.Single(activator.Activities);
        Assert.Equal(new ApprovalState("request-42"), activity.ObservedState);
        Assert.Equal(new ApprovalTrigger(true), activity.ObservedTrigger);
        Assert.Equal("actexec-1:attempt:2", activity.ObservedAttemptId);
        Assert.True(activity.Disposed);

        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(completed?.Completion);
        Assert.Equal("Done", completed.Completion.OutcomeKey);
        Assert.Null(completed.PrivateState);
        Assert.Empty(completed.TriggerRegistrations!);
        Assert.Empty(completed.BookmarkIds);
        Assert.Collection(
            completed.TriggerDeliveries!,
            delivery =>
            {
                Assert.Equal("delivery-42", delivery.DeliveryId);
                Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, delivery.Status);
                Assert.Equal(ValuePresence.Absent, delivery.Payload.Presence);
            });
        Assert.Equal(2, completed.Attempts!.Count);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_WrongTypedTriggerIsRejectedBeforeActivation()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var expectedType = Descriptor<ApprovalTrigger>();
        var activator = new StatefulResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, expectedType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(new ValueTypeDescriptor("test/wrong-trigger"), "dedupe-42")));

        Assert.Empty(activator.Requests);
        var unchanged = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Suspended, unchanged!.Status);
        Assert.Single(unchanged.Attempts!);
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_DuplicateTypedTriggerIsDeduplicatedBeforeActivation()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var state = NewTypedTriggerState(contract, triggerType);
        var priorDelivery = NewDelivery(triggerType, "dedupe-42", ActivityTriggerDeliveryStatus.Consumed);
        var activator = new StatefulResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(state with { TriggerDeliveries = [priorDelivery] });
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        Assert.Empty(activator.Requests);
        var unchanged = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Single(unchanged!.Attempts!);
        Assert.Single(unchanged.TriggerDeliveries!);
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_RedeliveredAfterWorkerCancellation_ClaimsFreshAttemptFromConsumedDelivery()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        using var cancellation = new CancellationTokenSource();
        var activator = new CancelThenCompleteResumeActivator(_activityStateStore, cancellation);
        var observer = new RecordingBookmarkObserver();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator, observer);
        var handler = NewHandler(provider);
        var workItem = NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(workItem, cancellation.Token).AsTask());

        var interrupted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal("actexec-1:attempt:2", interrupted!.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
        Assert.Equal("resume-work", interrupted.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId]);
        Assert.Equal(ActivityExecutionStatus.Running, interrupted.Status);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, Assert.Single(interrupted.TriggerDeliveries!).Status);
        Assert.Empty(interrupted.BookmarkIds);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));

        await handler.HandleAsync(workItem);

        Assert.Collection(
            activator.Requests,
            first =>
            {
                Assert.Equal("actexec-1:attempt:2", first.Attempt.AttemptId);
                Assert.Equal("delivery-42", first.Attempt.TriggerDeliveryId);
            },
            second =>
            {
                Assert.Equal("actexec-1:attempt:3", second.Attempt.AttemptId);
                Assert.Equal(ActivityAttemptReason.Resume, second.Attempt.Reason);
                Assert.Equal("delivery-42", second.Attempt.TriggerDeliveryId);
            });
        Assert.Collection(
            activator.StatesObservedBeforeActivation,
            first =>
            {
                Assert.Equal("actexec-1:attempt:2", first.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
                Assert.Single(first.TriggerDeliveries!);
            },
            second =>
            {
                var attempts = second.Attempts!.OrderBy(attempt => attempt.Ordinal).ToArray();
                Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, attempts[1].TransitionKind);
                Assert.NotNull(attempts[1].EndedAt);
                Assert.Null(attempts[2].EndedAt);
                Assert.Equal("actexec-1:attempt:3", second.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
                Assert.Single(second.TriggerDeliveries!);
            });
        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Completed, completed!.Status);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, Assert.Single(completed.TriggerDeliveries!).Status);
        Assert.Equal(3, completed.Attempts!.Count);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Collection(observer.Consumed, bookmark => Assert.Equal("bookmark-1", bookmark.BookmarkId));
        var claimCommits = _checkpointWriter.ListCommits()
            .Where(record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed)
            .Select(record => record.Commit)
            .ToArray();
        Assert.Equal(2, claimCommits.Length);
        Assert.Single(claimCommits[0].StateChanges.Bookmarks);
        Assert.Empty(claimCommits[1].StateChanges.Bookmarks);
    }

    [Fact]
    public async Task TypedTriggerHistory_RemainsBoundedAndRetiresPayloadsAcrossLongSuspendResumeLoop()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new ResuspendingResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(executable.RootActivity.ActivityContract!, triggerType));
        await using var provider = NewProvider(activator);
        var handler = NewHandler(provider);

        for (var index = 0; index < 52; index++)
        {
            var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
            Assert.Equal(ActivityExecutionStatus.Suspended, state!.Status);
            var registration = Assert.Single(state.TriggerRegistrations!);
            await _bookmarkStateStore.SaveAsync(new BookmarkState(
                registration.RegistrationId,
                "wfexec-1",
                "actexec-1",
                "node-wait",
                registration.ResumeTargetKey,
                registration.StimulusType,
                registration.StimulusHash,
                null,
                new Dictionary<string, string>(),
                _now.AddSeconds(index),
                null));
            var payload = new RuntimeResumeBookmarkCommandPayload(
                NewIdentity(),
                registration.RegistrationId,
                "actexec-1",
                "node-wait",
                registration.ResumeTargetKey,
                registration.StimulusType,
                registration.StimulusHash,
                JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
                RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason,
                new RuntimeTypedTriggerDeliveryMetadata(
                    $"delivery-{index}",
                    triggerType,
                    "provider.delivery-status",
                    _now.AddSeconds(index),
                    $"dedupe-{index}"));
            var workItem = new RuntimeSchedulerWorkItem(
                $"resume-work-{index}",
                "wfexec-1",
                $"command-{index}",
                WorkflowExecutionCommandKind.ResumeBookmark,
                $"envelope-{index}",
                $"wfexec-1:resume:{registration.RegistrationId}:{index}",
                _now.AddSeconds(index),
                _now.AddSeconds(index),
                40 + index,
                JsonSerializer.SerializeToElement(payload),
                new Dictionary<string, string>(),
                new Dictionary<string, string>());

            await handler.HandleAsync(workItem);
        }

        var finalState = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(finalState);
        Assert.InRange(finalState.TriggerDeliveries!.Count, 1, 32);
        Assert.InRange(finalState.Attempts!.Count, 1, 32);
        Assert.All(finalState.TriggerDeliveries, delivery => Assert.Equal(ValuePresence.Absent, delivery.Payload.Presence));
        var retainedDeliveryIds = finalState.TriggerDeliveries
            .Select(delivery => delivery.DeliveryId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            finalState.Attempts.Where(attempt => attempt.TriggerDeliveryId is not null),
            attempt => Assert.Contains(attempt.TriggerDeliveryId!, retainedDeliveryIds));
    }

    [Fact]
    public async Task HandleAsync_ValidTriggerNotifiesBookmarkConsumptionOnceAfterClaimCommit()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var observer = new RecordingBookmarkObserver();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(new StatefulResumeActivator(), observer);
        var handler = NewHandler(provider);
        var workItem = NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42"));

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        Assert.Collection(observer.Consumed, bookmark => Assert.Equal("bookmark-1", bookmark.BookmarkId));
        var commits = _checkpointWriter.ListCommits().Select(record => record.Commit).ToArray();
        var claim = Assert.Single(commits, commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed);
        Assert.Equal(RuntimeStateChangeOperation.Delete, Assert.Single(claim.StateChanges.Bookmarks).Operation);
        var completion = Assert.Single(commits, commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted);
        Assert.Empty(completion.StateChanges.Bookmarks);
    }

    [Fact]
    public async Task HandleAsync_SelectedTriggerAtomicallyRetiresSiblingBookmarksAndRegistrations()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var state = NewTypedTriggerState(contract, triggerType);
        var siblingRegistration = new Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration(
            "bookmark-2",
            state.InvocationId,
            "resume-target:delivery",
            triggerType,
            "delivery-status",
            "sha256:delivery-status:order-456",
            Elsa.Workflows.Runtime.Core.Models.ActivityTriggerDeduplicationPolicy.IdempotencyKey);
        state = state with
        {
            BookmarkIds = ["bookmark-1", "bookmark-2"],
            TriggerRegistrations = state.TriggerRegistrations!.Append(siblingRegistration).ToArray()
        };
        var siblingBookmark = new BookmarkState(
            "bookmark-2", "wfexec-1", "actexec-1", "node-wait", "resume-target:delivery",
            "delivery-status", "sha256:delivery-status:order-456", null,
            new Dictionary<string, string>(), _now.AddMinutes(-1), null);
        var observer = new RecordingBookmarkObserver();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(state);
        await SaveBookmarkAsync();
        await _bookmarkStateStore.SaveAsync(siblingBookmark);
        await using var provider = NewProvider(new StatefulResumeActivator(), observer);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-2"));
        Assert.Equal(["bookmark-1", "bookmark-2"], observer.Consumed.Select(bookmark => bookmark.BookmarkId));
        var claim = Assert.Single(
            _checkpointWriter.ListCommits(),
            record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed).Commit;
        Assert.Equal(2, claim.StateChanges.Bookmarks.Count);
        Assert.All(claim.StateChanges.Bookmarks, change => Assert.Equal(RuntimeStateChangeOperation.Delete, change.Operation));
        var claimedState = Assert.Single(claim.StateChanges.ActivityExecutions).State;
        Assert.Empty(claimedState.BookmarkIds);
        Assert.Empty(claimedState.TriggerRegistrations!);
        var selectedDelivery = Assert.Single(claimedState.TriggerDeliveries!);
        Assert.Equal("delivery-42", selectedDelivery.DeliveryId);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, selectedDelivery.Status);
    }

    [Fact]
    public async Task HandleAsync_ActivationConstructionFailureFaultsConsumedClaimWithoutOrphanBookmark()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(new ConstructionFailingResumeActivator());

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        await AssertConsumedResumeFaultAsync("ActivityResumeConstructionFailed");
    }

    [Fact]
    public async Task HandleAsync_ThrownResumeFaultsConsumedClaimWithoutOrphanBookmark()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(new ThrowingResumeActivator());

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        await AssertConsumedResumeFaultAsync("ActivityResumeFaulted");
    }

    [Fact]
    public async Task HandleAsync_UnsolicitedOperationCancellation_FaultsConsumedClaimWithoutReactivation()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new UnsolicitedCancellationResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);
        var handler = NewHandler(provider);
        var workItem = NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42"));

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        await AssertConsumedResumeFaultAsync("ActivityResumeFaulted");
        Assert.Equal(1, activator.ActivateCalls);
        Assert.True(activator.Activity.Disposed);
    }

    [Fact]
    public async Task HandleAsync_ActivityDisposalFailure_ReleasesScopeAndFaultsWithoutDoubleExecution()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new ThrowingDisposeResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);
        var handler = NewHandler(provider);
        var workItem = NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42"));

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        await AssertConsumedResumeFaultAsync("ActivityDisposalFailed");
        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(1, activator.Activity.ResumeCalls);
        Assert.True(activator.Scope.Disposed);
    }

    private async Task AssertConsumedResumeFaultAsync(string subStatus)
    {
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Equal(subStatus, state.SubStatus);
        var consumedDelivery = Assert.Single(state.TriggerDeliveries!);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, consumedDelivery.Status);
        Assert.Equal(ValuePresence.Absent, consumedDelivery.Payload.Presence);
        Assert.Empty(state.TriggerRegistrations!);
        Assert.Empty(state.BookmarkIds);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, state.Attempts!.OrderBy(attempt => attempt.Ordinal).Last().TransitionKind);
        var commits = _checkpointWriter.ListCommits().Select(record => record.Commit).ToArray();
        Assert.Equal(RuntimeStateChangeOperation.Delete, Assert.Single(commits.Single(commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed).StateChanges.Bookmarks).Operation);
        Assert.Empty(commits.Single(commit => commit.Checkpoint.Name == RuntimeCheckpointNames.IncidentRecorded).StateChanges.Bookmarks);
    }

    [Fact]
    public async Task HandleAsync_TypedResumeCanAtomicallyConsumeOneTriggerAndRegisterItsReplacement()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new ResuspendingResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        var suspended = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Suspended, suspended!.Status);
        Assert.Equal("TriggerWaiting", suspended.SubStatus);
        Assert.Null(suspended.Completion);
        Assert.Equal("request-43", suspended.PrivateState!.Value.InlineValue!.Value.GetProperty("RequestId").GetString());
        Assert.Collection(
            suspended.Attempts!.OrderBy(attempt => attempt.Ordinal),
            attempt => Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, attempt.TransitionKind),
            attempt => Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, attempt.TransitionKind));
        Assert.Collection(
            suspended.TriggerDeliveries!,
            delivery => Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, delivery.Status));
        var replacement = Assert.Single(suspended.TriggerRegistrations!);
        Assert.Equal("actexec-1:attempt:2:trigger:1", replacement.RegistrationId);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));

        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivitySuspended).Commit;
        Assert.Empty(commit.StateChanges.Bookmarks);
        var claimCommit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed).Commit;
        Assert.Equal(RuntimeStateChangeOperation.Delete, Assert.Single(claimCommit.StateChanges.Bookmarks).Operation);
        var replacementIntent = Assert.Single(commit.PostCommitIntents);
        var replacementWork = replacementIntent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.CreateBookmark, replacementWork.CommandKind);
        Assert.Equal(replacement.RegistrationId, replacementWork.Payload!.Value.Deserialize<RuntimeCreateBookmarkCommandPayload>()!.BookmarkId);
        Assert.True(Assert.Single(activator.Activities).Disposed);
    }

    [Fact]
    public async Task HandleAsync_TypedResumeCommitsReturnedFaultAsFaultTransition()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new FaultingResumeActivator();
        var observer = new RecordingBookmarkObserver();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator, observer);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        Assert.True(Assert.Single(activator.Activities).Disposed);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Equal("approval.rejected", state.Fault!.Code);
        Assert.Equal("Approval was rejected", state.Fault.Message);
        Assert.Collection(
            state.Attempts!.OrderBy(attempt => attempt.Ordinal),
            attempt => Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, attempt.TransitionKind),
            attempt => Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, attempt.TransitionKind));
        Assert.NotNull(state.Attempts!.OrderBy(attempt => attempt.Ordinal).Last().IncidentId);
        Assert.Null(state.Completion);
        var consumedDelivery = Assert.Single(state.TriggerDeliveries!);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, consumedDelivery.Status);
        Assert.Equal(ValuePresence.Absent, consumedDelivery.Payload.Presence);
        Assert.Empty(state.BookmarkIds);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Collection(observer.Consumed, bookmark => Assert.Equal("bookmark-1", bookmark.BookmarkId));
        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.IncidentRecorded).Commit;
        Assert.Empty(commit.StateChanges.Bookmarks);
        var claimCommit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed).Commit;
        var bookmarkChange = Assert.Single(claimCommit.StateChanges.Bookmarks);
        Assert.Equal(RuntimeStateChangeOperation.Delete, bookmarkChange.Operation);
        Assert.Equal("bookmark-1", bookmarkChange.StateId);
    }

    [Fact]
    public async Task HandleAsync_TypedResumeCommitsReturnedCancellationAndConsumesDelivery()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new CancellingResumeActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        Assert.True(Assert.Single(activator.Activities).Disposed);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Cancelled, state!.Status);
        Assert.Equal("Approval request withdrawn", state.Metadata[Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.CancellationReason]);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Cancel, state.Attempts!.OrderBy(attempt => attempt.Ordinal).Last().TransitionKind);
        var consumedDelivery = Assert.Single(state.TriggerDeliveries!);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, consumedDelivery.Status);
        Assert.Equal(ValuePresence.Absent, consumedDelivery.Payload.Presence);
        Assert.Empty(state.BookmarkIds);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCancelled).Commit;
        Assert.Equal(Elsa.Workflows.Runtime.Core.Constants.RuntimeCheckpointNames.ActivityCancelled, commit.Checkpoint.Name);
        var cancelWork = Assert.Single(commit.PostCommitIntents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.Cancel, cancelWork.CommandKind);
    }

    private ActivityExecutionState NewTypedTriggerState(ActivityContract contract, ValueTypeDescriptor triggerType)
    {
        var stateType = Descriptor<ApprovalState>();
        var state = NewTypedSuspendedState(contract, "original");
        return state with
        {
            PrivateState = new ActivityPrivateState(
                state.InvocationId,
                stateType.SchemaVersion!.Value,
                ValueEnvelope.Inline(
                    stateType,
                    JsonSerializer.SerializeToElement(new ApprovalState("request-42")),
                    ValueProtectionPolicy.InstanceInline),
                "actexec-1:attempt:1",
                _now.AddMinutes(-1)),
            TriggerRegistrations =
            [
                new Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration(
                    "bookmark-1",
                    state.InvocationId,
                    "resume-target:delivery",
                    triggerType,
                    "delivery-status",
                    "sha256:delivery-status:order-123",
                    Elsa.Workflows.Runtime.Core.Models.ActivityTriggerDeduplicationPolicy.IdempotencyKey)
            ],
            TriggerDeliveries = []
        };
    }

    private RuntimeTypedTriggerDeliveryMetadata Delivery(ValueTypeDescriptor triggerType, string deduplicationKey) =>
        new("delivery-42", triggerType, "provider.delivery-status", _now, deduplicationKey);

    private static ActivityTriggerDelivery NewDelivery(
        ValueTypeDescriptor triggerType,
        string deduplicationKey,
        ActivityTriggerDeliveryStatus status) =>
        new(
            "prior-delivery",
            "bookmark-1",
            triggerType,
            ValueEnvelope.Inline(
                triggerType,
                JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
                ValueProtectionPolicy.InstanceInline),
            "provider.delivery-status",
            DateTimeOffset.UnixEpoch,
            deduplicationKey,
            status);

    private static ValueTypeDescriptor Descriptor<T>() =>
        new(TypeAliasConvention.CanonicalAlias(typeof(T)), schemaVersion: 1);

    private sealed class StatefulResumeActivator : IActivityActivator
    {
        public List<ActivityActivationRequest> Requests { get; } = [];
        public List<ApprovalActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var activity = new ApprovalActivity();
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class CancelThenCompleteResumeActivator(
        IActivityExecutionStateStore activityStateStore,
        CancellationTokenSource cancellation) : IActivityActivator
    {
        public List<ActivityActivationRequest> Requests { get; } = [];
        public List<ActivityExecutionState> StatesObservedBeforeActivation { get; } = [];

        public async ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            StatesObservedBeforeActivation.Add(
                (await activityStateStore.FindAsync("wfexec-1", "actexec-1", cancellationToken))!);
            IActivity activity = Requests.Count == 1 ? new InterruptedApprovalActivity(cancellation) : new ApprovalActivity();
            return new ActivityActivationLease(activity);
        }
    }

    private sealed class InterruptedApprovalActivity(CancellationTokenSource cancellation) : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context)
        {
            cancellation.Cancel();
            context.CancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The cancelled activity should not continue.");
        }
    }

    private sealed class ResuspendingResumeActivator : IActivityActivator
    {
        public List<ResuspendingApprovalActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            var activity = new ResuspendingApprovalActivity();
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class ConstructionFailingResumeActivator : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ActivityActivationLease>(new InvalidOperationException("Construction failed."));
    }

    private sealed class ThrowingResumeActivator : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(new ThrowingApprovalActivity()));
    }

    private sealed class UnsolicitedCancellationResumeActivator : IActivityActivator
    {
        public int ActivateCalls { get; private set; }
        public UnsolicitedCancellationApprovalActivity Activity { get; } = new();

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            return ValueTask.FromResult(new ActivityActivationLease(Activity));
        }
    }

    private sealed class ThrowingDisposeResumeActivator : IActivityActivator
    {
        public int ActivateCalls { get; private set; }
        public ThrowingDisposeApprovalActivity Activity { get; } = new();
        public RecordingAsyncDisposable Scope { get; } = new();

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            return ValueTask.FromResult(new ActivityActivationLease(Activity, Scope));
        }
    }

    private sealed class RecordingBookmarkObserver : IBookmarkLifecycleObserver
    {
        public List<BookmarkState> Consumed { get; } = [];

        public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default)
        {
            Consumed.Add(bookmark);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FaultingResumeActivator : IActivityActivator
    {
        public List<FaultingApprovalActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            var activity = new FaultingApprovalActivity();
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class CancellingResumeActivator : IActivityActivator
    {
        public List<CancellingApprovalActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            var activity = new CancellingApprovalActivity();
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class ApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IDisposable
    {
        public ApprovalState? ObservedState { get; private set; }
        public ApprovalTrigger? ObservedTrigger { get; private set; }
        public string? ObservedAttemptId { get; private set; }
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context)
        {
            ObservedState = context.State;
            ObservedTrigger = context.Trigger;
            ObservedAttemptId = context.AttemptId;
            return ValueTask.FromResult(Complete(ActivityUnit.Value));
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class ResuspendingApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            ValueTask.FromResult(Suspend(
                new ApprovalState("request-43"),
                [new ActivityTriggerRegistration<ApprovalTrigger>(
                    "resume-target:delivery",
                    "delivery-status",
                    "sha256:delivery-status:order-124",
                    ActivityTriggerDeduplicationMode.IdempotencyKey)]));

        public void Dispose() => Disposed = true;
    }

    private sealed class FaultingApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            ValueTask.FromResult(Fault(new ActivityFault("approval.rejected", "Approval was rejected")));

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            ValueTask.FromException<ActivityTransition<ActivityUnit, ApprovalState>>(new InvalidOperationException("Resume failed."));
    }

    private sealed class UnsolicitedCancellationApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            ValueTask.FromException<ActivityTransition<ActivityUnit, ApprovalState>>(
                new OperationCanceledException("Activity-local timeout without host cancellation."));

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingDisposeApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IDisposable
    {
        public int ResumeCalls { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context)
        {
            ResumeCalls++;
            return ValueTask.FromResult(Complete(ActivityUnit.Value));
        }

        public void Dispose() => throw new InvalidOperationException("Activity disposal failed.");
    }

    private sealed class RecordingAsyncDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            ValueTask.FromResult(Cancel("Approval request withdrawn"));

        public void Dispose() => Disposed = true;
    }

    private sealed record ApprovalState(string RequestId);
    private sealed record ApprovalTrigger(bool Approved);
}
