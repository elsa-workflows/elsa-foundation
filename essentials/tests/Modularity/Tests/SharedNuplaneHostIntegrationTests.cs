using CShells;
using CShells.Lifecycle;
using CShells.Nuplane;
using Nuplane.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class SharedNuplaneHostColdActivationTests
{
    [Theory(DisplayName = "The registered participant consumes cold catalog freshness before snapshot selection")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ColdParticipantBegin_RefreshesPendingCatalogBeforeSnapshotSelection(bool foundation)
    {
        using var host = NuplaneHostTestComposition.CreateAdapter(foundation);

        await host.DispatchAsync(new PackageChangeSet([], [new ResolvedPackage("Elsa.Samples.Nuplane.Notes", "1.1.0", "local-packages", "/packages/notes", DateTimeOffset.UnixEpoch, "feed")], [], "cold-build", DateTimeOffset.UnixEpoch));

        Assert.Empty(host.Catalog.Operations);
        Assert.Equal(0, host.Registry.Reloads);
        var context = new ShellGenerationBuildContext(ShellDescriptor.Create("cold", 1), new ShellId("cold"));
        await using var lease = await host.Participant.BeginAsync(context, CancellationToken.None);

        Assert.Equal(["refresh"], host.Catalog.Operations);
        Assert.Equal(0, host.Registry.Reloads);
        var selectedSnapshot = await host.Catalog.GetSnapshotAsync();
        await lease.OnSnapshotSelectedAsync(selectedSnapshot, CancellationToken.None);
        Assert.Equal(["refresh", "snapshot"], host.Catalog.Operations);
    }
}
