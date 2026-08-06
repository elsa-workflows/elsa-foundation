using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.ExecutionEvidence;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Execution Evidence")]
[ShellFeature(
    name: "WorkflowsExecutionEvidence",
    DisplayName = "Workflows Execution Evidence",
    Description = "Provider-neutral registration for explicit execution-evidence capture.")]
public class WorkflowsExecutionEvidenceFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }
}
