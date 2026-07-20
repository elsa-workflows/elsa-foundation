using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class FeatureManagementServiceTests
{
    private readonly FakeShellStore _store = new();
    private readonly FakeRuntimeRefresher _refresher = new();
    private readonly FakeShellReloader _reloader = new();

    [Fact]
    public async Task CatalogMergesShellAndContributorFeatures()
    {
        _store.Features["EnabledFeature"] = Json("{}");
        var service = CreateService(new ContributingFeatureCatalogContributor("AvailableFeature"));

        var catalog = await service.GetCatalogAsync();

        Assert.Contains(catalog.Features, x => x.Id == "EnabledFeature" && x.Enabled);
        Assert.Contains(catalog.Features, x => x.Id == "AvailableFeature" && !x.Enabled);
    }

    [Fact]
    public async Task ApplyValidatesRevisionPersistsChangesRefreshesAndReloads()
    {
        _store.Features["OldFeature"] = Json("{}");
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        var result = await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [
                new("OldFeature", false, Json("{}")),
                new("NewFeature", true, Json("""{"answer":42}"""))
            ]));

        Assert.False(_store.Features.ContainsKey("OldFeature"));
        Assert.Equal(42, _store.Features["NewFeature"].GetProperty("answer").GetInt32());
        Assert.Equal(1, _refresher.RefreshCount);
        Assert.Equal(1, _reloader.ReloadCount);
        Assert.Contains(result.Catalog.Features, x => x.Id == "NewFeature" && x.Enabled);
    }

    [Fact]
    public async Task ApplyRejectsStaleRevision()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<FeatureCatalogRevisionConflictException>(() =>
            service.ApplyAsync(new FeatureApplyRequest("stale", [])));
    }

    [Fact]
    public async Task ApplyRejectsRequestOmittingEnabledFeatures()
    {
        _store.Features["KeepMe"] = Json("{}");
        _store.Features["AlsoKeepMe"] = Json("{}");
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ApplyAsync(new FeatureApplyRequest(
                catalog.Revision,
                [new("NewFeature", true, Json("{}"))])));

        Assert.Contains("AlsoKeepMe, KeepMe", exception.Message);
        Assert.True(_store.Features.ContainsKey("KeepMe"));
        Assert.True(_store.Features.ContainsKey("AlsoKeepMe"));
        Assert.False(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(0, _refresher.RefreshCount);
        Assert.Equal(0, _reloader.ReloadCount);
    }

    [Fact]
    public async Task ApplyAllowsOmittingDisabledFeatures()
    {
        _store.Features["KeepMe"] = Json("{}");
        var service = CreateService(new ContributingFeatureCatalogContributor("AvailableFeature"));
        var catalog = await service.GetCatalogAsync();

        var result = await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [new("KeepMe", true, Json("{}"))]));

        Assert.True(_store.Features.ContainsKey("KeepMe"));
        Assert.Contains(result.Catalog.Features, x => x.Id == "AvailableFeature" && !x.Enabled);
    }

    [Fact]
    public async Task ApplyMatchesEnabledFeatureIdsCaseInsensitively()
    {
        _store.Features["KeepMe"] = Json("{}");
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        var result = await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [new("keepme", true, Json("{}"))]));

        Assert.Contains(result.Catalog.Features, x => x.Enabled);
    }

    private FeatureManagementService CreateService(params IFeatureCatalogContributor[] contributors) =>
        new(_store, contributors, _refresher, _reloader);

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeShellStore : IShellFeatureConfigurationStore
    {
        public Dictionary<string, JsonElement> Features { get; } = new(StringComparer.OrdinalIgnoreCase);

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

        private ShellFeatureConfigurationSnapshot Snapshot()
        {
            var revision = string.Join('|', Features.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value.GetRawText()}"));
            return new ShellFeatureConfigurationSnapshot("default", revision, Features);
        }
    }

    private sealed class ContributingFeatureCatalogContributor(string featureId) : IFeatureCatalogContributor
    {
        public Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
        {
            var feature = context.GetOrAdd(featureId);
            feature.DisplayName = featureId;
            feature.SourceKind = FeatureSourceKinds.Runtime;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRuntimeRefresher : IRuntimeFeatureCatalogRefresher
    {
        public int RefreshCount { get; private set; }

        public Task<int> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(++RefreshCount);
    }

    private sealed class FakeShellReloader : IShellReloader
    {
        public int ReloadCount { get; private set; }

        public Task<int> ReloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(++ReloadCount);
    }
}
