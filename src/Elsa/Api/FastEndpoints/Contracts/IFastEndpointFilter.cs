using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Contracts
{
    public interface IFastEndpointFilter
    {
        bool Exclude(EndpointDefinition endpointDefinition);
    }
}
