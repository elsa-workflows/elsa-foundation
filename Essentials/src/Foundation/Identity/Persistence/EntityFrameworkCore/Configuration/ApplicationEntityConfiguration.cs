using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;

public sealed class ApplicationEntityConfiguration : IEntityTypeConfiguration<ApplicationEntity>
{
    public void Configure(EntityTypeBuilder<ApplicationEntity> builder)
    {
        builder.ToTable(IdentityIamEfModule.ApplicationTableName);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasMaxLength(64).IsRequired();

        ConfigureText(builder.Property(entity => entity.TenantId));
        UserEntityConfiguration.ConfigureTechnical(builder.Property(entity => entity.TenantLookupKey));
        ConfigureText(builder.Property(entity => entity.ApplicationId));
        UserEntityConfiguration.ConfigureSortable(builder.Property(entity => entity.ApplicationIdOrderKey));
        ConfigureText(builder.Property(entity => entity.ClientId));
        ConfigureText(builder.Property(entity => entity.DisplayName));

        builder.Property(entity => entity.Type).HasConversion<int>().IsRequired();
        builder.Property(entity => entity.Ownership).HasConversion<int>().IsRequired();
        builder.Property(entity => entity.Status).HasConversion<int>().IsRequired();
        builder.Property(entity => entity.AllowedGrantTypesJson).IsRequired();
        builder.Property(entity => entity.ScopesJson).IsRequired();
        builder.Property(entity => entity.RoleIdsJson).IsRequired();
        builder.Property(entity => entity.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.HasIndex(entity => new { entity.TenantLookupKey, entity.ApplicationIdOrderKey, entity.Id })
            .HasDatabaseName("ix_identity_applications_page");
    }

    private static void ConfigureText(PropertyBuilder<string> property) =>
        property.HasConversion(
            value => IdentityEntityFrameworkUtf16Codec.Encode(value),
            value => IdentityEntityFrameworkUtf16Codec.Decode(value));
}
