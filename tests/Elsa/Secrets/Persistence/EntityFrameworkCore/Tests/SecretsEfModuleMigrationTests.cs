using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Secrets migrates through the shared <see cref="EfModuleMigrator{TContext}"/> every other EF module uses
/// (#1877), not through a hosted service of its own. This is the evidence that retiring
/// <c>SecretsEfMigrationHostedService</c> dropped none of its behaviour: the provider guard it ran through
/// <see cref="EfDatabaseMigrator.ApplyAsync"/>, the pair of lifecycle hooks — CShells'
/// <see cref="IShellInitializer"/> and a plain host's <see cref="IHostedService"/>, one instance under both —
/// its idempotence on repeat calls, and its apply-then-audit sequencing.
/// </summary>
public sealed class SecretsEfModuleMigrationTests
{
    [Fact]
    public async Task AutoMigrate_applies_on_hosted_start_and_stays_idempotent_on_the_same_instance()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate);
        await fixture.Lifecycle.StartAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.TableName));
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.HistoryTableName));

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

        var reloaded = fixture.Provider.GetRequiredService<EfModuleMigrator<SecretsDbContext>>();
        await using var second = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate, fixture.Path);
        await second.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(second, SecretsEfModule.TableName));
        Assert.NotSame(fixture.Lifecycle, second.Lifecycle);
        Assert.Same(fixture.Lifecycle, reloaded);
    }

    [Fact]
    public async Task Validate_fails_cleanly_when_migrations_are_pending()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.Validate);
        var exception = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            fixture.Lifecycle.InitializeAsync(CancellationToken.None));
        Assert.Contains("pending migrations", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    [Fact]
    public async Task Validate_fails_on_hosted_start_when_migrations_are_pending()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.Validate);
        var exception = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            fixture.Lifecycle.StartAsync(CancellationToken.None));
        Assert.Contains("pending migrations", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    [Fact]
    public async Task Validate_fails_again_when_retried_on_the_same_instance()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(EfMigratePolicy.Validate);
        await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            fixture.Lifecycle.InitializeAsync(CancellationToken.None));

        var retry = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            fixture.Lifecycle.InitializeAsync(CancellationToken.None));
        Assert.Contains("pending migrations", retry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    [Fact]
    public async Task Validate_succeeds_after_AutoMigrate_on_a_reloaded_instance()
    {
        await using var applied = await MigrationHostFixture.CreateAsync(EfMigratePolicy.AutoMigrate);
        await applied.Lifecycle.InitializeAsync(CancellationToken.None);

        await using var validated = await MigrationHostFixture.CreateAsync(EfMigratePolicy.Validate, applied.Path);
        await validated.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(validated, SecretsEfModule.TableName));
    }

    /// <summary>
    /// The provider guard the retired hosted service ran by hand is the one
    /// <see cref="EfDatabaseMigrator.ApplyAsync"/> already runs, on the expected provider name the
    /// registration hands the migrator — the same value <c>SecretsEfMigrationHostedService</c> computed from
    /// its own options. This asserts that input, and the declared action beside it, because what the guard
    /// does with a mismatch is <see cref="EfProviderGuard"/>'s own tested behaviour, not this module's.
    /// </summary>
    [Fact]
    public async Task The_registration_hands_the_migrator_the_expected_provider_and_the_declared_action()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync();
        var migration = fixture.Provider.GetRequiredService<EfModuleMigration<SecretsDbContext>>();

        Assert.Equal(EfProviderNames.Sqlite, migration.ExpectedProviderName);
        Assert.Equal("Secrets", migration.Module);
        Assert.Equal("Sqlite", migration.Provider);
        Assert.IsType<SecretsProjectionReindex>(Assert.Single(migration.PostMigration));
    }

    /// <summary>
    /// The migrator reads the host-wide key, never a Secrets-only setting: an unset key keeps the
    /// <see cref="EfMigratePolicy.AutoMigrate"/> default (FR-058).
    /// </summary>
    [Fact]
    public async Task The_policy_comes_from_the_host_wide_key_and_defaults_to_AutoMigrate()
    {
        await using var fixture = await MigrationHostFixture.CreateAsync(policy: null);

        Assert.Equal(EfMigratePolicy.AutoMigrate, fixture.Provider.GetRequiredService<IOptions<EfMigrateOptions>>().Value.Policy);
        await fixture.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.True(await TableExistsAsync(fixture, SecretsEfModule.TableName));
    }

    private static async Task<bool> TableExistsAsync(MigrationHostFixture fixture, string table)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count == 1;
    }

    private sealed class MigrationHostFixture(
        string path,
        string connectionString,
        ServiceProvider provider,
        EfModuleMigrator<SecretsDbContext> lifecycle) : IAsyncDisposable
    {
        public string Path { get; } = path;
        public string ConnectionString { get; } = connectionString;
        public ServiceProvider Provider { get; } = provider;
        public EfModuleMigrator<SecretsDbContext> Lifecycle { get; } = lifecycle;

        public static ValueTask<MigrationHostFixture> CreateAsync(
            EfMigratePolicy? policy = EfMigratePolicy.AutoMigrate,
            string? existingPath = null)
        {
            var path = existingPath ?? System.IO.Path.Join(
                System.IO.Path.GetTempPath(),
                $"elsa-secrets-ef-lifecycle-{Guid.NewGuid():N}.db");
            // One connection string for EF and assertions. Pooling=False avoids a leftover
            // pool connection locking the file when a second fixture (reload / Validate)
            // opens the same path.
            var connectionString = $"Data Source={path};Cache=Shared;Pooling=False";
            var services = new ServiceCollection()
                .AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connectionString
                });
            // The host-wide key, the same one every other module's migrator reads — never a feature setting.
            if (policy is { } configured)
                services.Configure<EfMigrateOptions>(options => options.Policy = configured);
            var built = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var lifecycle = built.GetRequiredService<EfModuleMigrator<SecretsDbContext>>();
            // One instance under both hooks, exactly as the retired hosted service was registered.
            Assert.Same(lifecycle, Assert.Single(built.GetServices<IHostedService>().OfType<EfModuleMigrator<SecretsDbContext>>()));
            Assert.Same(lifecycle, Assert.Single(built.GetServices<IShellInitializer>().OfType<EfModuleMigrator<SecretsDbContext>>()));
            return ValueTask.FromResult(new MigrationHostFixture(path, connectionString, built, lifecycle));
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            TemporarySqliteDatabase.ClearPoolAndDeleteFiles(Path);
        }
    }
}
