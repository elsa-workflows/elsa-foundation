using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Schema ownership for opt-in EF persistence of tenant-local Identity IAM records.</summary>
public static class IdentityIamEfModule
{
    public const string HistoryModuleName = "ElsaIdentityIam";
    public const string ApplicationTableName = "identity_applications";
    public const string CredentialTableName = "identity_credentials";
    public const string UserTableName = "identity_users";
    public const string RoleTableName = "identity_roles";
    public const string ClaimMappingTableName = "identity_claim_mappings";
    public const string ExternalIdentityTableName = "identity_external_logins";
    public const string UserClaimTableName = "identity_user_claims";
    public const string RoleClaimTableName = "identity_role_claims";
    public const string UserRoleTableName = "identity_user_roles";
    public const string UserTokenTableName = "identity_user_tokens";
    public const string TenantMembershipTableName = "identity_tenant_memberships";
    public const string UserNameReservationTableName = "identity_user_name_reservations";
    public const string EmailReservationTableName = "identity_email_reservations";
    public const string RoleNameReservationTableName = "identity_role_name_reservations";
    public const string MutationReceiptTableName = "identity_mutation_receipts";
    public const string DefaultConnectionName = "ElsaIdentity";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-identity.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
