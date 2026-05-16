using Elsa.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.FastEndpoints.Abstractions
{
    public class ElsaEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull, new()
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
