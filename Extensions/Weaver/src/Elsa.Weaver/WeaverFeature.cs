using CShells.Features;
using Elsa.Weaver.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ShellFeature(
    name: "Weaver",
    DisplayName = "Foundation Weaver Abstractions",
    Description = "Registers provider-agnostic Weaver sessions, policy, context, proposal, streaming, feedback, audit, and provider facade contracts.",
    DependsOn = new object[] { "Events" }
)]
public class WeaverFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddWeaver();
    }
}
