using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Agent.Specialists.Inspection.Api;

/// <summary>The specialist evidence reads over generic draft Test Runs, with the specialist runtime's invocation inspector.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "SpecialistsInspectionApi", DisplayName = "Specialists: Inspection API",
    Description = "Read the specialist evidence of a draft Test Run or a runtime occurrence: exact version, model, turn status, requests, usage, conversation and permitted accepted output.",
    DependsOn = new object[] { "SpecialistsRuntime", "WorkflowsPublishingApi", "ApiCapabilities" })]
public class SpecialistsInspectionApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.TryAddScoped<SpecialistEvidenceAccess>();
        services.TryAddKeyedSingleton<NativeEndpoints.IEndpointFaultRenderer, SpecialistInspectionFaultRenderer>(SpecialistInspectionApi.OwnerId);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent-specialist-inspection", 1,
        [
            new("agent-specialist-test-run-evidence", SpecialistInspectionApi.TestRunEvidenceRoute, true),
            new("agent-specialist-invocation-evidence", SpecialistInspectionApi.InvocationEvidenceRoute, true)
        ], "SpecialistsInspectionApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => SpecialistInspectionApi.MapSpecialistInspectionApi(endpoints);
}
