using CShells.Features;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn.Interchange;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Interchange")]
[ShellFeature(
    name: "ActivitiesBpmnInterchange",
    DisplayName = "Activities BPMN Interchange",
    Description = "BPMN 2.0 XML + BPMNDI import/export for the BPMN process composite."
)]
public class ActivitiesBpmnInterchangeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBpmnDocumentImporter, BpmnDocumentImporter>();
        services.AddSingleton<IBpmnDocumentExporter, BpmnDocumentExporter>();
    }
}
