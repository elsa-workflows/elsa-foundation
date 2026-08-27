using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Bpmn.Interchange.Authorization;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Endpoints;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Interchange")]
[ShellFeature(
    name: "ActivitiesBpmnInterchange",
    DisplayName = "Activities BPMN Interchange",
    Description = "BPMN 2.0 XML + BPMNDI import/export for the BPMN process composite."
)]
public class ActivitiesBpmnInterchangeFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddSingleton<IBpmnDocumentImporter, BpmnDocumentImporter>();
        services.AddSingleton<IBpmnDocumentExporter, BpmnDocumentExporter>();
        services.AddDynamicEndpointApiExplorerRefresh();
        // The owner's failure services are keyed so hosts composing several modules keep each
        // module's own error shapes; the endpoint pipeline falls back to unkeyed registrations.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, Endpoints.BpmnInterchangeProblemWriter>(BpmnInterchangeApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, Endpoints.BpmnInterchangeFaultRenderer>(BpmnInterchangeApi.OwnerId);
        services.AddPermissionContributor<BpmnInterchangePermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        BpmnInterchangeApi.MapBpmnInterchangeApi(endpoints);
}
