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
    // Empty unless a test composes one: the ordinary host is the one that has no activation guard at all.
    private readonly List<IFeatureActivationGuard> _guards = [];

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
    public async Task RefreshFailureLeavesTheSavedSelectionAheadOfActivation()
    {
        _refresher.Failure = new InvalidOperationException("refresh failed");
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            new FeatureApplyRequest(catalog.Revision, [new("NewFeature", true, Json("{}"))])));

        Assert.True(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _refresher.RefreshCount);
        Assert.Equal(0, _reloader.ReloadCount);
    }

    [Fact]
    public async Task ReloadFailureLeavesTheSavedSelectionAndDoesNotReturnAnApplyResult()
    {
        _reloader.Failure = new InvalidOperationException("reload failed");
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            new FeatureApplyRequest(catalog.Revision, [new("NewFeature", true, Json("{}"))])));

        Assert.True(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _refresher.RefreshCount);
        Assert.Equal(1, _reloader.ReloadCount);
    }

    [Fact]
    public async Task ReportedReloadFailureReturnsSuccessWithZeroReloadedShells()
    {
        _reloader.Result = 0;
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        var result = await service.ApplyAsync(
            new FeatureApplyRequest(catalog.Revision, [new("NewFeature", true, Json("{}"))]));

        Assert.True(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _refresher.RefreshCount);
        Assert.Equal(1, _reloader.ReloadCount);
        Assert.Equal(0, result.ReloadedShellCount);
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
    public async Task ApplyRoundTripsEnabledUnknownFeaturesAndTheirUnrecognizedSettings()
    {
        _store.Features["FutureFeature"] = Json("""{"Flag":false,"Limit":0,"Label":"","Optional":null}""");
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();
        var feature = Assert.Single(catalog.Features, item => item.Id == "FutureFeature");

        await service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
            [new(feature.Id, true, feature.Configuration)]));

        var preserved = _store.Features["FutureFeature"];
        Assert.False(preserved.GetProperty("Flag").GetBoolean());
        Assert.Equal(0, preserved.GetProperty("Limit").GetInt32());
        Assert.Equal("", preserved.GetProperty("Label").GetString());
        Assert.Equal(JsonValueKind.Null, preserved.GetProperty("Optional").ValueKind);
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
    public async Task ApplyKeepsAValueHiddenAtReadTimeEvenIfItsSettingIsDeclaredByApplyTime()
    {
        // The package declaring ApiUrl loads between the read and the apply; shells.json, and so the revision, is unchanged.
        _store.Features["Sample"] = Json("""{"ApiUrl":"https://real"}""");
        var contributor = new ContributingFeatureCatalogContributor("Sample");
        var service = CreateService(contributor);
        var catalog = await service.GetCatalogAsync();
        contributor.Settings = [Setting("ApiUrl", "string", secret: false)];

        await service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [new("Sample", true, Assert.Single(catalog.Features).Configuration)]));

        Assert.Equal("https://real", _store.Features["Sample"].GetProperty("ApiUrl").GetString());
    }

    [Fact]
    public async Task ApplyRestoresASecretWhenTheStoreMatchesFeatureIdsCaseSensitively()
    {
        var store = new FakeShellStore(StringComparer.Ordinal);
        store.Features[SecuredFeatureId] = Json($$"""{"SigningKey":"{{SigningKeyValue}}"}""");
        var service = new FeatureManagementService(store, [SecuredFeature()], _guards,
            new LegacyFeatureActivationContextPreparer(), _refresher, _reloader);
        var catalog = await service.GetCatalogAsync();

        await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [new(SecuredFeatureId.ToLowerInvariant(), true, Json($$"""{"SigningKey":"{{SecretSettingMask.Placeholder}}"}"""))]));

        Assert.Equal(SigningKeyValue, store.Features[SecuredFeatureId.ToLowerInvariant()].GetProperty("SigningKey").GetString());
    }

    [Fact]
    public async Task ApplyWithoutFeaturesIsRejectedAsAnInvalidRequest()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => CreateService().ApplyAsync(new FeatureApplyRequest("", null!)));

        Assert.Contains("Revision is required", exception.Message);
    }

    [Fact]
    public async Task ApplyRejectsAFeatureWithoutAnId()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            [new(null!, true, Json($$"""{"SigningKey":"{{SecretSettingMask.Placeholder}}"}"""))])));

        Assert.Contains("Feature ID is required", exception.Message);
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

    /// <summary>
    /// Spec 171 FR-060: a refusal stops the save, so nothing is stored, no catalog is refreshed and no
    /// shell is reloaded — and the guard saw the request before any of that could happen.
    /// </summary>
    [Fact]
    public async Task ApplyRefusedByAGuardSavesNothingAndRefreshesNothing()
    {
        var guard = new RefusingActivationGuard("NewFeature", "module 'Sample' has a pending migration");
        _guards.Add(guard);
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        var exception = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [new("NewFeature", true, Json("{}"))])));

        Assert.Equal("module 'Sample' has a pending migration", exception.Message);
        Assert.Equal("NewFeature", Assert.Single(exception.Refusals).Feature);
        Assert.False(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(0, _refresher.RefreshCount);
        Assert.Equal(0, _reloader.ReloadCount);
    }

    /// <summary>Every guard is asked, so one apply reports every feature that would have been refused.</summary>
    [Fact]
    public async Task ApplyReportsEveryGuardsRefusals()
    {
        _guards.Add(new RefusingActivationGuard("First", "first reason"));
        _guards.Add(new RefusingActivationGuard("Second", "second reason"));
        var service = CreateService(
            new ContributingFeatureCatalogContributor("First"),
            new ContributingFeatureCatalogContributor("Second"));
        var catalog = await service.GetCatalogAsync();

        var exception = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            service.ApplyAsync(new FeatureApplyRequest(
                catalog.Revision,
                [new("First", true, Json("{}")), new("Second", true, Json("{}"))])));

        Assert.Equal(new[] { "First", "Second" }, exception.Refusals.Select(refusal => refusal.Feature).ToArray());
        Assert.Contains("first reason", exception.Message);
        Assert.Contains("second reason", exception.Message);
    }

    /// <summary>
    /// A guard that throws is not a guard that allowed the request: nothing is saved there either, and the
    /// failure surfaces as itself rather than as a refusal it never made.
    /// </summary>
    [Fact]
    public async Task ApplyDoesNotSaveWhenAGuardThrows()
    {
        _guards.Add(new ThrowingActivationGuard());
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [new("NewFeature", true, Json("{}"))])));

        Assert.False(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(0, _refresher.RefreshCount);
        Assert.Equal(0, _reloader.ReloadCount);
    }

    /// <summary>
    /// FR-061's premise, which the EF guard's redaction obligation rests on: RestoreSecrets runs before
    /// validation, so a guard reads the request's real, unmasked values and not the placeholder a client
    /// round-tripped. A guard that assumed otherwise would probe a database with the placeholder as its
    /// password and report it as unreachable.
    /// </summary>
    [Fact]
    public async Task AGuardSeesTheRequestsRestoredSecretsRatherThanThePlaceholder()
    {
        _store.Features[SecuredFeatureId] = Json($$"""{"SigningKey":"{{SigningKeyValue}}"}""");
        var guard = new CapturingActivationGuard();
        _guards.Add(guard);
        var service = CreateService(SecuredFeature());
        var catalog = await service.GetCatalogAsync();

        await service.ApplyAsync(new FeatureApplyRequest(
            catalog.Revision,
            catalog.Features.Where(x => x.Enabled).Select(x => new FeatureApplyItem(x.Id, true, x.Configuration)).ToArray()));

        var seen = Assert.Single(guard.Context!.EnabledFeatures);
        Assert.Equal(SecuredFeatureId, seen.Id);
        Assert.Equal(SigningKeyValue, seen.Configuration.GetProperty("SigningKey").GetString());
        Assert.Equal(catalog.Revision, guard.Context.Shell.Revision);
    }

    [Fact]
    public async Task Preparation_sees_restored_secrets_and_its_returned_context_reaches_guards_only()
    {
        _store.Features[SecuredFeatureId] = Json($$"""{"SigningKey":"{{SigningKeyValue}}"}""");
        var preparer = new ProjectingActivationContextPreparer();
        var guard = new CapturingActivationGuard();
        _guards.Add(guard);
        var service = new FeatureManagementService(_store, [SecuredFeature()], _guards, preparer,
            _refresher, _reloader);
        var catalog = await service.GetCatalogAsync();

        await service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
            [new(SecuredFeatureId, true, Json($$"""{"SigningKey":"{{SecretSettingMask.Placeholder}}"}"""))]));

        Assert.Equal(1, preparer.CallCount);
        Assert.Equal(SigningKeyValue,
            Assert.Single(preparer.Seen!.EnabledFeatures).Configuration.GetProperty("SigningKey").GetString());
        Assert.Equal("prepared",
            Assert.Single(guard.Context!.EnabledFeatures).Configuration.GetProperty("Marker").GetString());
        Assert.False(_store.Features[SecuredFeatureId].TryGetProperty("Marker", out _));
        Assert.Equal(SigningKeyValue, _store.Features[SecuredFeatureId].GetProperty("SigningKey").GetString());
    }

    [Fact]
    public async Task Preparation_refusal_stops_every_guard_and_mutation()
    {
        var preparer = new RefusingActivationContextPreparer();
        var guard = new CapturingActivationGuard();
        _guards.Add(guard);
        var service = new FeatureManagementService(_store,
            [new ContributingFeatureCatalogContributor("NewFeature")], _guards, preparer,
            _refresher, _reloader);
        var catalog = await service.GetCatalogAsync();

        var exception = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
                [new("NewFeature", true, Json("{}"))])));

        Assert.Equal(1, preparer.CallCount);
        Assert.Null(guard.Context);
        Assert.Equal("NewFeature", Assert.Single(exception.Refusals).Feature);
        Assert.False(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(0, _refresher.RefreshCount);
        Assert.Equal(0, _reloader.ReloadCount);
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_preparation()
    {
        var preparer = new ProjectingActivationContextPreparer();
        var service = new FeatureManagementService(_store, [], _guards, preparer,
            _refresher, _reloader);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ApplyAsync(new FeatureApplyRequest("", [])));

        Assert.Equal(0, preparer.CallCount);
        Assert.Equal(0, _refresher.RefreshCount);
        Assert.Equal(0, _reloader.ReloadCount);
    }

    /// <summary>
    /// FR-070: a host that composes no guard keeps today's ordering exactly — shells.json is written first,
    /// and the shell's own Validate-policy check is what refuses later, after the save.
    /// </summary>
    [Fact]
    public async Task ApplyWithNoGuardComposedSavesAsBefore()
    {
        var service = CreateService(new ContributingFeatureCatalogContributor("NewFeature"));
        var catalog = await service.GetCatalogAsync();

        await service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [new("NewFeature", true, Json("{}"))]));

        Assert.Empty(_guards);
        Assert.True(_store.Features.ContainsKey("NewFeature"));
        Assert.Equal(1, _refresher.RefreshCount);
        Assert.Equal(1, _reloader.ReloadCount);
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
        new(_store, contributors, _guards, new LegacyFeatureActivationContextPreparer(), _refresher, _reloader);

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class ContributingFeatureCatalogContributor(string featureId, params FeatureSettingDescriptor[] settings) : IFeatureCatalogContributor
    {
        public FeatureSettingDescriptor[] Settings { get; set; } = settings;

        public Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
        {
            var feature = context.GetOrAdd(featureId);
            feature.DisplayName = featureId;
            feature.SourceKind = FeatureSourceKinds.Runtime;
            feature.Settings = Settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RefusingActivationGuard(string feature, string reason) : IFeatureActivationGuard
    {
        public Task<FeatureActivationDecision> EvaluateAsync(FeatureActivationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(FeatureActivationDecision.Refused(new FeatureActivationRefusal(feature, reason)));
    }

    private sealed class ThrowingActivationGuard : IFeatureActivationGuard
    {
        public Task<FeatureActivationDecision> EvaluateAsync(FeatureActivationContext context, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("the guard could not decide");
    }

    private sealed class CapturingActivationGuard : IFeatureActivationGuard
    {
        public FeatureActivationContext? Context { get; private set; }

        public Task<FeatureActivationDecision> EvaluateAsync(FeatureActivationContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(FeatureActivationDecision.Allowed);
        }
    }

    private sealed class ProjectingActivationContextPreparer : IFeatureActivationContextPreparer
    {
        public int CallCount { get; private set; }
        public FeatureActivationContext? Seen { get; private set; }

        public Task<FeatureActivationContext> PrepareAsync(
            FeatureActivationContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Seen = context;
            var feature = Assert.Single(context.Request.Features);
            var projected = context.Request with
            {
                Features = [feature with { Configuration = Json("""{"Marker":"prepared"}""") }]
            };
            return Task.FromResult(new FeatureActivationContext(context.Shell, projected));
        }
    }

    private sealed class RefusingActivationContextPreparer : IFeatureActivationContextPreparer
    {
        public int CallCount { get; private set; }

        public Task<FeatureActivationContext> PrepareAsync(
            FeatureActivationContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new FeatureActivationRefusedException(
                [new FeatureActivationRefusal("NewFeature", "[resource-managed-configuration] test refusal")]);
        }
    }
}
