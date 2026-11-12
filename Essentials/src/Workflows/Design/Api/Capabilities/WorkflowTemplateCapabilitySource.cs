using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Capabilities;

internal sealed class WorkflowTemplateCapabilitySource(IWorkflowTemplateStore? store = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(store is null ? [] :
        [new(WorkflowDesignApiCapabilities.CapabilityId, 1,
            [new("workflow-templates", "design/workflows/templates"),
             new("workflow-template", "design/workflows/templates/{templateId}", templated: true),
             new("workflow-template-delete", "design/workflows/templates/{templateId}/delete", templated: true)],
            "WorkflowsDesignApi.Templates")]);
}
