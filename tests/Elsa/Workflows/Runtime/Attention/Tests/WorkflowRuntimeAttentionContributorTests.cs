using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Attention.Tests;

public sealed class WorkflowRuntimeAttentionContributorTests
{
    [Fact]
    public async Task EvaluatesCompleteQueryOnceAndReturnsBoundedCorrelatedItems()
    {
        var now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");
        var query = new StubQuery(new(
            7,
            [
                Record("run-1", "definition-1", "incident-1", WorkflowRuntimeAttentionKind.BlockingIncident, now, "Payment capture failed"),
                Record("run-2", "definition-2", null, WorkflowRuntimeAttentionKind.FaultedExecution, now.AddMinutes(-1), "Execution faulted")
            ]));
        var contributor = new WorkflowRuntimeAttentionContributor(query);

        var contribution = await contributor.EvaluateAsync(Context(2));

        Assert.Equal(1, query.CallCount);
        Assert.Equal(5, query.LastRequest!.MaximumItems);
        Assert.Equal("tenant-1", query.LastRequest.TenantId);
        Assert.Equal(7, contribution.TotalCount);
        Assert.True(contribution.IsTruncated);
        Assert.Equal(2, contribution.Items.Count);
        var incident = contribution.Items.First();
        Assert.Equal("incident:incident-1", incident.Id);
        Assert.Equal(AttentionSeverity.Critical, incident.Severity);
        Assert.Contains(incident.Correlations, x => x.Kind == "workflowExecution" && x.Value == "run-1");
        Assert.Contains(incident.Correlations, x => x.Kind == "incident" && x.Value == "incident-1");
        Assert.Equal("/workflows/instances/run-1", incident.Destination.Path);
    }

    [Fact]
    public async Task PropagatesExplicitQueryUnavailabilityWithoutInventingAllClear()
    {
        var contributor = new WorkflowRuntimeAttentionContributor(new StubQuery(
            WorkflowRuntimeAttentionSnapshot.Unavailable("RUNTIME_ATTENTION_QUERY_UNAVAILABLE", "Provider adapter missing.")));

        var contribution = await contributor.EvaluateAsync(Context(2));

        Assert.Equal(AttentionContributionStatus.Unavailable, contribution.Status);
        Assert.Equal("RUNTIME_ATTENTION_QUERY_UNAVAILABLE", contribution.ErrorCode);
    }

    [Fact]
    public async Task ConsumesOneDownstreamCallFromBudget()
    {
        var contributor = new WorkflowRuntimeAttentionContributor(new StubQuery(new(0, [])));
        var context = Context(0);

        await Assert.ThrowsAsync<AttentionBudgetExceededException>(async () => await contributor.EvaluateAsync(context));
    }

    [Fact]
    public async Task InMemoryAdapterEvaluatesBeyondNormalPageAndReturnsOnlyMostUrgentFive()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        var incidents = new InMemoryIncidentStateStore();
        var now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");
        for (var index = 0; index < 125; index++)
        {
            var state = Execution($"run-{index:D3}", "tenant-1", WorkflowExecutionStatus.Faulted, now.AddMinutes(-index));
            await executions.SaveAsync(state);
        }
        await executions.SaveAsync(Execution("other-tenant", "tenant-2", WorkflowExecutionStatus.Faulted, now));
        await incidents.SaveAsync(new(
            "incident-1",
            "run-124",
            null,
            null,
            IncidentSeverity.Critical,
            IncidentStatus.Blocking,
            IncidentResolutionAction.WaitForIntervention,
            "TestFailure",
            "Sensitive failure detail",
            now.AddMinutes(-124),
            null));

        var adapter = new InMemoryWorkflowRuntimeAttentionQuery(executions, incidents, new FixedTimeProvider(now));
        var result = await adapter.QueryAsync(new(new(new ClaimsPrincipal(), "tenant-1"), 5));

        Assert.Equal(125, result.TotalCount);
        Assert.Equal(5, result.Records.Count);
        Assert.DoesNotContain(result.Records, record => record.WorkflowExecutionId == "other-tenant");
        Assert.Contains(result.Records, record => record.IncidentId == "incident-1");
        Assert.All(result.Records, record => Assert.Null(record.SanitizedSummary));
    }

    [Fact]
    public void FeatureUsesExactInMemoryAdapterForDefaultRuntimeStores()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeAttentionFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryWorkflowRuntimeAttentionQuery>(provider.GetRequiredService<IWorkflowRuntimeAttentionQuery>());
        Assert.Contains(provider.GetServices<IAttentionContributor>(), contributor => contributor is WorkflowRuntimeAttentionContributor);
    }

    private static AttentionContributorContext Context(int calls) => new(
        new(new ClaimsPrincipal(), "tenant-1"),
        new(calls),
        new Dictionary<string, string>());

    private static WorkflowRuntimeAttentionRecord Record(
        string executionId,
        string definitionId,
        string? incidentId,
        WorkflowRuntimeAttentionKind kind,
        DateTimeOffset occurredAt,
        string summary) => new(
        executionId,
        definitionId,
        incidentId,
        kind,
        kind == WorkflowRuntimeAttentionKind.BlockingIncident ? "incident-generation-1" : "execution-generation-1",
        occurredAt,
        occurredAt.AddMinutes(1),
        1,
        summary);

    private sealed class StubQuery(WorkflowRuntimeAttentionSnapshot result) : IWorkflowRuntimeAttentionQuery
    {
        public int CallCount { get; private set; }
        public WorkflowRuntimeAttentionQuery? LastRequest { get; private set; }

        public ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(WorkflowRuntimeAttentionQuery request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return ValueTask.FromResult(result);
        }
    }

    private static WorkflowExecutionState Execution(
        string id,
        string tenantId,
        WorkflowExecutionStatus status,
        DateTimeOffset timestamp) => new(
        id,
        new("artifact", "definition", "version", "1.0.0", "hash"),
        status,
        null,
        timestamp,
        timestamp,
        timestamp,
        status.IsTerminal() ? timestamp : null,
        null,
        null,
        tenantId,
        new Dictionary<string, string>());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
