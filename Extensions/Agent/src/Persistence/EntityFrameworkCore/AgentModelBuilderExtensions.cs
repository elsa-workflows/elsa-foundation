using Elsa.Agent.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

/// <summary>Installs only the agent-owned entities in an ordinary consumer-owned context.</summary>
public static class AgentModelBuilderExtensions
{
    public static ModelBuilder AddAgentModel(this ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.AddSourceRevisions("elsa_agent_sources", providerName);
        modelBuilder.ApplyConfiguration(new AgentTurnRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConversationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AgentOperationRecordConfiguration());
        var payloadType = providerName switch
        {
            EfProviderNames.Sqlite => "TEXT", EfProviderNames.SqlServer => "nvarchar(max)",
            EfProviderNames.PostgreSql => "jsonb", EfProviderNames.MySql => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(providerName))
        };
        modelBuilder.Entity<AgentTurnRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        modelBuilder.Entity<AgentConversationRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        modelBuilder.Entity<AgentOperationRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(AgentOperationRecord) },
            nameof(AgentOperationRecord.ScopeKey), nameof(AgentOperationRecord.OperationKey), nameof(AgentOperationRecord.SchemaVersion));
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(AgentConversationRecord) },
            nameof(AgentConversationRecord.ScopeKey), nameof(AgentConversationRecord.Id), nameof(AgentConversationRecord.SchemaVersion));
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(AgentTurnRecord) },
            nameof(AgentTurnRecord.ScopeKey), nameof(AgentTurnRecord.Handle), nameof(AgentTurnRecord.OperationKey), nameof(AgentTurnRecord.SchemaVersion), nameof(AgentTurnRecord.Fence));
        return modelBuilder;
    }
}
