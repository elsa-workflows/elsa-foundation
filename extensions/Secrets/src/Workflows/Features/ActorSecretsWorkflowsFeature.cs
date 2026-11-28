using CShells.Features;
using Elsa.Secrets.Workflows.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Workflows.Features;

/// <summary>Registers the workflow secret resolver in an Actor Foundation shell.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "ActorSecretsWorkflows",
    DisplayName = "Actor Workflow Secret Resolution",
    Description = "Registers the workflow secret resolver and validates its composition.",
    DependsOn = new object[] { "Secrets", "ActivitiesRuntimeHosting" })]
public sealed class ActorSecretsWorkflowsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddSecretsWorkflows();
}
