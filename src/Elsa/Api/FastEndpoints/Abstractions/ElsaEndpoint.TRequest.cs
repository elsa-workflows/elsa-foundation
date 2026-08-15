using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public class ElsaEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(ElsaEndpointPermissions.ComposePolicy(permissions));
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}
