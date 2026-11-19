using Elsa.Connections.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

/// <summary>Installs Connections mappings in a consumer-owned context without inheritance.</summary>
public static class ConnectionsModelBuilderExtensions
{
    /// <summary>
    /// Adds only the connection metadata and reference tables. The host owns its migrations and
    /// connection; unrelated entities retain their configured comparison and payload semantics.
    /// </summary>
    public static ModelBuilder AddConnectionsModel(this ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var mapping = new ConnectionRecordConfiguration();
        modelBuilder.ApplyConfiguration<ConnectionRecord>(mapping);
        modelBuilder.ApplyConfiguration<ConnectionSecretReferenceRecord>(mapping);
        var payloadType = providerName switch
        {
            EfProviderNames.Sqlite => "TEXT",
            EfProviderNames.SqlServer => "nvarchar(max)",
            EfProviderNames.PostgreSql => "jsonb",
            EfProviderNames.MySql => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(providerName))
        };
        modelBuilder.Entity<ConnectionRecord>().Property(x => x.Payload).HasColumnType(payloadType);
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName,
            new Type[] { typeof(ConnectionRecord), typeof(ConnectionSecretReferenceRecord) },
            "TenantId", "NormalizedName", "NameSearchKey", "DisplayNameSearchKey", "TypeLookupKey", "SchemeLookupKey", "SecretLookupHash", "SchemaVersion");
        return modelBuilder;
    }
}
