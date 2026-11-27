using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The invoke path hands an ordinary activity the logical operation keys resolved from its committed state: an
/// assigned key first, then the first-attempt identity, then the invocation id.
/// </summary>
public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Fact]
    public async Task HandleAsync_AssignedOperationKeys_ReachTheActivityContext()
    {
        var activity = new OperationKeyRecordingActivity();
        await _executableStore.SaveAsync(NewTypedExecutable());
        var state = NewTypedRunningState();
        await _activityStateStore.SaveAsync(state with
        {
            Provenance = state.Provenance with
            {
                Metadata = new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.OperationKey] = "scope-1/step/0/compensate",
                    [RuntimeMetadataKeys.TargetOperationKey] = "scope-1/step/0"
                }
            }
        });
        await using var provider = NewProvider(new FixedActivityActivator(activity));

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var observed = Assert.Single(activity.Observed);
        Assert.Equal("scope-1/step/0/compensate", observed.OperationKey);
        Assert.Equal("scope-1/step/0", observed.TargetOperationKey);
    }

    [Fact]
    public async Task HandleAsync_WithoutAssignedKey_UsesTheFirstAttemptIdentity()
    {
        var activity = new OperationKeyRecordingActivity();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState() with
        {
            Attempt = new ActivityExecutionAttemptLineage(2, "actexec-first", "actexec-previous")
        });
        await using var provider = NewProvider(new FixedActivityActivator(activity));

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var observed = Assert.Single(activity.Observed);
        Assert.Equal("actexec-first", observed.OperationKey);
        Assert.Null(observed.TargetOperationKey);
    }

    [Fact]
    public async Task HandleAsync_WithoutAssignedKeyOrLineage_UsesTheInvocationId()
    {
        var activity = new OperationKeyRecordingActivity();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(new FixedActivityActivator(activity));

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var observed = Assert.Single(activity.Observed);
        Assert.Equal("actexec-1", observed.OperationKey);
        Assert.Null(observed.TargetOperationKey);
    }

    [Fact]
    public async Task HandleAsync_CrashReattempt_KeepsTheOperationKey()
    {
        using var cancellation = new CancellationTokenSource();
        var activity = new OperationKeyRecordingActivity(cancelFirstAttempt: cancellation);
        await _executableStore.SaveAsync(NewTypedExecutable());
        var state = NewTypedRunningState();
        await _activityStateStore.SaveAsync(state with
        {
            Provenance = state.Provenance with
            {
                Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.OperationKey] = "assigned-key" }
            }
        });
        await using var provider = NewProvider(new FixedActivityActivator(activity));
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(workItem, cancellation.Token).AsTask());
        await handler.HandleAsync(workItem);

        Assert.Collection(
            activity.Observed,
            first =>
            {
                Assert.Equal("actexec-1:attempt:1", first.AttemptId);
                Assert.Equal("assigned-key", first.OperationKey);
            },
            second =>
            {
                Assert.Equal("actexec-1:attempt:2", second.AttemptId);
                Assert.Equal("assigned-key", second.OperationKey);
            });
        Assert.Equal(ActivityExecutionStatus.Completed, (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Status);
    }

    private sealed record ObservedOperationKeys(string AttemptId, string OperationKey, string? TargetOperationKey);

    /// <summary>
    /// Records the keys of every attempt it runs. With <c>cancelFirstAttempt</c> it stops the first attempt the way a
    /// host shutdown does, so the redelivery runs a fresh attempt of the same invocation.
    /// </summary>
    private sealed class OperationKeyRecordingActivity(CancellationTokenSource? cancelFirstAttempt = null) : Activity<TypedResult>
    {
        public List<ObservedOperationKeys> Observed { get; } = [];

        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            Observed.Add(new ObservedOperationKeys(context.AttemptId, context.OperationKey, context.TargetOperationKey));
            if (cancelFirstAttempt is not null && Observed.Count == 1)
            {
                cancelFirstAttempt.Cancel();
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(0)));
        }
    }
}
