using Elsa.Git;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The Writer-only export reconciler (ADR 0034 D4): a set-diff sweep making git's version files match
/// the catalog. Every decision comes from git state (#2197): a version is written and committed only when its file is
/// absent from the HEAD tree (present ⇒ skip), so loop-avoidance is structural and an export→import round-trip is a
/// no-op; a version is tagged when its tag is missing from HEAD's history; and the branch is pushed whenever HEAD is
/// ahead of the remote. A rerun after a stop at any point therefore completes the work. Commits use a machine identity.
/// </summary>
/// <remarks>
/// Every Writer node runs it at shell start, without a lock: the fence is the push (ADR 0034 D7). Git refuses a push
/// that is not a fast-forward, so the remote branch only ever advances by one writer's commits on top of what every
/// other writer has to build on. A writer whose push is refused because the remote moved resets onto the remote through
/// the workspace and sweeps again, and since another writer of the same catalog pushed the same files, it normally finds
/// nothing left to commit. A push refused for any other reason throws, as it always has.
/// A version not yet committed is written only once it passes the credential-literal rule (spec 188, FR-008),
/// <see cref="ICredentialLiteralValidator"/>: a refused version gets no directory, file, commit or tag, and the pass logs a
/// value-free warning, exports everything else and does not fail. A committed version file is skipped before the rule
/// runs, and a node whose activity the catalog does not hold is not judged.
/// </remarks>
public sealed class GitWorkflowExporter(
    IGitWorkspace workspace,
    IGitClient gitClient,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore,
    IOptions<GitReconciliationOptions> options,
    ICredentialLiteralValidator credentialLiterals,
    ILogger<GitWorkflowExporter> logger) : IGitWorkflowExporter
{
    /// <summary>Pushes one pass attempts against a remote other writers keep moving before it leaves the rest to the next pass.</summary>
    public const int MaxPushAttempts = 3;

    private readonly GitReconciliationOptions _options = options.Value;

    private string ExportBranch => _options.ResolvedExportBranch;

    private bool ExportsToTrackedBranch => string.Equals(ExportBranch, _options.Branch, StringComparison.Ordinal);

    public async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (_options.Role != GitReconciliationRole.Writer)
            return; // Consumers never export (defensive; the feature also skips registration).

        var repoPath = await workspace.EnsureReadyAsync(cancellationToken);
        var catalog = await ReadCatalogAsync(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            await SweepAsync(repoPath, catalog, cancellationToken);
            if (_options.Export.PushMode != GitPushMode.Immediate || !await IsAheadOfRemoteAsync(repoPath, cancellationToken))
                return;

            try
            {
                await PushAsync(cancellationToken);
                return;
            }
            catch (InvalidOperationException)
            {
                // Only a remote that moved is a lost race; anything else (credentials, a hook, a protected branch) is
                // the failure it always was.
                if (!await RemoteMovedAsync(repoPath, cancellationToken))
                    throw;
            }

            if (!ExportsToTrackedBranch)
            {
                LogExportBranchDiverged();
                return;
            }

            if (attempt == MaxPushAttempts)
            {
                LogPushRefused(attempt);
                return;
            }

            LogRebuilding(attempt);
            await workspace.EnsureReadyAsync(cancellationToken);

            // The workspace fetched the tracked branch. A clone it could not move onto it (it keeps a commit the export
            // did not make, or a change of the operator's) is behind it still, and another pass would only meet the same
            // refusal; the workspace has said why.
            if ((await GitRemoteRefs.AheadBehindAsync(gitClient, repoPath, ExportBranch, cancellationToken)).Behind > 0)
            {
                LogRebuildBlocked();
                return;
            }
        }
    }

    private async Task<IReadOnlyList<CatalogDefinition>> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        var definitions = await definitionStore.ListAsync(new WorkflowDefinitionFilter(), cancellationToken);
        var catalog = new List<CatalogDefinition>(definitions.Count);
        foreach (var definition in definitions)
            catalog.Add(new(definition, await versionStore.ListByDefinitionAsync(definition.Id, cancellationToken)));

        return catalog;
    }

    private async Task SweepAsync(string repoPath, IReadOnlyList<CatalogDefinition> catalog, CancellationToken cancellationToken)
    {
        var committed = await CommittedPathsAsync(repoPath, cancellationToken);
        foreach (var (definition, versions) in catalog)
        {
            await RefreshDefinitionMetadataAsync(repoPath, committed, definition, cancellationToken);
            foreach (var version in versions)
                await ExportVersionAsync(repoPath, committed, definition, version, cancellationToken);
        }

        if (_options.Export.Tag)
            await TagVersionsAsync(repoPath, catalog, cancellationToken);
    }

    /// <summary>The repository paths under the workflows root in the HEAD tree: what is committed, whatever is on disk.</summary>
    private async Task<HashSet<string>> CommittedPathsAsync(string repoPath, CancellationToken cancellationToken)
    {
        var listing = await gitClient.RunAsync(repoPath, cancellationToken,
            GitPathspecs.Literal, "ls-tree", "-r", "-z", "--name-only", "HEAD", "--", _options.WorkflowsPath);
        return listing.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private async Task ExportVersionAsync(
        string repoPath, HashSet<string> committed, WorkflowDefinition definition, WorkflowDefinitionVersion version, CancellationToken cancellationToken)
    {
        var file = VersionFile(repoPath, definition.Id, version.Version);
        var path = RepositoryPath(repoPath, file);
        if (committed.Contains(path))
            return; // immutable: committed ⇒ skip (idempotent, structural loop-avoidance).

        var refusals = await credentialLiterals.Validate(version.State, cancellationToken);
        if (refusals.Count > 0)
        {
            LogRefused(definition.Id, version.Version, refusals);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var indented = GitCanonicalJson.Indent(GitCanonicalJson.ToCompact(version.State, payloadSerializer));
        await File.WriteAllTextAsync(file, indented, cancellationToken);
        await CommitAsync(repoPath, path, $"Publish {definition.Name} v{version.Version} ({definition.Id})", cancellationToken);
        committed.Add(path);
    }

    /// <summary>Writes and commits <c>definition.json</c> unless the committed copy already holds the catalog's name/description/deleted.</summary>
    private async Task RefreshDefinitionMetadataAsync(
        string repoPath, HashSet<string> committed, WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var file = Path.Join(repoPath, _options.WorkflowsPath, definition.Id, "definition.json");
        var path = RepositoryPath(repoPath, file);
        var desired = new GitDefinitionMetadata(definition.Name, definition.Description, definition.DeletedAt is not null);

        // The workspace leaves the working tree at HEAD, so a committed file reads from disk as its committed content.
        if (committed.Contains(path) && GitDefinitionMetadata.Read(file) == desired)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, desired.ToJson(), cancellationToken);
        await CommitAsync(repoPath, path, $"Update metadata {definition.Name} ({definition.Id})", cancellationToken);
        committed.Add(path);
    }

    /// <summary>
    /// Tags each catalog version whose tag is not in HEAD's history at the commit that added its file. A tag outside that
    /// history points at a commit the workspace discarded when it reset onto the remote, so it is replaced.
    /// </summary>
    private async Task TagVersionsAsync(string repoPath, IReadOnlyList<CatalogDefinition> catalog, CancellationToken cancellationToken)
    {
        var tagged = (await gitClient.RunAsync(repoPath, cancellationToken, "tag", "--list", "--merged", "HEAD", "wf/*"))
            .Lines()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (definition, versions) in catalog)
        foreach (var version in versions)
        {
            var tag = $"wf/{definition.Id}/v{version.Version}";
            if (tagged.Contains(tag))
                continue;

            var path = RepositoryPath(repoPath, VersionFile(repoPath, definition.Id, version.Version));
            var commit = await gitClient.RunAsync(repoPath, cancellationToken,
                GitPathspecs.Literal, "log", "-1", "--format=%H", "--no-renames", "--diff-filter=A", "HEAD", "--", path);
            if (commit.Length > 0)
                await gitClient.RunAsync(repoPath, cancellationToken, "tag", "--force", tag, commit);
        }
    }

    /// <summary>Stages only the given path and commits it under the machine identity (never <c>add -A</c>).</summary>
    private async Task CommitAsync(string repoPath, string path, string message, CancellationToken cancellationToken)
    {
        await gitClient.RunAsync(repoPath, cancellationToken, GitPathspecs.Literal, "add", "--", path);
        await gitClient.RunAsync(repoPath, cancellationToken, [.. GitExportIdentity.CommitArgs, GitPathspecs.Literal, "commit", "-m", message, "--", path]);
    }

    /// <summary>
    /// Whether HEAD has commits the remote export branch lacks. The tracked branch was fetched when the workspace got
    /// ready; any other export branch is fetched here, and one that cannot be fetched (not created yet) counts as behind,
    /// so the push goes ahead and git compares against the remote itself.
    /// </summary>
    private async Task<bool> IsAheadOfRemoteAsync(string repoPath, CancellationToken cancellationToken)
    {
        if (!ExportsToTrackedBranch && !await TryFetchExportBranchAsync(cancellationToken))
            return true;

        return (await GitRemoteRefs.AheadBehindAsync(gitClient, repoPath, ExportBranch, cancellationToken)).Ahead > 0;
    }

    /// <summary>Whether the remote export branch has commits HEAD lacks, which makes a refused push a lost race.</summary>
    private async Task<bool> RemoteMovedAsync(string repoPath, CancellationToken cancellationToken) =>
        await TryFetchExportBranchAsync(cancellationToken)
        && (await GitRemoteRefs.AheadBehindAsync(gitClient, repoPath, ExportBranch, cancellationToken)).Behind > 0;

    private async Task<bool> TryFetchExportBranchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await workspace.RunRemoteAsync(cancellationToken, GitRemoteRefs.FetchArgs(ExportBranch));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task PushAsync(CancellationToken cancellationToken)
    {
        // No --force: git refuses a non-fast-forward push by default, so a divergent remote is rejected
        // (surfaced, never forced or merged — the D7 single-writer gate).
        LogPush(ExportBranch);
        await workspace.RunRemoteAsync(cancellationToken, "push", "origin", $"HEAD:{ExportBranch}");
    }

    private string VersionFile(string repoPath, string definitionId, string version) =>
        Path.Join(repoPath, _options.WorkflowsPath, definitionId, "versions", $"{version}.json");

    private static string RepositoryPath(string repoPath, string file) =>
        Path.GetRelativePath(repoPath, file).Replace(Path.DirectorySeparatorChar, '/');

    private void LogRefused(string definitionId, string version, IReadOnlyList<ValidationError> refusals)
    {
        // Each finding names the rule, the node and the input; none carries the bound value.
        foreach (var refusal in refusals)
            logger.LogWarning(
                "Not exporting workflow definition '{definitionId}' v{version}: {refusal}",
                definitionId, version, refusal.Message);
    }

    private void LogPush(string branch)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Pushing exported workflow versions to origin/{branch} (fast-forward only)", branch);
    }

    private void LogRebuilding(int attempt)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Push {attempt} of exported workflow versions to origin/{branch} was refused because another writer pushed first; rebuilding on the remote and exporting again.",
                attempt, ExportBranch);
    }

    private void LogPushRefused(int attempts)
    {
        logger.LogWarning(
            "Exported workflow versions were not pushed to origin/{branch}: other writers moved it before each of {attempts} pushes. " +
            "The commits stay in the clone, and the next pass pushes whatever the remote still lacks.",
            ExportBranch, attempts);
    }

    private void LogRebuildBlocked()
    {
        logger.LogWarning(
            "Exported workflow versions were not pushed to origin/{branch}: another writer moved it, and the clone could not be moved onto it (the workflows clone's own log says why). " +
            "The commits stay in the clone, and the next pass pushes whatever the remote still lacks once it can.",
            ExportBranch);
    }

    private void LogExportBranchDiverged()
    {
        logger.LogError(
            "Exported workflow versions were not pushed to origin/{exportBranch}: it has commits this clone lacks, and the export rebuilds only onto the branch the clone tracks, origin/{branch}. " +
            "The commits stay in the clone; once the export branch is back in line with the tracked one, the next pass pushes them.",
            ExportBranch, _options.Branch);
    }

    private sealed record CatalogDefinition(WorkflowDefinition Definition, IReadOnlyList<WorkflowDefinitionVersion> Versions);
}
