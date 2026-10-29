using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Http.Capabilities;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Http.Core.Options;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http;

/// <summary>
/// HTTP activities. CLR activity types are transiently activated by the runtime — no per-type DI registration
/// is required. This feature owns the outbound transport (the named
/// <see cref="System.Net.Http.IHttpClientFactory"/> client configured from <see cref="HttpActivityOptions"/>),
/// contributes the <see cref="HttpEndpoint"/> start-trigger's stimulus provider to the publish-time trigger
/// extractor, and mounts the inbound <see cref="HttpEndpointMiddleware"/> into the shell's request pipeline
/// through the CShells middleware seam (<see cref="IMiddlewareShellFeature"/>) — enabling the feature is all a
/// host needs; no manual <c>UseMiddleware</c> call.
/// </summary>
/// <remarks>
/// Depends on <c>WorkflowsRuntimeHttp</c> (not just <c>Http</c>): that feature contributes the route-table
/// populators (<c>UpdateRouteTableStartupTask</c>, <c>RouteTableTriggerIndexObserver</c>,
/// <c>IHttpEndpointRoutesResolver</c>) that fill the route table from the durable trigger index. Enabling the
/// endpoint activity alone without them would compose an empty route table, so every endpoint 404s — the
/// DependsOn closure guarantees the populators always compose with this feature. No cycle: WorkflowsRuntimeHttp
/// depends only on <c>Http</c> and <c>WorkflowsRuntimeTriggers</c>, not on <c>ActivitiesHttp</c>.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("HTTP")]
[ShellFeature(
    name: "ActivitiesHttp",
    DisplayName = "Activities HTTP",
    Description = "HTTP activities: SendHttpRequest, the HttpEndpoint start trigger, and WriteHttpResponse.",
    DependsOn = new object[] { "Http", "WorkflowsRuntimeHttp" }
)]
public sealed class ActivitiesHttpFeature : IShellFeature, IMiddlewareShellFeature
{
    [ManifestSetting(DisplayName = "Base path", Description = "Base HTTP path used by workflow endpoints.", Category = "Routing")]
    public string BasePath { get; set; } = "/workflows/http";

    /// <summary>
    /// Runs after the identity shell feature (order 0), which installs authentication middleware. Request-handler
    /// authentication schemes such as OpenIddict must handle the request before this feature's ingress explicitly
    /// calls AuthenticateAsync, because they initialize request-scoped authentication state in that earlier pass.
    /// </summary>
    public int Order => 1;

    [ManifestSetting(DisplayName = "Synchronous request timeout", Description = "Maximum duration of synchronous HTTP requests. An endpoint may select a shorter timeout.", Category = "Routing", UIHint = "duration")]
    public TimeSpan SyncRequestTimeout { get; set; } = new HttpEndpointOptions().SyncRequestTimeout;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddApiCapabilitySource<HttpWorkflowEndpointsCapabilitySource>();
        services.Configure<HttpEndpointOptions>(options => options.BasePath = BasePath);
        services.Configure<HttpEndpointOptions>(options => options.SyncRequestTimeout = SyncRequestTimeout);
        services.Configure<HttpRoutePublicationOptions>(options => options.BasePath = BasePath);

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

        // The inbound request middleware is resolved from DI per request (IMiddleware) so it can take the scoped
        // stimulus router; mounted into the shell pipeline by UseMiddleware below. CShells guarantees an
        // IMiddlewareFactory in the shell container, so only the middleware itself needs registering.
        services.AddScoped<HttpEndpointMiddleware>();
        services.AddScoped<HttpTestRunIngressMiddleware>();

        // Request-owned synchronous response services. The activity returns a typed instruction inside its
        // isolated attempt scope; delivery reads the committed completion only after the inline drain returns.
        // The sink remains the scoped marker for a custom response that already started during dispatch.
        services.AddScoped<SyncHttpResponseSink>();
        services.AddScoped<HttpResponseInstructionDelivery>();
        services.AddScoped<HttpResponseDeliveryEvidence>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerDrainObserver, HttpResponseDeliveryDrainObserver>());
    }

    /// <summary>
    /// Mounts the endpoint middleware in the shell's pipeline. CShells composes this per shell and runs it only
    /// for requests resolved to this shell, right after shell resolution — so <c>HttpContext.RequestServices</c>
    /// is already the shell scope — including shells activated dynamically at runtime. Non-endpoint requests pass
    /// through.
    /// <para>
    /// <see cref="IMiddlewareShellFeature.Order"/> places this feature after the identity shell feature (order 0),
    /// which mounts authentication and authorization middleware in the shell branch. This ordering lets request-
    /// handler authentication schemes initialize request-scoped state before the TestRun ingress performs its
    /// explicit authentication fallback. The fallback remains useful for hosts that populate
    /// <c>HttpContext.User</c> themselves or use handlers that do not require an earlier request-handler pass.
    /// </para>
    /// </summary>
    public void UseMiddleware(IApplicationBuilder app, Microsoft.Extensions.Hosting.IHostEnvironment? environment)
    {
        app.UseMiddleware<HttpTestRunIngressMiddleware>();
        app.UseMiddleware<HttpEndpointMiddleware>();
    }
}
