using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Abstractions.Security;

/// <summary>
/// Startup guard that makes the identity features' <c>IsDevelopmentOrDemo</c> flag safe by construction.
/// </summary>
/// <remarks>
/// <para>
/// The default <c>shells.json</c> ships <c>IsDevelopmentOrDemo: true</c> for the enabled-by-default identity
/// features. That flag is bound purely from configuration; on its own it decides the insecure development
/// posture — ephemeral per-process signing keys and a seeded, startup-logged <c>admin</c>/<c>Password123!</c>
/// account. A host launched in the <b>Production</b> environment on the unedited default would otherwise boot
/// straight into that posture.
/// </para>
/// <para>
/// This guard hard-fails startup when <c>IsDevelopmentOrDemo == true</c> but
/// <see cref="IHostEnvironment.IsDevelopment"/> is <c>false</c>, so the two dangerous behaviours (ephemeral
/// keys, well-known-credential seeding) are unreachable outside Development. This mirrors the product decision
/// that there is <b>no insecure escape hatch in production</b> — the same locked rule the
/// <c>ApiSecurity.AllowAnonymous</c> kill-switch and the <c>SecurityDefaultGuard</c>s enforce — and, like the
/// seeder, runs under both lifecycle hooks (<see cref="IHostedService"/> for plain hosts/tests and the CShells
/// <see cref="IShellInitializer"/> for the shell-composed <c>Elsa.Server</c> host) so it fires wherever the
/// features are composed. A misconfigured production host aborts before serving a request rather than silently
/// running insecure.
/// </para>
/// </remarks>
public sealed class DevelopmentOrDemoGuard(IServiceProvider services, string featureName) : IHostedService, IShellInitializer
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve IHostEnvironment lazily here (not at construction) so merely enumerating the registered
        // hosted services / shell initializers in a bare DI container — as unit-level composition tests do —
        // does not force a host environment to be present. If no environment is resolvable (a pure unit test),
        // there is nothing to guard against and the check is skipped; a real host always supplies one.
        var environment = services.GetService<IHostEnvironment>();
        if (environment is null || environment.IsDevelopment())
            return Task.CompletedTask;

        throw new InvalidOperationException(
            $"The identity feature '{featureName}' is configured with IsDevelopmentOrDemo = true, but the host "
                + $"environment is '{environment.EnvironmentName}', not 'Development'. This mode uses ephemeral "
                + "per-process signing keys and seeds a well-known 'admin'/'Password123!' account, and is refused "
                + "outside Development — there is no insecure escape hatch in production. To fix: set "
                + $"IsDevelopmentOrDemo = false for '{featureName}' and configure a real signing key (and, for the "
                + "ASP.NET Core Identity feature, a durable connection string), or run the host with "
                + "ASPNETCORE_ENVIRONMENT=Development for local/demo use.");
    }

    /// <summary>CShells shell-activation hook (see class remarks); shares the <see cref="StartAsync"/> logic.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
