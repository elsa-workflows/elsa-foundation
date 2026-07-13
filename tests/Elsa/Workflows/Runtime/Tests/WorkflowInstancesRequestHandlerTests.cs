using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowInstancesRequestHandlerTests
{
    private readonly InMemoryWorkflowExecutionStateStore _workflowStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryIncidentStateStore _incidentStore = new();
    private readonly InMemoryDurableValueStateStore _durableValueStore = new();

    private GetWorkflowInstanceRequestHandler NewGetInstanceHandler() =>
        new(_workflowStore, _inspectionStore, _incidentStore, _durableValueStore, new DefaultRuntimePayloadCapturePolicy());

    [Fact]
    public async Task ListWorkflowInstances_ReturnsFilteredSummariesWithActivityAndIncidentCounts()
    {
        await _workflowStore.SaveAsync(Workflow("wf-old", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-20)));
        await _workflowStore.SaveAsync(Workflow("wf-new", WorkflowExecutionStatus.Running, "definition-1", correlationId: "correlation-1", updatedAt: Now(-1)));
        await _workflowStore.SaveAsync(Workflow("wf-other", WorkflowExecutionStatus.Running, "definition-2", updatedAt: Now(-2)));
        await _activityStore.SaveAsync(Activity("wf-new", "activity-1", ActivityExecutionStatus.Running));
        await _activityStore.SaveAsync(Activity("wf-new", "activity-2", ActivityExecutionStatus.Completed));
        await _incidentStore.TryAddAsync(Incident("wf-new", "incident-1"));
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var result = await handler.Handle(new ListWorkflowInstances("Running", "definition-1", "correlation-1", 10), CancellationToken.None);

        var summary = Assert.Single(result.Items);
        Assert.Equal("wf-new", summary.WorkflowExecutionId);
        Assert.Equal("Running", summary.Status);
        Assert.Equal("definition-1", summary.DefinitionId);
        Assert.Equal("correlation-1", summary.CorrelationId);
        Assert.Equal(2, summary.ActivityCount);
        Assert.Equal(1, summary.IncidentCount);
        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListWorkflowInstances_FiltersAndProjectsDurableRunKind()
    {
        await _workflowStore.SaveAsync(Workflow("wf-test", WorkflowExecutionStatus.Completed, "definition-1", runKind: WorkflowRunKind.TestRun));
        await _workflowStore.SaveAsync(Workflow("wf-published", WorkflowExecutionStatus.Completed, "definition-1", runKind: WorkflowRunKind.PublishedRun));
        await _workflowStore.SaveAsync(Workflow("wf-weaver", WorkflowExecutionStatus.Completed, "definition-1", runKind: WorkflowRunKind.BackgroundWeaverRun));
        await _workflowStore.SaveAsync(Workflow("wf-legacy", WorkflowExecutionStatus.Completed, "definition-1"));
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var result = await handler.Handle(new ListWorkflowInstances(null, null, null, 10, RunKind: "TestRun"), CancellationToken.None);
        var publishedResult = await handler.Handle(new ListWorkflowInstances(null, null, null, 10, RunKind: "PublishedRun"), CancellationToken.None);
        var weaverResult = await handler.Handle(new ListWorkflowInstances(null, null, null, 10, RunKind: "BackgroundWeaverRun"), CancellationToken.None);
        var legacyResult = await handler.Handle(new ListWorkflowInstances(null, null, null, 10, RunKind: "Unknown"), CancellationToken.None);

        var summary = Assert.Single(result.Items);
        Assert.Equal("wf-test", summary.WorkflowExecutionId);
        Assert.Equal("TestRun", summary.RunKind);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("wf-published", Assert.Single(publishedResult.Items).WorkflowExecutionId);
        Assert.Equal("wf-weaver", Assert.Single(weaverResult.Items).WorkflowExecutionId);
        Assert.Equal("wf-legacy", Assert.Single(legacyResult.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task ListWorkflowInstances_NavigatesStableCursorPagesAcrossEqualTimestamps()
    {
        var timestamp = Now(-1);
        foreach (var id in new[] { "wf-d", "wf-b", "wf-a", "wf-c" })
            await _workflowStore.SaveAsync(Workflow(id, WorkflowExecutionStatus.Completed, "definition-1", updatedAt: timestamp));
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var first = await handler.Handle(new ListWorkflowInstances(null, null, null, 2), CancellationToken.None);
        var second = await handler.Handle(new ListWorkflowInstances(null, null, null, 2, first.NextCursor), CancellationToken.None);
        var previous = await handler.Handle(new ListWorkflowInstances(null, null, null, 2, second.PreviousCursor), CancellationToken.None);

        Assert.Equal(["wf-a", "wf-b"], first.Items.Select(x => x.WorkflowExecutionId));
        Assert.False(first.HasPrevious);
        Assert.True(first.HasNext);
        Assert.Equal(["wf-c", "wf-d"], second.Items.Select(x => x.WorkflowExecutionId));
        Assert.True(second.HasPrevious);
        Assert.False(second.HasNext);
        Assert.Equal(first.Items.Select(x => x.WorkflowExecutionId), previous.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(4, second.TotalCount);
    }

    [Fact]
    public async Task ListWorkflowInstances_ClampsPageSizeAtBothBounds()
    {
        for (var index = 0; index < 105; index++)
            await _workflowStore.SaveAsync(Workflow($"wf-{index:D3}", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-index)));
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var maximum = await handler.Handle(new ListWorkflowInstances(null, null, null, 500), CancellationToken.None);
        var minimum = await handler.Handle(new ListWorkflowInstances(null, null, null, 0), CancellationToken.None);

        Assert.Equal(100, maximum.Count);
        Assert.True(maximum.HasNext);
        Assert.Single(minimum.Items);
    }

    [Fact]
    public async Task ListWorkflowInstances_FiltersByOperationalIdentifiersAndTimeRange()
    {
        await _workflowStore.SaveAsync(Workflow("match", WorkflowExecutionStatus.Running, "definition-1", "correlation-1", Now(-5)));
        await _workflowStore.SaveAsync(Workflow("wrong-execution", WorkflowExecutionStatus.Running, "definition-1", "correlation-1", Now(-5)));
        await _workflowStore.SaveAsync(Workflow("wrong-definition", WorkflowExecutionStatus.Running, "definition-2", "correlation-1", Now(-5)));
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var result = await handler.Handle(new ListWorkflowInstances(
            "Running",
            "definition-1",
            "correlation-1",
            10,
            WorkflowExecutionId: "match",
            ArtifactId: "artifact-definition-1",
            From: Now(-6),
            To: Now(-4)), CancellationToken.None);

        Assert.Equal("match", Assert.Single(result.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task ListWorkflowInstances_RejectsMalformedCursor()
    {
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ListWorkflowInstances(null, null, null, 10, "not-a-cursor"), CancellationToken.None));

        Assert.Equal("cursor", exception.ParamName);
    }

    [Fact]
    public async Task ListWorkflowInstances_RejectsUnknownRunKind()
    {
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ListWorkflowInstances(null, null, null, 10, RunKind: "TestRnu"), CancellationToken.None));

        Assert.Equal("RunKind", exception.ParamName);
        Assert.Contains("TestRnu", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListWorkflowInstances_MapsMalformedCursorToBadRequestAtHttpBoundary()
    {
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);
        var sender = new StubRequestSender((request, cancellationToken) => handler.Handle(request, cancellationToken));
        var endpoint = Factory.Create<TestListInstancesEndpoint>(
            context => context.Response.Body = new MemoryStream(),
            sender,
            NullLogger<TestListInstancesEndpoint>.Instance);

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            endpoint.HandleAsync(new ListWorkflowInstances(null, null, null, 10, "not-a-cursor"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("cursor is invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListWorkflowInstances_MapsUnknownRunKindToBadRequestAtHttpBoundary()
    {
        var handler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);
        var sender = new StubRequestSender((request, cancellationToken) => handler.Handle(request, cancellationToken));
        var endpoint = Factory.Create<TestListInstancesEndpoint>(
            context => context.Response.Body = new MemoryStream(),
            sender,
            NullLogger<TestListInstancesEndpoint>.Instance);

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            endpoint.HandleAsync(new ListWorkflowInstances(null, null, null, 10, RunKind: "TestRnu"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("run kind", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWorkflowInstance_ReturnsActivitiesAndIncidents()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Faulted, "definition-1"));
        await _activityStore.SaveAsync(Activity("wf-1", "activity-2", ActivityExecutionStatus.Faulted, scheduledAt: Now(-1)));
        await _activityStore.SaveAsync(Activity("wf-1", "activity-1", ActivityExecutionStatus.Completed, scheduledAt: Now(-2)));
        await _inspectionStore.SaveAsync(Inspection(Activity("wf-1", "activity-2", ActivityExecutionStatus.Faulted, scheduledAt: Now(-1))));
        await _inspectionStore.SaveAsync(Inspection(Activity("wf-1", "activity-1", ActivityExecutionStatus.Completed, scheduledAt: Now(-2))));
        await _incidentStore.TryAddAsync(Incident("wf-1", "incident-1"));
        var handler = NewGetInstanceHandler();

        var result = await handler.Handle(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.NotNull(result.Instance);
        Assert.Equal("wf-1", result.Instance.Instance.WorkflowExecutionId);
        Assert.Equal("Faulted", result.Instance.Instance.Status);
        Assert.Equal(["activity-1", "activity-2"], result.Instance.Activities.Select(activity => activity.ActivityExecutionId));
        Assert.Equal("incident-1", Assert.Single(result.Instance.Incidents).IncidentId);
    }

    [Fact]
    public async Task ListAndDetailReturnTheSameRunKindAndKeepLegacyStatesUnknown()
    {
        await _workflowStore.SaveAsync(Workflow("wf-published", WorkflowExecutionStatus.Running, "definition-1", runKind: WorkflowRunKind.PublishedRun));
        await _workflowStore.SaveAsync(Workflow("wf-legacy", WorkflowExecutionStatus.Completed, "definition-1"));
        var listHandler = new ListWorkflowInstancesRequestHandler(_workflowStore, _activityStore, _incidentStore);

        var list = await listHandler.Handle(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);
        var detail = await NewGetInstanceHandler().Handle(new GetWorkflowInstance("wf-published"), CancellationToken.None);

        Assert.Equal("PublishedRun", Assert.Single(list.Items, item => item.WorkflowExecutionId == "wf-published").RunKind);
        Assert.Equal("PublishedRun", detail.Instance!.Instance.RunKind);
        Assert.Equal("Unknown", Assert.Single(list.Items, item => item.WorkflowExecutionId == "wf-legacy").RunKind);
    }

    [Fact]
    public async Task GetWorkflowInstance_ReturnsNullForMissingInstance()
    {
        var handler = NewGetInstanceHandler();

        var result = await handler.Handle(new GetWorkflowInstance("missing"), CancellationToken.None);

        Assert.Null(result.Instance);
    }

    private static WorkflowExecutionState Workflow(
        string id,
        WorkflowExecutionStatus status,
        string definitionId,
        string? correlationId = null,
        DateTimeOffset? updatedAt = null,
        WorkflowRunKind runKind = WorkflowRunKind.Unknown) =>
        new(
            WorkflowExecutionId: id,
            PinnedExecutable: new WorkflowExecutableIdentity(
                ArtifactId: $"artifact-{definitionId}",
                DefinitionId: definitionId,
                DefinitionVersionId: $"version-{definitionId}",
                ArtifactVersion: "1.0.0",
                ArtifactHash: "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: Now(-30),
            StartedAt: Now(-29),
            UpdatedAt: updatedAt,
            CompletedAt: status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted ? Now(-1) : null,
            CorrelationId: correlationId,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>())
        {
            RunKind = runKind
        };

    private static ActivityExecutionState Activity(
        string workflowExecutionId,
        string activityExecutionId,
        ActivityExecutionStatus status,
        DateTimeOffset? scheduledAt = null) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: workflowExecutionId,
                ExecutableNodeId: $"node-{activityExecutionId}",
                AuthoredActivityId: $"authored-{activityExecutionId}",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ScheduledAt: scheduledAt ?? Now(-10),
            StartedAt: Now(-9),
            CompletedAt: status == ActivityExecutionStatus.Completed ? Now(-8) : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: status == ActivityExecutionStatus.Faulted ? 1 : 0,
            AggregateFaultCount: status == ActivityExecutionStatus.Faulted ? 1 : 0,
            Metadata: new Dictionary<string, string>());

    private static IncidentState Incident(string workflowExecutionId, string incidentId) =>
        new(
            incidentId: incidentId,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: "activity-2",
            executableNodeId: "node-activity-2",
            severity: IncidentSeverity.Error,
            status: IncidentStatus.Blocking,
            resolutionAction: IncidentResolutionAction.Retry,
            failureType: "TestFailure",
            message: "The activity failed.",
            createdAt: Now(-7),
            resolvedAt: null);

    private static ActivityExecutionInspectionProjection Inspection(ActivityExecutionState state) =>
        ActivityExecutionInspectionProjection.FromState(
            state,
            checkpointId: $"checkpoint-{state.Execution.ActivityExecutionId}",
            committedAt: Now(-1));

    private static DateTimeOffset Now(int minutes) =>
        new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero) + TimeSpan.FromMinutes(minutes);

    private sealed class StubRequestSender(
        Func<ListWorkflowInstances, CancellationToken, Task<WorkflowInstanceListView>> send) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is ListWorkflowInstances listWorkflowInstances && typeof(T) == typeof(WorkflowInstanceListView))
                return (Task<T>)(object)send(listWorkflowInstances, cancellationToken);

            throw new InvalidOperationException($"Unexpected request type '{request.GetType()}'.");
        }
    }

    private sealed class TestListInstancesEndpoint(
        IRequestSender requestSender,
        Microsoft.Extensions.Logging.ILogger<TestListInstancesEndpoint> logger)
        : ElsaRequestHandlerEndpoint<ListWorkflowInstances, WorkflowInstanceListView>(requestSender, logger)
    {
        public override void Configure()
        {
        }
    }
}
