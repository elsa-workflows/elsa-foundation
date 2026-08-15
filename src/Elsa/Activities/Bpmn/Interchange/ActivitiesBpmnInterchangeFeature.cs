using CShells.Features;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Api.FastEndpoints;
using Elsa.Activities.Bpmn.Interchange.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
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
public class ActivitiesBpmnInterchangeFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IBpmnDocumentImporter, BpmnDocumentImporter>();
        services.AddSingleton<IBpmnDocumentExporter, BpmnDocumentExporter>();
        services.AddPermissionContributor<BpmnInterchangePermissionContributor>();
    }
}
