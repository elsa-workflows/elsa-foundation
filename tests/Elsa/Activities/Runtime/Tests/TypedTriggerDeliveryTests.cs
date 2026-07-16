using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_TypedSuspension_CommitsAttemptPrivateStateRegistrationsAndBookmarkIntentsAtomically(bool includeInspection)
    {
        var transition = new PayloadOnlySuspensionTransition(
            Json("""{"step":3}"""),
            new ValueTypeDescriptor("test/approval-state", schemaVersion: 7),
            [
                new ActivityTriggerRegistration<ApprovalTrigger>("approved", "approval", "approval:42"),
                new ActivityTriggerRegistration<ApprovalTrigger>(
                    "rejected",
                    "approval",
                    "rejection:42",
                    ActivityTriggerDeduplicationMode.IdempotencyKey)
            ]);
        var activator = new SuspendingTypedActivator(transition);
        await _executableStore.SaveAsync(NewTypedStatefulExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(new RecordingActivity()),
            includeInspection: includeInspection,
            activityActivator: activator);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivitySuspended, write.Commit.Checkpoint.Name);
        var stateChange = Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        var state = stateChange.State;
        Assert.Equal(ActivityExecutionStatus.Suspended, state.Status);
        Assert.Equal("TriggerWaiting", state.SubStatus);
        Assert.Null(state.Completion);

        var attempt = Assert.Single(state.Attempts!);
        Assert.Equal(_now, attempt.EndedAt);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, attempt.TransitionKind);

        Assert.NotNull(state.PrivateState);
        Assert.Equal(7, state.PrivateState.StateVersion);
        Assert.Equal("test/approval-state", state.PrivateState.Value.Type.Alias);
        Assert.Equal(3, state.PrivateState.Value.InlineValue!.Value.GetProperty("step").GetInt32());
        Assert.Equal(attempt.AttemptId, state.PrivateState.ProducedByAttemptId);
        Assert.Equal(_now, state.PrivateState.CommittedAt);

        var registrations = state.TriggerRegistrations!.ToArray();
        Assert.Equal(2, registrations.Length);
        Assert.Equal("actexec-1:attempt:1:trigger:1", registrations[0].RegistrationId);
        Assert.Equal("approved", registrations[0].ResumeTargetKey);
        Assert.Equal("Elsa.Activities.Runtime.Tests.WorkflowInvokeActivitySchedulerWorkHandlerTests+ApprovalTrigger", registrations[0].PayloadType.Alias);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTriggerDeduplicationPolicy.Once, registrations[0].DeduplicationPolicy);
        Assert.Equal("actexec-1:attempt:1:trigger:2", registrations[1].RegistrationId);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTriggerDeduplicationPolicy.IdempotencyKey, registrations[1].DeduplicationPolicy);

        var bookmarkItems = write.Commit.PostCommitIntents
            .Select(intent => intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!)
            .ToArray();
        Assert.Equal(2, bookmarkItems.Length);
        Assert.Equal(2, write.Commit.StateChanges.PostCommitOutbox.Count);
        Assert.All(bookmarkItems, item => Assert.Equal(WorkflowExecutionCommandKind.CreateBookmark, item.CommandKind));
        Assert.Equal("actexec-1:attempt:1:trigger:1", bookmarkItems[0].Payload!.Value.Deserialize<RuntimeCreateBookmarkCommandPayload>()!.BookmarkId);
        Assert.Equal("actexec-1:attempt:1:trigger:2", bookmarkItems[1].Payload!.Value.Deserialize<RuntimeCreateBookmarkCommandPayload>()!.BookmarkId);
        Assert.Empty(state.BookmarkIds);

        var persisted = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(state, persisted);
        Assert.True(activator.Activity.Disposed);
        Assert.Equal(0, transition.StateReadCount);
    }

    private sealed class SuspendingTypedActivator(ActivityTransition transition) : IActivityActivator
    {
        public PayloadOnlySuspendingActivity Activity { get; } = new(transition);

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(Activity));
    }

    private sealed class PayloadOnlySuspendingActivity(ActivityTransition transition) : ActivityBase, IDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition> ExecuteTransitionAsync(IActivityExecutionContext context) =>
            ValueTask.FromResult(transition);

        public void Dispose() => Disposed = true;
    }

    private sealed record PayloadOnlySuspensionTransition : ActivityTransition, IStatefulActivitySuspensionTransition
    {
        public PayloadOnlySuspensionTransition(
            JsonElement statePayload,
            ValueTypeDescriptor stateValueType,
            IReadOnlyCollection<IActivityTriggerRegistration> registrations)
        {
            StatePayload = statePayload.Clone();
            StateValueType = stateValueType;
            Registrations = registrations.ToArray();
            Triggers = registrations
                .Select(registration => new ActivityTriggerExpectation(
                    registration.ResumeTargetKey,
                    registration.StimulusType,
                    registration.PayloadType))
                .ToArray();
        }

        public override Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind Kind =>
            Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind.Suspend;

        public int StateReadCount { get; private set; }
        public object State
        {
            get
            {
                StateReadCount++;
                throw new InvalidOperationException("The runtime must persist StatePayload without reserializing State.");
            }
        }

        public Type StateType => typeof(object);
        public IReadOnlyCollection<ActivityTriggerExpectation> Triggers { get; }
        public ValueTypeDescriptor StateValueType { get; }
        public JsonElement StatePayload { get; }
        public Type TriggerType => typeof(ApprovalTrigger);
        public IReadOnlyCollection<IActivityTriggerRegistration> Registrations { get; }
    }

    private sealed record ApprovalTrigger(string Decision);

    private static WorkflowExecutable NewTypedStatefulExecutable()
    {
        var executable = NewTypedExecutable();
        return new WorkflowExecutable(
            executable.Identity,
            executable.RootActivity,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["approved"] = new("approved", "node-start", "approved", new Dictionary<string, string>()),
                ["rejected"] = new("rejected", "node-start", "rejected", new Dictionary<string, string>())
            },
            executable.CreatedAt,
            executable.CompatibilityMetadata);
    }
}
