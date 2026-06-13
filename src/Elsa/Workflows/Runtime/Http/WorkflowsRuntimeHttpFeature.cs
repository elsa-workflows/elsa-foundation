using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Http;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("HTTP")]
[ShellFeature(
    name: "WorkflowsRuntimeHttp",
    DisplayName = "Workflows Runtime HTTP",
    Description = "Provides HTTP endpoint routing, authorization, and fault handling for workflow runtime endpoints."
)]
public class WorkflowsRuntimeHttpFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Base path", Description = "Base HTTP path used by workflow runtime endpoints.", Category = "Routing")]
    public string BasePath { get; set; } = string.Empty;

    [ManifestSetting(DisplayName = "Fault handler type", Description = "CLR type name of the HTTP endpoint fault handler implementation.", Category = "Services", Advanced = true)]

    public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).FullName!;

    [ManifestSetting(DisplayName = "Authorization handler type", Description = "CLR type name of the HTTP endpoint authorization handler implementation.", Category = "Services", Advanced = true)]

    public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).FullName!;

    [ManifestSetting(DisplayName = "Route resolver type", Description = "CLR type name of the HTTP endpoint route resolver implementation.", Category = "Services", Advanced = true)]

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