using CShells.FastEndpoints.Contracts;
using CShells.FastEndpoints.Features;
using CShells.Features;
using Elsa.FastEndpoints.Configurators;
using Elsa.FastEndpoints.Contracts;
using Elsa.FastEndpoints.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.FastEndpoints
{
    public abstract class FastEndpointsFeatureBase : IFastEndpointsShellFeature
    {
        public IEnumerable<FastEndpointsFilter> EndpointFilters { get; set; } = [];

        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.TryAddEnumerable(
                [
                    new(
                        typeof(IFastEndpointsConfigurator), 
                        typeof(SerializationFastEndpointConfigurator), 
                        ServiceLifetime.Scoped
                    )                    
                ]    
            );

            if (EndpointFilters.Any())
            {
                services.AddScoped<IFastEndpointsConfigurator>(
                    sp => new EndpointFilterFastEndpointConfigurator(EndpointFilters)
                );
            }
        }
    }
}
