namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Configuration for authenticated runtime recovery continuations.</summary>
public sealed class RuntimeRecoveryContinuationOptions
{
    /// <summary>
    /// UTF-8 signing key shared by every node that must accept recovery continuations. Durable Groundwork
    /// composition rejects the development fallback when this value is not configured.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Allows a process-local key for non-durable development composition. Durable providers should set this to
    /// <see langword="false"/> and configure <see cref="SigningKey"/>.
    /// </summary>
    public bool AllowEphemeralDevelopmentKey { get; set; } = true;
}
