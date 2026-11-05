using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

public sealed class ExternalAuthenticationApiOptions
{
    /// <summary>Backend-owned chooser path below the configured public backend base.</summary>
    public string LoginPath { get; set; } = "/_elsa/identity/login";
}

public static class ExternalAuthenticationApiRegistration
{
    public static IServiceCollection AddFoundationExternalAuthenticationApi(this IServiceCollection services, Action<ExternalAuthenticationApiOptions>? configure = null)
    {
        services.AddFoundationExternalAuthentication();
        services.AddElsaEndpoints();
        services.AddAuthentication();
        services.AddAntiforgery();
        ApiCapabilityRegistration.Add(services, new("elsa.api.identity.external-authentication", 1,
            [new("external-connections", "_elsa/identity/external-connections")], "FoundationIdentityExternalAuthenticationApi"));
        services.AddOptions<ExternalAuthenticationApiOptions>();
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderSource, ManagedConnectionProviderSource>());
        return services;
    }
}

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "FoundationIdentityExternalAuthenticationApi", DisplayName = "External authentication API",
    Description = "Backend-owned authentication, connection administration and administrator-only preview.",
    DependsOn = new object[] { "ApiCapabilities" })]
public class ExternalAuthenticationApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) => services.AddFoundationExternalAuthenticationApi();
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => ExternalAuthenticationApi.MapExternalAuthenticationApi(endpoints);
}
