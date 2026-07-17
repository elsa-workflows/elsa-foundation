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
    public async Task HandleAsync_RedeliveredAfterWorkerCancellation_ClaimsFreshAttemptWithoutAcceptingTriggerTwice()
    {
        var executable = NewTypedExecutable(currentLiteral: "original");
        var contract = executable.RootActivity.ActivityContract!;
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new CancelThenCompleteResumeActivator(_activityStateStore);
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);
        var handler = NewHandler(provider);
        var workItem = NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(workItem).AsTask());

        var interrupted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal("actexec-1:attempt:2", interrupted!.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
        Assert.Equal("resume-work", interrupted.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId]);
        Assert.Equal(ActivityTriggerDeliveryStatus.Received, Assert.Single(interrupted.TriggerDeliveries!).Status);

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
                Assert.Null(attempts[2].EndedAt);
                Assert.Equal("actexec-1:attempt:3", second.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
                Assert.Single(second.TriggerDeliveries!);
            });
        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Completed, completed!.Status);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, Assert.Single(completed.TriggerDeliveries!).Status);
        Assert.Equal(3, completed.Attempts!.Count);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
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
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed).Commit;
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
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(contract, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

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
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, Assert.Single(state.TriggerDeliveries!).Status);
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

    private sealed class CancelThenCompleteResumeActivator(IActivityExecutionStateStore activityStateStore) : IActivityActivator
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
            IActivity activity = Requests.Count == 1 ? new InterruptedApprovalActivity() : new ApprovalActivity();
            return new ActivityActivationLease(activity);
        }
    }

    private sealed class InterruptedApprovalActivity : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            ValueTask.FromException<ActivityTransition<ActivityUnit, ApprovalState>>(
                new OperationCanceledException("Simulated worker cancellation after typed resume activation."));
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
