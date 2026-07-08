using Elsa.Git;

namespace Elsa.Modularity.ExtensionBuilder;

/// <summary>Point-in-time snapshot of a managed repository's Git state.</summary>
internal sealed record RepositoryState(string? ActiveBranch, bool IsDirty, string RemoteState);

/// <summary>
/// Read-only, Git-derived semantic model over a managed repository path. It answers the questions the
/// storage façade asks about a repository — active branch, dirtiness, origin, remote divergence — by
/// interpreting <see cref="GitClient"/> output. It owns no persisted state and mutates nothing except
/// via the explicit fetch inside <see cref="FetchAndMeasureRemoteDivergenceAsync"/>.
/// </summary>
internal sealed class RepositoryInspector(IGitClient git)
{
    public RepositoryState GetRepositoryState(string repositoryPath)
    {
        if (!Directory.Exists(Path.Combine(repositoryPath, ".git")))
            return new(null, false, "not-connected");

        var activeBranch = git.RunOrDefault(repositoryPath, "branch", "--show-current");
        var status = git.RunOrDefault(repositoryPath, "status", "--porcelain");
        var remotes = git.RunOrDefault(repositoryPath, "remote", "-v")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var origin = remotes
            .Select(ParseRemoteUrl)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new(
            string.IsNullOrWhiteSpace(activeBranch) ? null : activeBranch,
            !string.IsNullOrWhiteSpace(status),
            origin ?? "not-connected");
    }

    public string GetPrimaryRemote(string repositoryPath)
    {
        var remote = git.RunOrDefault(repositoryPath, "remote")
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(remote))
            throw new InvalidOperationException("Remote sync requires a configured Git remote.");
        return remote;
    }

    public string GetRequiredActiveBranch(string repositoryPath)
    {
        var branch = git.RunOrDefault(repositoryPath, "branch", "--show-current");
        if (string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException("Remote sync requires an active named branch.");
        return branch;
    }

    public async Task<RemoteDivergence> FetchAndMeasureRemoteDivergenceAsync(string repositoryPath, string remote, string branch, CancellationToken cancellationToken)
    {
        if (!RemoteBranchExists(repositoryPath, remote, branch))
            return new(false, 0, 0);

        var remoteRef = $"refs/remotes/{remote}/{branch}";
        await git.RunAsync(repositoryPath, cancellationToken, "fetch", remote, $"{branch}:{remoteRef}");
        var counts = git.RunOrDefault(repositoryPath, "rev-list", "--left-right", "--count", $"HEAD...{remoteRef}")
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return counts.Length == 2 && int.TryParse(counts[0], out var ahead) && int.TryParse(counts[1], out var behind)
            ? new(true, ahead, behind)
            : new(true, 0, 0);
    }

    public ISet<string> GetDirtyRepositoryPaths(string repositoryPath)
    {
        var status = git.RunOrDefault(repositoryPath, "status", "--porcelain", "--untracked-files=all");
        if (string.IsNullOrWhiteSpace(status))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return status.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(ParseDirtyStatusPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool RemoteBranchExists(string repositoryPath, string remote, string branch) =>
        !string.IsNullOrWhiteSpace(git.RunOrDefault(repositoryPath, "ls-remote", "--heads", remote, branch));

    private static IEnumerable<string> ParseDirtyStatusPaths(string line)
    {
        if (line.Length < 4)
            yield break;

        var path = line[3..].Trim().Trim('"').Replace('\\', '/');
        var renameSeparator = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameSeparator >= 0)
        {
            yield return path[(renameSeparator + 4)..];
            yield break;
        }

        yield return path;
    }

    private static string? ParseRemoteUrl(string remoteLine)
    {
        var parts = remoteLine.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && parts[0].Equals("origin", StringComparison.OrdinalIgnoreCase)
            ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            : null;
    }
}
