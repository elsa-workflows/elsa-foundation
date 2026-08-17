using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

/// <summary>
/// An endpoint that maps a request to a response.
/// </summary>
public abstract class ElsaEndpointWithMapper<TRequest, TMapper>
    : EndpointWithMapper<TRequest, TMapper> where TMapper : class, IRequestMapper where TRequest : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(ElsaEndpointPermissions.ComposePolicy(permissions));
        Description(ElsaEndpointPermissions.StandardMetadata(GetType(), permissions));
    }
}
