using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

public sealed class ConnectionsPostgreSqlDbContext(DbContextOptions<ConnectionsPostgreSqlDbContext> options) : ConnectionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        modelBuilder.Entity<ConnectionRecord>().Property(x => x.Payload).HasColumnType("jsonb");
    }
}
