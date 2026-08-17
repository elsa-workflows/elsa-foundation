using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Agent.Api.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Agent.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(
    name: "FoundationAgentApi",
    DisplayName = "Foundation Agent API",
    Description = "Exposes provider-agnostic agent bootstrap, session, message, stream, proposal, feedback, and audit endpoints."
)]
public class FoundationAgentApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAgentApi();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        AgentApi.MapAgentApi(endpoints);
}
