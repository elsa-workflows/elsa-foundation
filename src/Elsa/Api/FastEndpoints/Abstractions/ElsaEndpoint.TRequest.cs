using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public class ElsaEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Permissions([PermissionNames.All, .. permissions]);
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}