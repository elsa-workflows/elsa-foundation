using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// CShells does not run shell-scoped <see cref="IHostedService"/>s. Enable and reload must apply
/// <see cref="EfMigratePolicy"/> through <c>IShellInitializer</c> on a new shell provider.
/// </summary>
public sealed class SecretsEntityFrameworkCoreShellReloadTests
{
    private const string ShellName = "secrets-ef-migrate";

    [Fact]
    public async Task AutoMigrate_applies_on_activation_and_again_after_reload_drops_the_schema()
    {
        var path = NewDbPath();
        try
        {
            await using var host = await StartHostAsync(path, EfMigratePolicy.AutoMigrate);
            var registry = host.Services.GetRequiredService<IShellRegistry>();

            var first = await registry.GetOrActivateAsync(ShellName);
            await AssertSchemaAndRepositoryAsync(first.ServiceProvider);

            await DropSecretsSchemaAsync(path);
            Assert.False(await TableExistsAsync(path, SecretsEfModule.TableName));
            Assert.False(await TableExistsAsync(path, SecretsEfModule.HistoryTableName));

            var reload = await registry.ReloadAsync(ShellName);
            if (reload.Drain is not null)
                await reload.Drain.WaitAsync();

            var second = registry.GetActive(ShellName)
                         ?? await registry.GetOrActivateAsync(ShellName);
            Assert.NotSame(first, second);
            await AssertSchemaAndRepositoryAsync(second.ServiceProvider);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Fact]
    public async Task Validate_fails_activation_when_migrations_are_pending()
    {
        var path = NewDbPath();
        try
        {
            await using var host = await StartHostAsync(path, EfMigratePolicy.Validate);
            var registry = host.Services.GetRequiredService<IShellRegistry>();

            var exception = await Assert.ThrowsAnyAsync<Exception>(() => registry.GetOrActivateAsync(ShellName));
            Assert.Contains("pending migrations", Flatten(exception), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [SkippableFact]
    public async Task Older_database_is_rejected_then_real_operator_apply_allows_fresh_validate()
    {
        Skip.IfNot(DualMigrateProcessRunner.HasDotnetEf(), "dotnet-ef is not available.");
        var path = NewDbPath();
        try
        {
            await CreateInitialSchemaAsync(path);

            await using (var validatingHost = await StartHostAsync(path, EfMigratePolicy.Validate))
            {
                var registry = validatingHost.Services.GetRequiredService<IShellRegistry>();
                var exception = await Assert.ThrowsAnyAsync<Exception>(() => registry.GetOrActivateAsync(ShellName));
                var message = Flatten(exception);
                Assert.Contains(nameof(SecretsSqliteDbContext), message, StringComparison.Ordinal);
                Assert.Contains("20260911010717_WidenLookupKeys", message, StringComparison.Ordinal);
            }

            var result = DualMigrateProcessRunner.Run(
                ["apply", "--sqlite"],
                new Dictionary<string, string?>
                {
                    ["ELSA_SECRETS_EF_SQLITE"] = SqliteConnectionString(path),
                    ["ELSA_SECRETS_EF_SQLSERVER"] = null,
                    ["ELSA_SECRETS_EF_POSTGRESQL"] = null,
                    ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
                });
            Assert.True(result.ExitCode == 0, result.Describe());
            Assert.Contains("database update --context SecretsSqliteDbContext", result.Output, StringComparison.Ordinal);
            Assert.Contains("20260911010717_WidenLookupKeys", result.Output, StringComparison.Ordinal);

            await using var freshHost = await StartHostAsync(path, EfMigratePolicy.Validate);
            var freshRegistry = freshHost.Services.GetRequiredService<IShellRegistry>();
            var shell = await freshRegistry.GetOrActivateAsync(ShellName);
            await AssertSchemaAndRepositoryAsync(shell.ServiceProvider);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Fact]
    public async Task Validate_succeeds_on_reload_after_schema_is_applied()
    {
        var path = NewDbPath();
        try
        {
            await using (var applyHost = await StartHostAsync(path, EfMigratePolicy.AutoMigrate))
            {
                await applyHost.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
                await applyHost.StopAsync();
            }

            await using var validateHost = await StartHostAsync(path, EfMigratePolicy.Validate);
            var registry = validateHost.Services.GetRequiredService<IShellRegistry>();
            var shell = await registry.GetOrActivateAsync(ShellName);
            await AssertSchemaAndRepositoryAsync(shell.ServiceProvider);

            var reload = await registry.ReloadAsync(ShellName);
            if (reload.Drain is not null)
                await reload.Drain.WaitAsync();
            var reloaded = registry.GetActive(ShellName)
                           ?? await registry.GetOrActivateAsync(ShellName);
            await AssertSchemaAndRepositoryAsync(reloaded.ServiceProvider);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    private static async Task AssertSchemaAndRepositoryAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISecretRepository>();
        Assert.IsType<EfSecretRepository>(repository);
        var context = scope.ServiceProvider.GetRequiredService<SecretsDbContext>();
        Assert.IsType<SecretsSqliteDbContext>(context);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    private static async Task CreateInitialSchemaAsync(string path)
    {
        var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(SqliteConnectionString(path), sqlite => sqlite
                .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;
        await using var context = new SecretsSqliteDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync("20260910210210_Initial");
    }

    private static async Task DropSecretsSchemaAsync(string path)
    {
        await using var connection = new SqliteConnection(SqliteConnectionString(path));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP TABLE IF EXISTS \"{SecretsEfModule.TableName}\"; " +
            $"DROP TABLE IF EXISTS \"{SecretsEfModule.HistoryTableName}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(string path, string table)
    {
        await using var connection = new SqliteConnection(SqliteConnectionString(path));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count == 1;
    }

    private static async Task<WebApplication> StartHostAsync(string path, EfMigratePolicy policy)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCShellsAspNetCore(shells =>
        {
            shells
                .WithAssemblies(typeof(SecretsEntityFrameworkCoreFeature).Assembly)
                .AddShell(ShellName, shell => shell.WithFeature<SecretsEntityFrameworkCoreFeature>(feature =>
                {
                    feature.Provider = "Sqlite";
                    feature.ConnectionString = SqliteConnectionString(path);
                    feature.MigratePolicy = policy;
                }));
        });

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }

    private static string SqliteConnectionString(string path) =>
        $"Data Source={path};Cache=Shared;Pooling=False";

    private static string NewDbPath() =>
        Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-shell-{Guid.NewGuid():N}.db");

    private static void DeleteSqliteFiles(string path)
    {
        File.Delete(path);
        File.Delete($"{path}-wal");
        File.Delete($"{path}-shm");
    }

    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add(current.Message);
        return string.Join(" | ", parts);
    }
}
