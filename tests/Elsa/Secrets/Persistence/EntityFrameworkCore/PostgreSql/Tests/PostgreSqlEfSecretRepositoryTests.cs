using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlEfSecretRepositoryTests(PostgresContainerFixture fixture)
{
    [SkippableFact]
    public async Task Migrate_crud_revision_and_search_work_on_postgres()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        var options = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;
        await using var context = new SecretsPostgreSqlDbContext(options);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsPostgreSqlDbContext.ExpectedProviderName);

        var repository = new EfSecretRepository(context);
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        Assert.True(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "alpha", "Payments API")));
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "duplicate")));

        var current = await revisions.FindWithRevisionAsync("tenant-a", "payments.api");
        current!.Secret.DisplayName = "updated";
        var saved = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, saved.Status);
        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);

        var page = await repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(search: "PAYMENTS", take: 10));
        Assert.Equal("payments.api", Assert.Single(page.Items).Name);
        Assert.Equal("updated", page.Items[0].DisplayName);

        var plusTwo = new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.True(await repository.TryAddAsync(Secret("tenant-a", "offset.future", "v", expiresAt: plusTwo)));
        var now = new DateTimeOffset(2026, 8, 16, 11, 30, 0, TimeSpan.FromHours(-1));
        var active = await repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(activeOnly: true, now: now, take: 10));
        Assert.Contains(active.Items, secret => secret.Name == "offset.future");
    }

    [SkippableFact]
    public async Task Provider_guard_refuses_a_postgres_context_when_sqlite_is_expected()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        var options = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;
        await using var context = new SecretsPostgreSqlDbContext(options);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName));
        Assert.Contains(SecretsPostgreSqlDbContext.ExpectedProviderName, exception.Message, StringComparison.Ordinal);
    }

    private static Secret Secret(
        string tenantId,
        string name,
        string value,
        string? displayName = null,
        DateTimeOffset? expiresAt = null) => new()
    {
        TenantId = tenantId,
        Name = name,
        DisplayName = displayName ?? name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                ExpiresAt = expiresAt,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };
}
