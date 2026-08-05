using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.ExecutionEvidence.InMemory;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Execution Evidence")]
[ShellFeature(
    name: "WorkflowsExecutionEvidenceInMemory",
    DisplayName = "Workflows Execution Evidence InMemory",
    Description = "Process-local Execution Evidence provider.",
    DependsOn = new object[] { "WorkflowsExecutionEvidence" })]
public class WorkflowsExecutionEvidenceInMemoryFeature : WorkflowsExecutionEvidenceFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
    }
}
