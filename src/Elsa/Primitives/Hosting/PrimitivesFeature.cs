using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Primitives.Hosting;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Primitives")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "Primitives",
    DisplayName = "Primitives",
    Description = "Registers primitive hosting services shared by Elsa foundation features."
)]
public sealed class PrimitivesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISystemClock, SystemClock>();
    }
}
