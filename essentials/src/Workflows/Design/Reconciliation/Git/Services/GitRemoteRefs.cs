using System.Globalization;
using Elsa.Git;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The remote-tracking ref of a branch and the fetch that updates it. The fetch names the destination explicitly, so the
/// ref the workspace and the exporter compare against is current even when the branch is not the one the clone tracks.
/// </summary>
internal static class GitRemoteRefs
{
    public static string Tracking(string branch) => $"refs/remotes/origin/{branch}";

    public static string[] FetchArgs(string branch) => ["fetch", "origin", $"+refs/heads/{branch}:{Tracking(branch)}"];

    /// <summary>
    /// How many commits HEAD has that the remote-tracking ref of <paramref name="branch"/> lacks (ahead) and the other
    /// way round (behind), as of the last fetch of that ref.
    /// </summary>
    public static async Task<(int Ahead, int Behind)> AheadBehindAsync(
        IGitClient gitClient, string repoPath, string branch, CancellationToken cancellationToken)
    {
        var counts = (await gitClient.RunAsync(repoPath, cancellationToken, "rev-list", "--left-right", "--count", $"HEAD...{Tracking(branch)}"))
            .Split('\t', StringSplitOptions.TrimEntries);
        return (int.Parse(counts[0], CultureInfo.InvariantCulture), int.Parse(counts[1], CultureInfo.InvariantCulture));
    }
}
