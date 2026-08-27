using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Attention.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "AttentionApi",
    DisplayName = "Attention API",
    Description = "Aggregates permission-scoped attention contributors through a shared query endpoint.")]
public sealed class AttentionApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddAttentionCore();
        services.AddPermissionContributor<AttentionPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        AttentionApi.MapAttentionApi(endpoints);
}
