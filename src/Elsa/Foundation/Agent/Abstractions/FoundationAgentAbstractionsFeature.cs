using CShells.Features;
using Elsa.Foundation.Agent.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Agent.Abstractions;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(
    name: "FoundationAgentAbstractions",
    DisplayName = "Foundation Agent Abstractions",
    Description = "Registers provider-agnostic agent sessions, policy, context, proposal, streaming, feedback, audit, and provider facade contracts."
)]
public class FoundationAgentAbstractionsFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
    }
}
