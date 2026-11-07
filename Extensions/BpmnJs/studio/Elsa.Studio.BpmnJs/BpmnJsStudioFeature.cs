using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.BpmnJs;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("bpmn-js", "bpmn-js editor", "workflow-designer-canvas")]
[ShellFeature(name: "BpmnJsStudio", DisplayName = "bpmn-js Studio editor", Description = "Optional BPMN canvas using the controlled Workflows authoring protocol.")]
public sealed class BpmnJsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) { }
}
