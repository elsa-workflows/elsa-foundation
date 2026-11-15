namespace Elsa.Diagnostics.Persistence.Draining;

/// <summary>
/// Host-owned lifecycle seam for a singleton diagnostics capture drain. Concrete persistence adapters implement
/// this contract; the shared coordinator starts them after the host is composed and stops them before dependency
/// injection disposes their provider sessions.
/// </summary>
public interface IDiagnosticsPersistenceDrain
{
    /// <summary>Starts accepting and committing captured diagnostics. Repeated starts must be idempotent.</summary>
    void Start();

    /// <summary>
    /// Closes producers and completes the adapter's bounded shutdown policy. Implementations own their timeout;
    /// the host coordinator deliberately does not abandon this operation when the host stop token is canceled.
    /// If startup never reached this drain, stopping must become terminal without initiating provider I/O.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
