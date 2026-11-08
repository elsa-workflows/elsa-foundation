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

namespace Elsa.Agent.Specialists.Api;

/// <summary>Authoring reads for the specialist editor: which model providers are registered and what a selected connection supports.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "SpecialistsApi", DisplayName = "Specialists: Authoring API",
    Description = "List the registered specialist model providers and read the current capabilities of a selected model connection.",
    DependsOn = new object[] { "SpecialistsDesign", "ApiCapabilities" })]
public class SpecialistsApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaEndpoints();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Elsa.Foundation.Identity.Core.Authorization.IPermissionContributor, AgentSourcePermissions>());
        services.TryAddKeyedSingleton<NativeEndpoints.IEndpointFaultRenderer, SpecialistsApiFaultRenderer>(SpecialistsApi.OwnerId);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.agent-specialists", 1,
        [
            new("agent-specialist-model-providers", SpecialistsApi.ModelProvidersRoute, true),
            new("agent-specialist-model-capabilities", SpecialistsApi.ModelCapabilitiesRoute, true),
            new("agent-source-definitions", "agent/definitions", true)
        ], "SpecialistsApi"));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => SpecialistsApi.MapSpecialistsApi(endpoints);
}
