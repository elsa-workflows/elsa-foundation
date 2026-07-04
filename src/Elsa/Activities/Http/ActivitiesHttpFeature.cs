using CShells.Features;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
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
/// extractor, and registers the inbound <see cref="HttpEndpointMiddleware"/> (a host adds it to the pipeline
/// with <c>app.UseMiddleware&lt;HttpEndpointMiddleware&gt;()</c>).
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
public sealed class ActivitiesHttpFeature : IShellFeature
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
        // stimulus router. A host inserts it into the pipeline with app.UseMiddleware<HttpEndpointMiddleware>().
        services.AddScoped<HttpEndpointMiddleware>();
    }
}
