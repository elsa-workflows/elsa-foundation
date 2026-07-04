using CShells.Features;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http;

/// <summary>
/// HTTP activities. The activity types (<see cref="Activities.SendHttpRequest"/>) are resolved by the
/// runtime's <c>ClrActivityConstructor</c> — no per-type DI registration is required. This feature owns the
/// outbound transport: it configures the named <see cref="System.Net.Http.IHttpClientFactory"/> client
/// (<see cref="HttpActivityConstants.HttpClientName"/>) with the redirect/timeout policy from
/// <see cref="HttpActivityOptions"/>, so every <c>SendHttpRequest</c> instance shares one pooled, bounded
/// client rather than constructing its own.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Http")]
[ShellFeature(
    name: "ActivitiesHttp",
    DisplayName = "Activities HTTP",
    Description = "HTTP activities such as SendHttpRequest for calling external services."
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
    }
}
