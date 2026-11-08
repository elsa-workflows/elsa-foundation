using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Capabilities;

public static class RuntimeApiCapabilities
{
    public const string CapabilityId = "elsa.api.runtime";
    public const string SourceFeatureId = "WorkflowsRuntimeApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
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

public sealed class RuntimeOperationalCapabilitySource(
    IRuntimeDiagnosticsSettingsStore? diagnosticsSettingsStore = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = diagnosticsSettingsStore is null
            ? []
            : [new(
                RuntimeApiCapabilities.CapabilityId,
                1,
                [new ApiCapabilityLink("runtime-diagnostics", "runtime/workflows/diagnostics/settings")],
                $"{RuntimeApiCapabilities.SourceFeatureId}.Operational")];
        return ValueTask.FromResult(declarations);
    }
}

/// <summary>Advertises browsing only for inspection providers that implement the bounded collection read.</summary>
public sealed class RuntimeActivityExecutionBrowsingCapabilitySource(IActivityExecutionInspectionStore? inspectionStore = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(inspectionStore?.SupportsExecutionBrowsing == true
            ? [new(RuntimeApiCapabilities.CapabilityId, 1,
                [new ApiCapabilityLink("activity-executions", "runtime/workflows/instances/{workflowExecutionId}/activity-executions", templated: true)],
                $"{RuntimeApiCapabilities.SourceFeatureId}.ExecutionBrowsing")]
            : []);
}

/// <summary>Advertises alteration routes only when this shell composed both durable plan state and the orchestration service.</summary>
public sealed class RuntimeAlterationCapabilitySource(IServiceProvider serviceProvider) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var hasStore = serviceProvider.GetService<IWorkflowAlterationStore>() is not null;
        var hasPlanService = serviceProvider.GetService<WorkflowAlterationPlanService>() is not null;
        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = !hasStore || !hasPlanService
            ? []
            : [new(
                RuntimeApiCapabilities.CapabilityId,
                1,
                [
                    new ApiCapabilityLink("workflow-alteration-plans", "runtime/workflows/alteration-plans"),
                    new ApiCapabilityLink("workflow-alteration-plan", "runtime/workflows/alteration-plans/{planId}", templated: true),
                    new ApiCapabilityLink("workflow-alteration-plan-jobs-page", "runtime/workflows/alteration-plans/{planId}/jobs/page", templated: true),
                    new ApiCapabilityLink("workflow-alteration-job", "runtime/workflows/alteration-plans/{planId}/jobs/{jobId}", templated: true),
                    new ApiCapabilityLink("workflow-alteration-plan-cancel", "runtime/workflows/alteration-plans/{planId}/cancel", templated: true)
                ],
                $"{RuntimeApiCapabilities.SourceFeatureId}.Alterations")];
        return ValueTask.FromResult(declarations);
    }
}

/// <summary>
/// Advertises the activity state view (spec 260 R-6) only when the shell composed at least one projector, so a client
/// never follows a link that can only answer <c>NoProjector</c>.
/// </summary>
public sealed class RuntimeActivityStateViewCapabilitySource(IServiceProvider services) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = services.GetServices<IActivityStateViewProjector>().Any()
            ? [new(RuntimeApiCapabilities.CapabilityId, 1,
                [new ApiCapabilityLink("activity-execution-state-view", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view", templated: true)],
                $"{RuntimeApiCapabilities.SourceFeatureId}.ActivityStateView")]
            : [];
        return ValueTask.FromResult(declarations);
    }
}

/// <summary>Deletion requires a cohesive provider capable of atomic, fenced run cleanup.</summary>
public sealed class RuntimeRunDeletionCapabilitySource(IServiceProvider services) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = services.GetService<IWorkflowExecutionDeletionStore>() is not { IsSupported: true }
            ? []
            : [new(RuntimeApiCapabilities.CapabilityId, 1,
                [new ApiCapabilityLink("workflow-instance-delete", "runtime/workflows/instances/{workflowExecutionId}", templated: true)],
                $"{RuntimeApiCapabilities.SourceFeatureId}.RunDeletion")];
        return ValueTask.FromResult(declarations);
    }
}
