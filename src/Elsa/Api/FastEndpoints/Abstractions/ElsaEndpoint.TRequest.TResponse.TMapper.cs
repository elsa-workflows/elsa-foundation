using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public class ElsaEndpoint<TRequest, TResponse, TMapper> : Endpoint<TRequest, TResponse, TMapper>
    where TRequest : notnull, new()
    where TResponse : notnull
    where TMapper : class, IMapper, new()
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Permissions(ElsaEndpointPermissions.Compose(permissions));
    }
}