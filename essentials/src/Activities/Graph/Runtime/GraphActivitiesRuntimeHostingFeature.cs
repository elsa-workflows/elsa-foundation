using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Graph.Runtime;

/// <summary>Provides graph activity capabilities and descriptors for hosts with their own workflow runner.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesGraphRuntimeHosting",
    DisplayName = "Activities Graph Hosting",
    Description = "Graph activity construction and capability registration for Actor Foundation shells.",
    DependsOn = new object[] { "ActivitiesRuntimeHosting" })]
public sealed class GraphActivitiesRuntimeHostingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => GraphActivitiesRuntimeFeature.ConfigureGraphServices(services);
}
