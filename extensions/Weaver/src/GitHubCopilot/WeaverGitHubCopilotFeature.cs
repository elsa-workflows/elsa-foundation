using CShells.Features;
using Elsa.Weaver.GitHubCopilot.Extensions;
using Elsa.Weaver.GitHubCopilot.Options;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.GitHubCopilot;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ManifestFeatureCategory("Provider")]
[ShellFeature(
    name: "WeaverGitHubCopilot",
    DisplayName = "GitHub Copilot Weaver Provider",
    Description = "Registers the GitHub Copilot Weaver provider facade seam without leaking SDK-specific details to Studio or workflow modules.",
    DependsOn = new object[] { typeof(WeaverFeature) }
)]
public class WeaverGitHubCopilotFeature : WeaverFeature
{
    public bool Enabled { get; set; }

    [ManifestSetting(Secret = true)]
    public string? GitHubToken { get; set; }

    public string? GitHubTokenEnvironmentVariable { get; set; } = "COPILOT_GITHUB_TOKEN";

    public bool UseLoggedInUser { get; set; }

    public string? RuntimeUrl { get; set; }

    [ManifestSetting(Secret = true)]
    public string? RuntimeConnectionToken { get; set; }

    public string? BaseDirectory { get; set; }

    public string? WorkingDirectory { get; set; }

    public string Model { get; set; } = "auto";

    public string? ReasoningEffort { get; set; }

    public string? SystemMessage { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddWeaverGitHubCopilotProvider();
        services.Configure<WeaverGitHubCopilotOptions>(options =>
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
