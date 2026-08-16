using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Abstractions;

public abstract class ElsaEndpointWithoutRequest : EndpointWithoutRequest
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(ElsaEndpointPermissions.ComposePolicy(permissions));
        Description(ElsaEndpointPermissions.StandardMetadata(GetType(), permissions));
    }
}
