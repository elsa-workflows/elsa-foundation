using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

/// <summary>Agent-owned session, record and audit model; installing the mappings in a consumer context is also supported.</summary>
public abstract class AgentDbContext : DbContext
{
    protected AgentDbContext(DbContextOptions options) : base(options) { }
    internal DbSet<AgentSessionRow> Sessions => Set<AgentSessionRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.AddAgentModel(Database.ProviderName!);
        modelBuilder.MapSchemaFinalization(AgentEfModule.HistoryModuleName);
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
        EfOrdinalCollation.Apply(modelBuilder, Database.ProviderName!);
    }
    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
