using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDiagnosticsHistoryIncidentContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RuntimeHistoryEventCategories_ContainLockedObservabilityCategories()
    {
        var categoryNames = Enum.GetNames<RuntimeHistoryEventCategory>();

        Assert.Contains(nameof(RuntimeHistoryEventCategory.WorkflowLifecycle), categoryNames);
        Assert.Contains(nameof(RuntimeHistoryEventCategory.ActivityLifecycle), categoryNames);
        Assert.Contains(nameof(RuntimeHistoryEventCategory.BookmarkLifecycle), categoryNames);
        Assert.Contains(nameof(RuntimeHistoryEventCategory.ValueLifecycle), categoryNames);
        Assert.Contains(nameof(RuntimeHistoryEventCategory.IncidentLifecycle), categoryNames);
        Assert.Contains(nameof(RuntimeHistoryEventCategory.SchedulerLifecycle), categoryNames);
        Assert.Contains(nameof(RuntimeHistoryEventCategory.OperationalLifecycle), categoryNames);
    }

    [Fact]
    public void WorkflowAndActivityLifecycleHistoryEvents_CarryRuntimeIdentitiesOnly()
    {
        var identity = new WorkflowExecutableIdentity(
            ArtifactId: "artifact-1",
            DefinitionId: "orders",
            DefinitionVersionId: "orders-v7",
            ArtifactVersion: "7.0.0",
            ArtifactHash: "sha256:artifact");
        var workflowEvent = new WorkflowLifecycleHistoryEvent(
            historyEventId: "history-workflow-started",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionStatus.Running,
            name: "WorkflowStarted",
            occurredAt: _now,
            pinnedExecutable: identity);
        var activityEvent = new ActivityLifecycleHistoryEvent(
            historyEventId: "history-activity-started",
            execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-send",
                AuthoredActivityId: "activity-send",
                ActivityType: "Elsa.SendEmail",
                ActivityTypeVersion: "1.0.0"),
            status: ActivityExecutionStatus.Running,
            name: "ActivityStarted",
            occurredAt: _now);

        Assert.Equal("artifact-1", workflowEvent.PinnedExecutable!.ArtifactId);
        Assert.Equal("actexec-1", activityEvent.Execution.ActivityExecutionId);
        Assert.Equal("node-send", activityEvent.Execution.ExecutableNodeId);
        Assert.DoesNotContain("Design", PublicTypeNames(workflowEvent));
        Assert.DoesNotContain("Design", PublicTypeNames(activityEvent));
    }

    [Fact]
    public void IncidentState_IsQueryableRuntimeStateWhileIncidentHistoryCarriesProjectionPayload()
    {
        var incident = NewIncident(IncidentStatus.Blocking);
        var projection = new IncidentHistoryProjection(
            historyEventId: "history-incident-1",
            incidentId: incident.IncidentId,
            workflowExecutionId: incident.WorkflowExecutionId,
            activityExecutionId: incident.ActivityExecutionId,
            executableNodeId: incident.ExecutableNodeId,
            activityType: "Elsa.SendEmail",
            severity: incident.Severity,
            status: incident.Status,
            failureType: incident.FailureType,
            message: incident.Message,
            exceptionType: "System.InvalidOperationException",
            stackTrace: "at test",
            occurredAt: _now,
            diagnosticPayload: Json("""{"exceptionMessage":"boom"}"""));

        var blockingIncidents = new[] { incident, NewIncident(IncidentStatus.Resolved, "incident-2") }
            .Where(item => item.IsBlocking)
            .ToArray();

        Assert.Single(blockingIncidents);
        Assert.Equal("incident-1", blockingIncidents[0].IncidentId);
        Assert.Equal("boom", projection.DiagnosticPayload!.Value.GetProperty("exceptionMessage").GetString());
    }

    [Fact]
    public void RuntimeContinuationState_DoesNotDependOnHistoryOrAuditRecords()
    {
        var stateChangeProperties = typeof(RuntimeCheckpointStateChangeSet)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var stateCategoryNames = Enum.GetNames<RuntimeStateCategory>();

        Assert.Contains(nameof(RuntimeCheckpointStateChangeSet.Incidents), stateChangeProperties);
        Assert.DoesNotContain(stateChangeProperties, name => name.Contains("History", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(stateChangeProperties, name => name.Contains("Audit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(stateCategoryNames, name => name.Contains("History", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(stateCategoryNames, name => name.Contains("Audit", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(RuntimePayloadCaptureSubject.WorkflowInput)]
    [InlineData(RuntimePayloadCaptureSubject.WorkflowOutput)]
    [InlineData(RuntimePayloadCaptureSubject.ActivityInput)]
    [InlineData(RuntimePayloadCaptureSubject.ActivityOutput)]
    public void DefaultPayloadCapturePolicy_OmitsInputAndOutputSnapshots(RuntimePayloadCaptureSubject subject)
    {
        IRuntimePayloadCapturePolicy policy = new DefaultRuntimePayloadCapturePolicy();

        var decision = policy.Decide(NewCaptureRequest(subject));

        Assert.Equal(RuntimePayloadCaptureMode.None, decision.Mode);
        Assert.False(decision.CapturesPayload);
    }

    [Fact]
    public void DefaultPayloadCapturePolicy_ExcludesSensitiveValuesAndKeepsIncidentPayloadsMetadataOnly()
    {
        IRuntimePayloadCapturePolicy policy = new DefaultRuntimePayloadCapturePolicy();

        var sensitiveDecision = policy.Decide(NewCaptureRequest(RuntimePayloadCaptureSubject.Incident, isSensitive: true));
        var incidentDecision = policy.Decide(NewCaptureRequest(RuntimePayloadCaptureSubject.Incident));

        Assert.Equal(RuntimePayloadCaptureMode.None, sensitiveDecision.Mode);
        Assert.Equal(RuntimePayloadCaptureMode.MetadataOnly, incidentDecision.Mode);
        Assert.False(incidentDecision.CapturesPayload);
    }

    private IncidentState NewIncident(IncidentStatus status, string incidentId = "incident-1") =>
        new(
            incidentId: incidentId,
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-send",
            severity: IncidentSeverity.Error,
            status: status,
            resolutionAction: IncidentResolutionAction.FaultWorkflow,
            failureType: "ActivityFaulted",
            message: "Activity failed.",
            createdAt: _now,
            resolvedAt: status == IncidentStatus.Resolved ? _now.AddMinutes(1) : null);

    private RuntimePayloadCaptureRequest NewCaptureRequest(RuntimePayloadCaptureSubject subject, bool isSensitive = false) =>
        new(
            subject: subject,
            workflowExecutionId: "wfexec-1",
            requestedAt: _now,
            activityExecutionId: "actexec-1",
            valueName: "payload",
            isSensitive: isSensitive);

    private static string PublicTypeNames(object instance) =>
        string.Join(
            '|',
            instance.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType.FullName)
                .Where(name => name is not null));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
