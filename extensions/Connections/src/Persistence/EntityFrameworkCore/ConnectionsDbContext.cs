using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

/// <summary>Connection-owned metadata model. The selected provider context owns its migration set.</summary>
public abstract class ConnectionsDbContext : DbContext
{
    protected ConnectionsDbContext(DbContextOptions options) : base(options) { }

    internal DbSet<ConnectionRecord> Connections => Set<ConnectionRecord>();
    internal DbSet<ConnectionSecretReferenceRecord> SecretReferences => Set<ConnectionSecretReferenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.AddConnectionsModel(Database.ProviderName!);
        modelBuilder.MapSchemaFinalization(ConnectionsEfModule.HistoryModuleName);
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
        // This context owns its whole model, including the shared schema-management tables.
        // Consumer-owned contexts use the entity-scoped declaration in AddConnectionsModel instead.
        EfOrdinalCollation.Apply(modelBuilder, Database.ProviderName!);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
