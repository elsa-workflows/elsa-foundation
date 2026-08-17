using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Api.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Preferences.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "StudioPreferencesApi",
    DisplayName = "Studio Preferences API",
    Description = "Exposes authenticated, conditionally updated Studio preference documents.",
    DependsOn = new object[] { "StudioPreferences" })]
public class StudioPreferencesApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<StudioPreferenceScopeResolver>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        StudioPreferencesApi.MapStudioPreferencesApi(endpoints);
}
