using Elsa.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.FastEndpoints.Abstractions
{
    public class ElsaEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull
    {
        protected void ConfigurePermissions(params string[] permissions)
        {
            if (!EndpointSecurityOptions.SecurityIsEnabled)
                AllowAnonymous();
            else
                Permissions([PermissionNames.All, .. permissions]);
        }

        protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
    }
}
