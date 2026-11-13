using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Theory]
    [InlineData("complete", false)]
    [InlineData("complete", true)]
    [InlineData("returned-fault", true)]
    [InlineData("throw", true)]
    public async Task Actual_invoke_outcome_carries_callback_observations_without_inventing_a_return_after_a_throw(string outcome, bool captured)
    {
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        await native.SaveAsync((await native.FindAsync("wfexec-1"))! with { HistoryCapturePolicy = captured ? HistoryOwnerInputTestComposition.Policy : null });
        IActivityActivator activator = outcome switch
        {
            "returned-fault" => new ReturningTypedActivator(ActivityTransition.Fault<TypedResult>(new ActivityFault("private-code", "private failure"))),
            "throw" => new FixedActivityActivator(new ThrowingHistoryActivity()),
            _ => new RecordingTypedActivator()
        };
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true,
            configure: services => HistoryOwnerInputTestComposition.Configure(services, native, _checkpointWriter));
        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));
        var facts = _checkpointWriter.ListCommits().SelectMany(record => record.Commit.HistorySourceFacts ?? []).ToArray();
        if (!captured) { Assert.Empty(facts); return; }
        var expected = outcome switch
        {
            "complete" => new[] { RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationReturned, RuntimeHistorySourceFactKind.ActivityContinuationCompleted },
            "returned-fault" => new[] { RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationReturned, RuntimeHistorySourceFactKind.ActivityContinuationFaulted, RuntimeHistorySourceFactKind.IncidentCreated },
            _ => new[] { RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationFaulted, RuntimeHistorySourceFactKind.IncidentCreated }
        };
        Assert.Equal(expected, facts.Select(fact => fact.Kind));
        Assert.All(facts, fact =>
        {
            Assert.Equal("actexec-1", fact.ActivityExecutionId);
            Assert.Equal("invoke-work", fact.Causation!.SchedulerWorkItemId);
            Assert.DoesNotContain("private failure", fact.ReasonCode ?? "");
        });
        Assert.All(facts.Where(fact => fact.Kind != RuntimeHistorySourceFactKind.IncidentCreated), fact => Assert.False(string.IsNullOrWhiteSpace(fact.AttemptId)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Actual_parent_completion_and_notification_callbacks_keep_their_suspension_observations(bool notification, bool captured)
    {
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        await native.SaveAsync((await native.FindAsync("wfexec-1"))! with { HistoryCapturePolicy = captured ? HistoryOwnerInputTestComposition.Policy : null });
        var executable = NewCallbackExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewCallbackParentState(executable));
        await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        var activity = new HistoryNotificationActivity();
        await using var provider = NewProvider(new FixedActivityActivator(notification ? activity : new DeferringChildCompletionActivity()), includeInspection: true,
            configure: services => HistoryOwnerInputTestComposition.Configure(services, native, _checkpointWriter));
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        if (notification)
            await new WorkflowNotifyParentActivitySchedulerWorkHandler(factory, new FakeTimeProvider(_now))
                .HandleAsync(NewParentNotificationWorkItem("history-notification", "history-command"));
        else
            await new WorkflowParentActivityCompletionSchedulerWorkHandler(factory, new FakeTimeProvider(_now))
                .HandleAsync(NewParentCallbackWorkItem("history-parent", "history-command"));
        var facts = _checkpointWriter.ListCommits().SelectMany(record => record.Commit.HistorySourceFacts ?? []).ToArray();
        if (!captured) { Assert.Empty(facts); return; }
        Assert.Equal(new[] { RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationReturned,
            RuntimeHistorySourceFactKind.ActivityContinuationSuspended }, facts.Select(fact => fact.Kind));
        Assert.All(facts, fact => Assert.Equal("history-command", fact.Causation!.CommandId));
        if (notification) Assert.Equal(1, activity.Calls);
    }

    private sealed class ThrowingHistoryActivity : Activity<TypedResult>
    {
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromException<ActivityTransition<TypedResult>>(new InvalidOperationException("private failure"));
    }

    private sealed class HistoryNotificationActivity : StructuralActivity, IRuntimeActivityChildNotificationHandler
    {
        public int Calls { get; private set; }
        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(RuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
        { Calls++; return ValueTask.FromResult(RuntimeStructuralContinuation.Defer); }
    }
}
