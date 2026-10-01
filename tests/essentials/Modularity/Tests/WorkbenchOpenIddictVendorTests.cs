using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>Proves the host-owned OpenIddict vendor choice remains executable after removing Elsa's wrapper.</summary>
public sealed class WorkbenchOpenIddictVendorTests
{
    [Fact]
    public async Task Workbench_vendor_registration_creates_and_reads_an_openiddict_token()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:IsDevelopmentOrDemo"] = "true"
            })
            .Build();
        await using var provider = CreateProvider(configuration);
        await StartAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;
        Assert.True(database.IsInMemory());
        Assert.True(await database.CanConnectAsync());

        var id = await CreateTokenAsync(scope.ServiceProvider, "workbench-user");

        Assert.False(string.IsNullOrWhiteSpace(id));
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        Assert.NotNull(await manager.FindByIdAsync(id!));
    }

    [Fact]
    public async Task Workbench_vendor_registration_migrates_and_reopens_durable_sqlite_store()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "tokens.db");
            await AssertDurableSqliteStoreAsync(DurableConfiguration(databasePath, autoMigrate: true), databasePath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Shared_elsa_resource_does_not_redirect_the_host_owned_openiddict_store()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-resource-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "tokens.db");
            var configuration = new ConfigurationBuilder()
                .AddConfiguration(DurableConfiguration(databasePath, autoMigrate: true))
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Elsa:Persistence:DefaultResource"] = "primary",
                    ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
                    ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
                    ["ConnectionStrings:Shared"] = "Host=resource-should-not-be-opened;Database=elsa;Username=elsa;Password=canary"
                })
                .Build();

            await AssertDurableSqliteStoreAsync(configuration, databasePath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Workbench_vendor_registration_honors_disabled_auto_migration()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-");
        try
        {
            var configuration = DurableConfiguration(Path.Combine(directory.FullName, "tokens.db"), autoMigrate: false);
            await using var provider = CreateProvider(configuration);
            await StartAsync(provider);
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;

            Assert.True(database.IsSqlite());
            Assert.Empty(await database.GetAppliedMigrationsAsync());
            Assert.Single(await database.GetPendingMigrationsAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The store's <c>AutoMigrate</c> is the options pipeline's one answer: a setting made in code after the section is bound turns
    /// the policy's migration off exactly as it turns the vendor's off, though the policy turns the vendor's off itself.
    /// </summary>
    [Fact]
    public async Task Migration_policy_honours_an_AutoMigrate_set_in_code()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-code-");
        try
        {
            await using var provider = CreateProvider(
                DurableConfiguration(Path.Join(directory.FullName, "tokens.db"), autoMigrate: true),
                withMigrationPolicy: true,
                services => services.Configure<OpenIddictIdentityOptions>(options => options.AutoMigrate = false));
            await StartAsync(provider);
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;

            Assert.Empty(await database.GetAppliedMigrationsAsync());
            Assert.Single(await database.GetPendingMigrationsAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Elsa's migration policy sits beside the vendor store, not in it: with it the durable store still migrates and reopens, and a
    /// store that turns <c>AutoMigrate</c> off is still left alone.
    /// </summary>
    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    public async Task Migration_policy_migrates_the_durable_store_as_its_AutoMigrate_asks(bool autoMigrate, int applied, int pending)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-policy-");
        try
        {
            await using var provider = CreateProvider(DurableConfiguration(Path.Join(directory.FullName, "tokens.db"), autoMigrate), withMigrationPolicy: true);
            await StartAsync(provider);
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;

            Assert.Equal(applied, (await database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(pending, (await database.GetPendingMigrationsAsync()).Count());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A SQLite migration lock a killed process left behind fails the host's start with the way to clear it instead of hanging it,
    /// under the bound the host's configuration sets (#2196).
    /// </summary>
    [Fact]
    public async Task Migration_policy_reports_a_stale_sqlite_lock_older_than_the_configured_bound()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-lock-");
        try
        {
            var databasePath = Path.Join(directory.FullName, "tokens.db");
            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE \"__EFMigrationsLock\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK___EFMigrationsLock\" PRIMARY KEY, \"Timestamp\" TEXT NOT NULL);" +
                    $"INSERT INTO \"__EFMigrationsLock\"(\"Id\", \"Timestamp\") VALUES(1, '{DateTimeOffset.UtcNow.AddMinutes(-2):yyyy-MM-dd HH:mm:ss.fffffffzzz}');";
                await command.ExecuteNonQueryAsync();
            }

            var configuration = new ConfigurationBuilder()
                .AddConfiguration(DurableConfiguration(databasePath, autoMigrate: true))
                .AddInMemoryCollection([new KeyValuePair<string, string?>(
                    $"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.SqliteMigrationLockStaleAfter)}", "00:01:00")])
                .Build();
            await using var provider = CreateProvider(configuration, withMigrationPolicy: true);

            var failure = await Assert.ThrowsAsync<EfMigrationLockStaleException>(() => StartAsync(provider).WaitAsync(TimeSpan.FromSeconds(60)));

            Assert.Contains("00:01:00", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    private static IConfiguration DurableConfiguration(string databasePath, bool autoMigrate) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:IsDevelopmentOrDemo"] = "false",
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:ConnectionString"] = $"Data Source={databasePath}",
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:AutoMigrate"] = autoMigrate.ToString()
            })
            .Build();

    private static async Task AssertDurableSqliteStoreAsync(IConfiguration configuration, string databasePath)
    {
        string id;
        await using (var writer = CreateProvider(configuration))
        {
            await StartAsync(writer);
            await using var scope = writer.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;
            Assert.True(database.IsSqlite());
            Assert.Single(await database.GetAppliedMigrationsAsync());
            id = await CreateTokenAsync(scope.ServiceProvider, "durable-workbench-user");
        }

        Assert.True(File.Exists(databasePath));
        await using var reader = CreateProvider(configuration);
        await StartAsync(reader);
        await using var readScope = reader.CreateAsyncScope();
        var manager = readScope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        Assert.NotNull(await manager.FindByIdAsync(id));
    }

    private static ServiceProvider CreateProvider(IConfiguration configuration, bool withMigrationPolicy = false, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkbenchOpenIddictVendor(configuration);
        if (withMigrationPolicy)
            services.AddWorkbenchOpenIddictMigrationPolicy(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The host's start, in registration order: the vendor initializer, then Elsa's migration policy when it is registered.</summary>
    private static async Task StartAsync(IServiceProvider provider)
    {
        await provider.GetRequiredService<OpenIddictIdentityStoreInitializer>().StartAsync(CancellationToken.None);
        if (provider.GetService<WorkbenchOpenIddictMigrator>() is { } migrator)
            await migrator.StartAsync(CancellationToken.None);
    }

    private static async Task<string> CreateTokenAsync(IServiceProvider provider, string subject)
    {
        var manager = provider.GetRequiredService<IOpenIddictTokenManager>();
        var token = await manager.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = subject,
            Type = OpenIddictConstants.TokenTypeHints.RefreshToken,
            Status = OpenIddictConstants.Statuses.Valid
        });
        return await manager.GetIdAsync(token)
               ?? throw new InvalidOperationException("OpenIddict did not assign an id to the created token.");
    }
}
