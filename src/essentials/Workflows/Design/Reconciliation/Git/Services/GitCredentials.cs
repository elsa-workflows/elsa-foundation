using Elsa.Workflows.Design.Reconciliation.Git.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// What a git command that reaches the remote carries to authenticate (FR-013): per-invocation <c>-c …</c> arguments,
/// and environment variables of that one git process. Nothing secret is on the command line or on disk.
/// </summary>
/// <remarks>
/// A token reaches git through a credential helper that reads it from the <see cref="TokenVariable"/> environment
/// variable (#2197). No credential file is written, so there is none to share between processes, to expose while it is
/// written, to pre-create in a predictable temp path, or to leave behind; the environment of a process is readable only
/// by its own user. The helper is scoped to the remote's scheme and host, so it never answers for another host, such as
/// one a redirect leads to, and it first clears the helpers configured on the machine for that host: they cannot answer
/// in place of the configured token, and git does not hand them the token to store after it authenticates.
/// </remarks>
public sealed class GitCredentials
{
    /// <summary>The environment variable the token helper reads the token from.</summary>
    public const string TokenVariable = "ELSA_GIT_TOKEN";

    // Git appends the action (get, store, erase) as the argument; only a get is answered.
    private const string TokenHelper =
        "!f() { test \"$1\" = get || return 0; echo username=x-access-token; echo \"password=$" + TokenVariable + "\"; }; f";

    private static readonly GitCredentials None = new([], new Dictionary<string, string>());

    private GitCredentials(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        Arguments = arguments;
        Environment = environment;
    }

    /// <summary>The <c>-c …</c> arguments that go before the git command.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The variables added to the environment of the git process.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// The credentials <paramref name="options"/> configure: <c>core.sshCommand</c> for an SSH key, the token helper and
    /// its variable for a token, and nothing for <see cref="GitCredentialsMode.HostDefault"/> (ambient helper, ssh-agent).
    /// </summary>
    public static GitCredentials For(GitReconciliationOptions options) => options.CredentialsMode switch
    {
        GitCredentialsMode.SshKey when !string.IsNullOrWhiteSpace(options.KeyPath) =>
            new(["-c", $"core.sshCommand=ssh -i {options.KeyPath} -o IdentitiesOnly=yes"], None.Environment),
        GitCredentialsMode.Token when !string.IsNullOrWhiteSpace(options.Token) =>
            Token(options.RemoteUrl, options.Token),
        _ => None,
    };

    private static GitCredentials Token(string remoteUrl, string token)
    {
        // An empty value clears the helpers configured before it, so only this one answers for the remote's host.
        var helper = $"credential.{HostScope(remoteUrl)}helper";
        return new(["-c", $"{helper}=", "-c", $"{helper}={TokenHelper}"], new Dictionary<string, string> { [TokenVariable] = token });
    }

    /// <summary><c>{scheme}://{host[:port]}.</c> for an HTTP(S) remote, so git applies the helper to that host only; else empty.</summary>
    private static string HostScope(string remoteUrl) =>
        Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? $"{uri.Scheme}://{uri.Authority}."
            : "";
}
