using CShells.Features;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.GitHubCopilot.Extensions;
using Elsa.Agent.GitHubCopilot.Options;
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
    public bool Enabled { get; set; }

    public string? GitHubToken { get; set; }

    public string? GitHubTokenEnvironmentVariable { get; set; } = "COPILOT_GITHUB_TOKEN";

    public bool UseLoggedInUser { get; set; }

    public string? RuntimeUrl { get; set; }

    public string? RuntimeConnectionToken { get; set; }

    public string? BaseDirectory { get; set; }

    public string? WorkingDirectory { get; set; }

    public string Model { get; set; } = "auto";

    public string? ReasoningEffort { get; set; }

    public string? SystemMessage { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        services.AddGitHubCopilotAgentProvider();
        services.Configure<GitHubCopilotAgentOptions>(options =>
        {
            options.Enabled = Enabled;
            options.GitHubToken = GitHubToken;
            options.GitHubTokenEnvironmentVariable = GitHubTokenEnvironmentVariable;
            options.UseLoggedInUser = UseLoggedInUser;
            options.RuntimeUrl = RuntimeUrl;
            options.RuntimeConnectionToken = RuntimeConnectionToken;
            options.BaseDirectory = BaseDirectory;
            options.WorkingDirectory = WorkingDirectory;
            options.Model = Model;
            options.ReasoningEffort = ReasoningEffort;
            options.SystemMessage = SystemMessage;
        });
    }
}
