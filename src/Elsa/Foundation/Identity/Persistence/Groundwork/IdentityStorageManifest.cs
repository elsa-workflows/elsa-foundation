namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>
/// Stable logical Identity storage names shared by the v2 declarations and adapters. Fresh catalogs
/// are required for the clean cutover; these identifiers preserve domain meaning, not v1 schema
/// compatibility.
/// </summary>
public static class IdentityStorageManifest
{
    public const int MaxAggregateRelationshipEntries = 512;
    public const string SchemaVersion = "1.0.6";
    public const int ProjectedLookupColumnLength = 400;
    public const int SqlServerStorageScopeKeyBytes = 256;
    public const int SqlServerDocumentIdentityLookupKeyBytes = 32;
    public const int SqlServerDateTime2KeyBytes = 10;
    public const int SqlServerUnicodeBytesPerCodeUnit = 2;
    public const int SqlServerMaxNonclusteredIndexKeyBytes = 1_700;
    public const int MaxBoundedQueryParameters = 1;

    public const string IdentityUserDocumentKind = "identityUser";
    public const string IdentityRoleDocumentKind = "identityRole";
    public const string IdentityApplicationDocumentKind = "identityApplication";
    public const string IdentityCredentialDocumentKind = "identityCredential";
    public const string IdentityClaimMappingDocumentKind = "identityClaimMapping";
    public const string IdentityProviderConfigurationDocumentKind = "identityProviderConfiguration";
    public const string IdentityGlobalProviderConfigurationDocumentKind = "identityGlobalProviderConfiguration";
    public const string UserClaimDocumentKind = "identityUserClaim";
    public const string RoleClaimDocumentKind = "identityRoleClaim";
    public const string ExternalLoginDocumentKind = "identityExternalLogin";
    public const string UserRoleDocumentKind = "identityUserRole";
    public const string UserTokenDocumentKind = "identityUserToken";
    public const string IdentityTenantMembershipDocumentKind = "identityTenantMembership";
    public const string UserNameReservationDocumentKind = "identityUserNameReservation";
    public const string EmailReservationDocumentKind = "identityEmailReservation";
    public const string RoleNameReservationDocumentKind = "identityRoleNameReservation";
    public const string IdentityMutationReceiptDocumentKind = "identityMutationReceipt";

    public const string NormalizedUserNameField = "normalizedUserName";
    public const string NormalizedEmailField = "normalizedEmail";
    public const string NormalizedRoleNameField = "normalizedRoleName";
    public const string NormalizedUserNameKeyField = "normalizedUserNameKey";
    public const string NormalizedEmailKeyField = "normalizedEmailKey";
    public const string NormalizedRoleNameKeyField = "normalizedRoleNameKey";
    public const string UserLookupKeyField = "userLookupKey";
    public const string RoleLookupKeyField = "roleLookupKey";
    public const string ProviderLookupKeyField = "providerLookupKey";
    public const string ClaimKeyField = "claimKey";
    public const string TenantIdField = "tenantId";
    public const string MutationReceiptExpiresAtField = "expiresAt";

    public const string FindUserByNormalizedNameQuery = "find-user-by-normalized-name";
    public const string FindUserByNormalizedEmailQuery = "find-user-by-normalized-email";
    public const string FindRoleByNormalizedNameQuery = "find-role-by-normalized-name";
    public const string ListRolesByTenantQuery = "list-roles-by-tenant";
    public const string ListUserClaimsQuery = "list-user-claims";
    public const string FindUsersByClaimQuery = "find-users-by-claim";
    public const string ListRoleClaimsQuery = "list-role-claims";
    public const string ListUserRolesQuery = "list-user-roles";
    public const string ListRoleUsersQuery = "list-role-users";
    public const string ListUserLoginsQuery = "list-user-logins";
    public const string ListClaimMappingsByProviderQuery = "list-claim-mappings-by-provider";
    public const string ListExpiredMutationReceiptsQuery = "list-expired-mutation-receipts";
}
