using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

/// <summary>One provider-neutral relational unit for the D02 stream head and D03 command transport.</summary>
public abstract class ExecutionCommandTransportDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ExecutionCommandStreamHeadEntity> CommandStreamHeads => Set<ExecutionCommandStreamHeadEntity>();
    public DbSet<ExecutionCommandTransportItemEntity> CommandTransportItems => Set<ExecutionCommandTransportItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ExecutionCommandStreamHeadEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutionCommandTransportItemEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
