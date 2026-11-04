using CShells.Features;
using Elsa.Agent.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent;

/// <summary>Enables the generic effects contract for a compatible selected store.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature("FoundationAgentEffects", DisplayName = "Foundation Agent Effects",
    Description = "Enables generic privilege declarations, module codes and performed effects for the selected compatible store.",
    DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
public class FoundationAgentEffectsFeature : FoundationAgentAbstractionsFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        AgentEffectsAdmission.Register(services, GetType());
    }
}
