using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Etl.Api;
using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Runtime.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "EtlRuntimeApi", DisplayName = "ETL Runtime API", Description = "Inspect finite ETL runs and request cancellation.", DependsOn = new object[] { "ApiCapabilities" })]
public class EtlRuntimeApiFeature : IWebShellFeature
{
    /// <summary>Stable shared signing key for pagination across host replicas; defaults to a process-local key.</summary>
    [ManifestSetting(Secret = true)]
    public string? CursorSigningKey { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        EtlApiServices.Add(services, CursorSigningKey, EtlRuntimeApi.OwnerId);
        services.TryAddScoped<IEtlRuntimeOperations, EtlRuntimeOperations>();
        services.AddPermissionContributor<EtlRuntimePermissionContributor>();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.etl-runtime", 1,
        [
            new("etl-runs", "etl/runs"),
            new("etl-run", "etl/runs/{id}", true),
            new("etl-run-partitions", "etl/runs/{id}/partitions", true),
            new("etl-run-connector-partitions", "etl/runs/{id}/connector-partitions", true),
            new("etl-run-cancel", "etl/runs/{id}/cancel", true)
        ], "EtlRuntimeApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => EtlRuntimeApi.MapEtlRuntimeApi(endpoints);
}
