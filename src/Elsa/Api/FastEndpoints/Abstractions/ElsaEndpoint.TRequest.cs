using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public class ElsaEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Permissions(ElsaEndpointPermissions.Compose(permissions));
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}