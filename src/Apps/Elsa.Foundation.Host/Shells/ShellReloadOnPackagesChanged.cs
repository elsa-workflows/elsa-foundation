using CShells.Lifecycle;
using Nuplane.Abstractions;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// Optional extra #2 — the hot-reload bridge. When the Nuplane feed applies a package change (via the
/// directory folder listener or a manual reconcile), reload the active CShells shells so a running server
/// picks up the new feature assemblies without a restart. CShells re-composes each shell's middleware and
/// endpoints for the new generation automatically.
/// </summary>
/// <remarks>
/// Gated by <c>Elsa:Shells:ReloadOnPackageChange</c> (default <c>true</c>). When disabled, the observer stays
/// registered but does nothing — a package change then refreshes only Nuplane's catalog, and the new
/// assemblies apply on the next restart or an explicit reload call.
/// </remarks>
internal sealed class ShellReloadOnPackagesChanged(
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<ShellReloadOnPackagesChanged> logger) : INuplaneObserver
{
    public const string EnabledKey = "Elsa:Shells:ReloadOnPackageChange";

    // Default on: absent or unparseable configuration means enabled.
    private bool Enabled => !bool.TryParse(configuration[EnabledKey], out var enabled) || enabled;

    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public async Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct)
    {
        if (!Enabled)
            return;

        try
        {
            var results = await registry.ReloadActiveAsync(null, ct);
            logger.LogInformation("Reloaded {Count} active shell(s) after a Nuplane package change.", results.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to reload active shells after a Nuplane package change; the new assemblies apply on the next restart or an explicit reload.");
        }
    }

    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct) => Task.CompletedTask;
}
