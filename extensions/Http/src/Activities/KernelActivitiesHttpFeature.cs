using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Capabilities;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Core.Options;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

    // Request-handler authentication schemes initialize request-scoped state before HTTP ingress uses it.
    public int Order => 1;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<KernelWorkflowHttpActivities>();

        var basePath = BasePath;
        var syncRequestTimeout = SyncRequestTimeout;
        services.AddHttpContextAccessor();
        services.Configure<HttpEndpointOptions>(options => options.BasePath = basePath);
        services.Configure<HttpEndpointOptions>(options => options.SyncRequestTimeout = syncRequestTimeout);
        services.Configure<HttpRoutePublicationOptions>(options => options.BasePath = basePath);
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<HttpEndpointOptions>>().Value;
            return new WorkflowHttpEndpointSettings(options.BasePath, options.SyncRequestTimeout, options.MaxRequestBodyBytes,
                static (context, fault, token) =>
                {
                    if (context.RequestServices?.GetService<IHttpEndpointFaultHandler>() is { } handler)
                        return handler.HandleAsync(new HttpEndpointFaultContext(context, [fault], token));

                    context.Response.StatusCode = HttpEndpointFaultMapping.ToStatusCode(fault);
                    return ValueTask.CompletedTask;
                });
        });

        services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<HttpActivityOptions>>().Value;
                return new HttpClientHandler
                {
                    AllowAutoRedirect = options.AllowAutoRedirect,
                    MaxAutomaticRedirections = options.MaxAutomaticRedirections
                };
            })
            // The activity enforces its own timeout with a linked CancellationTokenSource so it composes with
            // the workflow's cancellation token; the ambient client timeout is disabled to avoid double-timeout.
            .ConfigureHttpClient(client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
            // The factory's default logging handlers carry raw request and response header values as structured state
            // at Trace, where a state-serializing sink would print an Authorization value. They are
            // replaced by one logger that writes the same request lines without any header.
            .RemoveAllLoggers()
            .AddLogger<HttpActivityClientLogger>();
        services.TryAddSingleton<HttpActivityClientLogger>();
        services.TryAddScoped<IHttpConnectionApplicator, UnboundHttpConnectionApplicator>();

        // Contribute the HttpEndpoint start-trigger's stimulus provider so the publish-time trigger
        // extractor can recognize published HttpEndpoint nodes and index them. Enumerable so other activity
        // features add their own providers.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, HttpEndpointTriggerStimulusProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITestRunTriggerDriver, HttpTestRunDriver>());
        services.AddApiCapabilitySource<HttpWorkflowEndpointsCapabilitySource>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (Has<IActivityExecutionStateStore>())
            services.TryAddScoped<HttpResponseInstructionDelivery>();
        if (Has<ITestRunTargetStore>() && Has<IWorkflowTestScopeStore>() && Has<ITestRunTargetDispatcher>() &&
            Has<IWorkflowExecutionPartitionAccessor>())
            services.TryAddScoped<HttpTestRunIngressMiddleware>();
        if (Has<IRuntimeIngressReservationReader>())
        {
            if (!Has<IStandaloneTriggerIngressFactory>())
                throw new InvalidOperationException("Standalone HTTP reservations require the standalone trigger ingress factory.");
            services.TryAddScoped<HttpTriggerSourceContext>();
            services.TryAddScoped<IStandaloneTriggerSourceContext<StandaloneHttpTriggerSource>>(provider => provider.GetRequiredService<HttpTriggerSourceContext>());
            services.AddStandaloneTriggerIngress<StandaloneHttpTriggerSource>(StandaloneHttpTriggerSource.Descriptor);
            services.TryAddScoped<StandaloneHttpTriggerMiddleware>();
        }

        bool Has<T>() => services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(T));
    }

    public void UseMiddleware(IApplicationBuilder app, IHostEnvironment? environment)
    {
        var shellServices = app.ApplicationServices.GetRequiredService<IServiceProviderIsService>();
        if (shellServices.IsService(typeof(HttpTestRunIngressMiddleware)))
            app.UseMiddleware<HttpTestRunIngressMiddleware>();
        if (shellServices.IsService(typeof(StandaloneHttpTriggerMiddleware)))
            app.UseMiddleware<StandaloneHttpTriggerMiddleware>();
    }
}
