using Elsa.Git;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Default <see cref="IGitWorkspace"/>: manages a single local working clone with role-based clone modes
/// (ADR 0034 D11). Credentials are applied as per-invocation <c>-c …</c> arguments (never on a visible
/// URL/argv secret, FR-013). A Writer clone is a persistent, ff-only-integrated working copy that is
/// never <c>reset --hard</c>; a Consumer clone is a disposable mirror.
/// </summary>
public sealed class GitWorkspace(
    IGitClient gitClient,
    IOptions<GitReconciliationOptions> options,
    ILogger<GitWorkspace> logger) : IGitWorkspace
{
    private readonly GitReconciliationOptions _options = options.Value;
    private string[] _credentialArgs = [];

    public string RepositoryPath => _options.ResolveLocalCachePath();

    public IReadOnlyList<string> CredentialArgs => _credentialArgs;

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var repoPath = _options.ResolveLocalCachePath();
        _credentialArgs = PrepareCredentialArgs(repoPath);

        if (!gitClient.IsGitRepository(repoPath))
            await CloneAsync(repoPath, cancellationToken);
        else
            await IntegrateAsync(repoPath, cancellationToken);

        return repoPath;
    }

    private async Task CloneAsync(string repoPath, CancellationToken cancellationToken)
    {
        // git clone requires an empty/absent target. A pre-existing non-repo directory carries no
        // commits, so clearing it is safe for both roles.
        if (Directory.Exists(repoPath))
            Directory.Delete(repoPath, recursive: true);

        var parent = Directory.GetParent(repoPath)?.FullName ?? Path.GetTempPath();
        Directory.CreateDirectory(parent);

        LogClone(_options.RemoteUrl, _options.Branch);
        await gitClient.RunAsync(parent, cancellationToken,
            [.. _credentialArgs, "clone", "--branch", _options.Branch, "--single-branch", _options.RemoteUrl, repoPath]);
    }

    private async Task IntegrateAsync(string repoPath, CancellationToken cancellationToken)
    {
        await gitClient.RunAsync(repoPath, cancellationToken, [.. _credentialArgs, "fetch", "origin", _options.Branch]);

        if (_options.Role == GitReconciliationRole.Writer)
        {
            // ff-only: a non-ff divergence throws (the D7 single-writer signal). Never reset --hard a
            // Writer clone — it may hold un-pushed export commits.
            await gitClient.RunAsync(repoPath, cancellationToken, "merge", "--ff-only", $"origin/{_options.Branch}");
        }
        else
        {
            // Consumer: disposable mirror.
            await gitClient.RunAsync(repoPath, cancellationToken, "reset", "--hard", $"origin/{_options.Branch}");
        }
    }

    /// <summary>
    /// Builds the credential <c>-c …</c> prefix per <see cref="GitCredentialsMode"/>, writing a 0600
    /// credential-store file for Token mode so the token never appears on argv. HostDefault contributes
    /// nothing (ambient helper / ssh-agent).
    /// </summary>
    private string[] PrepareCredentialArgs(string repoPath)
    {
        switch (_options.CredentialsMode)
        {
            case GitCredentialsMode.SshKey when !string.IsNullOrWhiteSpace(_options.KeyPath):
                return ["-c", $"core.sshCommand=ssh -i {_options.KeyPath} -o IdentitiesOnly=yes"];

            case GitCredentialsMode.Token when !string.IsNullOrWhiteSpace(_options.Token):
                var credFile = WriteTokenCredentialFile(repoPath);
                return ["-c", $"credential.helper=store --file={credFile}"];

            default:
                return [];
        }
    }

    private string WriteTokenCredentialFile(string repoPath)
    {
        var dir = Directory.GetParent(repoPath)?.FullName ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        var credFile = Path.Combine(dir, ".git-credentials");
        var host = TryGetHost(_options.RemoteUrl);
        File.WriteAllText(credFile, $"https://x-access-token:{_options.Token}@{host}{Environment.NewLine}");
        TrySetOwnerOnlyPermissions(credFile);
        return credFile;
    }

    private static string TryGetHost(string remoteUrl) =>
        Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) ? uri.Host : "github.com";

    private static void TrySetOwnerOnlyPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort hardening; the credential helper still functions without it.
        }
    }

    private void LogClone(string remoteUrl, string branch)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Cloning workflows repository {url} (branch {branch})", remoteUrl, branch);
    }
}
