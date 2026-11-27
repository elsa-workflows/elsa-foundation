namespace Elsa.Diagnostics.Persistence.Draining;

/// <summary>
/// Acquires provider resources that must be ready before diagnostics drains accept captures.
/// The shared lifecycle coordinator owns the returned lease and releases it only after every
/// diagnostics drain has completed its bounded shutdown.
/// </summary>
public interface IDiagnosticsPersistenceStartupResource
{
    /// <summary>
    /// Acquires and validates the resource without starting its associated capture drain.
    /// Implementations must either return a complete lease or release any partial resources
    /// before propagating the original startup failure.
    /// </summary>
    ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-resource ownership held by the diagnostics lifecycle coordinator.
/// </summary>
public interface IDiagnosticsPersistenceResourceLease : IAsyncDisposable;
