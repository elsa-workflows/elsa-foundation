using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class IdentityProviderConfigurationProviderModelTests
{
    [Fact]
    public void Derived_context_rejects_a_mismatched_provider_before_database_access()
    {
        using var context = new IdentityProviderConfigurationSqliteDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationSqliteDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;User ID=unused;Password=Unused123!;TrustServerCertificate=True").Options);

        var exception = Assert.Throws<InvalidOperationException>(() => context.EnsureProviderBinding());
        Assert.Contains(IdentityProviderConfigurationSqliteDbContext.ExpectedProviderName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("MySql", "MySql.EntityFrameworkCore")]
    public void Provider_models_build_without_connecting_and_contain_only_two_identity_entities(string provider, string expectedProvider)
    {
        using var context = provider switch
        {
            "SqlServer" => (DbContext)new IdentityProviderConfigurationSqlServerDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationSqlServerDbContext>()
                .UseSqlServer("Server=localhost;Database=unused;User ID=unused;Password=Unused123!;TrustServerCertificate=True").Options),
            "PostgreSql" => new IdentityProviderConfigurationPostgreSqlDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationPostgreSqlDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options),
            "MySql" => new IdentityProviderConfigurationMySqlDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationMySqlDbContext>()
                .UseMySQL("Server=localhost;Database=unused;User ID=unused;Password=unused").Options),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

        Assert.Equal(expectedProvider, context.Database.ProviderName);
        var entityTypes = context.Model.GetEntityTypes().ToArray();
        Assert.Equal(2, entityTypes.Length);
        Assert.Contains(entityTypes, entity => entity.ClrType == typeof(TenantProviderConfigurationEntity));
        Assert.Contains(entityTypes, entity => entity.ClrType == typeof(GlobalProviderConfigurationEntity));
        Assert.DoesNotContain(entityTypes, entity => entity.ClrType == typeof(ProviderConfigurationEntity));
        Assert.DoesNotContain(entityTypes, entity => entity.ClrType.FullName?.Contains("OpenIddict", StringComparison.Ordinal) == true);

        var lookup = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GlobalProviderConfigurationEntity))!.FindProperty(nameof(GlobalProviderConfigurationEntity.ProviderLookupKey));
        Assert.NotNull(lookup);
        if (provider == "MySql")
            Assert.Equal("utf8mb4_0900_bin", lookup!.GetCollation());
    }
}
