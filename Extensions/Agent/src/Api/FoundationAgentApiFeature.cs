using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Agent.Api.Extensions;
using Elsa.Api.AspNetCore;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Agent.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(
    name: "FoundationAgentApi",
    DisplayName = "Foundation Agent API",
    Description = "Exposes provider-agnostic agent bootstrap, session, message, stream, proposal, feedback, and audit endpoints.",
    DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature), typeof(ApiCapabilitiesFeature) }
)]
public class FoundationAgentApiFeature : FoundationAgentAbstractionsFeature, IWebShellFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddElsaEndpoints();
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.api.agent", 1,
            [new("agent-bootstrap", "_elsa/agent/bootstrap"), new("agent-sessions", "_elsa/agent/sessions")],
            "FoundationAgentApi"));
        services.AddFoundationAgentApi();
        services.AddDynamicEndpointApiExplorerRefresh();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        AgentApi.MapAgentApi(endpoints);
}
