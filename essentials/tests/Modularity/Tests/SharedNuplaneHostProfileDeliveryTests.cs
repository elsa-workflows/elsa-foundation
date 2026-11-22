using CShells.Lifecycle;
using Nuplane.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class SharedNuplaneHostProfileDeliveryTests
{
    private static readonly ResolvedPackage Notes = new("Elsa.Samples.Nuplane.Notes", "1.1.0", "local-packages", "/packages/notes", DateTimeOffset.UnixEpoch, "feed");

    [Fact(DisplayName = "Foundation processes every eligible completion and preserves pending work while disabled")]
    public async Task FoundationDelivery_EveryCompletionAndDisabledPendingWork_ArePreserved()
    {
        using var host = NuplaneHostTestComposition.CreateAdapter(foundation: true, "default");

        await host.DispatchAsync(Changed());
        await host.DispatchAsync(Unchanged());
        Assert.Equal(2, host.Catalog.Refreshes);
        Assert.Equal(2, host.Registry.Reloads);

        var failure = new InvalidOperationException("catalog unavailable");
        host.Catalog.Failure = failure;
        await host.DispatchAsync(Changed());
        var refreshesWithPendingWork = host.Catalog.Refreshes;
        host.SetReloadOnPackageChange(false);
        await host.DispatchAsync(Changed());
        Assert.Equal(refreshesWithPendingWork, host.Catalog.Refreshes);

        host.Catalog.Failure = null;
        host.SetReloadOnPackageChange(true);
        await host.DispatchAsync(Unchanged());

        Assert.Equal(refreshesWithPendingWork + 1, host.Catalog.Refreshes);
        Assert.Equal(3, host.Registry.Reloads);
    }

    [Fact(DisplayName = "Workbench does not create reload work while disabled or when configuration alone changes")]
    public async Task WorkbenchRefreshOnly_ToggleAndUnchangedCompletion_DoNotInventReload()
    {
        using var host = NuplaneHostTestComposition.CreateAdapter(foundation: false, "default");

        await host.DispatchAsync(Changed());
        Assert.Equal((1, 0), (host.Catalog.Refreshes, host.Registry.Reloads));

        host.SetReloadOnPackageChange(true);
        Assert.Equal((1, 0), (host.Catalog.Refreshes, host.Registry.Reloads));
        await host.DispatchAsync(Unchanged());
        Assert.Equal((1, 0), (host.Catalog.Refreshes, host.Registry.Reloads));

        await host.DispatchAsync(Changed());
        Assert.Equal((2, 1), (host.Catalog.Refreshes, host.Registry.Reloads));
    }

    [Fact(DisplayName = "Workbench retries retained failed reload work after automatic reload is re-enabled without rescanning")]
    public async Task WorkbenchFailedReload_AutoReloadPause_RetriesWithoutCatalogRefresh()
    {
        using var host = NuplaneHostTestComposition.CreateAdapter(foundation: false, "default");
        host.SetReloadOnPackageChange(true);
        host.Registry.Results = [new ReloadResult("default", null, null, new InvalidOperationException("activation failed"))];

        await host.DispatchAsync(Changed());
        Assert.Equal((1, 1), (host.Catalog.Refreshes, host.Registry.Reloads));

        host.SetReloadOnPackageChange(false);
        host.Registry.Results = [new ReloadResult("default", null, null, null)];
        await host.DispatchAsync(Unchanged());
        Assert.Equal((1, 1), (host.Catalog.Refreshes, host.Registry.Reloads));

        host.SetReloadOnPackageChange(true);
        await host.DispatchAsync(Unchanged());
        Assert.Equal((1, 2), (host.Catalog.Refreshes, host.Registry.Reloads));
    }

    [Theory(DisplayName = "An in-flight delivery uses its captured profile and later deliveries see reloaded configuration")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfileReload_DuringBlockedRefresh_AppliesToTheNextDelivery(bool foundation)
    {
        using var host = NuplaneHostTestComposition.CreateAdapter(foundation, "default");
        if (!foundation)
            host.SetReloadOnPackageChange(true);

        var gate = host.Catalog.BlockNextRefresh();
        var dispatch = host.DispatchAsync(Changed());
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            host.SetReloadOnPackageChange(false);
        }
        finally
        {
            gate.Release.TrySetResult();
            await dispatch.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(1, host.Catalog.Refreshes);
        Assert.Equal(1, host.Registry.Reloads);

        await host.DispatchAsync(Changed());
        Assert.Equal(foundation ? 1 : 2, host.Catalog.Refreshes);
        Assert.Equal(1, host.Registry.Reloads);
    }

    private static PackageChangeSet Unchanged() => new([], [], [], "profile-cycle", DateTimeOffset.UnixEpoch);

    private static PackageChangeSet Changed() => new([], [Notes], [], "profile-cycle", DateTimeOffset.UnixEpoch);
}
