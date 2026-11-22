using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// The EF provider's own settings. They follow the other EF modules': provider, connection string or
/// connection name, schema and pooling, plus the cleanup period. The host id, heartbeat interval, expiry period and skew
/// allowance are the contract's <see cref="Core.Options.ClusterMembershipOptions"/>, shared by every provider.
/// </summary>
/// <remarks>
/// SQLite serves several processes on one machine only, and the connection must reach the primary, since a read
/// replica cannot give read-after-write.
/// </remarks>
public sealed class EfClusterMembershipOptions : EfHostStoreOptions
{
    /// <summary>The subsection of <see cref="Core.Options.ClusterMembershipOptions.SectionName"/> these are read from.</summary>
    public const string SectionKey = "EntityFrameworkCore";

    /// <summary>
    /// How long an entry that has left, or expired, is kept before any member may delete it. It must exceed the
    /// expiry period plus the skew allowance, which a startup check enforces.
    /// </summary>
    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromMinutes(10);
}
