using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.Schema;
using Nuplane.Abstractions;

namespace Elsa.Workbench;

/// <summary>
/// After a Nuplane reconcile that added, updated or removed a package, refreshes the CShells runtime feature catalog, so the
/// next shell reload (<c>POST /_admin/shells/reload/{name}</c>) composes the assemblies of the packages the feed just
/// installed instead of the ones the catalog was first built from. With <c>Elsa:Shells:ReloadOnPackageChange</c> set to
/// <c>true</c> it also reloads the active shells itself; the Workbench default is <c>false</c>, so a package that arrives
/// takes effect at the next explicit reload.
/// </summary>
/// <remarks>
/// <para>
/// The twin of <c>Elsa.Foundation.Host</c>'s <c>ShellReloadOnPackagesChanged</c>, which reloads by default and acts on every
/// reconcile. This one skips a reconcile that changed no package: Nuplane reconciles every minute and reports each cycle,
/// changed or not, and a refresh rescans every loaded assembly.
/// </para>
/// <para>
/// Without the refresh a reload rebuilt the shell from the catalog's first snapshot: a newer release of a package-loaded
/// feature was installed and loaded by Nuplane, and still never reached a shell until the process restarted. The work runs in
/// <see cref="OnPackagesReconciledAsync"/>, not <see cref="OnPackagesChangedAsync"/>: only by then has Nuplane's auto-loader,
/// registered before this observer, loaded the new assemblies.
/// </para>
/// <para>
/// A change stays pending until it has been acted on: when the refresh fails, or a reload leaves a shell on its previous
/// generation (a module refused it, say, for a pending migration), the next reconcile tries again although it changed nothing
/// itself. Skipping unchanged reconciles must not turn one failed attempt into a catalog that is never refreshed.
/// </para>
/// </remarks>
internal sealed class ShellCatalogRefreshOnPackagesChanged(
    IRuntimeFeatureCatalog runtimeFeatureCatalog,
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<ShellCatalogRefreshOnPackagesChanged> logger) : INuplaneObserver
{
    public const string ReloadKey = "Elsa:Shells:ReloadOnPackageChange";

    private static readonly string HostDirectory = $"\"{Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}\"";

    // Nuplane runs one reconcile at a time and calls its observers in turn, so this is never read and written concurrently.
    private bool _changePending;

    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public async Task OnPackagesReconciledAsync(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages, CancellationToken ct)
    {
        // Before the first activation the catalog is built from what is loaded then; nothing to refresh.
        if (!AnyShellActive())
            return;

        _changePending |= changeSet.Added.Count > 0 || changeSet.Updated.Count > 0 || changeSet.Removed.Count > 0;
        if (!_changePending)
            return;

        try
        {
            var snapshot = await runtimeFeatureCatalog.RefreshAsync(ct);
            logger.LogInformation("Refreshed runtime feature catalog after a Nuplane reconcile: {Count} feature descriptor(s).", snapshot.FeatureDescriptors.Count);

            if (bool.TryParse(configuration[ReloadKey], out var reload) && reload && !await ReloadActiveShellsAsync(ct))
                return;

            _changePending = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        // Fatal CLR exceptions are excluded so they still propagate; any other failure is logged and retried at the next reconcile.
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException or AppDomainUnloadedException or BadImageFormatException))
        {
            logger.LogError(exception, "Acting on a Nuplane reconcile failed; the next reconcile tries again, and until then a shell reload composes the catalog as it was.");
        }
    }

    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Reloads every active shell and reports each one that did not switch, as Foundation.Host's observer does: CShells keeps a
    /// shell's previous generation active when its reload fails, and only says so in the result. True when every shell reloaded.
    /// </summary>
    private async Task<bool> ReloadActiveShellsAsync(CancellationToken ct)
    {
        var results = await registry.ReloadActiveAsync(null, ct);
        var failures = results.Where(result => result.Error is not null).ToArray();
        logger.LogInformation("Reloaded {Count} active shell(s) after a Nuplane reconcile.", results.Count - failures.Length);
        foreach (var failure in failures)
        {
            if (Refusal(failure.Error) is { } refusal)
            {
                logger.LogWarning(
                    "Reloading shell '{Shell}' after a Nuplane reconcile was refused; its previous generation is still active. {Error}",
                    failure.Name,
                    refusal.Message.Replace(IEfModuleRefusal.HostPlaceholder, HostDirectory, StringComparison.Ordinal));
            }
            else
            {
                logger.LogError(failure.Error, "Reloading shell '{Shell}' after a Nuplane reconcile failed; its previous generation is still active.", failure.Name);
            }
        }

        return failures.Length == 0;
    }

    /// <summary>An EF module's refusal in <paramref name="error"/> or what it wraps, however deep a shell's initializer nested it.</summary>
    private static Exception? Refusal(Exception? error) => error switch
    {
        null => null,
        IEfModuleRefusal => error,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Refusal).FirstOrDefault(found => found is not null),
        _ => Refusal(error.InnerException)
    };

    private bool AnyShellActive() =>
        configuration.GetSection("CShells:Shells").GetChildren()
            .Select(child => child.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Any(name => registry.GetActive(name) is not null);
}
