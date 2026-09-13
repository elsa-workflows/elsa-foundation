using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Features;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;

namespace Elsa.Modularity.Tests;

/// <summary>An in-memory shell configuration whose revision changes with every feature's configuration.</summary>
internal sealed class FakeShellStore(IEqualityComparer<string>? featureIdComparer = null) : IShellFeatureConfigurationStore
{
    public Dictionary<string, JsonElement> Features { get; } = new(featureIdComparer ?? StringComparer.OrdinalIgnoreCase);

    public Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Snapshot());

    public Task<ShellFeatureConfigurationSnapshot> SaveAsync(
        string expectedRevision,
        IReadOnlyList<FeatureConfigurationChange> features,
        CancellationToken cancellationToken = default)
    {
        var current = Snapshot();
        if (expectedRevision != current.Revision)
            throw new FeatureCatalogRevisionConflictException(expectedRevision, current.Revision);

        Features.Clear();
        foreach (var feature in features.Where(x => x.Enabled))
            Features[feature.Id] = feature.Configuration.Clone();

        return Task.FromResult(Snapshot());
    }

    // Hashed, so the raw configuration never reaches the catalog. Unkeyed is fine for a double; the real store keys it.
    private ShellFeatureConfigurationSnapshot Snapshot()
    {
        var content = string.Join('|', Features.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value.GetRawText()}"));
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new ShellFeatureConfigurationSnapshot("default", revision, Features);
    }
}

internal sealed class FakeRuntimeFeatureCatalog(params ShellFeatureDescriptor[] descriptors) : IRuntimeFeatureCatalog
{
    public int RefreshCount { get; private set; }

    public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Build());

    public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCount++;
        return Task.FromResult(Build());
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

internal sealed class FakeRuntimeRefresher : IRuntimeFeatureCatalogRefresher
{
    public int RefreshCount { get; private set; }

    public Task<int> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(++RefreshCount);
}

internal sealed class FakeShellReloader : IShellReloader
{
    public int ReloadCount { get; private set; }

    public Task<int> ReloadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(++ReloadCount);
}

internal sealed class FakeNuplaneAdminOperations : INuplaneAdminOperations
{
    public List<ActivePackage> Packages { get; } = [];

    public bool ReconcileCalled { get; private set; }

    public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ActivePackagesSnapshot(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Packages, "generation"));

    public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult<OperationalStateSnapshot>(default!);

    public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken)
    {
        ReconcileCalled = true;
        return Task.FromResult<ManualReconcileOutcome>(default!);
    }
}
