using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Primitives.Extensions;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Http;

/// <summary>Projects durable standalone HTTP reservations into the shell route table.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("HTTP")]
[ShellFeature(
    name: "WorkflowsRuntimeStandaloneHttp",
    DisplayName = "Workflow Standalone HTTP",
    Description = "Provides standalone HTTP route projection, authorization and fault handling without the workflow runner.",
    DependsOn = new object[] { "Http", "Tasks" }
)]
public sealed class WorkflowsRuntimeStandaloneHttpFeature : IShellFeature, IPostConfigureShellServices
{
    [ManifestSetting(DisplayName = "Fault handler type", Description = "CLR type name of the HTTP endpoint fault handler implementation.", Category = "Services", Advanced = true)]
    public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Authorization handler type", Description = "CLR type name of the HTTP endpoint authorization handler implementation.", Category = "Services", Advanced = true)]
    public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Route table convergence interval (seconds)", Description = "Seconds between checks for changed reservations on other nodes.", Category = "Runtime", DefaultValue = "5")]
    public double RouteTableConvergenceIntervalSeconds { get; set; } = 5;

    [ManifestSetting(DisplayName = "Route table convergence max backoff (seconds)", Description = "Maximum interval while checks keep failing.", Category = "Runtime", DefaultValue = "60")]
    public double RouteTableConvergenceMaxBackoffSeconds { get; set; } = 60;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<StandaloneWorkflowHttpRouteProjection>();
        WorkflowsRuntimeHttpFeature.ConfigureSharedServices(services, FaultHandlerType, AuthorizationHandlerType,
            RouteTableConvergenceIntervalSeconds, RouteTableConvergenceMaxBackoffSeconds);
        services.AddScoped<IHttpEndpointRoutesResolver>(provider => new HttpEndpointRoutesResolver(null, null,
            provider.GetRequiredService<ILogger<HttpEndpointRoutesResolver>>(),
            provider.GetService<IRuntimeIngressReservationReader>(),
            provider.GetService<IPersistenceAccessContextAccessor>()));
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(LegacyWorkflowHttpRouting)))
            throw new InvalidOperationException("WorkflowsRuntimeStandaloneHttp and WorkflowsRuntimeHttp cannot serve the same shell.");
    }
}
