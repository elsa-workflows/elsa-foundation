using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Bpmn.Interchange.Endpoints.DirectPublication;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange;

/// <summary>Optional Design-side publication; absent from runtime-only compositions.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Publishing")]
[ShellFeature(name: "ActivitiesBpmnDirectPublishing", DisplayName = "Authored direct BPMN publication",
    Description = "Publish supported immutable authored BPMN versions to the direct runtime.",
    DependsOn = new object[] { "ApiCapabilities", "ActivitiesBpmnHostingApi", "WorkflowsPublishingApi" })]
public class ActivitiesBpmnDirectPublishingFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.TryAddScoped<BpmnAuthoredDirectPublisher>();
        services.TryAddKeyedScoped<IEndpointFaultRenderer, BpmnDirectPublishingFaultRenderer>(BpmnDirectPublishingApi.Owner);
        ApiCapabilityRegistration.Add(services, new ApiCapabilityDeclaration("elsa.api.bpmn-publishing", 1,
            [new("bpmn-authored-publish", "interchange/bpmn/versions/{versionId}/publish-direct", true)],
            "ActivitiesBpmnDirectPublishing"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => BpmnDirectPublishingApi.Map(endpoints);
}
