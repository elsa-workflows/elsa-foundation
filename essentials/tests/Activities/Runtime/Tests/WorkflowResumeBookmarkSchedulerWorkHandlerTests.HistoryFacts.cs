using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    [Theory]
    [InlineData("complete", false)]
    [InlineData("complete", true)]
    [InlineData("suspend", true)]
    [InlineData("returned-fault", true)]
    [InlineData("throw", true)]
    public async Task Actual_exact_resume_carries_its_callback_outcome_and_delivery_cause_without_claiming_a_return_after_throw(string outcome, bool captured)
    {
        var (work, _) = await PrepareExactResume();
        await _exactWorkflowStates!.SaveAsync((await _exactWorkflowStates.FindAsync("wfexec-1"))! with
            { HistoryCapturePolicy = captured ? HistoryOwnerInputTestComposition.Policy : null });
        IActivity activity = outcome switch
        {
            "suspend" => new ResuspendingApprovalActivity(),
            "returned-fault" => new FaultingApprovalActivity(),
            "throw" => new ThrowingApprovalActivity(),
            _ => new ApprovalActivity()
        };
        await using var provider = NewProvider(new HistoryResumeActivator(activity), configure: services =>
            HistoryOwnerInputTestComposition.Configure(services, _exactWorkflowStates, _checkpointWriter));
        await NewHandler(provider).HandleAsync(work);
        var facts = _checkpointWriter.ListCommits().SelectMany(record => record.Commit.HistorySourceFacts ?? []).ToArray();
        if (!captured) { Assert.Empty(facts); return; }
        var expected = new List<RuntimeHistorySourceFactKind> { RuntimeHistorySourceFactKind.ActivityContinuationStarted };
        if (outcome != "throw") expected.Add(RuntimeHistorySourceFactKind.ActivityContinuationReturned);
        expected.Add(outcome switch
        {
            "complete" => RuntimeHistorySourceFactKind.ActivityContinuationCompleted,
            "suspend" => RuntimeHistorySourceFactKind.ActivityContinuationSuspended,
            _ => RuntimeHistorySourceFactKind.ActivityContinuationFaulted
        });
        if (outcome is "throw" or "returned-fault") expected.Add(RuntimeHistorySourceFactKind.IncidentCreated);
        Assert.Equal(expected, facts.Select(fact => fact.Kind));
        Assert.All(facts, fact => Assert.Equal(work.WorkItemId, fact.Causation!.SchedulerWorkItemId));
        Assert.All(facts.Where(fact => fact.Kind != RuntimeHistorySourceFactKind.IncidentCreated), fact =>
        {
            Assert.False(string.IsNullOrWhiteSpace(fact.AttemptId));
            Assert.False(string.IsNullOrWhiteSpace(fact.Causation!.DeliveryId));
        });
        Assert.Single(_checkpointWriter.ListCommits(), record => record.Commit.ExpectedBookmarkResume is not null);
    }

    private sealed class HistoryResumeActivator(IActivity activity) : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(activity));
    }
}
