using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

/// <summary>Installs only the specialist-owned entities in an ordinary consumer-owned context.</summary>
public static class SpecialistsModelBuilderExtensions
{
    public static ModelBuilder AddSpecialistsModel(this ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new SpecialistTurnRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SpecialistConversationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SpecialistOperationRecordConfiguration());
        var payloadType = providerName switch
        {
            EfProviderNames.Sqlite => "TEXT", EfProviderNames.SqlServer => "nvarchar(max)",
            EfProviderNames.PostgreSql => "jsonb", EfProviderNames.MySql => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(providerName))
        };
        modelBuilder.Entity<SpecialistTurnRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        modelBuilder.Entity<SpecialistConversationRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        modelBuilder.Entity<SpecialistOperationRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(SpecialistOperationRecord) },
            nameof(SpecialistOperationRecord.ScopeKey), nameof(SpecialistOperationRecord.OperationKey), nameof(SpecialistOperationRecord.SchemaVersion));
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(SpecialistConversationRecord) },
            nameof(SpecialistConversationRecord.ScopeKey), nameof(SpecialistConversationRecord.Id), nameof(SpecialistConversationRecord.SchemaVersion));
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(SpecialistTurnRecord) },
            nameof(SpecialistTurnRecord.ScopeKey), nameof(SpecialistTurnRecord.Handle), nameof(SpecialistTurnRecord.OperationKey), nameof(SpecialistTurnRecord.SchemaVersion), nameof(SpecialistTurnRecord.Fence));
        return modelBuilder;
    }
}
