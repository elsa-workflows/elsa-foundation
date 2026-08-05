using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.ExecutionEvidence.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Execution Evidence")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsExecutionEvidenceApi",
    DisplayName = "Workflows Execution Evidence API",
    Description = "HTTP transport for Execution Evidence.",
    DependsOn = new object[] { "WorkflowsExecutionEvidence", "ApiCapabilities" })]
public class WorkflowsExecutionEvidenceApiFeature : WorkflowsExecutionEvidenceFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
    }
}
