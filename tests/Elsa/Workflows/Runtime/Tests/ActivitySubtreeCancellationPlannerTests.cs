using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivitySubtreeCancellationPlannerTests
{
    private const string WorkflowExecutionId = "wfexec-cancellation";
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Plan_preserves_an_existing_resolution_outcome_and_only_suppresses_undecided_incidents()
    {
        var incidentStore = new InMemoryIncidentStateStore();
        var decidedOutcome = new IncidentResolutionOutcome(
            IncidentResolutionActionKinds.ContinueWithIncidents,
            Now,
            new IncidentStrategyReference("ContinueWithIncidents", "1"),
            systemSource: null);
        await incidentStore.TryAddAsync(NewIncident("incident-decided", decidedOutcome));
        await incidentStore.TryAddAsync(NewIncident("incident-undecided", resolutionOutcome: null));
        var root = NewActivity();
        var planner = new ActivitySubtreeCancellationPlanner(new EmptyCleanupStore(), incidentStore);

        var plan = await planner.PlanAsync(
            WorkflowExecutionId,
            root,
            [root],
            "Cancelled",
            new Dictionary<string, string>(),
            Now);

        var suppressed = Assert.Single(plan.IncidentChanges);
        Assert.Equal("incident-undecided", suppressed.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.SuppressIncident, suppressed.ResolutionOutcome!.ActionKind);
        var decided = await incidentStore.FindAsync(WorkflowExecutionId, "incident-decided");
        Assert.Same(decidedOutcome, decided!.ResolutionOutcome);
    }

    private static IncidentState NewIncident(
        string incidentId,
        IncidentResolutionOutcome? resolutionOutcome) =>
        new(
            incidentId,
            WorkflowExecutionId,
            "activity-root",
            "node-root",
            IncidentSeverity.Error,
            IncidentStatus.Open,
            resolutionOutcome,
            "TestFault",
            "test",
            Now,
            null,
            new Dictionary<string, string>());

    private static ActivityExecutionState NewActivity() =>
        new(
            new ActivityExecution(
                "activity-root",
                WorkflowExecutionId,
                "node-root",
                "authored-root",
                "Test.Activity",
                "1"),
            ActivityExecutionStatus.Running,
            null,
            1,
            Now,
            Now,
            null,
            null,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From(
                WorkflowExecutionId,
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

    private sealed class EmptyCleanupStore : IActivityScopeCleanupStore
    {
        public ValueTask<ActivityScopeCleanupRequest> CaptureAsync(
            string workflowExecutionId,
            string executionScopeId,
            IReadOnlySet<string> activityExecutionIds,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityScopeCleanupRequest(
                workflowExecutionId,
                executionScopeId,
                activityExecutionIds.ToArray(),
                [],
                [],
                []));

        public ValueTask ApplyAsync(
            ActivityScopeCleanupRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
