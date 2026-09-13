using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;

internal sealed class CredentialEntityConfiguration : IEntityTypeConfiguration<CredentialEntity>
{
    public void Configure(EntityTypeBuilder<CredentialEntity> builder)
    {
        builder.ToTable(IdentityIamEfModule.CredentialTableName);
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).HasMaxLength(64).IsRequired();

        ConfigureText(builder.Property(credential => credential.TenantId), required: true);
        ConfigureText(builder.Property(credential => credential.TenantLookupKey), required: true);
        ConfigureText(builder.Property(credential => credential.CredentialId), required: true);
        ConfigureText(builder.Property(credential => credential.CredentialLookupKey), required: true);
        ConfigureText(builder.Property(credential => credential.SubjectId), required: true);
        ConfigureText(builder.Property(credential => credential.HashedSecret), required: true);
        ConfigureText(builder.Property(credential => credential.HashAlgorithm), required: true);

        builder.Property(credential => credential.SubjectType).IsRequired();
        builder.Property(credential => credential.Kind).IsRequired();
        builder.Property(credential => credential.Status).IsRequired();
        builder.Property(credential => credential.ExpiresAt)
            .HasMaxLength(35)
            .HasConversion(
                value => IdentityEntityFrameworkDateTimeOffsetCodec.Encode(value),
                value => IdentityEntityFrameworkDateTimeOffsetCodec.Decode(value));
        builder.Property(credential => credential.Revision).IsRequired().IsConcurrencyToken();
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required)
    {
        property.HasConversion(
            value => IdentityEntityFrameworkUtf16Codec.Encode(value),
            value => IdentityEntityFrameworkUtf16Codec.Decode(value));
        property.IsRequired(required);
    }
}
