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

namespace Elsa.Agent.Inspection.Api;

/// <summary>The agent evidence reads over generic draft Test Runs, with the agent runtime's invocation inspector.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "AgentInspectionApi", DisplayName = "Agents: Inspection API",
    Description = "Read the agent evidence of a draft Test Run or a runtime occurrence: exact version, model, turn status, requests, usage, conversation and permitted accepted output.",
    DependsOn = new object[] { "AgentRuntime", "WorkflowsPublishingApi", "ApiCapabilities" })]
public class AgentInspectionApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.TryAddScoped<AgentEvidenceAccess>();
        services.TryAddKeyedSingleton<MinimalEndpoints.IEndpointFaultRenderer, AgentInspectionFaultRenderer>(AgentInspectionApi.OwnerId);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent-inspection", 1,
        [
            new("agent-test-run-evidence", AgentInspectionApi.TestRunEvidenceRoute, true),
            new("agent-invocation-evidence", AgentInspectionApi.InvocationEvidenceRoute, true)
        ], "AgentInspectionApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => AgentInspectionApi.MapAgentInspectionApi(endpoints);
}
