using Elsa.Api.Capabilities.Models;

namespace Elsa.Workflows.Runtime.Api.Capabilities;

public static class RuntimeApiCapabilities
{
    public const string CapabilityId = "elsa.api.runtime";
    public const string SourceFeatureId = "WorkflowsRuntimeApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
            new("runtime-persistence-mode", "runtime/workflows/persistence-mode"),
            new("workflow-executables", "runtime/workflows/executables"),
            new("workflow-executable", "runtime/workflows/executables/{artifactId}", templated: true),
            new("workflow-executable-input-sources", "runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources", templated: true),
            new("workflow-executable-provenance", "runtime/workflows/executables/{artifactId}/provenance", templated: true),
            new("workflow-execute", "runtime/workflows/executables/{artifactId}/execute", templated: true),
            new("workflow-activation-slots", "runtime/workflows/activation-slots/{definitionId}", templated: true),
            new("workflow-activation-slot", "runtime/workflows/activation-slots/{definitionId}/{slotName}", templated: true),
            new("workflow-instances", "runtime/workflows/instances"),
            new("workflow-instances-page", "runtime/workflows/instances/page"),
            new("workflow-instances-health-filter", "runtime/workflows/instances/page"),
            new("workflow-instance", "runtime/workflows/instances/{workflowExecutionId}", templated: true),
            new("activity-execution", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("activity-execution-boundary-detail", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("activity-execution-descendants", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants", templated: true),
            new("activity-execution-layout", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout", templated: true),
            new("activity-execution-attempt-lineage", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("activity-execution-bookmarks", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("activity-execution-incidents", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("activity-execution-value-evidence", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("activity-execution-value-payload", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload", templated: true),
            new("workflow-incidents", "runtime/workflows/instances/{workflowExecutionId}/incidents", templated: true),
            new("workflow-dispatches", "runtime/workflows/dispatches"),
            new("workflow-dispatch", "runtime/workflows/dispatches/{dispatchId}", templated: true),
            new("workflow-dispatch-redrive", "runtime/workflows/dispatches/{dispatchId}/redrive", templated: true)
        ],
        SourceFeatureId);
}

internal sealed class SelectedRunDeletionAvailable;
