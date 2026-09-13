using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class IdentityIamProviderModelTests
{
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
    public void Provider_model_builds_offline_with_exactly_application_and_credential_entities(
        string provider,
        string expectedProvider)
    {
        using var context = CreateContext(provider, provider);

        Assert.Equal(expectedProvider, context.Database.ProviderName);

        var entityTypes = context.Model.GetEntityTypes().ToArray();
        Assert.Equal(
            new[] { typeof(ApplicationEntity), typeof(CredentialEntity) },
            entityTypes.Select(entity => entity.ClrType).OrderBy(type => type.FullName));
        Assert.DoesNotContain(
            entityTypes,
            entity => entity.ClrType.FullName?.Contains("OpenIddict", StringComparison.Ordinal) == true);

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(
            new[] { typeof(ApplicationEntity), typeof(CredentialEntity) },
            designTimeModel.GetEntityTypes().Select(entity => entity.ClrType).OrderBy(type => type.FullName));

        var application = Assert.IsAssignableFrom<IEntityType>(designTimeModel.FindEntityType(typeof(ApplicationEntity)));
        var credential = Assert.IsAssignableFrom<IEntityType>(designTimeModel.FindEntityType(typeof(CredentialEntity)));
        Assert.Equal(64, application.FindProperty(nameof(ApplicationEntity.Id))!.GetMaxLength());
        Assert.False(application.FindProperty(nameof(ApplicationEntity.TenantId))!.IsNullable);
        Assert.False(application.FindProperty(nameof(ApplicationEntity.ApplicationId))!.IsNullable);
        Assert.True(application.FindProperty(nameof(ApplicationEntity.Revision))!.IsConcurrencyToken);
        Assert.Equal(64, credential.FindProperty(nameof(CredentialEntity.Id))!.GetMaxLength());
        Assert.False(credential.FindProperty(nameof(CredentialEntity.TenantId))!.IsNullable);
        Assert.False(credential.FindProperty(nameof(CredentialEntity.CredentialId))!.IsNullable);
        Assert.True(credential.FindProperty(nameof(CredentialEntity.Revision))!.IsConcurrencyToken);
        var expiry = credential.FindProperty(nameof(CredentialEntity.ExpiresAt))!;
        Assert.True(expiry.IsNullable);
        Assert.Equal(35, expiry.GetMaxLength());
        Assert.Equal(typeof(string), expiry.GetValueConverter()!.ProviderClrType);

        if (provider == "MySql")
        {
            Assert.Equal(IdentityIamMySqlDbContext.CharacterSet, designTimeModel.FindAnnotation("MySQL:Charset")?.Value);
            Assert.Equal(IdentityIamMySqlDbContext.Collation, designTimeModel.GetCollation());
            Assert.Equal(
                IdentityIamMySqlDbContext.Collation,
                designTimeModel.FindEntityType(typeof(ApplicationEntity))!.FindAnnotation("MySQL:Collation")?.Value);
            Assert.Equal(
                IdentityIamMySqlDbContext.Collation,
                designTimeModel.FindEntityType(typeof(CredentialEntity))!.FindAnnotation("MySQL:Collation")?.Value);
        }
    }

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

    private static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        string provider)
        where TContext : IdentityIamDbContext
    {
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
