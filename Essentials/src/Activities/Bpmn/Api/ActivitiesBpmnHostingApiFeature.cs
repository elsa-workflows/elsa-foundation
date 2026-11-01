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
        services.AddPermissionContributor<BpmnHostingPermissionContributor>();
        services.TryAddKeyedScoped<IEndpointFaultRenderer, BpmnHostingFaultRenderer>(BpmnHostingApi.Owner);
    }
    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => BpmnHostingApi.Map(endpoints);
}
