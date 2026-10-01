using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// The EF provider's own settings (spec 183, FR-024). They follow the other EF modules': provider, connection string or
/// connection name, schema and pooling, plus the cleanup period. The host id, heartbeat interval, expiry period and skew
/// allowance are the contract's <see cref="Core.Options.ClusterMembershipOptions"/>, shared by every provider.
/// </summary>
public sealed class EfClusterMembershipOptions : IEfHostStoreOptions
{
    /// <summary>The subsection of <see cref="Core.Options.ClusterMembershipOptions.SectionName"/> these are read from.</summary>
    public const string SectionKey = "EntityFrameworkCore";

    /// <summary>Relational provider for the membership store: Sqlite, SqlServer, PostgreSql or MySql.</summary>
    /// <remarks>SQLite serves several processes on one machine only; it suits development and tests (FR-033).</remarks>
    public string Provider { get; set; } = EfProviderNames.Sqlite;

    /// <summary>
    /// Optional explicit connection string. When omitted, <see cref="ConnectionName"/> or the shared
    /// <c>ConnectionStrings:Elsa</c> is used, read from the host's configuration. It must point at the primary: a read
    /// replica cannot give read-after-write (FR-032).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Named connection under <c>ConnectionStrings</c> when <see cref="ConnectionString"/> is omitted.</summary>
    public string? ConnectionName { get; set; }

    /// <summary>Optional database schema for the membership table and its migrations history table.</summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per operation.</summary>
    public bool Pooling { get; set; }

    /// <summary>
    /// How long an entry that has left, or expired, is kept before any member may delete it (FR-031). It must exceed the
    /// expiry period plus the skew allowance, which a startup check enforces.
    /// </summary>
    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromMinutes(10);
}
