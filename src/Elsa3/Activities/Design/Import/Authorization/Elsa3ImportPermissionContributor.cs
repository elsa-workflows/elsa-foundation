using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa3.Activities.Design.Import.Authorization;

public sealed class Elsa3ImportPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa3.Activities.Design.Import";

    public IEnumerable<Permission> Contribute() =>
    [
        new(PermissionNames.Elsa3ImportRead, "Read Elsa 3 imports", "Elsa 3 import", "Inspect Elsa 3 reusable activity import analyses and status."),
        new(PermissionNames.Elsa3ImportManage, "Manage Elsa 3 imports", "Elsa 3 import", "Upload and apply Elsa 3 reusable activity imports.", new HashSet<string> { PermissionNames.Elsa3ImportRead })
    ];
}
