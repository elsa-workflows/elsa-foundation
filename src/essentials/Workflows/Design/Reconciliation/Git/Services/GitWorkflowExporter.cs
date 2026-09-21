using Elsa.Git;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The Writer-only export reconciler (ADR 0034 D4): a set-diff sweep making git's version files match
/// the catalog. Immutable versions are written only when absent (present ⇒ skip), so loop-avoidance is
/// structural and an export→import round-trip is a no-op. Commits use a machine identity; a divergent
/// remote refuses the ff push (surfaced, never forced or merged).
/// </summary>
public sealed class GitWorkflowExporter(
    IGitWorkspace workspace,
    IGitClient gitClient,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore,
    IOptions<GitReconciliationOptions> options,
    ILogger<GitWorkflowExporter> logger) : IGitWorkflowExporter
{
    private static readonly string[] MachineIdentityArgs =
        ["-c", "user.name=Elsa Design", "-c", "user.email=design@elsa.local"];

    private readonly GitReconciliationOptions _options = options.Value;

    public async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (_options.Role != GitReconciliationRole.Writer)
            return; // Consumers never export (defensive; the feature also skips registration).

        var repoPath = await workspace.EnsureReadyAsync(cancellationToken);
        var definitions = await definitionStore.ListAsync(new WorkflowDefinitionFilter(), cancellationToken);

        var committed = false;
        foreach (var definition in definitions)
            committed |= await ExportDefinitionAsync(repoPath, definition, cancellationToken);

        if (committed && _options.Export.PushMode == GitPushMode.Immediate)
            await PushAsync(repoPath, cancellationToken);
    }

    private async Task<bool> ExportDefinitionAsync(string repoPath, WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var definitionDir = Path.Combine(repoPath, _options.WorkflowsPath, definition.Id);
        var versionsDir = Path.Combine(definitionDir, "versions");
        var committed = false;

        committed |= await RefreshDefinitionMetadataAsync(repoPath, definitionDir, definition, cancellationToken);

        var versions = await versionStore.ListByDefinitionAsync(definition.Id, cancellationToken);
        foreach (var version in versions)
        {
            var file = Path.Combine(versionsDir, $"{version.Version}.json");
            if (File.Exists(file))
                continue; // immutable: present ⇒ skip (idempotent, structural loop-avoidance).

            Directory.CreateDirectory(versionsDir);
            var indented = GitCanonicalJson.Indent(GitCanonicalJson.ToCompact(version.State, payloadSerializer));
            await File.WriteAllTextAsync(file, indented, cancellationToken);

            await CommitAsync(repoPath, file, $"Publish {definition.Name} v{version.Version} ({definition.Id})", cancellationToken);
            if (_options.Export.Tag)
                await gitClient.RunAsync(repoPath, cancellationToken, "tag", $"wf/{definition.Id}/v{version.Version}");
            committed = true;
        }

        return committed;
    }

    /// <summary>Writes/commits <c>definition.json</c> only when its name/description/deleted differs from disk.</summary>
    private async Task<bool> RefreshDefinitionMetadataAsync(string repoPath, string definitionDir, WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var path = Path.Combine(definitionDir, "definition.json");
        var desired = new GitDefinitionMetadata(definition.Name, definition.Description, definition.DeletedAt is not null);
        if (GitDefinitionMetadata.Read(path) == desired)
            return false;

        Directory.CreateDirectory(definitionDir);
        await File.WriteAllTextAsync(path, desired.ToJson(), cancellationToken);
        await CommitAsync(repoPath, path, $"Update metadata {definition.Name} ({definition.Id})", cancellationToken);
        return true;
    }

    /// <summary>Stages only the given file and commits it under the machine identity (never <c>add -A</c>).</summary>
    private async Task CommitAsync(string repoPath, string file, string message, CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(repoPath, file).Replace(Path.DirectorySeparatorChar, '/');
        await gitClient.RunAsync(repoPath, cancellationToken, "add", "--", relativePath);
        await gitClient.RunAsync(repoPath, cancellationToken, [.. MachineIdentityArgs, "commit", "-m", message, "--", relativePath]);
    }

    private async Task PushAsync(string repoPath, CancellationToken cancellationToken)
    {
        // No --force: git refuses a non-fast-forward push by default, so a divergent remote is rejected
        // (surfaced, never forced or merged — the D7 single-writer gate).
        LogPush(_options.ResolvedExportBranch);
        await gitClient.RunAsync(repoPath, cancellationToken,
            [.. workspace.CredentialArgs, "push", "origin", $"HEAD:{_options.ResolvedExportBranch}"]);
    }

    private void LogPush(string branch)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Pushing exported workflow versions to origin/{branch} (fast-forward only)", branch);
    }
}
