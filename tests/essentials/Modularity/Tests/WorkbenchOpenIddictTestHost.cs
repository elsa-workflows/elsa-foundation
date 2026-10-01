using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Elsa.Modularity.Tests;

/// <summary>
/// A node of Workbench's OpenIddict store as the host composes it, and its start, shared by the store's tests on SQLite and, linked
/// into the provider tests, on the native engines.
/// </summary>
internal static class WorkbenchOpenIddictTestHost
{
    internal static IConfiguration DurableConfiguration(string databasePath, bool autoMigrate) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:IsDevelopmentOrDemo"] = "false",
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:ConnectionString"] = $"Data Source={databasePath}",
                ["CShells:Shells:default:Features:FoundationIdentityOpenIddict:AutoMigrate"] = autoMigrate.ToString()
            })
            .Build();

    internal static ServiceProvider CreateProvider(IConfiguration configuration, bool withMigrationPolicy = false, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddWorkbenchOpenIddictVendor(configuration);
        services.AddWorkbenchOpenIddictStoreProvider(configuration);
        if (withMigrationPolicy)
            services.AddWorkbenchOpenIddictMigrationPolicy(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The host's start, in registration order: the vendor initializer, then Elsa's migration policy when it is registered.</summary>
    internal static async Task StartAsync(IServiceProvider provider)
    {
        await provider.GetRequiredService<OpenIddictIdentityStoreInitializer>().StartAsync(CancellationToken.None);
        if (provider.GetService<WorkbenchOpenIddictMigrator>() is { } migrator)
            await migrator.StartAsync(CancellationToken.None);
    }

    internal static async Task<string> CreateTokenAsync(IServiceProvider provider, string subject)
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
