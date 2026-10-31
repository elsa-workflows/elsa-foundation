using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Attention.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "AttentionApi",
    DisplayName = "Attention API",
    Description = "Aggregates permission-scoped attention contributors through a shared query endpoint.",
    DependsOn = new object[] { "ApiCapabilities" }
)]
public sealed class AttentionApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.api.attention", 1,
            [new("attention-items", "_elsa/attention/items")],
            "AttentionApi"));
        services.AddAttentionCore();
        services.AddPermissionContributor<AttentionPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        AttentionApi.MapAttentionApi(endpoints);
}
