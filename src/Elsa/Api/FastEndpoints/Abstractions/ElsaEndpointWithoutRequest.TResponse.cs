using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public abstract class ElsaEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse> where TResponse : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(ElsaEndpointPermissions.ComposePolicy(permissions));
    }
}
