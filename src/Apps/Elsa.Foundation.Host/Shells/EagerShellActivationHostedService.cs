using CShells.Lifecycle;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// Optional. Activates the configured shell(s) at boot so shell-lifetime work starts without waiting for the
/// first HTTP request — most importantly the feed's Tasks feature, whose shell initializer runs every
/// registered startup task and starts the background/recurring task loops on activation.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a HOST-level <see cref="IHostedService"/>: CShells does not run shell-scoped hosted services,
/// so the trigger that activates the shells must live outside any shell. It drives each shell through the same
/// <see cref="IShellRegistry.GetOrActivateAsync"/> path a cold request uses, so the resulting shell state is
/// identical — just paid at boot.
/// </para>
/// <para>
/// Gated by <c>Elsa:Boot:EagerShellActivation:Enabled</c> (default off). Activation failures are logged and
/// swallowed: a shell that fails to activate eagerly simply activates lazily on its first request.
/// </para>
/// </remarks>
internal sealed class EagerShellActivationHostedService(
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<EagerShellActivationHostedService> logger) : IHostedService
{
    public const string EnabledKey = "Elsa:Boot:EagerShellActivation:Enabled";

    // The configured shell names are the child keys of the CShells composition — the same source CShells'
    // own configuration blueprint provider reads, so it matches shells.json exactly.
    private const string ConfiguredShellsSection = "CShells:Shells";

    public static bool IsEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration[EnabledKey], out var enabled) && enabled;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled(configuration))
            return;

        var shellNames = configuration.GetSection(ConfiguredShellsSection)
            .GetChildren()
            .Select(child => child.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (shellNames.Length == 0)
        {
            logger.LogWarning("Eager shell activation is enabled but no shells are configured under '{Section}'.", ConfiguredShellsSection);
            return;
        }

        foreach (var name in shellNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await registry.GetOrActivateAsync(name, cancellationToken);
                logger.LogInformation("Eagerly activated shell '{Shell}' at boot.", name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Eager activation of shell '{Shell}' failed; it will activate lazily on its first request.", name);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
