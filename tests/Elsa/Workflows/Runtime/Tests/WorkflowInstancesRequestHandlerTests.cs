using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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
        new(
            _workflowStore,
            _inspectionStore,
            _incidentStore,
            _durableValueStore,
            new DefaultRuntimePayloadCapturePolicy(),
            new AllowAllActivityExecutionInspectionAuthorizationContext());

    [Fact]
    public async Task ListWorkflowInstances_ReturnsFilteredSummariesWithActivityAndIncidentCounts()
    {
        await _workflowStore.SaveAsync(Workflow("wf-old", WorkflowExecutionStatus.Completed, "definition-1", updatedAt: Now(-20)));
        await _workflowStore.SaveAsync(Workflow("wf-new", WorkflowExecutionStatus.Running, "definition-1", correlationId: "correlation-1", updatedAt: Now(-1)));
        await _workflowStore.SaveAsync(Workflow("wf-other", WorkflowExecutionStatus.Running, "definition-2", updatedAt: Now(-2)));
        await _activityStore.SaveAsync(Activity("wf-new", "activity-1", ActivityExecutionStatus.Running));
        await _activityStore.SaveAsync(Activity("wf-new", "activity-2", ActivityExecutionStatus.Completed));
        await _incidentStore.TryAddAsync(Incident("wf-new", "incident-1"));
        var handler = new ListWorkflowInstancesRequestHandler(
            _workflowStore,
            _activityStore,
            _incidentStore,
            new AllowAllActivityExecutionInspectionAuthorizationContext());

        var result = await handler.Handle(new ListWorkflowInstances("Running", "definition-1", "correlation-1", 10), CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("wf-new", summary.WorkflowExecutionId);
        Assert.Equal("Running", summary.Status);
        Assert.Equal("definition-1", summary.DefinitionId);
        Assert.Equal("correlation-1", summary.CorrelationId);
        Assert.Equal(2, summary.ActivityCount);
        Assert.Equal(1, summary.IncidentCount);
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
    public async Task GetWorkflowInstance_ReturnsNullForMissingInstance()
    {
        var handler = NewGetInstanceHandler();

        var result = await handler.Handle(new GetWorkflowInstance("missing"), CancellationToken.None);

        Assert.Null(result.Instance);
    }

    [Fact]
    public async Task ListWorkflowInstances_FiltersUnauthorizedTenants_And_RedactsCorrelationWithoutValuePermission()
    {
        await _workflowStore.SaveAsync(Workflow("wf-visible", WorkflowExecutionStatus.Running, "definition-1", "secret-correlation", tenantId: "tenant-a"));
        await _workflowStore.SaveAsync(Workflow("wf-hidden", WorkflowExecutionStatus.Running, "definition-1", tenantId: "tenant-b"));
        var handler = new ListWorkflowInstancesRequestHandler(
            _workflowStore,
            _activityStore,
            _incidentStore,
            new TenantInspectionAuthorization("tenant-a", canInspectSensitiveValues: false));

        var result = await handler.Handle(new ListWorkflowInstances(null, null, null, null), CancellationToken.None);

        var visible = Assert.Single(result);
        Assert.Equal("wf-visible", visible.WorkflowExecutionId);
        Assert.Null(visible.CorrelationId);

        var filteredByProtectedCorrelation = await handler.Handle(
            new ListWorkflowInstances(null, null, "secret-correlation", null),
            CancellationToken.None);
        Assert.Empty(filteredByProtectedCorrelation);
    }

    [Fact]
    public async Task GetWorkflowInstance_RedactsOutputsIncidentsAndMetadataWithoutValuePermission()
    {
        var workflow = Workflow("wf-1", WorkflowExecutionStatus.Faulted, "definition-1", "secret-correlation", tenantId: "tenant-a");
        await _workflowStore.SaveAsync(workflow);
        var activity = Activity("wf-1", "activity-1", ActivityExecutionStatus.Faulted);
        var inspection = Inspection(activity) with
        {
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ScopedVariableValues] = "secret-variable" }
        };
        await _inspectionStore.SaveAsync(inspection);
        await _incidentStore.TryAddAsync(Incident(
            "wf-1",
            "incident-1",
            new Dictionary<string, string> { [RuntimeMetadataKeys.FaultStackTrace] = "secret-stack" }));
        await _durableValueStore.SaveAsync(new DurableValueState(
            "durable-output",
            "wf-1",
            "output:result",
            new RuntimeValueTypeDescriptor("clr", "System.String", null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement("secret-output"),
            null,
            "activity-1",
            Now(-1),
            new Dictionary<string, string> { [RuntimeMetadataKeys.OutputName] = "result" }));
        var handler = new GetWorkflowInstanceRequestHandler(
            _workflowStore,
            _inspectionStore,
            _incidentStore,
            _durableValueStore,
            new DefaultRuntimePayloadCapturePolicy(),
            new TenantInspectionAuthorization("tenant-a", canInspectSensitiveValues: false));

        var result = await handler.Handle(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        var instance = Assert.IsType<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceDetailsView>(result.Instance);
        Assert.Null(instance.Instance.CorrelationId);
        Assert.Empty(Assert.Single(instance.Activities).Metadata);
        var incident = Assert.Single(instance.Incidents);
        Assert.Equal("Incident details are redacted.", incident.Message);
        Assert.Empty(incident.Metadata);
        var output = Assert.Single(instance.Outputs).Value;
        Assert.True(output.IsRedacted);
        Assert.Null(output.Value);
    }

    [Fact]
    public async Task GetWorkflowInstance_HidesUnauthorizedTenantExistence()
    {
        await _workflowStore.SaveAsync(Workflow("wf-1", WorkflowExecutionStatus.Running, "definition-1", tenantId: "tenant-b"));
        var handler = new GetWorkflowInstanceRequestHandler(
            _workflowStore,
            _inspectionStore,
            _incidentStore,
            _durableValueStore,
            new DefaultRuntimePayloadCapturePolicy(),
            new TenantInspectionAuthorization("tenant-a", canInspectSensitiveValues: true));

        var result = await handler.Handle(new GetWorkflowInstance("wf-1"), CancellationToken.None);

        Assert.Null(result.Instance);
    }

    private static WorkflowExecutionState Workflow(
        string id,
        WorkflowExecutionStatus status,
        string definitionId,
        string? correlationId = null,
        DateTimeOffset? updatedAt = null,
        string? tenantId = null) =>
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
            TenantId: tenantId,
            SystemMetadata: new Dictionary<string, string>());

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

    private static IncidentState Incident(
        string workflowExecutionId,
        string incidentId,
        IReadOnlyDictionary<string, string>? metadata = null) =>
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
            resolvedAt: null,
            metadata: metadata);

    private static ActivityExecutionInspectionProjection Inspection(ActivityExecutionState state) =>
        ActivityExecutionInspectionProjection.FromState(
            state,
            checkpointId: $"checkpoint-{state.Execution.ActivityExecutionId}",
            committedAt: Now(-1));

    private static DateTimeOffset Now(int minutes) =>
        new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero) + TimeSpan.FromMinutes(minutes);

    private sealed class TenantInspectionAuthorization(string tenantId, bool canInspectSensitiveValues)
        : IActivityExecutionInspectionAuthorizationContext
    {
        public string TenantScope => $"tenant:{tenantId}";
        public string AuthorizationProfile => canInspectSensitiveValues ? "structure+values" : "structure";
        public bool CanInspectStructure(WorkflowExecutionState workflowExecution) =>
            workflowExecution.TenantId is null || StringComparer.Ordinal.Equals(workflowExecution.TenantId, tenantId);

        public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) =>
            CanInspectStructure(workflowExecution) && canInspectSensitiveValues;
    }
}
