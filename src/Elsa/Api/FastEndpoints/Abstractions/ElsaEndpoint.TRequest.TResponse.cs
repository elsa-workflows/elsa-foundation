using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public class ElsaEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Permissions([PermissionNames.All, .. permissions]);
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}