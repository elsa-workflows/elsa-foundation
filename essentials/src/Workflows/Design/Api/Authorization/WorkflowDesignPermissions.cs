namespace Elsa.Workflows.Design.Api.Authorization;

/// <summary>Catalog keys owned by the Workflow Design API.</summary>
public static class WorkflowDesignPermissions
{
    public const string Read = "workflows/design:read";
    public const string Manage = "workflows/design:manage";

    public const string AssignTags = "workflows/design/tag-assignments:manage";

    public const string ManageTags = "workflows/design/tags:manage";

    public const string ShareViews = "workflows/design/views:share";
}
