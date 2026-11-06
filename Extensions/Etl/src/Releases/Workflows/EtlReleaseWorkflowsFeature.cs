using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Releases.Workflows;

/// <summary>Opt-in native RunEtl release inspection; it installs no activity worker or target admission authority.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ShellFeature(name: "EtlReleaseWorkflows", DisplayName = "ETL native workflow release inspection",
    Description = "Exact captured native RunEtl selection, pin, scope and resume inspection.",
    DependsOn = new object[] { "EtlReleaseVerification", "WorkflowReleaseRuntime" })]
public class EtlReleaseWorkflowsFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<EtlCapturedReleaseArtifacts>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReleaseOccurrenceInspector, RunEtlReleaseInspector>());
    }
}
