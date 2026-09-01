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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkbenchOpenIddictVendor(configuration);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<OpenIddictIdentityStoreInitializer>()
            .StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database;
        Assert.True(database.IsInMemory());
        Assert.True(await database.CanConnectAsync());

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var token = await manager.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = "workbench-user",
            Type = OpenIddictConstants.TokenTypeHints.RefreshToken,
            Status = OpenIddictConstants.Statuses.Valid
        });
        var id = await manager.GetIdAsync(token);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.NotNull(await manager.FindByIdAsync(id!));
    }
}
