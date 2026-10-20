using Elsa.Etl.Api;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Design.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "EtlDesignApi", DisplayName = "ETL Design API", Description = "Author finite file ETL definitions and publish immutable versions.", DependsOn = new object[] { "ApiCapabilities", "EtlDesign" })]
public sealed class EtlDesignApiFeature : IWebShellFeature
{
    /// <summary>Stable shared signing key for pagination across host replicas; defaults to a process-local key.</summary>
    [ManifestSetting(Secret = true)]
    public string? CursorSigningKey { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        EtlApiServices.Add(services, CursorSigningKey, EtlDesignApi.OwnerId);
        services.TryAddScoped<IEtlDesignOperations, EtlDesignOperations>();
        services.AddPermissionContributor<EtlDesignPermissionContributor>();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.etl-design", 1,
        [
            new("etl-bindings", "etl/bindings"),
            new("etl-definitions", "etl/definitions"),
            new("etl-definition", "etl/definitions/{id}", true),
            new("etl-draft", "etl/definitions/{id}/draft", true),
            new("etl-validation", "etl/definitions/{id}/validate", true),
            new("etl-publication", "etl/definitions/{id}/publish", true),
            new("etl-versions", "etl/definitions/{id}/versions", true),
            new("etl-version", "etl/definitions/{id}/versions/{version}", true)
        ], "EtlDesignApi"));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => EtlDesignApi.MapEtlDesignApi(endpoints);
}
