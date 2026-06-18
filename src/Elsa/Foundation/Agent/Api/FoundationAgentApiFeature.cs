using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Foundation.Agent.Api.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Agent.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(
    name: "FoundationAgentApi",
    DisplayName = "Foundation Agent API",
    Description = "Exposes provider-agnostic agent bootstrap, session, message, stream, proposal, feedback, and audit endpoints."
)]
public sealed class FoundationAgentApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddFoundationAgentApi();
    }
}
