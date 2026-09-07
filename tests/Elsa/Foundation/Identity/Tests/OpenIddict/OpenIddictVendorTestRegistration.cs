using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>
/// Test-only host wiring for OpenIddict's vendor EF store. Production ownership lives in Elsa.Workbench; keeping
/// the test context here lets the behavior and contract suites exercise an explicit vendor choice without teaching
/// the provider-neutral Elsa composition extension about EF.
/// </summary>
internal static class OpenIddictVendorTestRegistration
{
    internal static IServiceCollection AddOpenIddictVendorForTests(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(configureDbContext);
        services.AddDbContext<OpenIddictIdentityDbContext>((_, builder) => configureDbContext(builder));

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>());

        services.AddSingleton<OpenIddictIdentityStoreInitializer>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OpenIddictIdentityStoreInitializer>());

        return services;
    }
}

/// <summary>Test-host context for OpenIddict's vendor-owned entity model.</summary>
internal sealed class OpenIddictIdentityDbContext(DbContextOptions<OpenIddictIdentityDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.UseOpenIddict();
        base.OnModelCreating(builder);
    }
}

/// <summary>Creates the test-host schema when a composed host starts.</summary>
internal sealed class OpenIddictIdentityStoreInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
