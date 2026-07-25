using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowInvokeActivitySchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: null,
            durableValueStateStore: _durableValueStateStore,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);
    }

    [Fact]
    public async Task HandleAsync_TypedNode_UsesActivationLeaseAndCommitsReturnedResultAtomically()
    {
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal(1, activator.ActivateCalls);
        Assert.True(activator.Activity!.Disposed);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal("hello", state!.InputSnapshot!.Values["text"].InlineValue!.Value.GetString());
        Assert.Equal("Done", state.Completion?.OutcomeKey);
        Assert.Equal(5, state.Completion?.Result.InlineValue!.Value.GetProperty("length").GetInt32());
        Assert.NotNull(Assert.Single(state.Attempts!).EndedAt);
        var inspection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        var outputSnapshot = Assert.Single(inspection!.ValueSnapshots, snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput);
        Assert.Equal("length", outputSnapshot.Name);
        Assert.Equal("Int32", outputSnapshot.Type!.Id);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_TypedNodeWithoutInspection_CommitsCompletionAndDownstreamIntentAtomically()
    {
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: false);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted).Commit;
        var stateChange = Assert.Single(commit.StateChanges.ActivityExecutions);
        Assert.Equal(ActivityExecutionStatus.Completed, stateChange.State.Status);
        Assert.Empty(commit.StateChanges.ActivityExecutionInspections);
        Assert.Single(commit.PostCommitIntents);
        Assert.Empty(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));

        var committer = provider.GetRequiredService<RuntimeCheckpointCommitter>();
        var commitCount = _checkpointWriter.ListCommits().Count;
        var replay = await committer.CommitAsync(commit);
        Assert.Single(replay.PendingPostCommitWorkIds);
        Assert.Equal(commitCount, _checkpointWriter.ListCommits().Count);
    }

    [Fact]
    public async Task HandleAsync_SensitiveContractInputsAndOutputsAreExcludedBeforeDiagnosticCapture()
    {
        var capturePolicy = new RecordingCapturePolicy();
        var executable = NewTypedExecutable(isSensitive: true);
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedRunningState(executable));
        await using var provider = NewProvider(new RecordingTypedActivator(), includeInspection: true, capturePolicy);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Collection(
            capturePolicy.Requests.OrderBy(request => request.Subject),
            request =>
            {
                Assert.Equal(RuntimePayloadCaptureSubject.ActivityInput, request.Subject);
                Assert.True(request.IsSensitive);
            },
            request =>
            {
                Assert.Equal(RuntimePayloadCaptureSubject.ActivityOutput, request.Subject);
                Assert.True(request.IsSensitive);
            });
        var inspection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.All(inspection!.ValueSnapshots, snapshot =>
        {
            Assert.True(snapshot.IsSensitive);
            Assert.Null(snapshot.Payload);
        });
    }

    [Fact]
    public async Task HandleAsync_TypedNode_CommitsReturnedFaultAsFaultTransition()
    {
        var activator = new ReturningTypedActivator(ActivityTransition.Fault<TypedResult>(
            new ActivityFault("payment.declined", "The payment was declined", isRetryable: false)));
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Equal("payment.declined", state.Fault!.Code);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, Assert.Single(state.Attempts!).TransitionKind);
        Assert.True(activator.Activity!.Disposed);
    }

    [Fact]
    public async Task HandleAsync_TypedNode_CommitsReturnedCancellationAndSchedulesWorkflowCancellation()
    {
        var activator = new ReturningTypedActivator(ActivityTransition.Cancel<TypedResult>("Caller disconnected"));
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Cancelled, state!.Status);
        Assert.Equal("Caller disconnected", state.Metadata[RuntimeMetadataKeys.CancellationReason]);
        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCancelled).Commit;
        var cancelWork = Assert.Single(commit.PostCommitIntents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.Cancel, cancelWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_ReplayedTypedInvocation_UsesCommittedCompletionWithoutReactivation()
    {
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        var committed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        var committedCheckpointCount = _checkpointWriter.ListCommits().Count;
        await handler.HandleAsync(workItem);

        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(committed, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Equal(committedCheckpointCount, _checkpointWriter.ListCommits().Count);
        Assert.Empty(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RedeliveredAfterWorkerCancellation_ClosesAbandonedAttemptAndClaimsFreshRetryBeforeActivation()
    {
        using var cancellation = new CancellationTokenSource();
        var activator = new CancelThenCompleteActivator(_activityStateStore, cancellation);
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(workItem, cancellation.Token).AsTask());

        var interrupted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        var interruptedAttempt = Assert.Single(interrupted!.Attempts!);
        Assert.Null(interruptedAttempt.EndedAt);
        Assert.Equal(interruptedAttempt.AttemptId, interrupted.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
        Assert.Equal(
            RuntimeCheckpointNames.ActivityAttemptClaimed,
            Assert.Single(_checkpointWriter.ListCommits()).Commit.Checkpoint.Name);

        await handler.HandleAsync(workItem);

        Assert.Collection(
            activator.Requests,
            request => Assert.Equal("actexec-1:attempt:1", request.Attempt.AttemptId),
            request =>
            {
                Assert.Equal("actexec-1:attempt:2", request.Attempt.AttemptId);
                Assert.Equal(ActivityAttemptReason.Retry, request.Attempt.Reason);
                Assert.Equal("sha256:bindings", request.Inputs.BindingFingerprint);
                Assert.Equal("hello", request.Inputs.Values["text"].InlineValue!.Value.GetString());
            });
        Assert.Collection(
            activator.StatesObservedBeforeActivation,
            first =>
            {
                var attempt = Assert.Single(first.Attempts!);
                Assert.Null(attempt.EndedAt);
                Assert.Equal(attempt.AttemptId, first.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
            },
            second =>
            {
                var attempts = second.Attempts!.OrderBy(attempt => attempt.Ordinal).ToArray();
                Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, attempts[0].TransitionKind);
                Assert.NotNull(attempts[0].EndedAt);
                Assert.Null(attempts[1].EndedAt);
                Assert.Equal(attempts[1].AttemptId, second.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
            });

        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Collection(
            completed!.Attempts!.OrderBy(attempt => attempt.Ordinal),
            attempt => Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, attempt.TransitionKind),
            attempt => Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete, attempt.TransitionKind));
        Assert.Equal(
            [
                RuntimeCheckpointNames.ActivityAttemptClaimed,
                RuntimeCheckpointNames.ActivityAttemptClaimed,
                RuntimeCheckpointNames.ActivityCompleted
            ],
            _checkpointWriter.ListCommits().Select(write => write.Commit.Checkpoint.Name));
    }

    [Fact]
    public async Task HandleAsync_UnsolicitedOperationCancellation_FaultsDurablyWithoutReactivation()
    {
        var activity = new UnsolicitedCancellationActivity();
        var activator = new FixedActivityActivator(activity);
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        var faulted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted!.Status);
        Assert.Equal("ActivityFaulted", faulted.SubStatus);
        Assert.Single(faulted.IncidentIds);
        Assert.Equal(1, activator.ActivateCalls);
        Assert.True(activity.Disposed);
        Assert.DoesNotContain(
            _checkpointWriter.ListCommits(),
            record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted);
    }

    [Fact]
    public async Task HandleAsync_ActivityDisposalFailure_ReleasesScopeAndFaultsWithoutDoubleExecution()
    {
        var activator = new ThrowingDisposeActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        var faulted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted!.Status);
        Assert.Equal("ActivityDisposalFailed", faulted.SubStatus);
        Assert.Single(faulted.IncidentIds);
        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(1, activator.Activity.ExecuteCalls);
        Assert.True(activator.Scope.Disposed);
        Assert.DoesNotContain(
            _checkpointWriter.ListCommits(),
            record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted);
    }

    [Fact]
    public async Task HandleAsync_ScopeDisposalFailure_FaultsDurablyWithoutDoubleExecution()
    {
        var activator = new ThrowingScopeDisposeActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        var faulted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted!.Status);
        Assert.Equal("ActivityDisposalFailed", faulted.SubStatus);
        Assert.Single(faulted.IncidentIds);
        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(1, activator.Activity.ExecuteCalls);
        Assert.True(activator.Scope.DisposeAttempted);
        Assert.DoesNotContain(
            _checkpointWriter.ListCommits(),
            record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted);
    }

    [Fact]
    public async Task HandleAsync_StructuralDeferDisposalFailure_DiscardsStagedStateAndFaultsOpenAttempt()
    {
        var activity = new ThrowingDisposeStatefulDeferringStructuralActivity();
        var activator = new FixedActivityActivator(activity);
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        await handler.HandleAsync(workItem);

        var faulted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted!.Status);
        Assert.Equal("ActivityDisposalFailed", faulted.SubStatus);
        Assert.Null(faulted.PrivateState);
        Assert.DoesNotContain(RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId, faulted.Metadata.Keys);
        var attempt = Assert.Single(faulted.Attempts!);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, attempt.TransitionKind);
        Assert.NotNull(attempt.EndedAt);
        Assert.NotNull(attempt.IncidentId);
        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(1, activity.ExecuteCalls);
        Assert.DoesNotContain(
            _checkpointWriter.ListCommits(),
            record => record.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityInspectionCaptured);
        Assert.Empty(_checkpointWriter.ListCommits().SelectMany(record => record.Commit.PostCommitIntents));
    }

    [Fact]
    public async Task HandleAsync_InitialStructuralDeferWithoutChild_FaultsInsteadOfLeavingInvocationRunning()
    {
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(new FixedActivityActivator(new EmptyDeferringStructuralActivity()), includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Contains("cannot defer without scheduling", state.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_OrdinaryActivityReceivesAuthorContextWithoutSchedulingSeam()
    {
        var activity = new OrdinaryContextActivity();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(new FixedActivityActivator(activity), includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Completed, state!.Status);
        Assert.False(activity.ExposesRuntimeSchedulingSeam);
    }

    [Fact]
    public async Task HandleAsync_TerminalStructuralCompletion_DiscardsPrivateState()
    {
        var executable = NewStructuralExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewStructuralRunningState(executable));
        await using var provider = NewProvider(new FixedActivityActivator(new CompletingWithStateStructuralActivity()), includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Completed, state!.Status);
        Assert.Null(state.PrivateState);
    }

    [Fact]
    public async Task HandleAsync_TerminalStructuralDecisionWithScheduledChild_FaultsAsProtocolViolation()
    {
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(new FixedActivityActivator(new CompletingAndSchedulingStructuralActivity()), includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Contains("terminal structural decision", state.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_RedeliveredAfterCommittedStructuralDefer_DoesNotReactivateOrDuplicateChildIntent()
    {
        var activator = new FixedActivityActivator(new ChildSchedulingDeferringStructuralActivity());
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        var deferred = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        var checkpointCount = _checkpointWriter.ListCommits().Count;
        var childIntentCount = _checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents).Count();

        await handler.HandleAsync(workItem);

        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(deferred, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Equal(checkpointCount, _checkpointWriter.ListCommits().Count);
        Assert.Equal(1, childIntentCount);
        Assert.Equal(childIntentCount, _checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents).Count());
        var attempt = Assert.Single(deferred!.Attempts!);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, attempt.TransitionKind);
        Assert.Equal("invoke-work", deferred.Metadata[RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId]);
    }

    [Fact]
    public async Task HandleAsync_CheckpointParticipantEntryChangesCommitWithChildScheduling()
    {
        var activity = new CheckpointParticipantStructuralActivity();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(new FixedActivityActivator(activity), includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal("hello", Assert.IsType<JsonElement>(activity.EffectiveInputs["text"]).GetString());
        var childSchedulingCommit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Metadata[RuntimeMetadataKeys.CheckpointReason] == "ChildActivityScheduling").Commit;
        var change = Assert.Single(childSchedulingCommit.StateChanges.DurableValues);
        Assert.Equal("checkpoint:entry", change.StateId);
        Assert.NotNull(await _durableValueStateStore.FindAsync("wfexec-1", "checkpoint:entry"));
    }

    [Fact]
    public async Task HandleAsync_CheckpointParticipantEntryAndCompletionChangesCommitAtomically()
    {
        var activity = new CompletingCheckpointParticipantActivity();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(new FixedActivityActivator(activity), includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var completionCommit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted).Commit;
        Assert.Equal(
            ["checkpoint:completion", "checkpoint:entry"],
            completionCommit.StateChanges.DurableValues.Select(change => change.StateId).Order(StringComparer.Ordinal));
        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(9, completed!.Completion!.Result.InlineValue!.Value.GetProperty("length").GetInt32());
    }

    [Fact]
    public async Task HandleAsync_DelayedReplayAfterNewerStructuralActivation_DoesNotReactivateOrClaimAnotherAttempt()
    {
        var activator = new FixedActivityActivator(new ChildSchedulingDeferringStructuralActivity());
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var oldWorkItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(oldWorkItem);
        var afterOldWork = await _activityStateStore.FindAsync("wfexec-1", "actexec-1")
                           ?? throw new InvalidOperationException("Deferred activity state was not persisted.");
        var metadata = afterOldWork.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId] = "invoke-work";
        var afterNewerWork = afterOldWork with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
        await _activityStateStore.SaveAsync(afterNewerWork);
        var commitCount = _checkpointWriter.ListCommits().Count;

        await handler.HandleAsync(oldWorkItem);

        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(afterNewerWork, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Equal(commitCount, _checkpointWriter.ListCommits().Count);
        Assert.Single(afterNewerWork.Attempts!);
    }

    [Fact]
    public async Task ParentCallback_DelayedReplayAfterNewerCallback_DoesNotReactivateOrClaimAnotherAttempt()
    {
        var activator = new FixedActivityActivator(new DeferringChildCompletionActivity());
        var executable = NewCallbackExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewCallbackParentState(executable));
        await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = new WorkflowParentActivityCompletionSchedulerWorkHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));
        var newerWorkItem = NewParentCallbackWorkItem("callback-work-newer", "command-newer");
        var oldWorkItem = NewParentCallbackWorkItem("callback-work-old", "command-old");

        await handler.HandleAsync(newerWorkItem);
        var afterNewerWork = await _activityStateStore.FindAsync("wfexec-1", "actexec-1")
                             ?? throw new InvalidOperationException("Deferred parent state was not persisted.");
        var completedChild = await _activityStateStore.FindAsync("wfexec-1", "actexec-child");
        var commitCount = _checkpointWriter.ListCommits().Count;

        await handler.HandleAsync(oldWorkItem);

        Assert.Equal("callback-work-newer", completedChild!.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(afterNewerWork, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Equal(commitCount, _checkpointWriter.ListCommits().Count);
        Assert.Equal(2, afterNewerWork.Attempts!.Count);
    }

    [Fact]
    public async Task ParentCallback_LongRunningContainerRetainsBoundedAttemptDiagnosticsAndMonotonicOrdinal()
    {
        var activator = new FixedActivityActivator(new DeferringChildCompletionActivity());
        var executable = NewCallbackExecutable();
        var parentState = NewCallbackParentState(executable) with
        {
            Attempts = Enumerable.Range(1, 100)
                .Select(ordinal => new ActivityAttempt(
                    $"actexec-1:attempt:{ordinal}",
                    "actexec-1",
                    ordinal,
                    ordinal == 1 ? ActivityAttemptReason.Initial : ActivityAttemptReason.Resume,
                    _now.AddSeconds(ordinal),
                    _now.AddSeconds(ordinal + 1),
                    transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend))
                .ToArray()
        };
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(parentState);
        await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = new WorkflowParentActivityCompletionSchedulerWorkHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewParentCallbackWorkItem("callback-work-101", "command-101"));

        var retained = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(32, retained!.Attempts!.Count);
        Assert.Equal(1, retained.Attempts.Min(attempt => attempt.Ordinal));
        Assert.Equal(101, retained.Attempts.Max(attempt => attempt.Ordinal));
        Assert.Equal("101", retained.Metadata[RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark]);
        Assert.DoesNotContain("runtime.activityActivationCompletedWorkItemIds", retained.Metadata.Keys);
    }

    [Fact]
    public async Task ParentCallback_WithoutInspection_CommitsCompletionAndDownstreamIntentAtomically()
    {
        var activator = new FixedActivityActivator(new CompletingChildCompletionActivity());
        var executable = NewCallbackExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewCallbackParentState(executable));
        await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        await using var provider = NewProvider(activator, includeInspection: false);
        var handler = new WorkflowParentActivityCompletionSchedulerWorkHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewParentCallbackWorkItem("callback-work-newer", "command-newer"));

        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted).Commit;
        var stateChange = Assert.Single(commit.StateChanges.ActivityExecutions);
        Assert.Equal(ActivityExecutionStatus.Completed, stateChange.State.Status);
        Assert.Empty(commit.StateChanges.ActivityExecutionInspections);
        Assert.Single(commit.PostCommitIntents);
        Assert.Empty(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));

        var committer = provider.GetRequiredService<RuntimeCheckpointCommitter>();
        var commitCount = _checkpointWriter.ListCommits().Count;
        var replay = await committer.CommitAsync(commit);
        Assert.Single(replay.PendingPostCommitWorkIds);
        Assert.Equal(commitCount, _checkpointWriter.ListCommits().Count);
    }

    [Fact]
    public async Task ParentCallback_CheckpointParticipantCompletionChangesCommitWithParentCompletion()
    {
        var executable = NewCallbackExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewCallbackParentState(executable));
        await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        await using var provider = NewProvider(
            new FixedActivityActivator(new CompletionCheckpointParticipantActivity()),
            includeInspection: true);
        var handler = new WorkflowParentActivityCompletionSchedulerWorkHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewParentCallbackWorkItem("callback-work", "callback-command"));

        var completionCommit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted).Commit;
        var change = Assert.Single(completionCommit.StateChanges.DurableValues);
        Assert.Equal("checkpoint:completion", change.StateId);
        Assert.NotNull(await _durableValueStateStore.FindAsync("wfexec-1", "checkpoint:completion"));
    }

    private WorkflowInvokeActivitySchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private async Task<RuntimeCompleteActivityCommandPayload> AssertCompletionWorkAsync()
    {
        var intents = _checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents).ToArray();
        var work = intents.Length == 0
            ? Assert.Single(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")))
            : Assert.Single(intents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, work.CommandKind);
        return work.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
    }

    private ServiceProvider NewProvider(
        IActivityActivator activityActivator,
        bool includeInspection = false,
        IRuntimePayloadCapturePolicy? capturePolicy = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityActivator>(activityActivator);
        services.AddSingleton<IWorkflowExecutionStateStore>(_ =>
            CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore()));
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => _activityStateStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_ => _schedulerWorkQueue);
        services.AddSingleton<IDurableValueStateStore>(_ => _durableValueStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_ => _checkpointWriter);
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton(sp => new ActivityFaultIncidentRecorder(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IRuntimeActivityExecutionInspectionAccumulator>()));
        services.AddSingleton<ActivityCompletionProjector>();
        services.AddSingleton<IRuntimeDurableValueStorageDriver, JsonRuntimeDurableValueStorageDriver>();
        services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry, RuntimeDurableValueStorageDriverRegistry>();
        services.AddSingleton<RuntimeOutputCaptureProjector>();
        services.AddSingleton<ActivityInputHydrator>();
        services.AddSingleton<IRuntimeExecutionIdGenerator, ShortRuntimeExecutionIdGenerator>();
        if (includeInspection)
        {
            services.AddSingleton<IActivityExecutionInspectionStore>(_ => _inspectionStore);
            services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
            services.AddSingleton<IRuntimePayloadCapturePolicy>(capturePolicy ?? new DefaultRuntimePayloadCapturePolicy());
        }
        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewInvokeWorkItem(WorkflowExecutableIdentity? pinnedExecutable = null) =>
        new(
            "invoke-work",
            "wfexec-1",
            "command-1",
            WorkflowExecutionCommandKind.InvokeActivity,
            "envelope-1",
            "wfexec-1:invoke:actexec-1",
            _now,
            _now,
            30,
            JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(), "node-start", "actexec-1", RuntimeInvokeActivityCommandPayload.StartedActivityReason)),
            new Dictionary<string, string> { ["source"] = "test" },
            new Dictionary<string, string> { ["transport"] = "in-process" });

    private RuntimeSchedulerWorkItem NewParentCallbackWorkItem(string workItemId, string commandId) =>
        new(
            workItemId,
            "wfexec-1",
            commandId,
            WorkflowExecutionCommandKind.CompleteActivity,
            "envelope-1",
            $"wfexec-1:callback:{workItemId}",
            _now,
            _now,
            31,
            JsonSerializer.SerializeToElement(new RuntimeCompleteActivityCommandPayload(
                NewIdentity(),
                "node-parent",
                "actexec-1",
                parentActivityExecutionId: null,
                branchId: null,
                outcomeNames: [ActivityOutcomes.Done],
                RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
                SchedulerCompletionKind.ParentCompletionEvaluation,
                completedChildActivityExecutionId: "actexec-child")),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

    private ActivityExecutionState NewTypedRunningState(WorkflowExecutable? executable = null)
    {
        executable ??= NewTypedExecutable();
        var type = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        return NewRunningState(
            executable,
            new Dictionary<string, ValueEnvelope>
            {
                ["text"] = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline)
            });
    }

    private ActivityExecutionState NewStructuralRunningState(WorkflowExecutable executable) =>
        NewRunningState(executable, new Dictionary<string, ValueEnvelope>());

    private ActivityExecutionState NewRunningState(
        WorkflowExecutable executable,
        IReadOnlyDictionary<string, ValueEnvelope> values)
    {
        var node = executable.RootActivity;
        var contract = node.ActivityContract!;
        var snapshot = new ActivityInputSnapshot(
            "actexec-1",
            contract.SchemaFingerprint,
            "sha256:bindings",
            values,
            _now);
        return new ActivityExecutionState(
            new ActivityExecution("actexec-1", "wfexec-1", node.ExecutableNodeId, node.AuthoredActivityId, contract.ActivityTypeKey, contract.ContractVersion),
            ActivityExecutionStatus.Running,
            null,
            _now,
            _now,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>())
        {
            ContractIdentity = new ActivityInvocationContractIdentity(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
            InputSnapshot = snapshot,
            Attempts = [new ActivityAttempt("actexec-1:attempt:1", "actexec-1", 1, ActivityAttemptReason.Initial, _now)]
        };
    }

    private ActivityExecutionState NewCallbackParentState(WorkflowExecutable executable)
    {
        var contract = executable.RootActivity.ActivityContract!;
        return new ActivityExecutionState(
            new ActivityExecution("actexec-1", "wfexec-1", "node-parent", "authored-node-parent", "test/callback-parent", "1.0.0"),
            ActivityExecutionStatus.Running,
            null,
            _now,
            _now,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>())
        {
            ContractIdentity = new ActivityInvocationContractIdentity(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
            InputSnapshot = new ActivityInputSnapshot("actexec-1", contract.SchemaFingerprint, "sha256:callback-bindings", new Dictionary<string, ValueEnvelope>(), _now),
            Attempts =
            [
                new ActivityAttempt(
                    "actexec-1:attempt:1",
                    "actexec-1",
                    1,
                    ActivityAttemptReason.Initial,
                    _now,
                    _now,
                    transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend)
            ]
        };
    }

    private ActivityExecutionState NewCompletedCallbackChildState() =>
        new(
            new ActivityExecution("actexec-child", "wfexec-1", "node-child", "authored-node-child", "test/child", "1.0.0"),
            ActivityExecutionStatus.Completed,
            null,
            _now,
            _now,
            _now,
            "actexec-1",
            "actexec-1",
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

    private static WorkflowExecutable NewCallbackExecutable()
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "callback-parent" });
        var contract = new ActivityContract(
            "test/callback-parent",
            "1.0.0",
            "test/callback-parent",
            descriptor,
            [],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement("test/callback-parent", "test/callback-parent"));
        var child = new ExecutableNode(
            "node-child",
            "authored-node-child",
            "test/child",
            "1.0.0",
            "test/child",
            JsonSerializer.SerializeToElement(new { type = "child" }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        var parent = new ExecutableNode(
            "node-parent",
            "authored-node-parent",
            "test/callback-parent",
            "1.0.0",
            "test/callback-parent",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot("Test.Child", [child])],
            activityContract: contract);
        return new WorkflowExecutable(NewIdentity(), parent, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewTypedExecutable(string literalText = "hello", bool isSensitive = false)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "typed" });
        var contract = new ActivityContract(
            "test/typed",
            "1.0.0",
            "typed",
            descriptor,
            [new ActivityInputContract("text", "Text", new Elsa.Primitives.Models.ValueTypeDescriptor("String"), true, false, false, null, ActivityValuePolicy.Default with { IsSensitive = isSensitive })],
            new ActivityResultContract(
                new Elsa.Primitives.Models.ValueTypeDescriptor("test/typed-result"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("length", "length", new Elsa.Primitives.Models.ValueTypeDescriptor("Int32"), true, ActivityValuePolicy.Default with { IsSensitive = isSensitive })]),
            ["Done"],
            new ActivityActivationRequirement("typed", "test/typed"));
        var type = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var input = new RuntimeInputBinding(
            "text", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(literalText), ValueProtectionPolicy.InstanceInline));
        var node = new ExecutableNode(
            "node-start",
            "authored-node-start",
            "test/typed",
            "1.0.0",
            "typed",
            descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["text"] = input },
            new Dictionary<string, string>(),
            activityContract: contract);
        return new WorkflowExecutable(NewIdentity(), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewStructuralExecutable()
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "structural" });
        var contract = new ActivityContract(
            "test/structural",
            "1.0.0",
            "structural",
            descriptor,
            [],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement("structural", "test/structural"));
        var node = new ExecutableNode(
            "node-start",
            "authored-node-start",
            "test/structural",
            "1.0.0",
            "structural",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract);
        return new WorkflowExecutable(NewIdentity(), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingTypedActivator : IActivityActivator
    {
        public int ActivateCalls { get; private set; }
        public TypedCompletingActivity? Activity { get; private set; }
        public List<ActivityActivationRequest> Requests { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            Requests.Add(request);
            Activity = new TypedCompletingActivity(request.Inputs.Values["text"].InlineValue!.Value.GetString()!);
            return ValueTask.FromResult(new ActivityActivationLease(Activity));
        }
    }

    private sealed class CancelThenCompleteActivator(
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
            IActivity activity = Requests.Count == 1
                ? new CancellingTypedActivity(cancellation)
                : new TypedCompletingActivity(request.Inputs.Values["text"].InlineValue!.Value.GetString()!);
            return new ActivityActivationLease(activity);
        }
    }

    private sealed class CancellingTypedActivity(CancellationTokenSource cancellation) : Activity<TypedResult>
    {
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            cancellation.Cancel();
            context.CancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The cancelled activity should not continue.");
        }
    }

    private sealed class TypedCompletingActivity(string text) : Activity<TypedResult>, IDisposable
    {
        public bool Disposed { get; private set; }
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(text.Length)));
        public void Dispose() => Disposed = true;
    }

    private sealed class UnsolicitedCancellationActivity : Activity<TypedResult>, IDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromException<ActivityTransition<TypedResult>>(
                new OperationCanceledException("Activity-local timeout without host cancellation."));

        public void Dispose() => Disposed = true;
    }

    private sealed class ReturningTypedActivator(ActivityTransition<TypedResult> transition) : IActivityActivator
    {
        public ReturningTypedActivity? Activity { get; private set; }
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            Activity = new ReturningTypedActivity(transition);
            return ValueTask.FromResult(new ActivityActivationLease(Activity));
        }
    }

    private sealed class ReturningTypedActivity(ActivityTransition<TypedResult> transition) : Activity<TypedResult>, IDisposable
    {
        public bool Disposed { get; private set; }
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) => ValueTask.FromResult(transition);
        public void Dispose() => Disposed = true;
    }

    private sealed class FixedActivityActivator(IActivity activity) : IActivityActivator
    {
        public int ActivateCalls { get; private set; }

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class ThrowingDisposeActivator : IActivityActivator
    {
        public int ActivateCalls { get; private set; }
        public ThrowingDisposeActivity Activity { get; } = new();
        public RecordingAsyncDisposable Scope { get; } = new();

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            return ValueTask.FromResult(new ActivityActivationLease(Activity, Scope));
        }
    }

    private sealed class ThrowingScopeDisposeActivator : IActivityActivator
    {
        public int ActivateCalls { get; private set; }
        public CountingActivity Activity { get; } = new();
        public ThrowingAsyncDisposable Scope { get; } = new();

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            return ValueTask.FromResult(new ActivityActivationLease(Activity, Scope));
        }
    }

    private sealed class ThrowingDisposeActivity : Activity<TypedResult>, IDisposable
    {
        public int ExecuteCalls { get; private set; }

        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            ExecuteCalls++;
            return ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(5)));
        }

        public void Dispose() => throw new InvalidOperationException("Activity disposal failed.");
    }

    private sealed class CountingActivity : Activity<TypedResult>
    {
        public int ExecuteCalls { get; private set; }

        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            ExecuteCalls++;
            return ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(5)));
        }
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

    private sealed class ThrowingAsyncDisposable : IAsyncDisposable
    {
        public bool DisposeAttempted { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            return ValueTask.FromException(new InvalidOperationException("Scope disposal failed."));
        }
    }

    private sealed class EmptyDeferringStructuralActivity : StructuralActivity, IRuntimeStructuralActivity
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    private sealed class ChildSchedulingDeferringStructuralActivity : StructuralActivity, IRuntimeStructuralActivity
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.ScheduleChildActivity("child-node");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }

    private sealed class CheckpointParticipantStructuralActivity :
        StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityCheckpointParticipant
    {
        public IReadOnlyDictionary<string, object?> EffectiveInputs { get; private set; } =
            new Dictionary<string, object?>();

        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.ScheduleChildActivity("child-node");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyDictionary<string, object?> effectiveInputs,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default)
        {
            EffectiveInputs = effectiveInputs;
            return ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>(
                [CheckpointChange("entry", context.WorkflowExecutionId, capturedAt)]);
        }

        public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyCollection<DurableValueState> persistedValues,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The deferred entry path must not prepare completion.");
    }

    private sealed class CompletionCheckpointParticipantActivity :
        StructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityCheckpointParticipant
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyDictionary<string, object?> effectiveInputs,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The parent callback path must not prepare entry.");

        public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyCollection<DurableValueState> persistedValues,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeActivityCompletionCheckpointPreparation(
                [CheckpointChange("completion", context.WorkflowExecutionId, capturedAt)],
                ActivityTransition.Complete(ActivityUnit.Value)));
    }

    private sealed class CompletingCheckpointParticipantActivity :
        Activity<TypedResult>,
        IRuntimeActivityCheckpointParticipant
    {
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(5)));

        public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyDictionary<string, object?> effectiveInputs,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>(
                [CheckpointChange("entry", context.WorkflowExecutionId, capturedAt)]);

        public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
            IRuntimeActivityExecutionContext context,
            IReadOnlyCollection<DurableValueState> persistedValues,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default)
        {
            Assert.Contains(persistedValues, value => value.DurableValueId == "checkpoint:entry");
            return ValueTask.FromResult(new RuntimeActivityCompletionCheckpointPreparation(
                [CheckpointChange("completion", context.WorkflowExecutionId, capturedAt)],
                ActivityTransition.Complete(new TypedResult(9))));
        }
    }

    private static RuntimeStateChange<DurableValueState> CheckpointChange(
        string role,
        string workflowExecutionId,
        DateTimeOffset capturedAt)
    {
        var id = $"checkpoint:{role}";
        var state = new DurableValueState(
            id,
            workflowExecutionId,
            id,
            new RuntimeValueTypeDescriptor("object", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement(role),
            externalReference: null,
            sourceActivityExecutionId: "actexec-1",
            capturedAt,
            new Dictionary<string, string>());
        return new RuntimeStateChange<DurableValueState>(
            id,
            RuntimeStateChangeOperation.Upsert,
            state,
            state.Metadata);
    }

    private sealed class ThrowingDisposeStatefulDeferringStructuralActivity : StructuralActivity, IRuntimeStructuralActivity, IDisposable
    {
        public int ExecuteCalls { get; private set; }

        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            ExecuteCalls++;
            context.ScheduleChildActivity("child-node");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer.WithState(
                ValueEnvelope.Inline(
                    new ValueTypeDescriptor("test/structural-state"),
                    JsonSerializer.SerializeToElement(new { step = 1 }),
                    ValueProtectionPolicy.InstanceInline)));
        }

        public void Dispose() => throw new InvalidOperationException("Structural activity disposal failed.");
    }

    private sealed class DeferringChildCompletionActivity : StructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    private sealed class CompletingChildCompletionActivity : StructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    private sealed class CompletingAndSchedulingStructuralActivity : StructuralActivity, IRuntimeStructuralActivity
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.ScheduleChildActivity("child-node");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    private sealed class CompletingWithStateStructuralActivity : StructuralActivity, IRuntimeStructuralActivity
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete().WithState(
                ValueEnvelope.Inline(
                    new ValueTypeDescriptor("test/structural-state"),
                    JsonSerializer.SerializeToElement(new { step = 1 }),
                    ValueProtectionPolicy.InstanceInline)));
    }

    private sealed class OrdinaryContextActivity : Activity<TypedResult>
    {
        public bool ExposesRuntimeSchedulingSeam { get; private set; }

        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            ExposesRuntimeSchedulingSeam = (object)context is IRuntimeActivityExecutionContext;
            return ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(0)));
        }
    }

    private sealed record TypedResult(int Length);

    private sealed class RecordingCapturePolicy : IRuntimePayloadCapturePolicy
    {
        public List<RuntimePayloadCaptureRequest> Requests { get; } = [];

        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request)
        {
            Requests.Add(request);
            return request.IsSensitive
                ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive test payload excluded.")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Test payload captured.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
