using CShells.Features;
using CShells.Lifecycle;
using Nuplane.Abstractions;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// Hot-reload bridge: after the Nuplane feed reconciles, refresh the CShells runtime feature catalog and
/// reload the active shells, so a running server picks up new/updated feature assemblies without a restart.
/// </summary>
/// <remarks>
/// <para>
/// The work is done in <see cref="OnPackagesReconciledAsync"/> — the reconciliation-COMPLETION phase — NOT
/// <see cref="OnPackagesChangedAsync"/>. OnPackagesChanged fires at disk-apply time, before Nuplane's
/// auto-loader (registered first, via AutoloadPackages) has loaded the new assemblies into their load
/// contexts; refreshing there sees a stale/empty loaded-assembly catalog. By OnPackagesReconciled the
/// assemblies are loaded and the catalog is live, so the refresh rebinds each feature id to its new assembly
/// and the shell reload composes the new endpoints. This mirrors Nuplane's own sample host, which re-queries
/// its catalogs from OnPackagesReconciledAsync.
/// </para>
/// <para>
/// Gated by <c>Elsa:Shells:ReloadOnPackageChange</c> (default <c>true</c>). Skips while no shell is active
/// (startup): normal eager/lazy activation builds the initial catalog from the loaded assemblies.
/// </para>
/// </remarks>
internal sealed class ShellReloadOnPackagesChanged(
    IRuntimeFeatureCatalog runtimeFeatureCatalog,
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<ShellReloadOnPackagesChanged> logger) : INuplaneObserver
{
    public const string EnabledKey = "Elsa:Shells:ReloadOnPackageChange";

    private bool Enabled => !bool.TryParse(configuration[EnabledKey], out var enabled) || enabled;

    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    // Deliberately a no-op: too early (assemblies not loaded yet). See OnPackagesReconciledAsync.
    public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public async Task OnPackagesReconciledAsync(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages, CancellationToken ct)
    {
        if (!Enabled)
            return;

        // At startup normal activation builds the catalog; only act once a shell is active (post-startup).
        if (!AnyShellActive())
            return;

        try
        {
            var snapshot = await runtimeFeatureCatalog.RefreshAsync(ct);
            logger.LogInformation("Refreshed runtime feature catalog after reconcile: {Count} feature descriptor(s).", snapshot.FeatureDescriptors.Count);

            var results = await registry.ReloadActiveAsync(null, ct);
            logger.LogInformation("Reloaded {Count} active shell(s) after a Nuplane reconcile.", results.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to reload active shells after a Nuplane reconcile; the new assemblies apply on the next restart or an explicit reload.");
        }
    }

    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct) => Task.CompletedTask;

    // True once at least one configured shell has been activated (GetActive returns null until then).
    private bool AnyShellActive() =>
        configuration.GetSection("CShells:Shells").GetChildren()
            .Select(child => child.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Any(name => registry.GetActive(name) is not null);
}
