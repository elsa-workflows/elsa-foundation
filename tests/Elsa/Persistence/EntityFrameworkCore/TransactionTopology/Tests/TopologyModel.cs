using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests;

internal enum TopologyLane
{
    Runtime,
    Design,
    Publishing
}

internal enum TopologyProvider
{
    Sqlite,
    PostgreSql,
    SqlServer,
    MySql
}

internal sealed class TopologyRow
{
    public required string Id { get; init; }
    public required string Lane { get; init; }
    public required string TenantId { get; init; }
    public required string Payload { get; init; }
}

internal abstract class TopologyDbContext(DbContextOptions options, TopologyLane lane) : DbContext(options)
{
    public DbSet<TopologyRow> Rows => Set<TopologyRow>();
    public TopologyLane Lane { get; } = lane;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TopologyRow>(entity =>
        {
            entity.ToTable("ef_transaction_topology_rows");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lane).HasMaxLength(32).IsRequired();
            entity.Property(row => row.TenantId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Payload).HasMaxLength(2000).IsRequired();
        });
    }
}

internal sealed class RuntimeTopologyDbContext(DbContextOptions<RuntimeTopologyDbContext> options)
    : TopologyDbContext(options, TopologyLane.Runtime);

internal sealed class DesignTopologyDbContext(DbContextOptions<DesignTopologyDbContext> options)
    : TopologyDbContext(options, TopologyLane.Design);

internal sealed class PublishingTopologyDbContext(DbContextOptions<PublishingTopologyDbContext> options)
    : TopologyDbContext(options, TopologyLane.Publishing);
