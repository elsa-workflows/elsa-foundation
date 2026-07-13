using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

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
            new("workflow-executable-provenance", "runtime/workflows/executables/{artifactId}/provenance", templated: true),
            new("workflow-execute", "runtime/workflows/executables/{artifactId}/execute", templated: true),
            new("workflow-instances", "runtime/workflows/instances"),
            new("workflow-instances-page", "runtime/workflows/instances/page"),
            new("workflow-instance", "runtime/workflows/instances/{workflowExecutionId}", templated: true),
            new("activity-execution", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true),
            new("workflow-incidents", "runtime/workflows/instances/{workflowExecutionId}/incidents", templated: true)
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
