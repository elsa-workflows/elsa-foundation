using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Attention.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "AttentionApi",
    DisplayName = "Attention API",
    Description = "Aggregates permission-scoped attention contributors through a shared query endpoint.")]
public sealed class AttentionApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddAttentionCore();
    }
}
