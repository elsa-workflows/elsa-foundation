using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlEfSecretRepositoryTests(PostgresContainerFixture fixture)
{
    [Fact]
    public void UseNpgsql_sets_the_provider_history_table_when_the_engine_is_loaded()
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseNpgsql(
            builder,
            "Host=localhost;Database=x;Username=postgres;Password=postgres",
            SecretsEfModule.HistoryTableName,
            typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault()
                         ?? throw new InvalidOperationException("Npgsql binding did not install a relational extension.");
        Assert.Equal(SecretsEfModule.HistoryTableName, relational.MigrationsHistoryTableName);
        Assert.Contains("Npgsql", relational.GetType().Name, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Migrate_crud_revision_and_search_work_on_postgres()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260910210216_Initial");

        var repository = new EfSecretRepository(context);
        await repository.SaveAsync(Secret("tenant-a", "before.upgrade", "preserved", scope: "Finance"));
        await EfDatabaseMigrator.ApplyAsync(context, SecretsPostgreSqlDbContext.ExpectedProviderName);
        Assert.Equal(
            "before.upgrade",
            Assert.Single((await repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(scope: "FINANCE"))).Items).Name);

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
        var now = new DateTimeOffset(2026, 8, 16, 10, 30, 0, TimeSpan.FromHours(-1));
        var active = await repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(activeOnly: true, now: now, take: 10));
        Assert.Contains(active.Items, secret => secret.Name == "offset.future");

        var longScope = new string('s', 100) + "München";
        await repository.SaveAsync(Secret("tenant-a", "long.lookup", "long", scope: longScope));
        Assert.Contains(
            (await repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(scope: longScope.ToUpperInvariant()))).Items,
            secret => secret.Name == "long.lookup");
    }

    [SkippableFact]
    public async Task Provider_guard_refuses_a_postgres_context_when_sqlite_is_expected()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName));
        Assert.Contains(SecretsPostgreSqlDbContext.ExpectedProviderName, exception.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Real_operator_apply_then_fresh_runtime_validate_uses_the_postgres_artifact()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        Skip.IfNot(DualMigrateProcessRunner.HasDotnetEf(), "dotnet-ef is not available.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();

        await using (var olderContext = CreateContext(connectionString))
        {
            await olderContext.GetService<IMigrator>().MigrateAsync("20260910210216_Initial");
            var pending = (await olderContext.Database.GetPendingMigrationsAsync()).ToArray();
            Assert.Contains("20260911011058_WidenLookupKeys", pending);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                EfDatabaseMigrator.ApplyAsync(
                    olderContext,
                    SecretsPostgreSqlDbContext.ExpectedProviderName,
                    EfMigratePolicy.Validate));
            Assert.Contains("20260911011058_WidenLookupKeys", exception.Message, StringComparison.Ordinal);
        }

        var result = DualMigrateProcessRunner.RunFromExistingBuild(
            ["apply", "--postgresql"],
            new Dictionary<string, string?>
            {
                ["ELSA_SECRETS_EF_POSTGRESQL"] = connectionString,
                ["ELSA_SECRETS_EF_SQLITE"] = null,
                ["ELSA_SECRETS_EF_SQLSERVER"] = null,
                ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
            });
        Assert.True(result.ExitCode == 0, result.Describe());
        Assert.Contains("database update --context SecretsPostgreSqlDbContext", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, result.Error, StringComparison.Ordinal);

        await using var context = CreateContext(connectionString);
        Assert.Equal(EfProviderNames.PostgreSql, context.Database.ProviderName);
        Assert.Same(typeof(SecretsPostgreSqlDbContext).Assembly, context.GetService<IMigrationsAssembly>().Assembly);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains("20260910210216_Initial", applied);
        Assert.Contains("20260911011058_WidenLookupKeys", applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.True(await TableExistsAsync(context, SecretsEfModule.TableName));
        Assert.True(await TableExistsAsync(context, SecretsEfModule.HistoryTableName));
        await EfDatabaseMigrator.ApplyAsync(
            context,
            SecretsPostgreSqlDbContext.ExpectedProviderName,
            EfMigratePolicy.Validate);
    }

    private static SecretsPostgreSqlDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;
        return new SecretsPostgreSqlDbContext(options);
    }

    private static async Task<bool> TableExistsAsync(SecretsPostgreSqlDbContext context, string table)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static Secret Secret(
        string tenantId,
        string name,
        string value,
        string? displayName = null,
        DateTimeOffset? expiresAt = null,
        string? scope = null) => new()
    {
        TenantId = tenantId,
        Name = name,
        DisplayName = displayName ?? name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Scope = scope,
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
