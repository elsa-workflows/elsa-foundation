using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Expressions.Api.Authorization;

public sealed class ExpressionsPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Expressions.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(PermissionNames.ExpressionsRead, "Read expression metadata", "Expressions", "Read expression and variable-type descriptors.")
    ];
}
