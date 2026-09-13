using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(IdentityProviderPostgreSqlFixture.CollectionName)]
public sealed class IdentityProviderPostgreSqlSmokeTests(IdentityProviderPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task Crud_reopen_rollback_and_cas_on_postgresql() => IdentityProviderProviderSmoke.RunAsync(fixture, "PostgreSql");
}

[Collection(IdentityProviderSqlServerFixture.CollectionName)]
public sealed class IdentityProviderSqlServerSmokeTests(IdentityProviderSqlServerFixture fixture)
{
    [SkippableFact]
    public Task Crud_reopen_rollback_and_cas_on_sql_server() => IdentityProviderProviderSmoke.RunAsync(fixture, "SqlServer");
}

[Collection(IdentityProviderMySqlFixture.CollectionName)]
public sealed class IdentityProviderMySqlSmokeTests(IdentityProviderMySqlFixture fixture)
{
    [SkippableFact]
    public Task Crud_reopen_rollback_and_cas_on_mysql() => IdentityProviderProviderSmoke.RunAsync(fixture, "MySql");
}

internal static class IdentityProviderProviderSmoke
{
    public static async Task RunAsync(IdentityProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{provider} is unavailable.");
        var tenant = "provider-smoke-tenant";
        await using var context = CreateContext(provider, fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        if (provider == "MySql")
            await AssertMySqlTableEncodingAsync(context);
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var store = new EfProviderConfigurationStore(context, access);
        var configuration = Configuration(tenant, provider + "-unicode-😀", "roundtrip");

        await store.SaveAsync(configuration);
        var duplicateCreate = await store.SaveWithRevisionAsync(configuration with { Kind = "duplicate" }, expectedRevision: null);
        Assert.Equal(IamRevisionSaveStatus.Conflict, duplicateCreate.Status);
        var roundTrip = await store.FindForTenantAsync(tenant, configuration.Provider);
        Assert.NotNull(roundTrip);
        Assert.Equal(configuration.Provider, roundTrip!.Provider);
        Assert.Equal(configuration.TenantId, roundTrip.TenantId);
        Assert.Equal(configuration.Kind, roundTrip.Kind);
        Assert.Equal(configuration.Settings, roundTrip.Settings);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.SaveAsync(configuration with { Kind = "rolled-back" });
            await transaction.RollbackAsync();
        }
        await using (var reopened = CreateContext(provider, fixture.ConnectionString))
        {
            var reopenedStore = new EfProviderConfigurationStore(reopened, access);
            Assert.Equal("roundtrip", (await reopenedStore.FindForTenantAsync(tenant, configuration.Provider))!.Kind);
        }

        await using var firstContext = CreateContext(provider, fixture.ConnectionString);
        await using var secondContext = CreateContext(provider, fixture.ConnectionString);
        var firstStore = new EfProviderConfigurationStore(firstContext, access);
        var secondStore = new EfProviderConfigurationStore(secondContext, access);
        var firstRevision = await firstStore.FindForTenantWithRevisionAsync(tenant, configuration.Provider);
        var secondRevision = await secondStore.FindForTenantWithRevisionAsync(tenant, configuration.Provider);
        var results = await Task.WhenAll(
            firstStore.SaveWithRevisionAsync(configuration with { Kind = "cas-a" }, firstRevision!.Revision).AsTask(),
            secondStore.SaveWithRevisionAsync(configuration with { Kind = "cas-b" }, secondRevision!.Revision).AsTask());
        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Saved);
        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Conflict);
    }

    private static IdentityProviderConfigurationDbContext CreateContext(string provider, string connectionString) => provider switch
    {
        "PostgreSql" => new IdentityProviderConfigurationPostgreSqlDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationPostgreSqlDbContext>().UseNpgsql(connectionString).Options),
        "SqlServer" => new IdentityProviderConfigurationSqlServerDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationSqlServerDbContext>().UseSqlServer(connectionString).Options),
        "MySql" => new IdentityProviderConfigurationMySqlDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationMySqlDbContext>().UseMySQL(connectionString).Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static async Task AssertMySqlTableEncodingAsync(IdentityProviderConfigurationDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        foreach (var tableName in new[]
                 {
                     IdentityProviderConfigurationEfModule.TenantTableName,
                     IdentityProviderConfigurationEfModule.GlobalTableName
                 })
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SHOW CREATE TABLE `{tableName}`";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var ddl = reader.GetString(1);
            Assert.Contains($"DEFAULT CHARSET={IdentityProviderConfigurationMySqlDbContext.CharacterSet}", ddl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(IdentityProviderConfigurationMySqlDbContext.Collation, ddl, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ProviderConfigurationRecord Configuration(string tenantId, string provider, string kind) => new(
        provider, tenantId, kind, true, false,
        new ProviderCapabilities(true, true, true, true, true, true, true, PermissionPropagationMode.TokenRefreshBoundary),
        new Dictionary<string, string> { ["unicode-😀"] = "value:with:delimiters", ["empty"] = "" });

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
