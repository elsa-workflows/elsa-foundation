using Elsa.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.FastEndpoints.Abstractions
{
    public abstract class ElsaEndpointWithoutRequest : EndpointWithoutRequest
    {
        protected void ConfigurePermissions(params string[] permissions)
        {
            if (!EndpointSecurityOptions.SecurityIsEnabled)
                AllowAnonymous();
            else
                Permissions([PermissionNames.All, .. permissions]);
        }
    }
}
