using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsProjectionContractTests
{
    [Fact]
    public async Task Legacy_row_fails_closed_then_reindex_preserves_revision_and_restores_lookup()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        try
        {
            var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
                .UseSqlite(connection, sqlite => sqlite
                    .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
                .Options;
            await using var context = new SecretsSqliteDbContext(options);
            await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);

            const string legacyRuntimeKey = "\u019B";
            var current = SecretDocument.FromSecret(CreateSecret(legacyRuntimeKey));
            Assert.Equal("\uA7DC", current.TypeNameLookupKey);
            var legacy = current with { TypeNameLookupKey = legacyRuntimeKey };
            var record = legacy.ToRecord();
            context.Secrets.Add(record);
            await context.SaveChangesAsync();
            var originalToken = record.ConcurrencyToken.ToArray();
            context.ChangeTracker.Clear();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SecretsProjectionContract.EnsureCurrentAsync(context));
            Assert.Contains("dual-migrate.sh apply", exception.Message, StringComparison.Ordinal);
            Assert.Contains(SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId, exception.Message, StringComparison.Ordinal);

            Assert.Equal(1, await SecretsProjectionContract.ReindexAsync(context));
            context.ChangeTracker.Clear();
            await SecretsProjectionContract.EnsureCurrentAsync(context);

            var repaired = await context.Secrets.AsNoTracking().SingleAsync();
            Assert.Equal("\uA7DC", repaired.TypeNameLookupKey);
            Assert.Equal("\uA7DC", SecretDocument.Parse(repaired.Payload).TypeNameLookupKey);
            Assert.Equal(originalToken, repaired.ConcurrencyToken);
            Assert.Equal(0, await SecretsProjectionContract.ReindexAsync(context));

            var page = await new EfSecretRepository(context).ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(typeName: legacyRuntimeKey));
            Assert.Equal("legacy.projection", Assert.Single(page.Items).Name);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [SkippableFact]
    public async Task Operator_apply_reindexes_legacy_rows_before_validate_startup()
    {
        Skip.IfNot(DualMigrateProcessRunner.HasDotnetEf(), "dotnet-ef is not available.");
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-tool-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False";
        byte[] originalToken;
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var context = new SecretsSqliteDbContext(CreateOptions(connection));
                await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
                var current = SecretDocument.FromSecret(CreateSecret("\u019B"));
                var record = (current with { TypeNameLookupKey = "\u019B" }).ToRecord();
                context.Secrets.Add(record);
                await context.SaveChangesAsync();
                originalToken = record.ConcurrencyToken.ToArray();
            }

            await using var provider = new ServiceCollection()
                .AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connectionString,
                    MigratePolicy = EfMigratePolicy.Validate
                })
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var lifecycle = provider.GetRequiredService<SecretsEfMigrationHostedService>();
            var startupFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => lifecycle.InitializeAsync());
            Assert.Contains("dual-migrate.sh apply", startupFailure.Message, StringComparison.Ordinal);

            var result = DualMigrateProcessRunner.RunFromExistingBuild(
                ["apply", "--sqlite"],
                new Dictionary<string, string?> { ["ELSA_SECRETS_EF_SQLITE"] = connectionString });
            Assert.True(result.ExitCode == 0, result.Describe());
            Assert.Contains("reindexed 1 row(s)", result.Output, StringComparison.Ordinal);
            await lifecycle.InitializeAsync();

            await using var verifyConnection = new SqliteConnection(connectionString);
            await verifyConnection.OpenAsync();
            await using var verifyContext = new SecretsSqliteDbContext(CreateOptions(verifyConnection));
            await SecretsProjectionContract.EnsureCurrentAsync(verifyContext);
            var repaired = await verifyContext.Secrets.AsNoTracking().SingleAsync();
            Assert.Equal("\uA7DC", repaired.TypeNameLookupKey);
            Assert.Equal(originalToken, repaired.ConcurrencyToken);
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [Fact]
    public async Task Reindex_processes_more_than_one_bounded_batch()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-batches-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        try
        {
            await using var context = new SecretsSqliteDbContext(CreateOptions(connection));
            await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
            for (var index = 0; index < 101; index++)
            {
                var current = SecretDocument.FromSecret(CreateSecret("\u019B", $"legacy.projection.{index:D3}"));
                context.Secrets.Add((current with { TypeNameLookupKey = "\u019B" }).ToRecord());
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(101, await SecretsProjectionContract.ReindexAsync(context));
            await SecretsProjectionContract.EnsureCurrentAsync(context);
            Assert.Equal(101, await context.Secrets.CountAsync(record => record.TypeNameLookupKey == "\uA7DC"));
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    private static DbContextOptions<SecretsSqliteDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(connection, sqlite => sqlite
                .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;

    private static Secret CreateSecret(string typeName, string name = "legacy.projection") => new()
    {
        TenantId = "tenant-a",
        Name = name,
        DisplayName = "Legacy projection",
        TypeName = typeName,
        StoreName = SecretStoreNames.Encrypted,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                Payload = SecretPayload.FromValue("value")
            }
        ]
    };
}
