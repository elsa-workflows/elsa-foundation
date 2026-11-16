using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

public sealed class AgentSqliteDbContext(DbContextOptions<AgentSqliteDbContext> options) : AgentDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentTurnRecord>().Property(x => x.Payload).HasColumnType("TEXT");
    }
}
