using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Graph.Releases;

/// <summary>Optional inspection of the explicitly supported immutable reusable-graph profile.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ShellFeature(name: "ActivitiesGraphReleaseVerification", DisplayName = "Reusable Graph release verification",
    Description = "Inspects captured graph plans, provider data and occurrence overlays without recompilation.",
    DependsOn = new object[] { "WorkflowReleaseRuntime", "ActivitiesGraphRuntime" })]
public class GraphActivitiesReleaseVerificationFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseOccurrenceInspector, GraphReleaseOccurrenceInspector>());
}
