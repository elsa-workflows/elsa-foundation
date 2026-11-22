using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    [Fact]
    public async Task Unavailable_resume_keeps_native_work_and_retries_consumed_payload_in_a_fresh_activation()
    {
        var executable = NewTypedExecutable("original");
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new UnavailableThenCompleteActivator();
        var observer = new RecordingBookmarkObserver();
        var clock = new FakeTimeProvider(_now);
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        var delay = TimeSpan.FromSeconds(5);
        var work = NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42"));
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(executable.RootActivity.ActivityContract!, triggerType));
        await SaveBookmarkAsync();
        await _schedulerWorkQueue.EnqueueAsync(work);

        // The handler opens its own service scope, as in production. No singleton consume accessor
        // is injected to make the test silently share the drainer's claim with that separate scope.
        await using (var provider = NewProvider(activator, observer))
        {
            var first = await NewDrainer(provider).DrainAsync(new("wfexec-1", maxWorkItems: 1));
            Assert.True(first.StoppedOnDeferral);
            Assert.Equal(0, first.DrainedCount);
            var retained = Assert.Single(await _schedulerWorkQueue.ListAllAsync("wfexec-1"));
            Assert.Equal(work.WorkItemId, retained.WorkItemId);
            Assert.Equal(work.Payload!.Value.GetRawText(), retained.Payload!.Value.GetRawText());
            Assert.Empty(await poison.ListAsync("wfexec-1"));
            var waiting = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!;
            Assert.Equal(ActivityExecutionStatus.Running, waiting.Status);
            Assert.Null(waiting.Fault);
            var receipt = Assert.Single(waiting.TriggerDeliveries!);
            Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, receipt.Status);
            Assert.NotNull(receipt.Payload.InlineValue);
            Assert.NotNull(waiting.PrivateState);
            Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
            Assert.True(activator.Interrupted.Disposed);
            Assert.Empty((await NewDrainer(provider).DrainAsync(new("wfexec-1", maxWorkItems: 1))).Items);
            Assert.Single(activator.Requests);
        }

        clock.Advance(delay);
        // Rebuild the provider/handler: only the stores, queue, and clock survive this component test.
        // Database/process restart acceptance is covered separately, not implied by this scope reset.
        await using (var provider = NewProvider(activator, observer))
        {
            var resumed = await NewDrainer(provider).DrainAsync(new("wfexec-1", maxWorkItems: 1));
            Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(resumed.Items).Status);
        }

        var completed = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!;
        Assert.Equal(ActivityExecutionStatus.Completed, completed.Status);
        Assert.Null(completed.PrivateState);
        Assert.Null(completed.Fault);
        Assert.Equal(3, completed.Attempts!.Count);
        Assert.All(activator.Requests, request => Assert.Equal("delivery-42", request.Trigger!.DeliveryId));
        Assert.Equal(new[] { "actexec-1:attempt:2", "actexec-1:attempt:3" },
            activator.Requests.Select(request => request.Attempt.AttemptId));
        Assert.Single(observer.Consumed);
        Assert.Equal(ValuePresence.Absent, Assert.Single(completed.TriggerDeliveries!).Payload.Presence);
        Assert.DoesNotContain(await _schedulerWorkQueue.ListAllAsync("wfexec-1"), item => item.WorkItemId == work.WorkItemId);
        Assert.Empty(await poison.ListAsync("wfexec-1"));
        var commits = _checkpointWriter.ListCommits().Select(record => record.Commit).ToArray();
        Assert.DoesNotContain(commits, commit => commit.Checkpoint.Name == RuntimeCheckpointNames.IncidentRecorded);
        Assert.Single(commits, commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted);
        var claims = commits.Where(commit => commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed).ToArray();
        Assert.Equal(2, claims.Length);
        Assert.All(claims, commit => Assert.True(commit.RetainSchedulerWork));

        WorkflowSchedulerDrainer NewDrainer(ServiceProvider provider) => new(
            _schedulerWorkQueue,
            [NewHandler(provider)],
            provider.GetRequiredService<IWorkflowExecutionStateStore>(),
            poison,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), clock),
            clock,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = delay });
    }

    [Fact]
    public async Task Unavailable_resume_with_disposal_failure_records_a_real_native_fault()
    {
        var executable = NewTypedExecutable("original");
        var triggerType = Descriptor<ApprovalTrigger>();
        var activator = new UnavailableThenCompleteActivator(disposalFails: true);
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewTypedTriggerState(executable.RootActivity.ActivityContract!, triggerType));
        await SaveBookmarkAsync();
        await using var provider = NewProvider(activator);

        await NewHandler(provider).HandleAsync(NewResumeWorkItem(
            input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)),
            triggerDelivery: Delivery(triggerType, "dedupe-42")));

        await AssertConsumedResumeFaultAsync("ActivityDisposalFailed");
        Assert.True(activator.Interrupted.Disposed);
        Assert.Single(activator.Requests);
    }

    private sealed class UnavailableThenCompleteActivator(bool disposalFails = false) : IActivityActivator
    {
        public List<ActivityActivationRequest> Requests { get; } = [];
        public UnavailableApprovalActivity Interrupted { get; } = new(disposalFails);

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            IActivity activity = Requests.Count == 1 ? Interrupted : new ApprovalActivity();
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class UnavailableApprovalActivity(bool disposalFails) : StatefulActivity<ActivityUnit, ApprovalState, ApprovalTrigger>, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new NotSupportedException();

        protected override ValueTask<ActivityTransition<ActivityUnit, ApprovalState>> ResumeAsync(ActivityResumeContext<ApprovalState, ApprovalTrigger> context) =>
            throw new RuntimeSchedulerWorkUnavailableException("test.store-unavailable", "The test store is unavailable.");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (disposalFails) throw new InvalidOperationException("Test disposal failed.");
            return ValueTask.CompletedTask;
        }
    }
}
