using Elsa.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.FastEndpoints.Abstractions
{
    public abstract class ElsaEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse> where TResponse : notnull
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
