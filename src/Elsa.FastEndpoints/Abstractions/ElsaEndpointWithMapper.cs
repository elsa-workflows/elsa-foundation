using Elsa.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.FastEndpoints.Abstractions
{

    /// <summary>
    /// An endpoint that maps a request to a response.
    /// </summary>
    public abstract class ElsaEndpointWithMapper<TRequest, TMapper>
        : EndpointWithMapper<TRequest, TMapper> where TMapper : class, IRequestMapper where TRequest : notnull
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
