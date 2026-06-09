using CShells.Features;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Http
{
    [ShellFeature(
        name: "WorkflowsRuntimeHttp",
        DisplayName = "Workflows Runtime HTTP",
        Description = "Provides HTTP endpoint routing, authorization, and fault handling for workflow runtime endpoints."
    )]
    public class WorkflowsRuntimeHttpFeature : IShellFeature
    {
        public string BasePath { get; set; } = string.Empty;

        public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).FullName!;

        public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).FullName!;

        public string RouteResolverType { get; set; } = typeof(HttpEndpointRoutesResolver).FullName!;

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
            RegisterRouteResolver(services);
        }

        private void RegisterFaultHandler(IServiceCollection services)
        {
            var type = FaultHandlerType.GetLoadedType();
            services.AddScoped(typeof(IHttpEndpointFaultHandler), type);
        }

        private void RegisterAuthorizationHandler(IServiceCollection services)
        {
            var type = AuthorizationHandlerType.GetLoadedType();
            services.AddScoped(typeof(IHttpEndpointAuthorizationHandler), type);
        }

        private void RegisterRouteResolver(IServiceCollection services)
        {
            var type = RouteResolverType.GetLoadedType();
            services.AddScoped(typeof(IHttpEndpointRoutesResolver), type);
        }
    }
}
