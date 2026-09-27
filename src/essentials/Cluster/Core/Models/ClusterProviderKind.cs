namespace Elsa.Cluster.Core.Models;

/// <summary>What kind of membership provider a host runs (MR-006).</summary>
public enum ClusterProviderKind
{
    /// <summary>The default: a cluster of one, the host itself. It writes nothing durable and never lapses.</summary>
    InProcess,

    /// <summary>A provider whose members share an authoritative store, for clustered hosting.</summary>
    Durable
}
