using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Expressions.Api.Authorization;

public sealed class ExpressionsPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Expressions.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(ExpressionsPermissions.Read, "Read expression metadata", "Expressions", "Read expression and variable-type descriptors.")
    ];
}
