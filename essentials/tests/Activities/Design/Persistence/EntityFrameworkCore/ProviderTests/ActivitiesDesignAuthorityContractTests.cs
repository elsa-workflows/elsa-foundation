using Elsa.Activities.Design.Persistence.EntityFrameworkCore.AuthorityContract;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Runs the shared <c>ContentAuthorityIsValid</c> case table against each native provider. SQLite runs the same
/// table in the module's fast suite, so between them all four providers answer one set of inputs, and a dialect
/// that drifts from the others fails by name instead of passing review.
/// </summary>
public static class ActivitiesDesignAuthorityContract
{
    public static async Task RunAsync(
        ActivitiesDesignProviderFixture fixture,
        Func<string, ActivitiesDesignDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        await using var context = createContext(fixture.ConnectionString);
        Assert.Equal(expectedProviderName, context.Database.ProviderName);
        await context.Database.EnsureCreatedAsync();
        await ActivityAuthorityValidityContract.RunAsync(context, expectedProviderName, Guid.NewGuid().ToString("N"));
    }
}

[Collection(ActivitiesDesignPostgreSqlFixture.CollectionName)]
public sealed class ActivitiesDesignPostgreSqlAuthorityContractTests(ActivitiesDesignPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_agrees_with_the_cross_provider_authority_contract() =>
        ActivitiesDesignAuthorityContract.RunAsync(
            fixture,
            connection => new ActivitiesDesignPostgreSqlDbContext(
                new DbContextOptionsBuilder<ActivitiesDesignPostgreSqlDbContext>().UseNpgsql(connection).Options),
            ActivitiesDesignPostgreSqlDbContext.ExpectedProviderName);
}

[Collection(ActivitiesDesignSqlServerFixture.CollectionName)]
public sealed class ActivitiesDesignSqlServerAuthorityContractTests(ActivitiesDesignSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_agrees_with_the_cross_provider_authority_contract() =>
        ActivitiesDesignAuthorityContract.RunAsync(
            fixture,
            connection => new ActivitiesDesignSqlServerDbContext(
                new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>().UseSqlServer(connection).Options),
            ActivitiesDesignSqlServerDbContext.ExpectedProviderName);
}

[Collection(ActivitiesDesignMySqlFixture.CollectionName)]
public sealed class ActivitiesDesignMySqlAuthorityContractTests(ActivitiesDesignMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_agrees_with_the_cross_provider_authority_contract() =>
        ActivitiesDesignAuthorityContract.RunAsync(
            fixture,
            connection => new ActivitiesDesignMySqlDbContext(
                new DbContextOptionsBuilder<ActivitiesDesignMySqlDbContext>().UseMySQL(connection).Options),
            ActivitiesDesignMySqlDbContext.ExpectedProviderName);
}
