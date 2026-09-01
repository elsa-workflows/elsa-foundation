using CShells.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>
/// Bootstraps the OpenIddict token store schema at startup so tokens can be issued immediately: it migrates
/// the relational (durable) store or ensure-creates the in-memory (dev/demo) one. Runs on both paths — the
/// token store has no seed step to piggyback the migration on (unlike the identity module's
/// <c>IdentitySeeder</c>), so without this a durable deployment would 500 on first issuance with a missing
/// <c>OpenIddictTokens</c> table.
/// </summary>
/// <remarks>
/// The frozen EF slice keeps both lifecycle interfaces for compatibility. Workbench wires the initializer as a
/// root <see cref="IHostedService"/> because CShells copies root descriptors into shell providers; registering the
/// same initializer again as an <see cref="IShellInitializer"/> would run durable migrations twice on activation.
/// </remarks>
public sealed class OpenIddictIdentityStoreInitializer(
    IServiceProvider services,
    IOptions<OpenIddictEntityFrameworkCoreOptions> options) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken) => EnsureSchemaAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => EnsureSchemaAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        // The in-memory (dev/demo) provider has no migrations, so it is always ensure-created. Relational
        // providers migrate at startup unless AutoMigrate is turned off — a multi-instance deployment that
        // applies migrations out-of-band opts out here to avoid concurrent MigrateAsync races.
        if (!db.Database.IsRelational())
            await db.Database.EnsureCreatedAsync(cancellationToken);
        else if (options.Value.AutoMigrate)
            await db.Database.MigrateAsync(cancellationToken);
    }
}
