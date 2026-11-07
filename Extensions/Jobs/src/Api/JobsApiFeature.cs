using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MinimalEndpoints;

namespace Elsa.Jobs.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Jobs")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "JobsApi", DisplayName = "Jobs API", Description = "Provides authorized durable Jobs operations.",
    DependsOn = new object[] { typeof(ApiCapabilitiesFeature) })]
public sealed class JobsApiFeature : IWebShellFeature
{
    public string PersistencePartition { get; set; } = "default";
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddJobsApi(options => options.PersistencePartition = PersistencePartition);
        services.AddDynamicEndpointApiExplorerRefresh();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => JobsApi.MapJobsApi(endpoints);
}
