using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class IdentityIamProviderModelTests
{
    private const int SortableIdentityKeyWidth = 400 * sizeof(char) + sizeof(ushort);

    private static readonly HashSet<string> TechnicalKeyProperties =
    [
        "TenantLookupKey",
        "NormalizedUserNameKey",
        "NormalizedEmailKey",
        "NormalizedNameKey",
        "NormalizedRoleNameKey",
        "ProviderLookupKey",
        "ProviderSubjectLookupKey",
        "ExternalOrderKey",
        "RuleLookupKey",
        "RuleIdOrderKey",
        "UserLookupKey",
        "RoleLookupKey",
        "RoleIdOrderKey",
        "ClaimKey",
        "TokenKey"
    ];

    private static readonly HashSet<string> SortableKeyProperties =
    [
        "ExternalOrderKey",
        "RuleIdOrderKey",
        "RoleIdOrderKey"
    ];

    private static readonly string[] ExpectedIamTables =
    [
        "identity_applications",
        "identity_credentials",
        "identity_users",
        "identity_roles",
        "identity_claim_mappings",
        "identity_external_logins",
        "identity_user_claims",
        "identity_role_claims",
        "identity_user_roles",
        "identity_user_tokens",
        "identity_tenant_memberships",
        "identity_user_name_reservations",
        "identity_email_reservations",
        "identity_role_name_reservations",
        "identity_mutation_receipts"
    ];

    [Theory]
    [InlineData("SqlServer", "PostgreSql")]
    [InlineData("PostgreSql", "MySql")]
    [InlineData("MySql", "SqlServer")]
    public void Provider_context_rejects_a_mismatched_provider_before_database_access(
        string contextProvider,
        string actualProvider)
    {
        using var context = CreateContext(contextProvider, actualProvider);

        var exception = Assert.Throws<InvalidOperationException>(() => context.EnsureProviderBinding());

        Assert.Contains(ExpectedProvider(contextProvider), exception.Message, StringComparison.Ordinal);
        Assert.Contains(ExpectedProvider(actualProvider), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("MySql", "MySql.EntityFrameworkCore")]
    public void Provider_model_builds_offline_with_exactly_the_authoritative_IAM_entities(
        string provider,
        string expectedProvider)
    {
        using var context = CreateContext(provider, provider);

        Assert.Equal(expectedProvider, context.Database.ProviderName);

        var entityTypes = context.Model.GetEntityTypes().ToArray();
        Assert.Equal(ExpectedIamTables.OrderBy(name => name), entityTypes.Select(entity => entity.GetTableName()).OrderBy(name => name));
        Assert.All(entityTypes, entity =>
        {
            Assert.Equal(64, entity.FindProperty("Id")!.GetMaxLength());
            if (entity.FindProperty("TenantId") is { } tenantId)
                Assert.False(tenantId.IsNullable);
            Assert.True(entity.FindProperty("Revision")!.IsConcurrencyToken);
        });
        Assert.All(
            entityTypes.Where(IsAuthorityEntity).SelectMany(entity => entity.GetProperties())
                .Where(property => property.ClrType == typeof(string) &&
                                   TechnicalKeyProperties.Contains(property.Name) &&
                                   !SortableKeyProperties.Contains(property.Name)),
            property =>
            {
                Assert.Null(property.GetValueConverter());
                Assert.Equal(64, property.GetMaxLength());
                Assert.False(property.IsUnicode());
            });
        Assert.All(
            entityTypes.Where(IsAuthorityEntity).SelectMany(entity => entity.GetProperties())
                .Where(property => property.ClrType == typeof(byte[]) && SortableKeyProperties.Contains(property.Name)),
            property =>
            {
                Assert.Null(property.GetValueConverter());
                Assert.Equal(
                    property.Name == "ExternalOrderKey" ? SortableIdentityKeyWidth * 2 : SortableIdentityKeyWidth,
                    property.GetMaxLength());
            });
        Assert.All(
            entityTypes.Where(IsAuthorityEntity).SelectMany(entity => entity.GetProperties())
                .Where(property => property.ClrType == typeof(string) &&
                                   property.Name != "Id" &&
                                   !property.Name.EndsWith("Json", StringComparison.Ordinal) &&
                                   !TechnicalKeyProperties.Contains(property.Name)),
            property => Assert.NotNull(property.GetValueConverter()));
        Assert.DoesNotContain(
            entityTypes,
            entity => entity.ClrType.FullName?.Contains("OpenIddict", StringComparison.Ordinal) == true);

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(ExpectedIamTables.OrderBy(name => name), designTimeModel.GetEntityTypes().Select(entity => entity.GetTableName()).OrderBy(name => name));

        var application = Assert.IsAssignableFrom<IEntityType>(designTimeModel.GetEntityTypes().Single(entity => entity.GetTableName() == "identity_applications"));
        var credential = Assert.IsAssignableFrom<IEntityType>(designTimeModel.GetEntityTypes().Single(entity => entity.GetTableName() == "identity_credentials"));
        Assert.Equal(64, application.FindProperty("Id")!.GetMaxLength());
        Assert.False(application.FindProperty("TenantId")!.IsNullable);
        Assert.False(application.FindProperty("ApplicationId")!.IsNullable);
        Assert.True(application.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.Equal(64, credential.FindProperty("Id")!.GetMaxLength());
        Assert.False(credential.FindProperty("TenantId")!.IsNullable);
        Assert.False(credential.FindProperty("CredentialId")!.IsNullable);
        Assert.True(credential.FindProperty("Revision")!.IsConcurrencyToken);
        var expiry = credential.FindProperty("ExpiresAt")!;
        Assert.True(expiry.IsNullable);
        Assert.Equal(35, expiry.GetMaxLength());
        Assert.Equal(typeof(string), expiry.GetValueConverter()!.ProviderClrType);

        AssertUniqueIndex(designTimeModel, "identity_external_logins", "TenantLookupKey", "ProviderLookupKey", "ProviderSubjectLookupKey");
        AssertUniqueIndex(designTimeModel, "identity_user_roles", "TenantLookupKey", "UserLookupKey", "RoleLookupKey");
        AssertUniqueIndex(designTimeModel, "identity_user_tokens", "TenantLookupKey", "UserLookupKey", "TokenKey");
        AssertUniqueIndex(designTimeModel, "identity_user_name_reservations", "TenantLookupKey", "NormalizedUserNameKey");
        AssertUniqueIndex(designTimeModel, "identity_email_reservations", "TenantLookupKey", "NormalizedEmailKey");
        AssertUniqueIndex(designTimeModel, "identity_role_name_reservations", "TenantLookupKey", "NormalizedRoleNameKey");
        AssertUniqueIndex(designTimeModel, "identity_mutation_receipts", "MutationReceiptId");

        if (provider == "MySql")
            Assert.Equal(IdentityIamMySqlDbContext.CharacterSet, designTimeModel.FindAnnotation("MySQL:Charset")?.Value);

        // #1837: the collation is a per-column declaration on the lookup keys, never a model-wide one.
        // Reservation uniqueness is the reason: a case-insensitive default would let two rows that
        // differ only in case collide. OrdinalCollationMigrationTests proves it reaches the schema.
        Assert.Null(designTimeModel.GetCollation());
        Assert.All(designTimeModel.GetEntityTypes(), entity => Assert.Null(entity.FindAnnotation(RelationalAnnotationNames.Collation)?.Value));
        var expected = EfOrdinalCollation.ForProvider(EfRelationalProviderBinding.ExpectedProviderName(provider));
        var users = designTimeModel.GetEntityTypes().Single(entity => entity.GetTableName() == "identity_users");
        Assert.Equal(expected, users.FindProperty("NormalizedEmailKey")!.GetCollation());
        Assert.Null(users.FindProperty("PasswordHash")!.GetCollation());
    }

    private static bool IsAuthorityEntity(IEntityType entity) =>
        entity.GetTableName() is not ("identity_applications" or "identity_credentials");

    private static IdentityIamDbContext CreateContext(string contextProvider, string actualProvider) =>
        contextProvider switch
        {
            "SqlServer" => new IdentityIamSqlServerDbContext(
                Configure(new DbContextOptionsBuilder<IdentityIamSqlServerDbContext>(), actualProvider).Options),
            "PostgreSql" => new IdentityIamPostgreSqlDbContext(
                Configure(new DbContextOptionsBuilder<IdentityIamPostgreSqlDbContext>(), actualProvider).Options),
            "MySql" => new IdentityIamMySqlDbContext(
                Configure(new DbContextOptionsBuilder<IdentityIamMySqlDbContext>(), actualProvider).Options),
            _ => throw new ArgumentOutOfRangeException(nameof(contextProvider), contextProvider, null)
        };

    private static void AssertUniqueIndex(IModel model, string tableName, params string[] propertyNames)
    {
        var entity = model.GetEntityTypes().Single(entityType => entityType.GetTableName() == tableName);
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        string provider)
        where TContext : IdentityIamDbContext
    {
        // These options are parsed to build models but never opened. Keep them passwordless so
        // offline provider checks do not introduce credential-shaped test data.
        switch (provider)
        {
            case "SqlServer":
                builder.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True;TrustServerCertificate=True");
                break;
            case "PostgreSql":
                builder.UseNpgsql("Host=localhost;Database=unused;Username=unused");
                break;
            case "MySql":
                builder.UseMySQL("Server=localhost;Database=unused;User ID=unused");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }

        return builder;
    }

    private static string ExpectedProvider(string provider) => provider switch
    {
        "SqlServer" => EfProviderNames.SqlServer,
        "PostgreSql" => EfProviderNames.PostgreSql,
        "MySql" => EfProviderNames.MySql,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };
}
