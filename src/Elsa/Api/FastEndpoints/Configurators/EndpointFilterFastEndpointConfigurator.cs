using CShells.FastEndpoints.Contracts;
using Elsa.Api.FastEndpoints.Contracts;
using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Configurators
{
    internal sealed class EndpointFilterFastEndpointConfigurator(IEnumerable<IFastEndpointFilter> endpointFilters) : IFastEndpointsConfigurator
    {
        public void Configure(Config config)
        {
            config.Endpoints.Filter = endpointDefinition =>
            {
                foreach (var filter in endpointFilters)
                {
                    if (filter.Exclude(endpointDefinition))
                        return false;
                }

                return true;
            };
        }
    }
}
