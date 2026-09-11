using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Builds the OpenIddict server options during startup so a host that composes token issuance without a usable
/// signing key fails to activate instead of failing every request. The options, and with them the signing
/// credentials from <see cref="ConfigureOpenIddictServerOptions"/>, are otherwise first built when a request runs
/// authentication: without this guard the shell activates, readiness reports it ready, and every request that
/// authenticates returns 500.
/// </summary>
/// <remarks>
/// Registered under both lifecycle hooks, like <c>DevelopmentOrDemoGuard</c>: the CShells
/// <see cref="IShellInitializer"/> for shell-composed hosts, whose shell-scoped hosted services do not run, and
/// <see cref="IHostedService"/> for plain hosts. It reads the same <see cref="IOptionsMonitor{TOptions}"/> cache
/// the OpenIddict handlers read, so requests reuse the credentials built here.
/// </remarks>
internal sealed class ValidateOpenIddictServerOptionsInitializer(IOptionsMonitor<OpenIddictServerOptions> options)
    : IShellInitializer, IHostedService
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _ = options.CurrentValue;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
