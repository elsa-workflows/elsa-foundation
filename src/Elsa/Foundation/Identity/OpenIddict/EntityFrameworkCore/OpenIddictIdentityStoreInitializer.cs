using CShells.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>
/// Development/demo bootstrap for the OpenIddict token store: ensures the schema exists at startup so a
/// fresh checkout can issue tokens immediately. Mirrors the identity module's <c>IdentitySeeder</c> approach;
/// production deployments apply the migrations explicitly instead.
/// </summary>
/// <remarks>
/// Implemented as both an <see cref="IHostedService"/> (plain hosts / tests) and a CShells
/// <see cref="IShellInitializer"/> (the shell-composed Elsa.Server host, where shell-scoped hosted services
/// do not run). Ensure-created / migrate are idempotent, so running under either hook is safe.
/// </remarks>
public sealed class OpenIddictIdentityStoreInitializer(IServiceProvider services) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken) => EnsureSchemaAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => EnsureSchemaAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        // Relational providers migrate; the in-memory provider has no migrations, so ensure-created instead.
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(cancellationToken);
        else
            await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
