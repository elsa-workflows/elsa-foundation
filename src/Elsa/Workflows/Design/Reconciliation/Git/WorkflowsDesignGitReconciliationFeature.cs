using CShells.Features;
using Elsa.Git;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Elsa.Workflows.Design.Reconciliation.Git.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Reconciliation.Git;

/// <summary>
/// GitOps for workflow definitions (ADR 0034 / spec 085): a concrete reconciliation feature that reads
/// immutable version files from git into the operational catalog and — on a Writer node — mirrors the
/// catalog's versions back to git. Extends <see cref="WorkflowsDesignReconciliationFeature"/> (so the
/// reconciler + universal handler + import lifecycle are wired by the base) and contributes the git
/// source, workspace, and — Writer only — the export reconciler + export task. Never a runtime read
/// path; git is a source/sink over the catalog only.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Reconciliation")]
[ShellFeature(
    name: "WorkflowsDesignGitReconciliation",
    DisplayName = "Workflow Design Git Reconciliation",
    Description = "Reconciles workflow-definition versions from a git repository into the catalog, and (Writer role) exports the catalog's versions back to git."
)]
public class WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature
{
    [ManifestSetting(DisplayName = "Remote URL", Description = "The git remote to clone/fetch, e.g. git@github.com:acme/workflows.git.", Category = "Git")]
    public string RemoteUrl { get; set; } = string.Empty;

    [ManifestSetting(DisplayName = "Branch", Description = "The tracked branch.", Category = "Git", DefaultValue = "main")]
    public string Branch { get; set; } = "main";

    [ManifestSetting(DisplayName = "Workflows path", Description = "Repo-relative root under which definitions live.", Category = "Git", DefaultValue = "workflows")]
    public string WorkflowsPath { get; set; } = "workflows";

    [ManifestSetting(DisplayName = "Local cache path", Description = "Local working-clone directory. Empty defaults under the host data dir.", Category = "Git")]
    public string LocalCachePath { get; set; } = string.Empty;

    [ManifestSetting(DisplayName = "Role", Description = "Writer (authors + exports) or Consumer (imports read-only).", Category = "Git", DefaultValue = "Consumer")]
    public GitReconciliationRole Role { get; set; } = GitReconciliationRole.Consumer;

    [ManifestSetting(DisplayName = "Credentials mode", Description = "How credentials are supplied: SshKey, Token, or HostDefault.", Category = "Git", DefaultValue = "HostDefault")]
    public GitCredentialsMode CredentialsMode { get; set; } = GitCredentialsMode.HostDefault;

    [ManifestSetting(DisplayName = "SSH key path", Description = "SSH private-key path (SshKey mode).", Category = "Git")]
    public string KeyPath { get; set; } = string.Empty;

    [ManifestSetting(DisplayName = "Token", Description = "HTTPS token (Token mode).", Category = "Git", Secret = true)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Export sub-options (honored only when Role=Writer).</summary>
    public GitExportOptions Export { get; set; } = new();

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(BuildOptions()));
        services.AddGitClient();
        services.AddScoped<IGitWorkspace, GitWorkspace>();
        services.AddScoped<IWorkflowReconciliationSource, GitWorkflowReconciliationSource>();

        if (Role == GitReconciliationRole.Writer)
        {
            services.AddScoped<IGitWorkflowExporter, GitWorkflowExporter>();
            services.AddScoped<IStartupTask, GitWorkflowExportStartupTask>();
        }
    }

    private GitReconciliationOptions BuildOptions() => new()
    {
        RemoteUrl = RemoteUrl,
        Branch = Branch,
        WorkflowsPath = WorkflowsPath,
        LocalCachePath = LocalCachePath,
        Role = Role,
        CredentialsMode = CredentialsMode,
        KeyPath = KeyPath,
        Token = Token,
        Export = Export,
    };
}
