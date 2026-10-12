using CShells.Lifecycle;
using Nuplane.Abstractions;

namespace Elsa.Studio.Web;

internal sealed class StudioNuplaneShellReloadObserver(
    StudioRuntimeFeatureCatalogRefresher featureCatalogRefresher,
    IShellRegistry shellRegistry,
    IHostApplicationLifetime applicationLifetime,
    ILogger<StudioNuplaneShellReloadObserver> logger) : INuplaneObserver
{
    private readonly SemaphoreSlim reloadLock = new(1, 1);

    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;

    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct)
    {
        logger.LogWarning(exception, "Nuplane package {PackageId} failed during Studio package reconciliation.", packageId);
        return Task.CompletedTask;
    }

    public Task OnPackagesReconciledAsync(
        PackageChangeSet changeSet,
        IReadOnlyList<ResolvedPackage> appliedPackages,
        CancellationToken ct)
    {
        if (!HasPackageChanges(changeSet))
            return Task.CompletedTask;

        var appliedPackageCount = appliedPackages.Count;
        _ = Task.Run(
            () => RefreshAndReloadShellsAsync(changeSet, appliedPackageCount, applicationLifetime.ApplicationStopping),
            applicationLifetime.ApplicationStopping);

        return Task.CompletedTask;
    }

    private async Task RefreshAndReloadShellsAsync(PackageChangeSet changeSet, int appliedPackageCount, CancellationToken ct)
    {
        try
        {
            await reloadLock.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var catalogRefresh = await featureCatalogRefresher.RefreshAsync(ct);
            var reloadResults = await shellRegistry.ReloadActiveAsync(null, ct);
            var failedReloads = reloadResults.Where(result => result.Error is not null).ToArray();

            foreach (var reload in failedReloads)
            {
                logger.LogWarning(
                    reload.Error,
                    "Studio shell {ShellName} failed to reload after Nuplane package reconciliation {CorrelationId}.",
                    reload.Name,
                    changeSet.CorrelationId);
            }

            logger.LogInformation(
                "Studio refreshed CShells feature catalog generation {Generation} with {FeatureCount} feature(s) and reloaded {ReloadedShellCount} active shell(s) after Nuplane package reconciliation {CorrelationId}. Added={AddedCount}, Updated={UpdatedCount}, Removed={RemovedCount}, ActivePackages={ActivePackageCount}.",
                catalogRefresh.Generation,
                catalogRefresh.FeatureCount,
                reloadResults.Count - failedReloads.Length,
                changeSet.CorrelationId,
                changeSet.Added.Count,
                changeSet.Updated.Count,
                changeSet.Removed.Count,
                appliedPackageCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Studio failed to refresh CShells after Nuplane package reconciliation {CorrelationId}.",
                changeSet.CorrelationId);
        }
        finally
        {
            reloadLock.Release();
        }
    }

    private static bool HasPackageChanges(PackageChangeSet changeSet) =>
        changeSet.Added.Count > 0 || changeSet.Updated.Count > 0 || changeSet.Removed.Count > 0;
}
