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

    private static StudioRuntimeFeatureCatalogRefresher CreateRefresher(RuntimeFeatureCatalogSnapshot snapshot) =>
        new(new FakeRuntimeFeatureCatalog(snapshot), NullLogger<StudioRuntimeFeatureCatalogRefresher>.Instance);

    [Fact]
    public async Task RefreshAsyncMapsSnapshotGenerationAndFeatureCount()
    {
        var result = await CreateRefresher(Snapshot(7, "A", "B")).RefreshAsync();

        Assert.Equal(7, result.Generation);
        Assert.Equal(2, result.FeatureCount);
    }

    [Fact]
    public async Task GetFeatureDescriptorsAsyncReturnsSnapshotDescriptors()
    {
        var descriptors = await CreateRefresher(Snapshot(1, "A", "B", "C")).GetFeatureDescriptorsAsync();

        Assert.Equal(["A", "B", "C"], descriptors.Select(descriptor => descriptor.Id));
    }

    private sealed class FakeRuntimeFeatureCatalog(RuntimeFeatureCatalogSnapshot snapshot) : IRuntimeFeatureCatalog
    {
        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
