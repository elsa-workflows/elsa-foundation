using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

/// <summary>One provider-neutral relational unit for the D02 stream head and D03 command transport.</summary>
public abstract class ExecutionCommandTransportDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ExecutionCommandStreamHeadEntity> CommandStreamHeads => Set<ExecutionCommandStreamHeadEntity>();
    public DbSet<ExecutionCommandTransportItemEntity> CommandTransportItems => Set<ExecutionCommandTransportItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new ExecutionCommandStreamHeadEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutionCommandTransportItemEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
