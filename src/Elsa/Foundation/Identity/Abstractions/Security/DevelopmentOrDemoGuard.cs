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
/// posture — ephemeral per-process signing keys and a seeded, startup-logged administrator whose well-known
/// credentials are committed to <c>shells.json</c>. A host launched in the <b>Production</b> environment on the
/// unedited default would otherwise boot straight into that posture.
/// </para>
/// <para>
/// This guard hard-fails startup when <c>IsDevelopmentOrDemo == true</c> unless it can positively confirm the
/// host is in the <b>Development</b> environment, so the two dangerous behaviours (ephemeral keys,
/// well-known-credential seeding) are unreachable anywhere else. It <b>fails closed</b>: if the host
/// environment cannot even be resolved (no <see cref="IHostEnvironment"/> in scope), the guard refuses rather
/// than skipping itself — a security guard must never wave a request through because it "couldn't determine
/// the environment." This mirrors the product decision that there is <b>no insecure escape hatch in
/// production</b> — the same locked rule the <c>ApiSecurity.AllowAnonymous</c> kill-switch and the
/// <c>SecurityDefaultGuard</c>s enforce.
/// </para>
/// <para>
/// Registered under both lifecycle hooks, like the seeder: <see cref="IHostedService"/> for plain hosts/tests
/// and the CShells <see cref="IShellInitializer"/> for the shell-composed <c>Elsa.Server</c> host (whose
/// shell-scoped hosted services do not run — only initializers do). CShells resolves initializers from a scope
/// off the shell's provider, which copies the root service descriptors, so a real web host's
/// <see cref="IHostEnvironment"/> is resolvable there; the fail-closed behaviour is the backstop if it is not.
/// A misconfigured production host aborts before serving a request rather than silently running insecure.
/// </para>
/// </remarks>
public sealed class DevelopmentOrDemoGuard(IServiceProvider services, string featureName) : IHostedService, IShellInitializer
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve IHostEnvironment lazily here (not at construction) so merely enumerating the registered
        // hosted services / shell initializers does not eagerly require it. FAIL CLOSED: the dangerous flag is
        // only honoured when the environment is positively confirmed to be Development. A null environment (or
        // any non-Development one) refuses — a security guard must not skip itself when it cannot determine the
        // environment. A real host (plain or CShells shell scope) always supplies IHostEnvironment; unit-level
        // composition tests that build a bare container supply a Development IHostEnvironment to satisfy this.
        var environment = services.GetService<IHostEnvironment>();
        if (environment is not null && environment.IsDevelopment())
            return Task.CompletedTask;

        var actual = environment?.EnvironmentName ?? "(no IHostEnvironment could be resolved)";
        throw new InvalidOperationException(
            $"The identity feature '{featureName}' is configured with IsDevelopmentOrDemo = true, but the host "
                + $"environment is '{actual}', not 'Development'. This mode uses ephemeral "
                + "per-process signing keys and seeds an administrator from well-known credentials committed to "
                + "shells.json, and is refused "
                + "unless the environment is positively confirmed to be Development — there is no insecure escape "
                + "hatch in production. To fix: set "
                + $"IsDevelopmentOrDemo = false for '{featureName}' and configure a real signing key (and, for the "
                + "ASP.NET Core Identity feature, a durable connection string), or run the host with "
                + "ASPNETCORE_ENVIRONMENT=Development for local/demo use.");
    }

    /// <summary>CShells shell-activation hook (see class remarks); shares the <see cref="StartAsync"/> logic.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
