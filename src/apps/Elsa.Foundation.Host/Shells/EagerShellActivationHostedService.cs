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
/// Gated by <c>Elsa:Boot:EagerShellActivation:Enabled</c> (default on). Start makes one attempt per shell, in order, as it
/// always did, so a shell that activates is active when the host listens. A shell that fails is not left to its first request:
/// a readiness-gated load balancer never sends one, and the node would stay live, not ready and idle until restarted. It is
/// retried in the background, with the delay doubling up to <see cref="EagerShellActivationRetryOptions.MaxDelay"/> and then
/// held there, with no attempt count after which it stops: a database that is down for an hour is the same fault as one that is
/// down for a second. Each failure is logged, recorded in <see cref="ShellActivationTracker"/> for the readiness probe's reason
/// and the Attention item, and the record goes when the shell activates.
/// </para>
/// <para>
/// A fault is retried on the doubling delay: the database, the feed or a seeder that raced a peer may be fine next time. An EF
/// module's refusal (<c>IEfModuleRefusal</c>: a pending migration, a contracting one not yet appliable, the finalization gate)
/// is not a fault: it is a decision that waits for an operator, which retries cannot change. It is checked again at the
/// slowest interval and never sooner, so the node comes up by itself once the operator has applied the migrations, without
/// hammering the database until then.
/// </para>
/// </remarks>
public sealed class EagerShellActivationHostedService(
    IShellRegistry registry,
    IConfiguration configuration,
    ShellActivationTracker tracker,
    ILogger<EagerShellActivationHostedService> logger) : IHostedService, IDisposable
{
    public const string EnabledKey = "Elsa:Boot:EagerShellActivation:Enabled";

    // The configured shell names are the child keys of the CShells composition — the same source CShells'
    // own configuration blueprint provider reads, so it matches shells.json exactly.
    private const string ConfiguredShellsSection = "CShells:Shells";

    // Default ON in code (absent or unparseable value means enabled), mirroring
    // ShellReloadOnPackagesChanged. The default lives here, not only in the shipped appsettings.json, so a
    // consumer who REPLACES appsettings.json (rather than layering onto it) still gets eager activation —
    // set the key to "false" to opt out.
    public static bool IsEnabled(IConfiguration configuration) =>
        !bool.TryParse(configuration[EnabledKey], out var enabled) || enabled;

    private readonly CancellationTokenSource _stopping = new();
    private Task[] _retries = [];

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

        // Before the first attempt, so a shell that is activated by a request or a reload while it is failing is forgotten.
        tracker.Observe(registry);
        var retry = EagerShellActivationRetryOptions.Read(configuration);
        var failed = new List<(string Name, TimeSpan Delay)>();
        foreach (var name in shellNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryActivateAsync(name, retry, cancellationToken) is { } delay)
                failed.Add((name, delay));
        }

        // Past the start: the host listens while these keep trying, and StopAsync ends them.
        _retries = [.. failed.Select(shell => RetryAsync(shell.Name, shell.Delay, retry, _stopping.Token))];
    }

    /// <summary>
    /// Ends the retries. The wait for one that is mid-activation is bounded by <paramref name="cancellationToken"/>, the host's
    /// shutdown token, so an activation that ignores its cancellation cannot hold the shutdown beyond it.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await Task.WhenAll(_retries).WaitAsync(cancellationToken);
    }

    public void Dispose() => _stopping.Dispose();

    private async Task RetryAsync(string name, TimeSpan delay, EagerShellActivationRetryOptions retry, CancellationToken stopping)
    {
        try
        {
            while (true)
            {
                await Task.Delay(delay, tracker.TimeProvider, stopping);
                if (await TryActivateAsync(name, retry, stopping) is not { } next)
                    return;

                delay = next;
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>
    /// One attempt to activate <paramref name="name"/>: <see langword="null"/> when it is active, otherwise how long to wait
    /// before the next attempt, the failure having been logged and recorded.
    /// </summary>
    private async Task<TimeSpan?> TryActivateAsync(string name, EagerShellActivationRetryOptions retry, CancellationToken cancellationToken)
    {
        try
        {
            await registry.GetOrActivateAsync(name, cancellationToken);
            tracker.Activated(name);
            logger.LogInformation("Eagerly activated shell '{Shell}' at boot.", name);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Whatever stopped the activation is reported and retried, rather than leaving the node idle until it is restarted.
        // Fatal CLR exceptions are excluded from the filter so they still propagate.
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException or AppDomainUnloadedException or BadImageFormatException))
        {
            var failure = ShellReloadFailure.Describe(name, exception, ShellReloadFailure.HostDirectory);
            var recorded = tracker.Failed(name, exception.GetType().Name, failure.Refusal, retry);
            if (failure.Refusal is null)
                logger.LogWarning(exception, "Eager activation of shell '{Shell}' failed (attempt {Attempt}); trying again in {Delay}.", name, recorded.Attempts, recorded.RetryDelay);
            else
                logger.LogWarning("Eager activation of shell '{Shell}' was refused (attempt {Attempt}) and waits for an operator; checking again in {Delay}. {Error}", name, recorded.Attempts, recorded.RetryDelay, failure.Error);

            return recorded.RetryDelay;
        }
    }
}
