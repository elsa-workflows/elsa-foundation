using Elsa.Persistence.Spike.VariantA;
using Elsa.Persistence.Spike.VariantA.Tooling;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Persistence.Spike.VariantA.Tests;

public sealed class PostgreSqlSecretsStoreTests
{
    [SkippableFact]
    public async Task Migrate_then_add_and_get_by_name()
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

        await using var context = VariantAContextFactory.CreatePostgreSql(container.GetConnectionString());
        await VariantAMigrations.ApplyAsync(context, ProviderGuard.NpgsqlProviderName);

        ISecretStore store = new EfSecretStore<SecretsPostgreSqlDbContext>(context);
        await store.AddAsync(new SecretRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            Name = "smtp-password",
            Payload = """{"kind":"text"}"""
        });

        var loaded = await store.GetByNameAsync("tenant-a", "smtp-password");
        Assert.NotNull(loaded);
        Assert.Equal("smtp-password", loaded.Name);
    }
}
