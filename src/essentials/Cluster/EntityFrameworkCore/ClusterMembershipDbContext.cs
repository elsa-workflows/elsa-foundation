using Elsa.Cluster.EntityFrameworkCore.Configuration;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// The provider-neutral membership model: one table, one row per member. Provider-derived contexts stay separate so each
/// engine owns its collation and its migrations without leaking an engine into this module.
/// </summary>
public abstract class ClusterMembershipDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>
    /// Host ids are compared ordinally (spec 183, FR-003). On an engine whose default collation is linguistic, two host
    /// ids that differ only in case would otherwise share a key, and the join's uniqueness check would treat them as one.
    /// </summary>
    private static readonly string[] OrdinallyComparedColumns = ["HostId", "Incarnation", "CurrentHostId"];

    public DbSet<ClusterMemberEntity> Members => Set<ClusterMemberEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new ClusterMemberEntityConfiguration());
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    protected static void ApplyOrdinalCollation(ModelBuilder modelBuilder, string providerName) =>
        EfOrdinalCollation.Apply(modelBuilder, providerName, OrdinallyComparedColumns);
}
