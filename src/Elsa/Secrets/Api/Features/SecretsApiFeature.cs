using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Secrets.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Api.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Security")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "SecretsApi",
    DisplayName = "Secrets API",
    Description = "Provides HTTP endpoints for managing and resolving secret metadata."
)]
public class SecretsApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSecrets();
    }
}
