using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Bpmn.Api.Endpoints;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Api")]
[ShellFeature(name: "ActivitiesBpmnHostingApi", DisplayName = "BPMN hosting API",
    Description = "Publish and operate direct BPMN executions.", DependsOn = new object[] { "ActivitiesBpmnHosting", "FoundationIdentityAbstractions" })]
public class ActivitiesBpmnHostingApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.TryAddScoped<BpmnApiCaller>();
        services.TryAddScoped<BpmnCatalog>();
        ApiCapabilityRegistration.Add(services, new ApiCapabilityDeclaration("elsa.api.bpmn-catalog", 1,
            [new("bpmn-publications", "bpmn/publications"), new("bpmn-executions", "bpmn/executions")],
            "ActivitiesBpmnHostingApi"));
        ApiCapabilityRegistration.Add(services, new ApiCapabilityDeclaration("elsa.api.bpmn-inspection", 1,
            [new("bpmn-inspection", "bpmn/executions/{id}/inspection", true)], "ActivitiesBpmnHostingApi"));
        ApiCapabilityRegistration.Add(services, new ApiCapabilityDeclaration("elsa.api.bpmn-runtime", 1,
            [new("bpmn-admission", "bpmn/admission"), new("bpmn-start", "bpmn/executions"),
             new("bpmn-execution", "bpmn/executions/{id}", true), new("bpmn-work", "bpmn/executions/{id}/work", true),
             new("bpmn-complete-human", "bpmn/executions/{id}/work/{invocation}/complete", true),
             new("bpmn-cancel", "bpmn/executions/{id}/cancel", true), new("bpmn-incidents", "bpmn/executions/{id}/incidents", true),
             new("bpmn-retry-service", "bpmn/executions/{id}/retry-service", true),
             new("bpmn-command", "bpmn/executions/{id}/commands/{request}", true)], "ActivitiesBpmnHostingApi"));
        // Claims are advertised separately so clients never offer them to hosts that accept only unclaimed completion.
        ApiCapabilityRegistration.Add(services, new ApiCapabilityDeclaration("elsa.api.bpmn-claims", 1,
            [new("bpmn-claim-human", "bpmn/executions/{id}/work/{invocation}/claim", true),
             new("bpmn-complete-claimed-human", "bpmn/executions/{id}/work/{invocation}/complete-claimed", true)], "ActivitiesBpmnHostingApi"));
        services.AddPermissionContributor<BpmnHostingPermissionContributor>();
        services.TryAddKeyedScoped<IEndpointFaultRenderer, BpmnHostingFaultRenderer>(BpmnHostingApi.Owner);
    }
    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => BpmnHostingApi.Map(endpoints);
}
