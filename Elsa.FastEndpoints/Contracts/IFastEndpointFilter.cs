using FastEndpoints;

namespace Elsa.FastEndpoints.Contracts
{
    public interface IFastEndpointFilter
    {
        bool Exclude(EndpointDefinition endpointDefinition);
    }
}
