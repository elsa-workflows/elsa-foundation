using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsEfMigrationHostedServiceTests
{
    [Fact]
    public async Task AutoMigrate_applies_on_hosted_start_and_is_a_no_op_on_the_same_instance_initializer()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate);
        await fixture.Lifecycle.StartAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.TableName));
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.HistoryTableName));

        // Same instance: IShellInitializer must not start a second concurrent apply.
        await fixture.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    [Fact]
    public async Task InitializeAsync_applies_when_hosted_start_did_not_run()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate);
        await fixture.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    [Fact]
    public async Task Reload_uses_a_new_instance_and_AutoMigrate_stays_idempotent()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate);
        await fixture.Lifecycle.InitializeAsync(CancellationToken.None);

        var reloaded = fixture.Provider.GetRequiredService<SecretsEfMigrationHostedService>();
        // The fixture registers one singleton. A CShells reload builds a new provider; emulate that.
        await using var second = await MigrationHostFixture.CreateAsync(
            EfMigratePolicy.AutoMigrate,
            fixture.Path);
        await second.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(second, SecretsEfModule.TableName));
        Assert.NotSame(fixture.Lifecycle, second.Lifecycle);
        Assert.Same(fixture.Lifecycle, reloaded);
    }

    [Fact]
    public async Task Validate_fails_cleanly_when_migrations_are_pending()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.Validate);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Lifecycle.InitializeAsync(CancellationToken.None));
        Assert.Contains("pending migrations", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    [Fact]
    public async Task Validate_succeeds_after_AutoMigrate_on_a_reloaded_instance()
    {
        await using var applied = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate);
        await applied.Lifecycle.InitializeAsync(CancellationToken.None);

        await using var validated = await MigrationHostFixture.CreateAsync(
            EfMigratePolicy.Validate,
            applied.Path);
        await validated.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(validated, SecretsEfModule.TableName));
    }

    private static async Task<bool> TableExistsAsync(MigrationHostFixture fixture, string table)
    {
        await using var command = fixture.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count == 1;
    }

    private sealed class MigrationHostFixture : IAsyncDisposable
    {
        private MigrationHostFixture(
            string path,
            SqliteConnection connection,
            ServiceProvider provider,
            SecretsEfMigrationHostedService lifecycle)
        {
            Path = path;
            Connection = connection;
            Provider = provider;
            Lifecycle = lifecycle;
        }

        public string Path { get; }
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public SecretsEfMigrationHostedService Lifecycle { get; }

        public static async ValueTask<MigrationHostFixture> CreateAsync(
            EfMigratePolicy policy,
            string? existingPath = null)
        {
            var path = existingPath ?? System.IO.Path.Join(
                System.IO.Path.GetTempPath(),
                $"elsa-secrets-ef-lifecycle-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var services = new ServiceCollection()
                .AddSingleton(connection)
                .AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connection.ConnectionString,
                    MigratePolicy = policy
                });
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var lifecycle = provider.GetRequiredService<SecretsEfMigrationHostedService>();
            Assert.Same(lifecycle, Assert.Single(provider.GetServices<IHostedService>()));
            Assert.Same(lifecycle, Assert.Single(provider.GetServices<IShellInitializer>()));
            return new MigrationHostFixture(path, connection, provider, lifecycle);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Connection.DisposeAsync();
            File.Delete(Path);
        }
    }
}
