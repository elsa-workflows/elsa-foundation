using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;

/// <summary>Provider-neutral relational mapping for the Identity authority and relationship rows.</summary>
public sealed class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.UserTableName);
        ConfigureText(builder.Property(x => x.UserId), true);
        ConfigureSortable(builder.Property(x => x.UserIdOrderKey));
        ConfigureText(builder.Property(x => x.UserName), true);
        ConfigureNullableText(builder.Property(x => x.NormalizedUserName), false);
        ConfigureNullableTechnical(builder.Property(x => x.NormalizedUserNameKey), false);
        ConfigureNullableText(builder.Property(x => x.Email), false);
        ConfigureNullableText(builder.Property(x => x.NormalizedEmail), false);
        ConfigureNullableTechnical(builder.Property(x => x.NormalizedEmailKey), false);
        ConfigureNullableText(builder.Property(x => x.DisplayName), false);
        ConfigureText(builder.Property(x => x.RoleIdsJson), true);
        ConfigureText(builder.Property(x => x.DirectPermissionsJson), true);
        ConfigureText(builder.Property(x => x.ClaimIdsJson), true);
        ConfigureText(builder.Property(x => x.LoginIdsJson), true);
        ConfigureText(builder.Property(x => x.RoleLinkIdsJson), true);
        ConfigureText(builder.Property(x => x.TokenIdsJson), true);
        ConfigureText(builder.Property(x => x.TenantMembershipIdsJson), true);
        ConfigureNullableText(builder.Property(x => x.PasswordHash), false);
        ConfigureNullableText(builder.Property(x => x.SecurityStamp), false);
        ConfigureNullableText(builder.Property(x => x.ConcurrencyStamp), false);
        ConfigureNullableText(builder.Property(x => x.PhoneNumber), false);
        ConfigureDateTime(builder.Property(x => x.LockoutEnd));
        builder.HasIndex(x => new { x.TenantLookupKey, x.NormalizedUserNameKey }).HasDatabaseName("ix_identity_users_name");
        builder.HasIndex(x => new { x.TenantLookupKey, x.NormalizedEmailKey }).HasDatabaseName("ix_identity_users_email");
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserIdOrderKey, x.Id }).HasDatabaseName("ix_identity_users_page");
        ConfigureIntegers(builder.Property(x => x.Status), builder.Property(x => x.Ownership), builder.Property(x => x.AccessFailedCount));
    }

    internal static void ConfigureText(PropertyBuilder<string> property, bool required) =>
        property.HasConversion(
            value => IdentityEntityFrameworkUtf16Codec.Encode(value),
            value => IdentityEntityFrameworkUtf16Codec.Decode(value))
            .IsRequired(required);

    internal static void ConfigureNullableText(PropertyBuilder<string?> property, bool required) =>
        property.HasConversion(
            value => value == null ? null : IdentityEntityFrameworkUtf16Codec.Encode(value),
            value => value == null ? null : IdentityEntityFrameworkUtf16Codec.Decode(value))
            .IsRequired(required);

    internal static void ConfigureTechnical(PropertyBuilder<string> property, bool required = true) =>
        property.HasMaxLength(64).IsUnicode(false).IsRequired(required);

    internal static void ConfigureNullableTechnical(PropertyBuilder<string?> property, bool required = true) =>
        property.HasMaxLength(64).IsUnicode(false).IsRequired(required);

    internal static void ConfigureSortable(PropertyBuilder<byte[]> property) =>
        property.HasMaxLength(EfIdentityStoreSupport.SortableKeyWidthBytes).IsRequired();

    internal static void ConfigureDateTime(PropertyBuilder<DateTimeOffset?> property) =>
        property.HasMaxLength(35).HasConversion(
            value => IdentityEntityFrameworkDateTimeOffsetCodec.Encode(value),
            value => IdentityEntityFrameworkDateTimeOffsetCodec.Decode(value));

    internal static void ConfigureDateTime(PropertyBuilder<DateTimeOffset> property) =>
        property.HasMaxLength(35).HasConversion(
            value => value.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            value => DateTimeOffset.ParseExact(value, "O", System.Globalization.CultureInfo.InvariantCulture));

    private static void ConfigureIntegers(params PropertyBuilder<int>[] properties)
    {
        foreach (var property in properties)
            property.IsRequired();
    }
}

