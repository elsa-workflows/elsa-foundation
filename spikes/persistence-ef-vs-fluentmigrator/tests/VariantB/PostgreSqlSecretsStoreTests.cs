using Elsa.Persistence.Spike.VariantB;
using Elsa.Persistence.Spike.VariantB.Host;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Persistence.Spike.VariantB.Tests;

public sealed class PostgreSqlSecretsStoreTests
{
    [SkippableFact]
    public async Task FluentMigrator_apply_then_ef_crud()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        try
        {
            await container.StartAsync();
        }
        catch (Exception exception)
        {
            throw new SkipException($"PostgreSQL container unavailable: {exception.Message}");
        }

        var connectionString = container.GetConnectionString();
        FluentMigratorApply.Apply(connectionString, SpikeSqlProvider.PostgreSql);

        await using var context = VariantBContextFactory.CreatePostgreSql(connectionString);
        ISecretStore store = new EfSecretStore(context);
        await store.AddAsync(new SecretRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-b",
            Name = "api-key",
            Payload = """{"kind":"text"}"""
        });

        var loaded = await store.GetByNameAsync("tenant-b", "api-key");
        Assert.NotNull(loaded);
        Assert.Equal("api-key", loaded.Name);
    }
}
