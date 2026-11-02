using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Api.Services;

namespace Elsa.Workflows.Design.Api.Capabilities;

internal sealed class WorkflowLibraryCapabilitySource(IWorkflowLibraryStore? store = null,
    WorkflowOrganizationOptions? organization = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(store is null ? [] :
        [new(WorkflowDesignApiCapabilities.CapabilityId, 1,
            [new("workflow-definitions-page", "design/workflows/definitions/page"),
             new("workflow-definitions-query", "design/workflows/definitions/page"),
             ..organization is { Enabled: false } ? Array.Empty<ApiCapabilityLink>() : [new("workflow-folders", "design/workflows/folders"),
             new("workflow-folder-rename", "design/workflows/folders/{folderId}/rename", templated: true),
             new("workflow-folder-move", "design/workflows/folders/{folderId}/move", templated: true),
             new("workflow-folder-delete-empty", "design/workflows/folders/{folderId}", templated: true),
             new("workflow-definition-folder-move", "design/workflows/definitions/move"),
             new("workflow-organization", "design/workflows/definitions/{definitionId}/organization", templated: true),
             new("workflow-tags", "design/workflows/tags"),
             new("workflow-tag", "design/workflows/tags/{tagId}", templated: true),
             new("workflow-tags-bulk", "design/workflows/tags/bulk"),
             new("workflow-views", "design/workflows/views"),
             new("workflow-view", "design/workflows/views/{viewId}", templated: true),
             new("workflow-favorites", "design/workflows/definitions/{definitionId}/favorite", templated: true)]],
            "WorkflowsDesignApi.Library")]);
}
