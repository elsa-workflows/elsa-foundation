using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public abstract class ElsaEndpointWithoutRequest : EndpointWithoutRequest
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Permissions(ElsaEndpointPermissions.Compose(permissions));
    }
}