using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for tenant-local Foundation Identity IAM repositories.</summary>
public abstract class IdentityIamDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();
    public DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<ClaimMappingEntity> ClaimMappings => Set<ClaimMappingEntity>();
    public DbSet<ExternalIdentityEntity> ExternalIdentities => Set<ExternalIdentityEntity>();
    public DbSet<UserClaimEntity> UserClaims => Set<UserClaimEntity>();
    public DbSet<RoleClaimEntity> RoleClaims => Set<RoleClaimEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<UserTokenEntity> UserTokens => Set<UserTokenEntity>();
    public DbSet<TenantMembershipEntity> TenantMemberships => Set<TenantMembershipEntity>();
    public DbSet<UserNameReservationEntity> UserNameReservations => Set<UserNameReservationEntity>();
    public DbSet<EmailReservationEntity> EmailReservations => Set<EmailReservationEntity>();
    public DbSet<RoleNameReservationEntity> RoleNameReservations => Set<RoleNameReservationEntity>();
    public DbSet<MutationReceiptEntity> MutationReceipts => Set<MutationReceiptEntity>();

    protected abstract string ExpectedProviderNameValue { get; }

    /// <summary>Fails closed when a host binds a derived context to the wrong provider engine.</summary>
    public void EnsureProviderBinding() => EfProviderGuard.Ensure(this, ExpectedProviderNameValue);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new ApplicationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RoleEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ClaimMappingEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalIdentityEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserClaimEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RoleClaimEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserTokenEntityConfiguration());
        modelBuilder.ApplyConfiguration(new TenantMembershipEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserNameReservationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new EmailReservationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RoleNameReservationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new MutationReceiptEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    /// <summary>
    /// The string columns this module compares in SQL beyond the ones a key or an index already covers:
    /// the lookup keys every scoped read matches on, the normalized forms behind them, and the identity
    /// material stored beside each key. Display names, descriptions, JSON columns and secret material are
    /// absent; nothing compares them in SQL, and the uniqueness that matters runs through the normalized
    /// keys. A case-insensitive database default here would let two reservations differing only in case
    /// collide.
    /// </summary>
    private static readonly string[] OrdinallyComparedColumns =
    [
        "Id", "TenantId", "TenantLookupKey",
        "UserId", "UserLookupKey", "RoleId", "RoleLookupKey",
        "RuleId", "RuleLookupKey", "CredentialId", "CredentialLookupKey", "SubjectId",
        "Provider", "ProviderLookupKey", "ProviderSubject", "ProviderSubjectLookupKey",
        "NormalizedEmail", "NormalizedEmailKey",
        "NormalizedUserName", "NormalizedUserNameKey",
        "NormalizedRoleName", "NormalizedRoleNameKey",
        "NormalizedName", "NormalizedNameKey",
        "ClaimKey", "TokenKey",
        "MutationReceiptId", "OperationId", "RequestFingerprint",
        "ApplicationId", "ClientId"
    ];

    /// <summary>Binds this module's ordinal columns to <paramref name="providerName"/>'s binary collation, per column.</summary>
    protected static void ApplyOrdinalCollation(ModelBuilder modelBuilder, string providerName) =>
        EfOrdinalCollation.Apply(modelBuilder, providerName, OrdinallyComparedColumns);
}
