using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;

namespace Elsa.Workflows.Design.Api.Capabilities;

/// <summary>Metadata discovery is independent of optional expression runtimes and editor modules.</summary>
internal sealed class WorkflowActivityDataCapabilitySource : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([
            new("elsa.api.activity-data-selection", 1, [new("workflow-data-context", "design/workflows/activity-data/context")],
                "WorkflowsDesignApi.ActivityData")
        ]);
}
