namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The remote-tracking ref of a branch and the fetch that updates it. The fetch names the destination explicitly, so the
/// ref the workspace and the exporter compare against is current even when the branch is not the one the clone tracks.
/// </summary>
internal static class GitRemoteRefs
{
    public static string Tracking(string branch) => $"refs/remotes/origin/{branch}";

    public static string[] FetchArgs(IReadOnlyList<string> credentialArgs, string branch) =>
        [.. credentialArgs, "fetch", "origin", $"+refs/heads/{branch}:{Tracking(branch)}"];
}
