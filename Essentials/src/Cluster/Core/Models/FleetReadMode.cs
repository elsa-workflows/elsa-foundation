namespace Elsa.Cluster.Core.Models;

/// <summary>How current a fleet view must be (FR-010).</summary>
public enum FleetReadMode
{
    /// <summary>
    /// Reads the provider's authoritative store. Any decision that must not be premature, such as finalizing a schema
    /// version, uses fresh reads.
    /// </summary>
    Fresh,

    /// <summary>May lag by at most one heartbeat interval. Routing may use it.</summary>
    Cached
}
