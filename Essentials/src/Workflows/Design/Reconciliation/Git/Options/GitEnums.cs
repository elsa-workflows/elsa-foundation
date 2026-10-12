namespace Elsa.Workflows.Design.Reconciliation.Git.Options;

/// <summary>
/// The git↔catalog flow role (ADR 0034 D11). Drives the clone mode and whether the node exports.
/// </summary>
public enum GitReconciliationRole
{
    /// <summary>Imports git→catalog, read-only; never exports. Disposable <c>reset --hard</c> mirror.</summary>
    Consumer,

    /// <summary>
    /// Authors in Studio and exports catalog→git; imports at bootstrap. Persistent working copy that keeps its unpushed
    /// export commits until the remote moves (#2197). Every replica of a catalog may be a Writer: the push fences them.
    /// </summary>
    Writer,
}

/// <summary>How credentials reach git (FR-013). Never placed on the git command line.</summary>
public enum GitCredentialsMode
{
    /// <summary>Rely on the ambient credential helper / ssh-agent / deploy key. No configuration written.</summary>
    HostDefault,

    /// <summary>An SSH private key at <c>KeyPath</c> (configured via <c>core.sshCommand</c>).</summary>
    SshKey,

    /// <summary>
    /// An HTTPS token, handed to git by a credential helper scoped to the remote's host that reads it from the environment
    /// of each git process: never on the command line, never written to disk (#2197).
    /// </summary>
    Token,
}

/// <summary>When a Writer's local export commits reach the remote (FR-011). Honored only when Role=Writer.</summary>
public enum GitPushMode
{
    /// <summary>Commit locally; a push happens out-of-band. Default.</summary>
    Manual,

    /// <summary>Push fast-forward-only immediately after committing; refuse on divergence.</summary>
    Immediate,
}
