using Groundwork.Kernel;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>Fresh-catalog Groundwork v2 declarations owned by Foundation Identity.</summary>
public static class IdentityV2StorageManifest
{
    public const int IdentityKeyMaximumLength = 96;
    public const int TenantIdMaximumLength = 256;
    public const int LookupKeyMaximumLength = 96;
    public const int SchemaVersionMaximumLength = 32;
    public const string IdField = "id";
    public const string SchemaVersionField = "schemaVersion";
    public const string ContentField = "content";

    public static IReadOnlyList<StorageUnit> CreateUnits() =>
    [
        Scoped(
            IdentityStorageManifest.IdentityUserDocumentKind,
            "identity_users",
            [
                Lookup(IdentityStorageManifest.NormalizedUserNameKeyField, nullable: true),
                Lookup(IdentityStorageManifest.NormalizedEmailKeyField, nullable: true)
            ],
            [
                Index("identity_user_by_normalized_name", IdentityStorageManifest.NormalizedUserNameKeyField),
                Index("identity_user_by_normalized_email", IdentityStorageManifest.NormalizedEmailKeyField)
            ]),
        Scoped(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            "identity_roles",
            [
                Lookup(IdentityStorageManifest.NormalizedRoleNameKeyField, nullable: true),
                Tenant()
            ],
            [
                Index("identity_role_by_normalized_name", IdentityStorageManifest.NormalizedRoleNameKeyField),
                Index("identity_role_by_tenant", IdentityStorageManifest.TenantIdField)
            ]),
        Scoped(IdentityStorageManifest.IdentityApplicationDocumentKind, "identity_applications"),
        Scoped(IdentityStorageManifest.IdentityCredentialDocumentKind, "identity_credentials"),
        Scoped(
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            "identity_claim_mappings",
            [Lookup(IdentityStorageManifest.ProviderLookupKeyField)],
            [Index("identity_claim_mapping_by_provider", IdentityStorageManifest.ProviderLookupKeyField)]),
        Scoped(IdentityStorageManifest.IdentityProviderConfigurationDocumentKind, "identity_provider_configurations"),
        Global(IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind, "identity_global_provider_configurations"),
        Scoped(
            IdentityStorageManifest.UserClaimDocumentKind,
            "identity_user_claims",
            [Lookup(IdentityStorageManifest.UserLookupKeyField), Lookup(IdentityStorageManifest.ClaimKeyField)],
            [
                Index("identity_user_claim_by_user", IdentityStorageManifest.UserLookupKeyField),
                Index("identity_user_claim_by_claim", IdentityStorageManifest.ClaimKeyField)
            ]),
        Scoped(
            IdentityStorageManifest.RoleClaimDocumentKind,
            "identity_role_claims",
            [Lookup(IdentityStorageManifest.RoleLookupKeyField)],
            [Index("identity_role_claim_by_role", IdentityStorageManifest.RoleLookupKeyField)]),
        Scoped(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            "identity_external_logins",
            [Lookup(IdentityStorageManifest.UserLookupKeyField)],
            [Index("identity_login_by_user", IdentityStorageManifest.UserLookupKeyField)]),
        Scoped(
            IdentityStorageManifest.UserRoleDocumentKind,
            "identity_user_roles",
            [Lookup(IdentityStorageManifest.UserLookupKeyField), Lookup(IdentityStorageManifest.RoleLookupKeyField)],
            [
                Index("identity_user_role_by_user", IdentityStorageManifest.UserLookupKeyField),
                Index("identity_user_role_by_role", IdentityStorageManifest.RoleLookupKeyField)
            ]),
        Scoped(IdentityStorageManifest.UserTokenDocumentKind, "identity_user_tokens"),
        Scoped(IdentityStorageManifest.IdentityTenantMembershipDocumentKind, "identity_tenant_memberships"),
        Scoped(IdentityStorageManifest.UserNameReservationDocumentKind, "identity_user_name_reservations"),
        Scoped(IdentityStorageManifest.EmailReservationDocumentKind, "identity_email_reservations"),
        Scoped(IdentityStorageManifest.RoleNameReservationDocumentKind, "identity_role_name_reservations"),
        Scoped(
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
            "identity_mutation_receipts",
            [Timestamp(IdentityStorageManifest.MutationReceiptExpiresAtField)],
            [Index("identity_mutation_receipt_by_expiry", IdentityStorageManifest.MutationReceiptExpiresAtField)])
    ];

    public static StorageUnit Require(string unitId) =>
        CreateUnits().Single(unit => unit.Id.Value == unitId);

    private static StorageUnit Scoped(
        string id,
        string name,
        IReadOnlyList<ColumnSpec>? columns = null,
        IReadOnlyList<IndexSpec>? indexes = null) =>
        Build(id, name, ScopePolicy.Scoped, columns, indexes);

    private static StorageUnit Global(
        string id,
        string name,
        IReadOnlyList<ColumnSpec>? columns = null,
        IReadOnlyList<IndexSpec>? indexes = null) =>
        Build(id, name, ScopePolicy.Global, columns, indexes);

    private static StorageUnit Build(
        string id,
        string name,
        ScopePolicy scope,
        IReadOnlyList<ColumnSpec>? columns,
        IReadOnlyList<IndexSpec>? indexes)
    {
        var declaration = StorageUnit.Declare(id, name)
            .String(IdField, IdentityKeyMaximumLength, column => column.Required())
            .String(SchemaVersionField, SchemaVersionMaximumLength, column => column.Required())
            .Json(ContentField, column => column.Required());

        foreach (var column in columns ?? [])
        {
            if (column.Type == PortableType.String)
            {
                declaration.String(
                    column.Name,
                    column.MaximumLength!.Value,
                    definition =>
                    {
                        if (column.Nullable)
                            definition.Nullable();
                        else
                            definition.Required();
                    });
            }
            else if (column.Type == PortableType.DateTimeOffset)
            {
                declaration.Timestamp(
                    column.Name,
                    definition =>
                    {
                        if (column.Nullable)
                            definition.Nullable();
                        else
                            definition.Required();
                    });
            }
            else
            {
                throw new InvalidOperationException(
                    $"Identity v2 column '{column.Name}' uses unsupported portable type '{column.Type}'.");
            }
        }

        declaration.Key(IdField).OptimisticConcurrency();
        foreach (var index in indexes ?? [])
            declaration.Index(index.Name, index.Column);
        if (scope == ScopePolicy.Scoped)
            declaration.Scoped();
        return declaration.Build();
    }

    private static ColumnSpec Lookup(string name, bool nullable = false) =>
        new(name, PortableType.String, LookupKeyMaximumLength, nullable);

    private static ColumnSpec Tenant() =>
        new(IdentityStorageManifest.TenantIdField, PortableType.String, TenantIdMaximumLength, false);

    private static ColumnSpec Timestamp(string name) =>
        new(name, PortableType.DateTimeOffset, null, false);

    private static IndexSpec Index(string name, string column) => new(name, column);

    private sealed record ColumnSpec(string Name, PortableType Type, int? MaximumLength, bool Nullable);
    private sealed record IndexSpec(string Name, string Column);
}
