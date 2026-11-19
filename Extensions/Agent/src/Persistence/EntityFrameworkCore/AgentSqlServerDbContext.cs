using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

public sealed class AgentSqlServerDbContext(DbContextOptions<AgentSqlServerDbContext> options) : AgentDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentTurnRecord>().Property(x => x.Payload).HasColumnType("nvarchar(max)");
    }
}
