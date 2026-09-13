using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;

internal static class ProviderConfigurationEntityConfiguration
{
    public static void Configure<TEntity>(EntityTypeBuilder<TEntity> builder, string tableName, bool tenantRequired)
        where TEntity : ProviderConfigurationEntity
    {
        builder.ToTable(tableName);
        builder.Property(record => record.Id).HasMaxLength(64).IsRequired();
        builder.Property(record => record.TenantId).HasMaxLength(IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength).IsRequired(tenantRequired);
        builder.Property(record => record.TenantLookupKey).HasMaxLength(IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength).IsRequired(tenantRequired);
        builder.Property(record => record.Provider).HasMaxLength(IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength).IsRequired();
        builder.Property(record => record.ProviderLookupKey).HasMaxLength(IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength).IsRequired();
        builder.Property(record => record.Kind).HasMaxLength(IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength).IsRequired();
        builder.Property(record => record.Enabled).IsRequired();
        builder.Property(record => record.IsDefault).IsRequired();
        builder.Property(record => record.PermissionPropagation).IsRequired();
        builder.Property(record => record.SettingsJson).IsRequired();
        builder.Property(record => record.Revision).IsRequired().IsConcurrencyToken();

    }
}

public sealed class TenantProviderConfigurationEntityConfiguration : IEntityTypeConfiguration<TenantProviderConfigurationEntity>
{
    public void Configure(EntityTypeBuilder<TenantProviderConfigurationEntity> builder)
    {
        ProviderConfigurationEntityConfiguration.Configure(builder, IdentityProviderConfigurationEfModule.TenantTableName, tenantRequired: true);
    }
}

public sealed class GlobalProviderConfigurationEntityConfiguration : IEntityTypeConfiguration<GlobalProviderConfigurationEntity>
{
    public void Configure(EntityTypeBuilder<GlobalProviderConfigurationEntity> builder)
    {
        ProviderConfigurationEntityConfiguration.Configure(builder, IdentityProviderConfigurationEfModule.GlobalTableName, tenantRequired: false);
    }
}
