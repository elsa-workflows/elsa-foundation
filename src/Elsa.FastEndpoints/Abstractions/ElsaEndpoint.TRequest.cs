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

        protected Task SendError(string error, int statusCode = 500, string? errorCode = null, CancellationToken cancellationToken = default)
        {
            return Send.ResponseAsync(
                new { success = false, error, errorCode },
                statusCode,
                cancellationToken
            );
        }

        protected Task SendError(Exception exception, int statusCode = 500, string? errorCode = null, CancellationToken cancellationToken = default)
        {
            return Send.ResponseAsync(
                new { success = false, error = exception.Message, errorCode },
                statusCode,
                cancellationToken
            );
        }
    }
}
