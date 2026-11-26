using CShells.Features;

namespace Elsa.Testing;

internal sealed class FakeRuntimeFeatureCatalog(params ShellFeatureDescriptor[] descriptors) : IRuntimeFeatureCatalog
{
    public int RefreshCount { get; private set; }

    public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Build());

    public Task<IRuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCount++;
        return Task.FromResult(CurrentSnapshot);
    }

    public IRuntimeFeatureCatalogSnapshot CurrentSnapshot => new Projection(
        descriptors.Select(descriptor => new RuntimeFeatureDescriptor
        {
            Id = descriptor.Id,
            DisplayName = descriptor.Metadata.TryGetValue("DisplayName", out var displayName) ? displayName?.ToString() ?? descriptor.Id : descriptor.Id,
            Description = descriptor.Metadata.TryGetValue("Description", out var description) ? description?.ToString() : null,
            Dependencies = descriptor.Dependencies
        }).ToArray());

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private sealed record Projection(IReadOnlyList<RuntimeFeatureDescriptor> FeatureDescriptors) : IRuntimeFeatureCatalogSnapshot
    {
        public long Generation => 1;
        public DateTimeOffset RefreshedAt => DateTimeOffset.UnixEpoch;
    }

    private RuntimeFeatureCatalogSnapshot Build()
    {
        var map = new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in descriptors)
            if (!string.IsNullOrWhiteSpace(descriptor.Id))
                map[descriptor.Id] = descriptor;

        return new RuntimeFeatureCatalogSnapshot(1, [], descriptors, map, DateTimeOffset.UnixEpoch);
    }
}
