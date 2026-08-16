using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for RT-5: the operator incident query surface returns recorded incidents for a workflow execution,
/// supports a blocking-only filter, and signals a missing workflow so the endpoint can answer 404.
/// </summary>
public sealed class ListIncidentsRequestHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ReturnsAllIncidents_ForExistingWorkflow()
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var incidentStore = new InMemoryIncidentStateStore();
        await workflowStore.SaveAsync(NewWorkflowState());
        await incidentStore.TryAddAsync(NewIncident("incident-open", IncidentStatus.Open));
        await incidentStore.TryAddAsync(NewIncident("incident-blocking", IncidentStatus.Blocking));
        var handler = new ListIncidentsRequestHandler(workflowStore, incidentStore, new AllowAllActivityExecutionInspectionAuthorizationContext());

        var response = await handler.Handle(new ListIncidents("wfexec-1"), CancellationToken.None);

        Assert.True(response.WorkflowExists);
        Assert.Equal(2, response.Count);
    }

    [Fact]
    public async Task Handle_WithBlockingOnly_ReturnsOnlyBlockingIncidents()
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var incidentStore = new InMemoryIncidentStateStore();
        await workflowStore.SaveAsync(NewWorkflowState());
        await incidentStore.TryAddAsync(NewIncident("incident-open", IncidentStatus.Open));
        await incidentStore.TryAddAsync(NewIncident("incident-blocking", IncidentStatus.Blocking));
        var handler = new ListIncidentsRequestHandler(workflowStore, incidentStore, new AllowAllActivityExecutionInspectionAuthorizationContext());

        var response = await handler.Handle(new ListIncidents("wfexec-1", BlockingOnly: true), CancellationToken.None);

        Assert.True(response.WorkflowExists);
        var view = Assert.Single(response.Incidents);
        Assert.Equal("incident-blocking", view.IncidentId);
        Assert.True(view.IsBlocking);
    }

    [Fact]
    public async Task Handle_ForMissingWorkflow_ReportsWorkflowDoesNotExist()
    {
        var handler = new ListIncidentsRequestHandler(
            new InMemoryWorkflowExecutionStateStore(),
            new InMemoryIncidentStateStore(),
            new AllowAllActivityExecutionInspectionAuthorizationContext());

        var response = await handler.Handle(new ListIncidents("missing"), CancellationToken.None);

        Assert.False(response.WorkflowExists);
        Assert.Empty(response.Incidents);
    }

    [Fact]
    public async Task Handle_HidesUnauthorizedWorkflowExistence()
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStore.SaveAsync(NewWorkflowState());
        var handler = new ListIncidentsRequestHandler(
            workflowStore,
            new InMemoryIncidentStateStore(),
            new TestAuthorization(canInspectStructure: false, canInspectSensitiveValues: true));

        var response = await handler.Handle(new ListIncidents("wfexec-1"), CancellationToken.None);

        Assert.False(response.WorkflowExists);
        Assert.Empty(response.Incidents);
    }

    [Fact]
    public async Task Handle_RedactsIncidentDetailsWithoutValuePermission()
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var incidentStore = new InMemoryIncidentStateStore();
        await workflowStore.SaveAsync(NewWorkflowState());
        await incidentStore.TryAddAsync(NewIncident(
            "incident-blocking",
            IncidentStatus.Blocking,
            new Dictionary<string, string> { ["runtime.faultStackTrace"] = "secret-stack" }));
        var handler = new ListIncidentsRequestHandler(
            workflowStore,
            incidentStore,
            new TestAuthorization(canInspectStructure: true, canInspectSensitiveValues: false));

        var response = await handler.Handle(new ListIncidents("wfexec-1"), CancellationToken.None);

        var incident = Assert.Single(response.Incidents);
        Assert.Equal("Incident details are redacted.", incident.Message);
        Assert.Empty(incident.Metadata);
    }

    private WorkflowExecutionState NewWorkflowState() =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: WorkflowExecutionStatus.Faulted,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: _now,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private IncidentState NewIncident(
        string incidentId,
        IncidentStatus status,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            incidentId: incidentId,
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-1",
            severity: IncidentSeverity.Error,
            status: status,
            resolutionOutcome: null,
            failureType: "System.InvalidOperationException",
            message: "boom",
            createdAt: _now,
            resolvedAt: null,
            metadata: metadata);

    private sealed class TestAuthorization(bool canInspectStructure, bool canInspectSensitiveValues)
        : IActivityExecutionInspectionAuthorizationContext, IActivityExecutionInspectionAuthorizationContextAsync
    {
        public string TenantScope => "test";
        public string AuthorizationProfile => "test";
        public string AuditSubject => "test";
        public string RequestCorrelationId => "test-request";
        public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => canInspectStructure;
        public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => canInspectSensitiveValues;
        public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) => false;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationProfile);
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canInspectStructure);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canInspectSensitiveValues);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
}
