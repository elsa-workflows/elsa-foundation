using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Services;

internal static class WorkflowLibraryProjection
{
    public static WorkflowDefinitionView ToView(WorkflowLibraryDefinition item, bool includeOrganization = true) =>
        WithOrganization(new(item.Definition.Id, item.Definition.Name, item.Definition.Description,
            item.Definition.CreatedAt, item.Definition.LastModifiedAt, item.Definition.DeletedAt,
            item.DraftId, item.LatestVersionId, item.LatestVersion, item.VersionCount, item.PublicationState,
            item.DraftSourceVersionId, item.DraftSourceVersion, item.PublishedVersionId, item.PublishedVersion), item.Organization, includeOrganization)
        with { Favorite = includeOrganization && item.Favorite };

    public static WorkflowDefinitionView WithOrganization(WorkflowDefinitionView view, WorkflowOrganization organization, bool includeOrganization = true) =>
        includeOrganization ? view with { FolderId = organization.FolderId, FolderBreadcrumb = organization.FolderBreadcrumb, Tags = organization.Tags } : view;
}
