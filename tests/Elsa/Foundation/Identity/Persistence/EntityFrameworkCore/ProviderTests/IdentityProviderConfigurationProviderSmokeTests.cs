using System.Data.Common;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        var tenant = $"provider-smoke-{provider}-{Guid.NewGuid():N}";
        await using var context = CreateContext(provider, fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        if (provider == "MySql")
            await AssertMySqlTableEncodingAsync(context);
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var store = new EfProviderConfigurationStore(context, access);
        var configuration = Configuration(
            tenant,
            provider + "-unicode-😀",
            string.Concat("roundtrip-", new string('k', 800), "\ud800"));

        await store.SaveAsync(configuration);
        var roundTrip = await store.FindForTenantAsync(tenant, configuration.Provider);
        Assert.NotNull(roundTrip);
        Assert.Equal(configuration.Provider, roundTrip!.Provider);
        Assert.Equal(configuration.TenantId, roundTrip.TenantId);
        Assert.Equal(configuration.Kind, roundTrip.Kind);
        Assert.Equal(configuration.Settings, roundTrip.Settings);

        var createBarrier = new ConcurrentCreateBarrier();
        await using var firstCreateContext = CreateContext(provider, fixture.ConnectionString, createBarrier);
        await using var secondCreateContext = CreateContext(provider, fixture.ConnectionString, createBarrier);
        var createConfiguration = Configuration(tenant, provider + "-create-race", "create-race");
        var createResults = await Task.WhenAll(
            new EfProviderConfigurationStore(firstCreateContext, access)
                .SaveWithRevisionAsync(createConfiguration, expectedRevision: null).AsTask(),
            new EfProviderConfigurationStore(secondCreateContext, access)
                .SaveWithRevisionAsync(createConfiguration, expectedRevision: null).AsTask());
        Assert.Equal(2, createBarrier.Arrivals);
        Assert.Single(createResults, result => result.Status == IamRevisionSaveStatus.Saved);
        Assert.Single(createResults, result => result.Status == IamRevisionSaveStatus.Conflict);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.SaveAsync(configuration with { Kind = "rolled-back" });
            await transaction.RollbackAsync();
        }
        await using (var reopened = CreateContext(provider, fixture.ConnectionString))
        {
            var reopenedStore = new EfProviderConfigurationStore(reopened, access);
            Assert.Equal(configuration.Kind, (await reopenedStore.FindForTenantAsync(tenant, configuration.Provider))!.Kind);
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

    private static IdentityProviderConfigurationDbContext CreateContext(
        string provider,
        string connectionString,
        DbCommandInterceptor? interceptor = null) => provider switch
    {
        "PostgreSql" => new IdentityProviderConfigurationPostgreSqlDbContext(
            Configure(new DbContextOptionsBuilder<IdentityProviderConfigurationPostgreSqlDbContext>().UseNpgsql(connectionString), interceptor).Options),
        "SqlServer" => new IdentityProviderConfigurationSqlServerDbContext(
            Configure(new DbContextOptionsBuilder<IdentityProviderConfigurationSqlServerDbContext>().UseSqlServer(connectionString), interceptor).Options),
        "MySql" => new IdentityProviderConfigurationMySqlDbContext(
            Configure(new DbContextOptionsBuilder<IdentityProviderConfigurationMySqlDbContext>().UseMySQL(connectionString), interceptor).Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        DbCommandInterceptor? interceptor)
        where TContext : DbContext
    {
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return builder;
    }

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

    private sealed class ConcurrentCreateBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource bothReadersCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public int Arrivals => Volatile.Read(ref arrivals);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return result;
            if (Interlocked.Increment(ref arrivals) >= 2)
                bothReadersCompleted.TrySetResult();
            await bothReadersCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }
}
