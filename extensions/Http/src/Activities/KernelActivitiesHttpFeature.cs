using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Http.Capabilities;
using Elsa.Activities.Http.Middleware;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Activities.Http;

/// <summary>HTTP activities and request-owned ingress for shells where Actor Foundation serves authored endpoints.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("HTTP")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Kernel Activities HTTP",
    Description = "HTTP activity support, isolated Test Runs and standalone ingress for Actor Foundation shells.",
    DependsOn = new object[] { "Http", "WorkflowsRuntimeStandaloneHttp" }
)]
public sealed class KernelActivitiesHttpFeature : IShellFeature, IMiddlewareShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "KernelActivitiesHttp";

    [ManifestSetting(DisplayName = "Base path", Description = "Base HTTP path used by workflow endpoints.", Category = "Routing")]
    public string BasePath { get; set; } = new HttpEndpointOptions().BasePath;

    [ManifestSetting(DisplayName = "Synchronous request timeout", Description = "Maximum duration of synchronous HTTP requests.", Category = "Routing", UIHint = "duration")]
    public TimeSpan SyncRequestTimeout { get; set; } = new HttpEndpointOptions().SyncRequestTimeout;

    public int Order => 1;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<KernelWorkflowHttpActivities>();
        ActivitiesHttpFeature.ConfigureSharedServices(services, BasePath, SyncRequestTimeout);
        services.AddApiCapabilitySource<KernelHttpWorkflowEndpointsCapabilitySource>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (Has<HttpEndpointMiddleware>())
            throw new InvalidOperationException("KernelActivitiesHttp and legacy authored HTTP middleware cannot serve the same shell.");
        if (Has<ITestRunTargetStore>() && Has<IWorkflowTestScopeStore>() && Has<ITestRunTargetDispatcher>() &&
            Has<IWorkflowExecutionPartitionAccessor>())
            services.TryAddScoped<HttpTestRunIngressMiddleware>();
        if (Has<IRuntimeIngressReservationReader>())
        {
            if (!Has<IStandaloneTriggerIngressFactory>())
                throw new InvalidOperationException("Standalone HTTP reservations require the standalone trigger ingress factory.");
            ActivitiesHttpFeature.AddStandaloneHttpIngress(services);
        }

        bool Has<T>() => services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(T));
    }

    public void UseMiddleware(IApplicationBuilder app, IHostEnvironment? environment)
    {
        var shellServices = app.ApplicationServices.GetRequiredService<IServiceProviderIsService>();
        ActivitiesHttpFeature.UseSharedMiddleware(app,
            mountTestRuns: shellServices.IsService(typeof(HttpTestRunIngressMiddleware)),
            mountStandalone: shellServices.IsService(typeof(StandaloneHttpTriggerMiddleware)));
    }
}
