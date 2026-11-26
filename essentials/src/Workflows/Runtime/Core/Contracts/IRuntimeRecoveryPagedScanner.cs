namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Marker for recovery scanners that provide complete bounded continuation semantics through
/// <see cref="IRuntimeRecoveryScanner.ScanPageAsync"/>.
/// </summary>
/// <remarks>
/// Existing scanners may continue implementing only <see cref="IRuntimeRecoveryScanner.ScanAsync"/>. Resumption
/// preserves that legacy path without pretending that a one-page result is a complete traversal; new production
/// scanners should implement this marker and retain every continuation returned by the page contract.
/// </remarks>
public interface IRuntimeRecoveryPagedScanner : IRuntimeRecoveryScanner
{
    /// <summary>
    /// Indicates that this scanner's backing store can execute the bounded page contract for the current composition.
    /// A scanner may implement the marker for its built-in path while returning <see langword="false"/> for a
    /// legacy/custom store that has no due-ordered liveness capability.
    /// </summary>
    bool SupportsPaging => true;
}
