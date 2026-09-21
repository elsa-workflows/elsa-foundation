using System.Diagnostics;
using CShells.Lifecycle;

namespace Elsa.Workbench.Boot;

/// <summary>
/// Host-level hosted service that eagerly activates the configured shell(s) at boot (spec 132, program unit 4).
///
/// It is registered only when <c>Elsa:Boot:EagerShellActivation:Enabled</c> is set, so a host that leaves the
/// switch off never constructs it. When enabled, <see cref="StartAsync"/> resolves the target shells and drives
/// each through <see cref="IShellRegistry.GetOrActivateAsync"/> — the exact call <c>ShellMiddleware</c> makes on a
/// cold request — so the resulting shell state is byte-identical to lazy activation, just paid at boot instead of
/// on the first user request. This removes the mid-activation contention tail: by the time Kestrel reports the app
/// started, the activation wall has already been paid (or is in-flight and shared, not duplicated).
///
/// Deliberately a HOST-level <see cref="IHostedService"/>: CShells does not run shell-scoped hosted services, and
/// the eager trigger must live outside any shell container (it activates the shells). Activation failures are
/// logged and swallowed — eager activation is an optimization, so a failure degrades to today's lazy behavior
/// (the shell activates on its first request) rather than crashing the host.
/// </summary>
public sealed class EagerShellActivationHostedService(
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<EagerShellActivationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = EagerShellActivationOptions.Read(configuration);
        if (!options.Enabled)
            return; // Defensive: the service is only registered when enabled, but never activate if the switch flipped.

        var configured = EagerShellActivationOptions.ReadConfiguredShellNames(configuration);
        var targets = options.ResolveTargetShellNames(configured);
        if (targets.Count == 0)
        {
            logger.LogWarning(
                "Eager shell activation is enabled but resolved no target shells (configured shells: [{ConfiguredShells}]). Shells will activate lazily on first request.",
                string.Join(", ", configured));
            return;
        }

        logger.LogInformation("Eager shell activation: activating {Count} shell(s) at boot: [{Shells}]", targets.Count, string.Join(", ", targets));

        foreach (var name in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await registry.GetOrActivateAsync(name, cancellationToken);
                stopwatch.Stop();
                logger.LogInformation("Eager shell activation: shell '{Shell}' active in {ElapsedMs} ms.", name, stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                logger.LogWarning(
                    exception,
                    "Eager shell activation of shell '{Shell}' failed after {ElapsedMs} ms; it will activate lazily on its first request instead.",
                    name,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
