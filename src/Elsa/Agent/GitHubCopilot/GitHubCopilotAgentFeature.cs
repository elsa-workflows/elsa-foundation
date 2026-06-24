using CShells.Features;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.GitHubCopilot.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.GitHubCopilot;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Provider")]
[ShellFeature(
    name: "GitHubCopilotAgent",
    DisplayName = "GitHub Copilot Agent Provider",
    Description = "Registers the GitHub Copilot agent provider facade seam without leaking SDK-specific details to Studio or workflow modules."
)]
public sealed class GitHubCopilotAgentFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        services.AddGitHubCopilotAgentProvider();
    }
}
