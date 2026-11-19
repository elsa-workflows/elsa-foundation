using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Workflows.Design.Api.Authorization;

public sealed class WorkflowDesignPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Design.Api";

    public string ContributorType => typeof(WorkflowDesignPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        new(WorkflowDesignPermissions.Read, "Read workflow designs", "Workflow design", "Read workflow definitions and their design metadata."),
        new(WorkflowDesignPermissions.Manage, "Manage workflow designs", "Workflow design", "Create and change workflow definitions."),
        new(WorkflowDesignPermissions.AssignTags, "Assign workflow tags", "Workflow design", "Change the classification of workflow definitions."),
        new(WorkflowDesignPermissions.ManageTags, "Manage workflow tags", "Workflow design", "Create and maintain the tenant's workflow tag vocabulary."),
        new(WorkflowDesignPermissions.ShareViews, "Share workflow views", "Workflow design", "Create and maintain shared workflow library views.")
    ];
}
