using CShells.Features;
using CShells.Lifecycle;
using Nuplane.Abstractions;

namespace Elsa.Workbench;

/// <summary>
/// After the Nuplane feed reconciles, refreshes the CShells runtime feature catalog, so the next shell reload
/// (<c>POST /_admin/shells/reload/{name}</c>) composes the assemblies of the packages the feed just installed instead of
/// the ones the catalog was first built from. With <c>Elsa:Shells:ReloadOnPackageChange</c> set to <c>true</c> it also
/// reloads the active shells itself, as <c>Elsa.Foundation.Host</c> does; the Workbench default is <c>false</c>, so a
/// package that arrives takes effect at the next explicit reload.
/// </summary>
/// <remarks>
/// Without the refresh a reload rebuilt the shell from the catalog's first snapshot: a newer release of a package-loaded
/// feature was installed and loaded by Nuplane, and still never reached a shell until the process restarted.
/// The work runs in <see cref="OnPackagesReconciledAsync"/>, not <see cref="OnPackagesChangedAsync"/>: only by then has
/// Nuplane's auto-loader loaded the new assemblies (see the same bridge in Elsa.Foundation.Host).
/// </remarks>
internal sealed class ShellCatalogRefreshOnPackagesChanged(
    IRuntimeFeatureCatalog runtimeFeatureCatalog,
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<ShellCatalogRefreshOnPackagesChanged> logger) : INuplaneObserver
{
    public const string ReloadKey = "Elsa:Shells:ReloadOnPackageChange";

    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public async Task OnPackagesReconciledAsync(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages, CancellationToken ct)
    {
        // Before the first activation the catalog is built from what is loaded then; nothing to refresh.
        if (!AnyShellActive())
            return;

        try
        {
            var snapshot = await runtimeFeatureCatalog.RefreshAsync(ct);
            logger.LogInformation("Refreshed runtime feature catalog after a Nuplane reconcile: {Count} feature descriptor(s).", snapshot.FeatureDescriptors.Count);

            if (!bool.TryParse(configuration[ReloadKey], out var reload) || !reload)
                return;

            var results = await registry.ReloadActiveAsync(null, ct);
            logger.LogInformation("Reloaded {Count} active shell(s) after a Nuplane reconcile.", results.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            logger.LogError(exception, "Refreshing the runtime feature catalog after a Nuplane reconcile failed; the new assemblies apply after a restart.");
        }
    }

    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct) => Task.CompletedTask;

    private bool AnyShellActive() =>
        configuration.GetSection("CShells:Shells").GetChildren()
            .Select(child => child.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Any(name => registry.GetActive(name) is not null);
}
