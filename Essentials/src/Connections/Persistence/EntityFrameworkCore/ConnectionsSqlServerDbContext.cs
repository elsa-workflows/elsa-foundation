using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

public sealed class ConnectionsSqlServerDbContext(DbContextOptions<ConnectionsSqlServerDbContext> options) : ConnectionsDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConnectionRecord>().Property(x => x.Payload).HasColumnType("nvarchar(max)");
    }
}
