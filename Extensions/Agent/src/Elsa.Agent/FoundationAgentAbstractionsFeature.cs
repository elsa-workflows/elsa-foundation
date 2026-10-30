using CShells.Features;
using Elsa.Agent.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(
    name: "FoundationAgentAbstractions",
    DisplayName = "Foundation Agent Abstractions",
    Description = "Registers provider-agnostic agent sessions, policy, context, proposal, streaming, feedback, audit, and provider facade contracts.",
    DependsOn = new object[] { "Events" }
)]
public class FoundationAgentAbstractionsFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
    }
}
