using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityDefinitionRecommendationTests
{
    [Fact]
    public async Task Recommendation_moves_only_under_exact_head_recommendation_and_active_target_preconditions()
    {
        var stores = await StoresAsync();
        var service = new ActivityDefinitionRecommendationService(stores, stores, stores, new Context(), new Clock());

        var changed = await service.SetAsync(new(
            "definition-1",
            "version-2",
            "version-1",
            "version-2",
            ActivityDefinitionVersionLifecycle.Active,
            "Promote the reviewed version"), default);
        var stale = await Assert.ThrowsAsync<ActivityAuthoringException>(() => service.SetAsync(new(
            "definition-1",
            "version-2",
            "version-1",
            null,
            null,
            "Clear from a stale view"), default));

        Assert.Equal("version-2", changed.RecommendedVersionId);
        Assert.Equal("version-2", (await stores.FindAsync("definition-1"))!.RecommendedVersionId);
        Assert.Equal("activity.definition.stale-recommendation", stale.ErrorCode);
    }

    [Fact]
    public async Task Recommendation_never_accepts_a_retired_or_foreign_definition_version()
    {
        var stores = await StoresAsync(
            targetLifecycle: ActivityDefinitionVersionLifecycle.Retired,
            targetDefinitionId: "definition-2");
        var service = new ActivityDefinitionRecommendationService(stores, stores, stores, new Context(), new Clock());

        var error = await Assert.ThrowsAsync<ActivityAuthoringException>(() => service.SetAsync(new(
            "definition-1",
            "version-2",
            "version-1",
            "version-2",
            ActivityDefinitionVersionLifecycle.Retired,
            "Promote the target"), default));

        Assert.Equal("activity.version.not-found", error.ErrorCode);
        Assert.Equal("version-1", (await stores.FindAsync("definition-1"))!.RecommendedVersionId);
    }

    [Fact]
    public async Task Picker_returns_only_the_exact_recommended_active_version_and_never_substitutes_head()
    {
        var stores = await StoresAsync();
        var handler = new RecommendedActivityDefinitionReader(
            stores,
            new AllAvailable(),
            new EmptySettings(),
            new Context());

        var page = await handler.ListAsync(new(0, 25), default);

        var item = Assert.Single(page.Items);
        Assert.Equal("version-1", item.VersionId);
        Assert.NotEqual("version-2", item.VersionId);
        Assert.True(item.IsAvailable);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public async Task Picker_omits_a_recommendation_that_is_no_longer_active()
    {
        var stores = await StoresAsync(recommendedLifecycle: ActivityDefinitionVersionLifecycle.Retired);
        var handler = new RecommendedActivityDefinitionReader(
            stores,
            new AllAvailable(),
            new EmptySettings(),
            new Context());

        var page = await handler.ListAsync(new(0, 25), default);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Recommendation_does_not_disclose_a_foreign_tenant_definition()
    {
        var stores = await StoresAsync(definitionTenantId: "tenant-b");
        var service = new ActivityDefinitionRecommendationService(stores, stores, stores, new Context(), new Clock());

        var error = await Assert.ThrowsAsync<ActivityAuthoringException>(() => service.SetAsync(new(
            "definition-1",
            "version-2",
            "version-1",
            "version-2",
            ActivityDefinitionVersionLifecycle.Active,
            "Promote the target"), default));

        Assert.Equal("activity.tenant.reference-denied", error.ErrorCode);
        Assert.Equal("version-1", (await stores.FindAsync("definition-1"))!.RecommendedVersionId);
    }

    private static async Task<InMemoryReusableActivityStores> StoresAsync(
        ActivityDefinitionVersionLifecycle targetLifecycle = ActivityDefinitionVersionLifecycle.Active,
        string targetDefinitionId = "definition-1",
        ActivityDefinitionVersionLifecycle recommendedLifecycle = ActivityDefinitionVersionLifecycle.Active,
        string definitionTenantId = "tenant-a")
    {
        var stores = new InMemoryReusableActivityStores();
        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(
            new ActivityDefinition
            {
                Id = "definition-1",
                ActivityTypeKey = "acme.test",
                Category = "Tests",
                TenantId = definitionTenantId,
                CreatedAt = Now,
                LastModifiedAt = Now
            },
            new ActivityDefinitionAuthoringState
            {
                Id = "authoring-1",
                DefinitionId = "definition-1",
                TenantId = definitionTenantId,
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
                HeadVersionId = "version-2",
                RecommendedVersionId = "version-1",
                CreatedAt = Now,
                LastModifiedAt = Now
            },
            new ActivityDefinitionDraft
            {
                Id = "draft-1",
                DefinitionId = "definition-1",
                TenantId = definitionTenantId,
                State = new(Contract(), Provider(), new Dictionary<string, string>()),
                CreatedAt = Now,
                LastModifiedAt = Now
            },
            new ActivityDefinitionDraftLayout
            {
                Id = "layout-1",
                DraftId = "draft-1",
                TenantId = definitionTenantId,
                Records = [],
                CreatedAt = Now,
                LastModifiedAt = Now
            }));
        stores.SeedPublication(Publication("version-1", "definition-1", recommendedLifecycle, definitionTenantId), Layout("version-1", definitionTenantId));
        stores.SeedPublication(Publication("version-2", targetDefinitionId, targetLifecycle, definitionTenantId), Layout("version-2", definitionTenantId));
        return stores;
    }

    private static ActivityDefinitionVersionPublication Publication(
        string versionId,
        string definitionId,
        ActivityDefinitionVersionLifecycle lifecycle,
        string tenantId) => new()
    {
        Id = versionId,
        DefinitionVersionId = versionId,
        DefinitionId = definitionId,
        TenantId = tenantId,
        Version = versionId == "version-1" ? "1.0.0" : "2.0.0",
        ActivityTypeKey = "acme.test",
        Contract = Contract(),
        Provider = Provider(),
        TemplateId = $"template-{versionId}",
        TemplateHash = $"sha256:{versionId}",
        SourceReferenceId = $"source-{versionId}",
        ProviderFingerprint = "test/1",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 0,
        RuntimeRequirements = [],
        Lifecycle = lifecycle,
        PublishedAt = Now,
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    private static ActivityDefinitionVersionLayout Layout(string versionId, string tenantId) => new()
    {
        Id = $"layout-{versionId}",
        DefinitionVersionId = versionId,
        TenantId = tenantId,
        Records = [],
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    private static ActivityContract Contract() => new("1", [], [], []);
    private static ActivityProviderManifest Provider() => new("test.provider", "1", JsonSerializer.SerializeToElement(new { }));
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class Context : IActivityAuthoringContext, IActivityAuthoringContextAsync
    {
        public string? TenantId => "tenant-a";
        public string ActorId => "actor-a";
        public string AuthorizationProfile => "tenant-a/manage";
        public bool CanAuthorProvider(string providerKey) => true;
        public bool CanReadProviderPayload(string providerKey) => true;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationProfile);
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AllAvailable : IActivityAvailabilityEvaluator
    {
        public IReadOnlyCollection<IActivityDefinition> FilterAddable(
            IEnumerable<IActivityDefinition> activities,
            ActivityAvailabilitySettings? managementSettings = null) => activities.ToArray();
    }

    private sealed class EmptySettings : IActivityAvailabilitySettingsStore
    {
        public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityAvailabilitySettings?>(null);

        public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
