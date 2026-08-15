using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public class ElsaEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(ElsaEndpointPermissions.ComposePolicy(permissions));
        Description(ElsaEndpointPermissions.StandardMetadata(GetType(), permissions));
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}
