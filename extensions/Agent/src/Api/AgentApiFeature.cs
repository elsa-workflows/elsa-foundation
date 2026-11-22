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

namespace Elsa.Agent.Api;

/// <summary>Authoring reads for the agent editor: which model providers are registered and what a selected connection supports.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "AgentApi", DisplayName = "Agents: Authoring API",
    Description = "List the registered agent model providers and read the current capabilities of a selected model connection.",
    DependsOn = new object[] { "AgentDesign", "ApiCapabilities" })]
public class AgentApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Elsa.Foundation.Identity.Core.Authorization.IPermissionContributor, AgentSourcePermissions>());
        services.TryAddKeyedSingleton<MinimalEndpoints.IEndpointFaultRenderer, AgentApiFaultRenderer>(AgentApi.OwnerId);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent", 1,
        [
            new("agent-model-providers", AgentApi.ModelProvidersRoute, true),
            new("agent-model-capabilities", AgentApi.ModelCapabilitiesRoute, true)
        ], "AgentApi"));
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent-definitions", 1,
        [
            new("agent-source-revisions", "agent/definitions/revisions"),
            new("agent-source-revision", "agent/definitions/revision"),
            new("agent-source-draft", "agent/definitions/draft"),
            new("agent-source-draft-revisions", "agent/definitions/draft-revisions"),
            new("agent-source-draft-revision", "agent/definitions/draft-revision"),
            new("agent-source-captures", "agent/definitions/captures"),
            new("agent-source-discard", "agent/definitions/discard"),
            new("agent-source-forks", "agent/definitions/forks"),
            new("agent-source-definitions", "agent/definitions")
        ], "AgentApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => AgentApi.MapAgentApi(endpoints);
}
