using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Weaver.Api.Extensions;
using Elsa.Api.AspNetCore;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MinimalEndpoints;

namespace Elsa.Weaver.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ShellFeature(
    name: "WeaverApi",
    DisplayName = "Foundation Agent API",
    Description = "Exposes provider-agnostic agent bootstrap, session, message, stream, proposal, feedback, and audit endpoints.",
    DependsOn = new object[] { typeof(WeaverFeature), typeof(ApiCapabilitiesFeature) }
)]
public class WeaverApiFeature : WeaverFeature, IWebShellFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddElsaEndpoints();
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.api.weaver", 1,
            [new("weaver-bootstrap", "_elsa/weaver/bootstrap"), new("weaver-sessions", "_elsa/weaver/sessions")],
            "WeaverApi"));
        services.AddWeaverApi();
        services.AddDynamicEndpointApiExplorerRefresh();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        AgentApi.MapAgentApi(endpoints);
}
