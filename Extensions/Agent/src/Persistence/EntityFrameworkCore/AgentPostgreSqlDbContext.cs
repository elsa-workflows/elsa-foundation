using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

public sealed class AgentPostgreSqlDbContext(DbContextOptions<AgentPostgreSqlDbContext> options) : AgentDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        modelBuilder.Entity<AgentTurnRecord>().Property(x => x.Payload).HasColumnType("jsonb");
    }
}
