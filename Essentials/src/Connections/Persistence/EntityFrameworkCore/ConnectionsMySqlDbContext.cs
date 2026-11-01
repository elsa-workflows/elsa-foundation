using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

public sealed class ConnectionsMySqlDbContext(DbContextOptions<ConnectionsMySqlDbContext> options) : ConnectionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation("MySQL:Charset", "utf8mb4");
        modelBuilder.Entity<ConnectionRecord>().Property(x => x.Payload).HasColumnType("json");
    }
}