public sealed class RoleEntityConfiguration : IEntityTypeConfiguration<RoleEntity>
{
    public void Configure(EntityTypeBuilder<RoleEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.RoleTableName);
        ConfigureText(builder.Property(x => x.RoleId), true);
        UserEntityConfiguration.ConfigureSortable(builder.Property(x => x.RoleIdOrderKey));
        ConfigureText(builder.Property(x => x.Name), true);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.NormalizedName), false);
        UserEntityConfiguration.ConfigureNullableTechnical(builder.Property(x => x.NormalizedNameKey), false);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.Description), false);
        ConfigureText(builder.Property(x => x.PermissionsJson), true);
        ConfigureText(builder.Property(x => x.ClaimIdsJson), true);
        ConfigureText(builder.Property(x => x.UserLinkIdsJson), true);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.ConcurrencyStamp), false);
        builder.Property(x => x.System).IsRequired();
        builder.HasIndex(x => new { x.TenantLookupKey, x.NormalizedNameKey }).HasDatabaseName("ix_identity_roles_name");
        builder.HasIndex(x => new { x.TenantLookupKey, x.RoleIdOrderKey, x.Id }).HasDatabaseName("ix_identity_roles_page");
        builder.HasIndex(x => x.TenantLookupKey).HasDatabaseName("ix_identity_roles_tenant");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) =>
        UserEntityConfiguration.ConfigureText(property, required);

}

public sealed class ClaimMappingEntityConfiguration : IEntityTypeConfiguration<ClaimMappingEntity>
{
    public void Configure(EntityTypeBuilder<ClaimMappingEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.ClaimMappingTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.Provider), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.ProviderLookupKey), true);
        ConfigureText(builder.Property(x => x.RuleId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.RuleLookupKey), true);
        UserEntityConfiguration.ConfigureSortable(builder.Property(x => x.RuleIdOrderKey));
        ConfigureText(builder.Property(x => x.MatchClaimType), true);
        ConfigureText(builder.Property(x => x.MatchValue), true);
        ConfigureText(builder.Property(x => x.GrantRolesJson), true);
        ConfigureText(builder.Property(x => x.GrantPermissionsJson), true);
        builder.Property(x => x.Order).IsRequired();
        builder.Property(x => x.StopOnMatch).IsRequired();
        builder.HasIndex(x => new { x.TenantLookupKey, x.ProviderLookupKey, x.Order, x.RuleIdOrderKey, x.Id }).HasDatabaseName("ix_identity_claim_mappings_provider");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class ExternalIdentityEntityConfiguration : IEntityTypeConfiguration<ExternalIdentityEntity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentityEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.ExternalIdentityTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.Provider), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.ProviderLookupKey), true);
        ConfigureText(builder.Property(x => x.ProviderSubject), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.ProviderSubjectLookupKey), true);
        builder.Property(x => x.ExternalOrderKey)
            .HasMaxLength(EfIdentityStoreSupport.SortableKeyWidthBytes * 2)
            .IsRequired();
        ConfigureText(builder.Property(x => x.UserId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.UserLookupKey), true);
        ConfigureDateTime(builder.Property(x => x.LinkedAt));
        ConfigureDateTime(builder.Property(x => x.LastSeenAt));
        builder.Property(x => x.LinkPolicy).IsRequired();
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey, x.Id }).HasDatabaseName("ix_identity_external_logins_user");
        // UserLookupKey already includes the tenant identity. Omitting the redundant tenant and
        // record hashes keeps the 400+400-code-unit order tuple below SQL Server's 1,700-byte
        // index-key limit while retaining a selective, deterministic paging index.
        builder.HasIndex(x => new { x.UserLookupKey, x.ExternalOrderKey }).HasDatabaseName("ix_identity_external_logins_user_page");
        builder.HasIndex(x => new { x.TenantLookupKey, x.ProviderLookupKey, x.ProviderSubjectLookupKey }).IsUnique().HasDatabaseName("ux_identity_external_logins_subject");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
    private static void ConfigureDateTime(PropertyBuilder<DateTimeOffset> property) => UserEntityConfiguration.ConfigureDateTime(property);
    private static void ConfigureDateTime(PropertyBuilder<DateTimeOffset?> property) => UserEntityConfiguration.ConfigureDateTime(property);
}

