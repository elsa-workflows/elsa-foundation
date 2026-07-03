namespace Elsa.Api.FastEndpoints.Options;

/// <summary>
/// Per-shell security options for Elsa FastEndpoints. Bound via the standard options system;
/// each shell gets its own instance, so one shell relaxing security never affects another.
/// </summary>
public sealed class ApiSecurityOptions
{
    /// <summary>
    /// When <c>true</c>, every Elsa endpoint in the shell accepts anonymous requests.
    /// Defaults to <c>false</c> (secure). Intended for local development and testing only;
    /// enabling it produces a prominent startup warning.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// The name of the shell these options apply to. Used for diagnostics.
    /// </summary>
    public string? ShellName { get; set; }
}
