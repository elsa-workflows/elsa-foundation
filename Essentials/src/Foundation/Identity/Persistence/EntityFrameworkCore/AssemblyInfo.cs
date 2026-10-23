using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this assembly's two modules: [EfModule] is
// AllowMultiple for exactly the two assemblies that carry two contexts, this one and Runtime.Distributed's.
// EfModuleCatalog.Discover reads these, and EfModuleBinding.For derives each registration class's
// binding from them (#1872). Each one's name is mirrored below into elsa-package.json's
// extensions.efModules (#1881); EfModuleDescriptorTests guards that the two never
// drift apart.
[assembly: EfModule(
    "Identity.Iam",
    typeof(IdentityIamDbContext),
    HistoryModule = IdentityIamEfModule.HistoryModuleName,
    Sqlite = typeof(IdentityIamSqliteDbContext),
    SqlServer = typeof(IdentityIamSqlServerDbContext),
    PostgreSql = typeof(IdentityIamPostgreSqlDbContext),
    MySql = typeof(IdentityIamMySqlDbContext),
    DisplayName = "Identity IAM")]

[assembly: EfModule(
    "Identity.ProviderConfiguration",
    typeof(IdentityProviderConfigurationDbContext),
    HistoryModule = IdentityProviderConfigurationEfModule.HistoryModuleName,
    Sqlite = typeof(IdentityProviderConfigurationSqliteDbContext),
    SqlServer = typeof(IdentityProviderConfigurationSqlServerDbContext),
    PostgreSql = typeof(IdentityProviderConfigurationPostgreSqlDbContext),
    MySql = typeof(IdentityProviderConfigurationMySqlDbContext),
    DisplayName = "Identity provider-configuration")]

[assembly: ManifestExtension("efModules", "Identity.Iam")]
[assembly: ManifestExtension("efModules", "Identity.ProviderConfiguration")]

// The schema families these modules own, each at the version its skew check reads. A host's
// readability report is derived from these alone; every family the stores check must be declared
// here, at the version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(IdentityIamEfModule.SchemaFamily, "Identity.Iam", IdentityIamEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(IdentityProviderConfigurationEfModule.SchemaFamily, "Identity.ProviderConfiguration", IdentityProviderConfigurationEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
// The child rows - claims, tokens, role links, external logins, reservations - carry no document column, so they
// declare none (#2140).
[assembly: EfSchemaContent(IdentityIamEfModule.SchemaFamily, typeof(UserEntity),
    nameof(UserEntity.RoleIdsJson), nameof(UserEntity.DirectPermissionsJson), nameof(UserEntity.ClaimIdsJson), nameof(UserEntity.LoginIdsJson),
    nameof(UserEntity.RoleLinkIdsJson), nameof(UserEntity.TokenIdsJson), nameof(UserEntity.TenantMembershipIdsJson))]
[assembly: EfSchemaContent(IdentityIamEfModule.SchemaFamily, typeof(RoleEntity),
    nameof(RoleEntity.PermissionsJson), nameof(RoleEntity.ClaimIdsJson), nameof(RoleEntity.UserLinkIdsJson))]
[assembly: EfSchemaContent(IdentityIamEfModule.SchemaFamily, typeof(TenantMembershipEntity),
    nameof(TenantMembershipEntity.RoleIdsJson), nameof(TenantMembershipEntity.DirectPermissionsJson))]
[assembly: EfSchemaContent(IdentityIamEfModule.SchemaFamily, typeof(ClaimMappingEntity),
    nameof(ClaimMappingEntity.GrantRolesJson), nameof(ClaimMappingEntity.GrantPermissionsJson))]
[assembly: EfSchemaContent(IdentityIamEfModule.SchemaFamily, typeof(ApplicationEntity),
    nameof(ApplicationEntity.ScopesJson), nameof(ApplicationEntity.AllowedGrantTypesJson))]
[assembly: EfSchemaContent(IdentityProviderConfigurationEfModule.SchemaFamily, typeof(TenantProviderConfigurationEntity), nameof(TenantProviderConfigurationEntity.SettingsJson))]
[assembly: EfSchemaContent(IdentityProviderConfigurationEfModule.SchemaFamily, typeof(GlobalProviderConfigurationEntity), nameof(GlobalProviderConfigurationEntity.SettingsJson))]
