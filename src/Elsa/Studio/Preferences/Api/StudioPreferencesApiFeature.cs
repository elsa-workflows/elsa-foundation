using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Preferences.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "StudioPreferencesApi",
    DisplayName = "Studio Preferences API",
    Description = "Exposes authenticated, conditionally updated Studio preference documents.",
    DependsOn = new object[] { "StudioPreferences" })]
public sealed class StudioPreferencesApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<StudioPreferenceScopeResolver>();
    }
}