public sealed class UserClaimEntityConfiguration : IEntityTypeConfiguration<UserClaimEntity>
{
    public void Configure(EntityTypeBuilder<UserClaimEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.UserClaimTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.UserId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.UserLookupKey), true);
        ConfigureText(builder.Property(x => x.ClaimType), true);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.ClaimValue), false);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.ClaimKey), true);
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey, x.Id }).HasDatabaseName("ix_identity_user_claims_user");
        builder.HasIndex(x => new { x.TenantLookupKey, x.ClaimKey, x.Id }).HasDatabaseName("ix_identity_user_claims_claim");
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey, x.ClaimKey }).IsUnique().HasDatabaseName("ux_identity_user_claims_claim");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class RoleClaimEntityConfiguration : IEntityTypeConfiguration<RoleClaimEntity>
{
    public void Configure(EntityTypeBuilder<RoleClaimEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.RoleClaimTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.RoleId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.RoleLookupKey), true);
        ConfigureText(builder.Property(x => x.ClaimType), true);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.ClaimValue), false);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.ClaimKey), true);
        builder.HasIndex(x => new { x.TenantLookupKey, x.RoleLookupKey, x.Id }).HasDatabaseName("ix_identity_role_claims_role");
        builder.HasIndex(x => new { x.TenantLookupKey, x.RoleLookupKey, x.ClaimKey }).IsUnique().HasDatabaseName("ux_identity_role_claims_claim");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class UserRoleEntityConfiguration : IEntityTypeConfiguration<UserRoleEntity>
{
    public void Configure(EntityTypeBuilder<UserRoleEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.UserRoleTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.UserId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.UserLookupKey), true);
        ConfigureText(builder.Property(x => x.RoleId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.RoleLookupKey), true);
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey, x.Id }).HasDatabaseName("ix_identity_user_roles_user");
        builder.HasIndex(x => new { x.TenantLookupKey, x.RoleLookupKey, x.Id }).HasDatabaseName("ix_identity_user_roles_role");
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey, x.RoleLookupKey }).IsUnique().HasDatabaseName("ux_identity_user_roles_pair");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class UserTokenEntityConfiguration : IEntityTypeConfiguration<UserTokenEntity>
{
    public void Configure(EntityTypeBuilder<UserTokenEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.UserTokenTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.UserId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.UserLookupKey), true);
        ConfigureText(builder.Property(x => x.LoginProvider), true);
        ConfigureText(builder.Property(x => x.Name), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TokenKey), true);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.Value), false);
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey, x.TokenKey }).IsUnique().HasDatabaseName("ux_identity_user_tokens_key");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class TenantMembershipEntityConfiguration : IEntityTypeConfiguration<TenantMembershipEntity>
{
    public void Configure(EntityTypeBuilder<TenantMembershipEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.TenantMembershipTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.UserId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.UserLookupKey), true);
        ConfigureText(builder.Property(x => x.RoleIdsJson), true);
        ConfigureText(builder.Property(x => x.DirectPermissionsJson), true);
        builder.Property(x => x.Status).IsRequired();
        builder.HasIndex(x => new { x.TenantLookupKey, x.UserLookupKey }).IsUnique().HasDatabaseName("ux_identity_tenant_memberships_key");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class UserNameReservationEntityConfiguration : IEntityTypeConfiguration<UserNameReservationEntity>
{
    public void Configure(EntityTypeBuilder<UserNameReservationEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.UserNameReservationTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.NormalizedUserName), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.NormalizedUserNameKey), true);
        ConfigureText(builder.Property(x => x.UserId), true);
        builder.HasIndex(x => new { x.TenantLookupKey, x.NormalizedUserNameKey }).IsUnique().HasDatabaseName("ux_identity_user_name_reservations_key");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class EmailReservationEntityConfiguration : IEntityTypeConfiguration<EmailReservationEntity>
{
    public void Configure(EntityTypeBuilder<EmailReservationEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.EmailReservationTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.NormalizedEmail), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.NormalizedEmailKey), true);
        ConfigureText(builder.Property(x => x.UserId), true);
        builder.HasIndex(x => new { x.TenantLookupKey, x.NormalizedEmailKey }).IsUnique().HasDatabaseName("ux_identity_email_reservations_key");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class RoleNameReservationEntityConfiguration : IEntityTypeConfiguration<RoleNameReservationEntity>
{
    public void Configure(EntityTypeBuilder<RoleNameReservationEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.RoleNameReservationTableName);
        ConfigureText(builder.Property(x => x.TenantId), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.TenantLookupKey), true);
        ConfigureText(builder.Property(x => x.NormalizedRoleName), true);
        UserEntityConfiguration.ConfigureTechnical(builder.Property(x => x.NormalizedRoleNameKey), true);
        ConfigureText(builder.Property(x => x.RoleId), true);
        builder.HasIndex(x => new { x.TenantLookupKey, x.NormalizedRoleNameKey }).IsUnique().HasDatabaseName("ux_identity_role_name_reservations_key");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);
}

