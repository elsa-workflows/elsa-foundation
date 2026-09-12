using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class FeatureManagementServiceTests
{
    private const string SecuredFeatureId = "Secured";
    private const string SigningKeyValue = "signing-key-value-that-must-not-leak";
    private const string KeyRingValue = "key-ring-material-that-must-not-leak";
    private const string DefaultApiKeyValue = "default-api-key-that-must-not-leak";

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

    [Fact]
    public async Task CatalogMasksSetSecretsAndLeavesUnsetSecretsAndOtherSettingsAlone()
    {
        _store.Features[SecuredFeatureId] = Json($$"""{"signingKey":"{{SigningKeyValue}}","KeyRing":{"k1":"{{KeyRingValue}}"},"EmptySecret":"","Mode":"fast"}""");

        var catalog = await CreateService(SecuredFeature()).GetCatalogAsync();

        var configuration = Assert.Single(catalog.Features, x => x.Id == SecuredFeatureId).Configuration;
        Assert.Equal(SecretSettingMask.Placeholder, configuration.GetProperty("signingKey").GetString());
        Assert.Equal(SecretSettingMask.Placeholder, configuration.GetProperty("KeyRing").GetString());
        Assert.Equal("", configuration.GetProperty("EmptySecret").GetString());
        Assert.Equal("fast", configuration.GetProperty("Mode").GetString());
        AssertNoSecretValue(catalog);
    }

    [Fact]
    public async Task CatalogMasksSecretDefaultValues()
    {
        var catalog = await CreateService(SecuredFeature()).GetCatalogAsync();

        var settings = Assert.Single(catalog.Features, x => x.Id == SecuredFeatureId).Settings;
        Assert.Equal(SecretSettingMask.Placeholder, Assert.Single(settings, x => x.Name == "ApiKey").DefaultValue?.GetString());
        Assert.Equal("fast", Assert.Single(settings, x => x.Name == "Mode").DefaultValue?.GetString());
        AssertNoSecretValue(catalog);
    }

    [Fact]
    public async Task ApplyKeepsStoredSecretsWhenTheCatalogIsRoundTripped()
    {
        _store.Features[SecuredFeatureId] = Json($$"""{"SigningKey":"{{SigningKeyValue}}","KeyRing":{"k1":"{{KeyRingValue}}"},"Mode":"fast"}""");
        var service = CreateService(SecuredFeature());
        var catalog = await service.GetCatalogAsync();

        var result = await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            catalog.Features.Where(x => x.Enabled).Select(x => new FeatureApplyItem(x.Id, true, x.Configuration)).ToArray()));

        var stored = _store.Features[SecuredFeatureId];
        Assert.Equal(SigningKeyValue, stored.GetProperty("SigningKey").GetString());
        Assert.Equal(KeyRingValue, stored.GetProperty("KeyRing").GetProperty("k1").GetString());
        AssertNoSecretValue(result.Catalog);
    }

    [Fact]
    public async Task CatalogHidesTheConfigurationOfAFeatureWithoutSettingsMetadata()
    {
        // e.g. a feature still listed in shells.json whose package was pruned: nothing declares its settings.
        _store.Features["Orphaned"] = Json($$"""{"ApiToken":"{{SigningKeyValue}}"}""");

        var catalog = await CreateService().GetCatalogAsync();

        var configuration = Assert.Single(catalog.Features, x => x.Id == "Orphaned").Configuration;
        Assert.Equal(SecretSettingMask.Placeholder, configuration.GetProperty("ApiToken").GetString());
        AssertNoSecretValue(catalog);
    }

    [Fact]
    public async Task ApplyRestoresEachSecretFromItsOwnFeatureWhateverItsCasing()
    {
        _store.Features["SecuredA"] = Json("""{"signingKey":"signing-key-of-a"}""");
        _store.Features["SecuredB"] = Json("""{"SIGNINGKEY":"signing-key-of-b"}""");
        var service = CreateService(SecuredFeature("SecuredA"), SecuredFeature("SecuredB"));
        var catalog = await service.GetCatalogAsync();
        var placeholder = Json($$"""{"SigningKey":"{{SecretSettingMask.Placeholder}}"}""");

        await service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [new("SecuredA", true, placeholder), new("SecuredB", true, placeholder)]));

        Assert.Equal("signing-key-of-a", _store.Features["SecuredA"].GetProperty("SigningKey").GetString());
        Assert.Equal("signing-key-of-b", _store.Features["SecuredB"].GetProperty("SigningKey").GetString());
    }

    [Fact]
    public async Task ApplyStoresANewSecretValue()
    {
        _store.Features[SecuredFeatureId] = Json($$"""{"SigningKey":"{{SigningKeyValue}}"}""");
        var service = CreateService(SecuredFeature());
        var catalog = await service.GetCatalogAsync();

        await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [new(SecuredFeatureId, true, Json("""{"SigningKey":"rotated-signing-key"}"""))]));

        Assert.Equal("rotated-signing-key", _store.Features[SecuredFeatureId].GetProperty("SigningKey").GetString());
    }

    [Fact]
    public async Task ApplyDropsAPlaceholderThatHasNoStoredValue()
    {
        _store.Features[SecuredFeatureId] = Json("{}");
        var service = CreateService(SecuredFeature());
        var catalog = await service.GetCatalogAsync();

        await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [new(SecuredFeatureId, true, Json($$"""{"SigningKey":"{{SecretSettingMask.Placeholder}}","Mode":"slow"}"""))]));

        var stored = _store.Features[SecuredFeatureId];
        Assert.False(stored.TryGetProperty("SigningKey", out _));
        Assert.Equal("slow", stored.GetProperty("Mode").GetString());
    }

    private static ContributingFeatureCatalogContributor SecuredFeature(string featureId = SecuredFeatureId) =>
        new(
            featureId,
            Setting("SigningKey", "string", secret: true),
            Setting("KeyRing", "object", secret: true),
            Setting("EmptySecret", "string", secret: true),
            Setting("ApiKey", "string", secret: true, defaultValue: $"\"{DefaultApiKeyValue}\""),
            Setting("Mode", "string", secret: false, defaultValue: "\"fast\""));

    private static FeatureSettingDescriptor Setting(string name, string jsonType, bool secret, string? defaultValue = null) =>
        new(name, name, null, null, null, null, jsonType, false, defaultValue is null ? null : Json(defaultValue), secret, false, false, false, false, null, null, []);

    private static void AssertNoSecretValue(FeatureCatalogResponse catalog)
    {
        var json = JsonSerializer.Serialize(catalog);
        Assert.DoesNotContain(SigningKeyValue, json);
        Assert.DoesNotContain(KeyRingValue, json);
        Assert.DoesNotContain(DefaultApiKeyValue, json);
    }

    private FeatureManagementService CreateService(params IFeatureCatalogContributor[] contributors) =>
        new(_store, contributors, _refresher, _reloader);

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class ContributingFeatureCatalogContributor(string featureId, params FeatureSettingDescriptor[] settings) : IFeatureCatalogContributor
    {
        public Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
        {
            var feature = context.GetOrAdd(featureId);
            feature.DisplayName = featureId;
            feature.SourceKind = FeatureSourceKinds.Runtime;
            feature.Settings = settings;
            return Task.CompletedTask;
        }
    }
}
