using System.Reflection;
using CShells.Features;
using Elsa.Studio.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Studio.Tests;

public sealed class StudioRuntimeFeatureCatalogRefresherTests
{
    private static ShellFeatureDescriptor Descriptor(string id) => new() { Id = id, StartupType = typeof(object) };

    private static RuntimeFeatureCatalogSnapshot Snapshot(long generation, params string[] featureIds) =>
        new(
            generation,
            Array.Empty<Assembly>(),
            featureIds.Select(Descriptor).ToArray(),
            new Dictionary<string, ShellFeatureDescriptor>(),
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task RefreshAsyncMapsSnapshotGenerationAndFeatureCount()
    {
        var snapshot = Snapshot(7, "A", "B");
        var refresher = new StudioRuntimeFeatureCatalogRefresher(new FakeRuntimeFeatureCatalog(snapshot), NullLogger<StudioRuntimeFeatureCatalogRefresher>.Instance);

        var result = await refresher.RefreshAsync();

        Assert.Equal(7, result.Generation);
        Assert.Equal(2, result.FeatureCount);
    }

    [Fact]
    public async Task GetFeatureDescriptorsAsyncReturnsSnapshotDescriptors()
    {
        var snapshot = Snapshot(1, "A", "B", "C");
        var refresher = new StudioRuntimeFeatureCatalogRefresher(new FakeRuntimeFeatureCatalog(snapshot), NullLogger<StudioRuntimeFeatureCatalogRefresher>.Instance);

        var descriptors = await refresher.GetFeatureDescriptorsAsync();

        Assert.Equal(["A", "B", "C"], descriptors.Select(descriptor => descriptor.Id));
    }

    private sealed class FakeRuntimeFeatureCatalog(RuntimeFeatureCatalogSnapshot snapshot) : IRuntimeFeatureCatalog
    {
        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
