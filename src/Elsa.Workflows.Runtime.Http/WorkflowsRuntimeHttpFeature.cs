using CShells.Features;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Http
{
    public class WorkflowsRuntimeHttpFeature : IShellFeature
    {
        public string BasePath { get; set; } = string.Empty;

        public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).FullName!;

        public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).FullName!;

        public string RouteProviderType { get; set; } =  typeof(HttpEndpointRoutesProvider).FullName!;        

        public void ConfigureServices(IServiceCollection services)
        {
            // Startup tasks.
            //.AddStartupTask<UpdateRouteTableStartupTask>()
            // Very important we will make sure the Route Table is filled at start up, unless we come up with a better way to read endpoint routes!

            services.Configure<WorkflowsRuntimeHttpFeatureOptions>(o =>
            {
                o.BasePath = BasePath;
            });

            RegisterFaultHandler(services);
            RegisterAuthorizationHandler(services);
            RegisterRouteProvider(services);
        }

        void RegisterFaultHandler(IServiceCollection services)
        {           
            var type = FaultHandlerType.GetLoadedType();
            services.AddScoped(typeof(IHttpEndpointFaultHandler), type);
        }

        void RegisterAuthorizationHandler(IServiceCollection services)
        {
            var type = AuthorizationHandlerType.GetLoadedType();
            services.AddScoped(typeof(IHttpEndpointAuthorizationHandler), type);
        }

        void RegisterRouteProvider(IServiceCollection services)
        {
            var type = RouteProviderType.GetLoadedType();
            services.AddScoped(typeof(IHttpEndpointRoutesProvider), type);
        }
    }
}
