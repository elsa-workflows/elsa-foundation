using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Migrations.IdentityIam.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_applications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Ownership = table.Column<int>(type: "int", nullable: false),
                    AllowedGrantTypesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_claim_mappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RuleId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RuleLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RuleIdOrderKey = table.Column<byte[]>(type: "varbinary(802)", maxLength: 802, nullable: false),
                    MatchClaimType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GrantRolesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GrantPermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    StopOnMatch = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_claim_mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_credentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CredentialId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CredentialLookupKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubjectType = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    HashedSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HashAlgorithm = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_email_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedEmailKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_email_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_external_logins",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProviderSubject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderSubjectLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExternalOrderKey = table.Column<byte[]>(type: "varbinary(1604)", maxLength: 1604, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    LinkedAt = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    LastSeenAt = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    LinkPolicy = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_external_logins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_mutation_receipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MutationReceiptId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthoritativeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailedUnitId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_mutation_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_role_claims",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_role_claims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_role_name_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    NormalizedRoleName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedRoleNameKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_role_name_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_roles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleIdOrderKey = table.Column<byte[]>(type: "varbinary(802)", maxLength: 802, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedNameKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    System = table.Column<bool>(type: "bit", nullable: false),
                    ClaimIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserLinkIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_tenant_memberships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RoleIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DirectPermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_tenant_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_claims",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_claims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_name_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedUserNameKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_name_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_roles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_tokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantLookupKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserIdOrderKey = table.Column<byte[]>(type: "varbinary(802)", maxLength: 802, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedUserNameKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedEmailKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Ownership = table.Column<int>(type: "int", nullable: false),
                    RoleIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DirectPermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClaimIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LoginIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleLinkIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantMembershipIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_claim_mappings_provider",
                table: "identity_claim_mappings",
                columns: new[] { "TenantLookupKey", "ProviderLookupKey", "Order", "RuleIdOrderKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_email_reservations_key",
                table: "identity_email_reservations",
                columns: new[] { "TenantLookupKey", "NormalizedEmailKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_external_logins_user",
                table: "identity_external_logins",
                columns: new[] { "TenantLookupKey", "UserLookupKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_external_logins_user_page",
                table: "identity_external_logins",
                columns: new[] { "UserLookupKey", "ExternalOrderKey" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_external_logins_subject",
                table: "identity_external_logins",
                columns: new[] { "TenantLookupKey", "ProviderLookupKey", "ProviderSubjectLookupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_mutation_receipts_expiry",
                table: "identity_mutation_receipts",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ux_identity_mutation_receipts_id",
                table: "identity_mutation_receipts",
                column: "MutationReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_role_claims_role",
                table: "identity_role_claims",
                columns: new[] { "TenantLookupKey", "RoleLookupKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_role_claims_claim",
                table: "identity_role_claims",
                columns: new[] { "TenantLookupKey", "RoleLookupKey", "ClaimKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_identity_role_name_reservations_key",
                table: "identity_role_name_reservations",
                columns: new[] { "TenantLookupKey", "NormalizedRoleNameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_roles_name",
                table: "identity_roles",
                columns: new[] { "TenantLookupKey", "NormalizedNameKey" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_roles_page",
                table: "identity_roles",
                columns: new[] { "TenantLookupKey", "RoleIdOrderKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_roles_tenant",
                table: "identity_roles",
                column: "TenantLookupKey");

            migrationBuilder.CreateIndex(
                name: "ux_identity_tenant_memberships_key",
                table: "identity_tenant_memberships",
                columns: new[] { "TenantLookupKey", "UserLookupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_claims_claim",
                table: "identity_user_claims",
                columns: new[] { "TenantLookupKey", "ClaimKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_claims_user",
                table: "identity_user_claims",
                columns: new[] { "TenantLookupKey", "UserLookupKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_user_claims_claim",
                table: "identity_user_claims",
                columns: new[] { "TenantLookupKey", "UserLookupKey", "ClaimKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_identity_user_name_reservations_key",
                table: "identity_user_name_reservations",
                columns: new[] { "TenantLookupKey", "NormalizedUserNameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_roles_role",
                table: "identity_user_roles",
                columns: new[] { "TenantLookupKey", "RoleLookupKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_roles_user",
                table: "identity_user_roles",
                columns: new[] { "TenantLookupKey", "UserLookupKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_user_roles_pair",
                table: "identity_user_roles",
                columns: new[] { "TenantLookupKey", "UserLookupKey", "RoleLookupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_identity_user_tokens_key",
                table: "identity_user_tokens",
                columns: new[] { "TenantLookupKey", "UserLookupKey", "TokenKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_users_email",
                table: "identity_users",
                columns: new[] { "TenantLookupKey", "NormalizedEmailKey" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_users_name",
                table: "identity_users",
                columns: new[] { "TenantLookupKey", "NormalizedUserNameKey" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_users_page",
                table: "identity_users",
                columns: new[] { "TenantLookupKey", "UserIdOrderKey", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_applications");

            migrationBuilder.DropTable(
                name: "identity_claim_mappings");

            migrationBuilder.DropTable(
                name: "identity_credentials");

            migrationBuilder.DropTable(
                name: "identity_email_reservations");

            migrationBuilder.DropTable(
                name: "identity_external_logins");

            migrationBuilder.DropTable(
                name: "identity_mutation_receipts");

            migrationBuilder.DropTable(
                name: "identity_role_claims");

            migrationBuilder.DropTable(
                name: "identity_role_name_reservations");

            migrationBuilder.DropTable(
                name: "identity_roles");

            migrationBuilder.DropTable(
                name: "identity_tenant_memberships");

            migrationBuilder.DropTable(
                name: "identity_user_claims");

            migrationBuilder.DropTable(
                name: "identity_user_name_reservations");

            migrationBuilder.DropTable(
                name: "identity_user_roles");

            migrationBuilder.DropTable(
                name: "identity_user_tokens");

            migrationBuilder.DropTable(
                name: "identity_users");
        }
    }
}