public sealed class MutationReceiptEntityConfiguration : IEntityTypeConfiguration<MutationReceiptEntity>
{
    public void Configure(EntityTypeBuilder<MutationReceiptEntity> builder)
    {
        AuthorityEntityConfiguration.Configure(builder, IdentityIamEfModule.MutationReceiptTableName);
        ConfigureText(builder.Property(x => x.MutationReceiptId), true);
        ConfigureText(builder.Property(x => x.OperationId), true);
        ConfigureText(builder.Property(x => x.RequestFingerprint), true);
        ConfigureText(builder.Property(x => x.Message), true);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.AuthoritativeId), false);
        UserEntityConfiguration.ConfigureNullableText(builder.Property(x => x.FailedUnitId), false);
        ConfigureDateTime(builder.Property(x => x.CreatedAt));
        ConfigureDateTime(builder.Property(x => x.ExpiresAt));
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.Version).IsRequired(false);
        builder.HasIndex(x => x.MutationReceiptId).IsUnique().HasDatabaseName("ux_identity_mutation_receipts_id");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_identity_mutation_receipts_expiry");
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) => UserEntityConfiguration.ConfigureText(property, required);

    // Receipt expiry participates in a provider-side <= predicate and oldest-first ordering.
    // Persist UTC ticks as a bigint rather than an ISO string: ISO values with different offsets
    // are lexically ordered by their textual offset, not by their instant. The domain-facing
    // entity still exposes DateTimeOffset; offset is intentionally not receipt identity state.
    private static void ConfigureDateTime(PropertyBuilder<DateTimeOffset> property) => property.HasConversion(
        value => value.UtcDateTime.Ticks,
        value => new DateTimeOffset(new DateTime(value, DateTimeKind.Utc)));
}

internal static class AuthorityEntityConfiguration
{
    public static void Configure<TEntity>(EntityTypeBuilder<TEntity> builder, string tableName) where TEntity : class
    {
        builder.ToTable(tableName);
        builder.HasKey("Id");
        builder.Property<string>("Id").HasMaxLength(64).IsRequired();
        if (typeof(TEntity).GetProperty(nameof(UserEntity.TenantId)) is not null)
        {
            ConfigureText(builder.Property<string>(nameof(UserEntity.TenantId)), true);
            UserEntityConfiguration.ConfigureTechnical(builder.Property<string>(nameof(UserEntity.TenantLookupKey)), true);
        }
        builder.Property<long>("Revision").IsRequired().IsConcurrencyToken();
    }

    private static void ConfigureText(PropertyBuilder<string> property, bool required) =>
        property.HasConversion(value => IdentityEntityFrameworkUtf16Codec.Encode(value), value => IdentityEntityFrameworkUtf16Codec.Decode(value)).IsRequired(required);
}
