using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http;

/// <summary>
/// HTTP activities. The activity types (<see cref="Activities.SendHttpRequest"/>, <see cref="HttpEndpoint"/>,
/// <see cref="WriteHttpResponse"/>) are resolved by the runtime's <c>ClrActivityConstructor</c> — no per-type
/// DI registration is required. This feature owns the outbound transport (the named
/// <see cref="System.Net.Http.IHttpClientFactory"/> client configured from <see cref="HttpActivityOptions"/>),
/// contributes the <see cref="HttpEndpoint"/> start-trigger's stimulus provider to the publish-time trigger
/// extractor, and mounts the inbound <see cref="HttpEndpointMiddleware"/> into the shell's request pipeline
/// through the CShells middleware seam (<see cref="IMiddlewareShellFeature"/>) — enabling the feature is all a
/// host needs; no manual <c>UseMiddleware</c> call (spec 089 FR-003).
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Http")]
[ShellFeature(
    name: "ActivitiesHttp",
    DisplayName = "Activities HTTP",
    Description = "HTTP activities: SendHttpRequest, the HttpEndpoint start trigger, and WriteHttpResponse."
)]
public sealed class ActivitiesHttpFeature : IShellFeature, IMiddlewareShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
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
            .ConfigureHttpClient(client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan);

        // Contribute the HttpEndpoint start-trigger's stimulus provider (W7 seam) so the publish-time trigger
        // extractor can recognize published HttpEndpoint nodes and index them. Enumerable so other activity
        // features add their own providers.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, HttpEndpointTriggerStimulusProvider>());

        // The inbound request middleware is resolved from DI per request (IMiddleware) so it can take the scoped
        // stimulus router; mounted into the shell pipeline by UseMiddleware below. CShells guarantees an
        // IMiddlewareFactory in the shell container, so only the middleware itself needs registering.
        services.AddScoped<HttpEndpointMiddleware>();
    }

    /// <summary>
    /// Mounts the endpoint middleware in the shell's pipeline. CShells composes this per shell and runs it only
    /// for requests resolved to this shell, right after shell resolution — so <c>HttpContext.RequestServices</c>
    /// is already the shell scope — including shells activated dynamically at runtime. Non-endpoint requests pass
    /// through. <see cref="IMiddlewareShellFeature.Order"/> is left at its default (0): this is the only shell
    /// middleware the platform mounts today; give it an explicit order if middleware that must run earlier
    /// (e.g. authentication) joins the shell pipeline.
    /// </summary>
    public void UseMiddleware(IApplicationBuilder app, Microsoft.Extensions.Hosting.IHostEnvironment? environment)
    {
        app.UseMiddleware<HttpEndpointMiddleware>();
    }
}
