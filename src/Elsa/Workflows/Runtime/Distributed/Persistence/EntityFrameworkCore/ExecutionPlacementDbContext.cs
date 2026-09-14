using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for the D01 placement store.</summary>
public abstract class ExecutionPlacementDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ExecutionPlacementLeaseEntity> PlacementLeases => Set<ExecutionPlacementLeaseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ExecutionPlacementLeaseEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
