using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Authorization;

public sealed class ActivityDesignPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Activities.Design.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(PermissionNames.ActivityDesignRead, "Read activity designs", "Activity design", "Read activity catalogs and design metadata."),
        new(PermissionNames.ActivityDesignManage, "Manage activity designs", "Activity design", "Create and change activity definitions.", new HashSet<string> { PermissionNames.ActivityDesignRead })
    ];
}
