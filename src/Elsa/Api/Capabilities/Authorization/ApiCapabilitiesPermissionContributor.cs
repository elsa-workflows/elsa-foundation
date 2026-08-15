using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Api.Capabilities.Authorization;

public sealed class ApiCapabilitiesPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Api.Capabilities";

    public IEnumerable<Permission> Contribute() =>
    [
        new(PermissionNames.ApiCapabilitiesRead, "Read API capabilities", "API capabilities", "Read the active first-party API capability document.")
    ];
}
