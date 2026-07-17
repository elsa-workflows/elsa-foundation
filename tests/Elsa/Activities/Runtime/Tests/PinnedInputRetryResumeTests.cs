using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Fact]
    public async Task HandleAsync_TypedRetry_ReusesPinnedSnapshotAndCreatesFreshAttempt()
    {
        var executable = NewTypedExecutable(literalText: "changed-after-snapshot");
        var state = NewTypedRunningState();
        var initialAttempt = Assert.Single(state.Attempts!);
        var endedInitialAttempt = new ActivityAttempt(
            initialAttempt.AttemptId,
            initialAttempt.InvocationId,
            initialAttempt.Ordinal,
            initialAttempt.Reason,
            initialAttempt.StartedAt,
            _now,
            transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault);
        state = state with { Attempts = [endedInitialAttempt] };
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(state);
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var request = Assert.Single(activator.Requests);
        Assert.Equal("hello", request.Inputs.Values["text"].InlineValue!.Value.GetString());
        Assert.Equal("actexec-1", request.Inputs.InvocationId);
        Assert.Equal("actexec-1", request.Attempt.InvocationId);
        Assert.Equal("actexec-1:attempt:2", request.Attempt.AttemptId);
        Assert.Equal(2, request.Attempt.Ordinal);
        Assert.Equal(ActivityAttemptReason.Retry, request.Attempt.Reason);

        var completed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(completed?.InputSnapshot);
        Assert.Equal("hello", completed.InputSnapshot.Values["text"].InlineValue!.Value.GetString());
        Assert.Collection(
            completed.Attempts!.OrderBy(attempt => attempt.Ordinal),
            attempt =>
            {
                Assert.Equal(ActivityAttemptReason.Initial, attempt.Reason);
                Assert.NotNull(attempt.EndedAt);
            },
            attempt =>
            {
                Assert.Equal(ActivityAttemptReason.Retry, attempt.Reason);
                Assert.NotNull(attempt.EndedAt);
                Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete, attempt.TransitionKind);
            });
    }
}
