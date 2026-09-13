using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;

internal sealed class ApplicationEntityConfiguration : IEntityTypeConfiguration<ApplicationEntity>
{
    public void Configure(EntityTypeBuilder<ApplicationEntity> builder)
    {
        builder.ToTable(IdentityIamEfModule.ApplicationTableName);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasMaxLength(64).IsRequired();

        ConfigureText(builder.Property(entity => entity.TenantId));
        ConfigureText(builder.Property(entity => entity.ApplicationId));
        ConfigureText(builder.Property(entity => entity.ClientId));
        ConfigureText(builder.Property(entity => entity.DisplayName));

        builder.Property(entity => entity.Type).HasConversion<int>().IsRequired();
        builder.Property(entity => entity.Ownership).HasConversion<int>().IsRequired();
        builder.Property(entity => entity.AllowedGrantTypesJson).IsRequired();
        builder.Property(entity => entity.ScopesJson).IsRequired();
        builder.Property(entity => entity.Revision).IsRequired().IsConcurrencyToken();
    }

    private static void ConfigureText(PropertyBuilder<string> property) =>
        property.HasConversion(
            value => IdentityEntityFrameworkUtf16Codec.Encode(value),
            value => IdentityEntityFrameworkUtf16Codec.Decode(value));
}
