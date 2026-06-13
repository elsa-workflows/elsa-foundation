using CShells.Features;
using Elsa.Admin.Api.Contracts;
using Elsa.Admin.Api.Options;
using Elsa.Admin.Api.Services;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Admin.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Admin")]
[ManifestFeatureCategory("Api")]
[ShellFeature(
    name: "AdminApi",
    Description = "Provides the admin module manifest endpoint for the React admin shell."
)]
public class AdminApiFeature : FastEndpointsFeatureBase
{
    public AdminApiOptions Options { get; set; } = new();

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.TryAddScoped<IAdminModuleManifestProvider, AdminModuleManifestProvider>();
    }
}
