using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(ConnectionsPostgreSqlContainerFixture.CollectionName)]
public sealed class ConnectionsPostgreSqlTests(ConnectionsPostgreSqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Migrations_restart_cas_and_reference_isolation_on_postgresql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason);
        await ConnectionsProviderContract.RunAsync("PostgreSql", fixture.ConnectionString);
    }
}

[Collection(ConnectionsSqlServerContainerFixture.CollectionName)]
public sealed class ConnectionsSqlServerTests(ConnectionsSqlServerContainerFixture fixture)
{
    [SkippableFact]
    public async Task Migrations_restart_cas_and_reference_isolation_on_sql_server()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason);
        await ConnectionsProviderContract.RunAsync("SqlServer", fixture.ConnectionString);
    }
}

[Collection(ConnectionsMySqlContainerFixture.CollectionName)]
public sealed class ConnectionsMySqlTests(ConnectionsMySqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Migrations_restart_cas_and_reference_isolation_on_mysql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason);
        await ConnectionsProviderContract.RunAsync("MySql", fixture.ConnectionString);
    }
}

internal static class ConnectionsProviderContract
{
    public static async Task RunAsync(string providerName, string connectionString)
    {
        var tenant = "Tenant-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConnectionsEntityFrameworkCore(new() { Provider = providerName, ConnectionString = connectionString });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ConnectionsDbContext>();
            var binding = EfModuleBinding.For(typeof(ConnectionsDbContext));
            var relational = context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();
            Assert.Equal(binding.HistoryTableName, relational.MigrationsHistoryTableName);
            Assert.Same(typeof(ConnectionsDbContext).Assembly, relational.MigrationsAssemblyObject);
            Assert.Null(relational.MigrationsAssembly);
            await context.Database.MigrateAsync();
            Assert.Single(await context.Database.GetAppliedMigrationsAsync());
            var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
            Assert.True((await repository.GetCapabilitiesAsync()).IsDurable);
            Assert.True(await repository.TryAddAsync(Document(tenant, "Mail", "shared")));
            Assert.False(await repository.TryAddAsync(Document(tenant, "MAIL", "must-not-appear")));
            Assert.True(await repository.TryAddAsync(Document(tenant.ToLowerInvariant(), "mail", "foreign")));
        }
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IConnectionRepository>();
        var second = secondScope.ServiceProvider.GetRequiredService<IConnectionRepository>();
        var winner = (await first.FindAsync(tenant, "mail"))!;
        var stale = (await second.FindAsync(tenant, "MAIL"))!;
        Assert.Equal(0, winner.State.Version);
        Assert.Equal("https://example.test", winner.Definition.Settings["endpoint"]);
        Assert.True(await first.IsSecretReferencedAsync(tenant, "SHARED"));
        Assert.False(await first.IsSecretReferencedAsync(tenant, "foreign"));
        winner.State.Status = ConnectionStatuses.Revoked;
        winner.Definition.SecretFields["token"] = new() { SecretName = "winner" };
        await first.SaveAsync(winner);
        stale.Definition.SecretFields["token"] = new() { SecretName = "loser" };
        await Assert.ThrowsAsync<ConnectionConflictException>(async () => await second.SaveAsync(stale));
        Assert.True(await second.IsSecretReferencedAsync(tenant, "winner"));
        Assert.False(await second.IsSecretReferencedAsync(tenant, "shared"));
        Assert.False(await second.IsSecretReferencedAsync(tenant, "loser"));
        var restored = (await second.FindAsync(tenant, "mail"))!;
        Assert.Equal(1, restored.State.Version);
        Assert.Equal(ConnectionStatuses.Revoked, restored.State.Status);
        Assert.Single(await second.ListAsync(tenant, new() { TypeName = "HTTP", AuthScheme = "BEARER", Search = "MAIL" }));
        Assert.False(await first.DeleteAsync(tenant, "mail", 0));
        Assert.True(await first.DeleteAsync(tenant, "mail", 1));
        Assert.False(await second.IsSecretReferencedAsync(tenant, "winner"));
        Assert.True(await second.IsSecretReferencedAsync(tenant.ToLowerInvariant(), "foreign"));
        await Assert.ThrowsAsync<ConnectionConflictException>(async () => await second.SaveAsync(restored));
        Assert.True(await second.DeleteAsync(tenant.ToLowerInvariant(), "mail"));
    }

    private static ConnectionDocument Document(string tenant, string name, string secret) => new()
    {
        Definition = new()
        {
            TenantId = tenant, Name = name, DisplayName = name, TypeName = "http", AuthScheme = "bearer",
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Endpoint"] = "https://example.test" },
            SecretFields = new Dictionary<string, ConnectionSecretField>(StringComparer.Ordinal) { ["token"] = new() { SecretName = secret } }
        },
        State = new() { CreatedAt = DateTimeOffset.UnixEpoch }
    };
}
