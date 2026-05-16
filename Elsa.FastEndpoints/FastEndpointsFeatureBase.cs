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
            RegisterFastEndpointFilters(services);

            services.TryAddEnumerable(
                [
                    new(typeof(IFastEndpointsConfigurator), typeof(SerializationFastEndpointConfigurator), ServiceLifetime.Scoped),
                    new(typeof(IFastEndpointsConfigurator), typeof(EndpointFilterFastEndpointConfigurator), ServiceLifetime.Scoped)
                ]    
            );
        }

        protected void RegisterFastEndpointFilters(IServiceCollection services)
        {
            services.AddScoped<IEnumerable<IFastEndpointFilter>>(sp => EndpointFilters);
        }
    }
}
