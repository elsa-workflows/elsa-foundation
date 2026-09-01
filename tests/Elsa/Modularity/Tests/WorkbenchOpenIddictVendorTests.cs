using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Microsoft.EntityFrameworkCore;
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
            var configuration = DurableConfiguration(Path.Combine(directory.FullName, "tokens.db"), autoMigrate: true);
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

            await using var reader = CreateProvider(configuration);
            await StartAsync(reader);
            await using var readScope = reader.CreateAsyncScope();
            var manager = readScope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            Assert.NotNull(await manager.FindByIdAsync(id));
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

    private static IConfiguration DurableConfiguration(string databasePath, bool autoMigrate) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:IsDevelopmentOrDemo"] = "false",
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:ConnectionString"] = $"Data Source={databasePath}",
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:AutoMigrate"] = autoMigrate.ToString()
            })
            .Build();

    private static ServiceProvider CreateProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkbenchOpenIddictVendor(configuration);
        return services.BuildServiceProvider();
    }

    private static Task StartAsync(IServiceProvider provider) =>
        provider.GetRequiredService<OpenIddictIdentityStoreInitializer>()
            .StartAsync(CancellationToken.None);

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
