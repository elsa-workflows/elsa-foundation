using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityVersionLifecycleTests
{
    [Fact]
    public async Task Retire_restore_and_revoke_are_optimistic_and_revocation_is_terminal()
    {
        var stores = await StoresAsync();
        var service = new ActivityVersionLifecycleService(stores, stores, stores, new Context(), new Clock());

        var retired = await service.RetireAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Superseded"), default);
        var stale = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Restore"), default));
        var restored = await service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Retired, "Restore"), default);
        var revoked = await service.RevokeAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Unsafe"), default);
        var terminal = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Revoked, "Restore"), default));

        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired, retired.Lifecycle);
        Assert.Equal("activity.version.stale-lifecycle", stale.ErrorCode);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Active, restored.Lifecycle);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Revoked, revoked.Lifecycle);
        Assert.Equal("activity.version.lifecycle-conflict", terminal.ErrorCode);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Revoked,
            (await ((IActivityDefinitionVersionPublicationStore)stores).FindAsync("version-1"))!.Lifecycle);
    }

    [Fact]
    public async Task Retiring_the_recommended_version_requires_an_atomic_clear_or_active_replacement()
    {
        var stores = await StoresAsync("version-1", includeReplacement: true);
        var service = new ActivityVersionLifecycleService(stores, stores, stores, new Context(), new Clock());

        var missingDecision = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.RetireAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Superseded"), default));
        var retired = await service.RetireAsync(new(
            "version-1",
            ActivityDefinitionVersionLifecycle.Active,
            "Superseded",
            new(
                "version-2",
                "version-1",
                ActivityRecommendationDisposition.Replace,
                "version-2",
                ActivityDefinitionVersionLifecycle.Active)), default);

        Assert.Equal("activity.definition.recommendation-required", missingDecision.ErrorCode);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired, retired.Lifecycle);
        Assert.Equal("version-2", (await stores.FindAsync("definition-1"))!.RecommendedVersionId);
    }

    [Fact]
    public async Task Clearing_on_retirement_is_explicit_and_restore_does_not_recommend_the_version()
    {
        var stores = await StoresAsync("version-1");
        var service = new ActivityVersionLifecycleService(stores, stores, stores, new Context(), new Clock());

        await service.RetireAsync(new(
            "version-1",
            ActivityDefinitionVersionLifecycle.Active,
            "No replacement",
            new("version-1", "version-1", ActivityRecommendationDisposition.Clear)), default);
        await service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Retired, "Restored for exact use"), default);

        Assert.Null((await stores.FindAsync("definition-1"))!.RecommendedVersionId);
    }

    [Fact]
    public async Task Revoking_the_recommended_version_also_requires_an_explicit_decision()
    {
        var stores = await StoresAsync("version-1");
        var service = new ActivityVersionLifecycleService(stores, stores, stores, new Context(), new Clock());

        var missingDecision = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.RevokeAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Unsafe"), default));
        var revoked = await service.RevokeAsync(new(
            "version-1",
            ActivityDefinitionVersionLifecycle.Active,
            "Unsafe",
            new("version-1", "version-1", ActivityRecommendationDisposition.Clear)), default);

        Assert.Equal("activity.definition.recommendation-required", missingDecision.ErrorCode);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Revoked, revoked.Lifecycle);
        Assert.Null((await stores.FindAsync("definition-1"))!.RecommendedVersionId);
    }

    [Fact]
    public void Selection_policy_blocks_retired_direct_selection_but_preserves_closed_parent_dependencies()
    {
        var policy = new DefaultActivityVersionSelectionPolicy();

        Assert.False(policy.CanSelectDirectly(ActivityDefinitionVersionLifecycle.Retired));
        Assert.True(policy.CanUseClosedDependency(ActivityDefinitionVersionLifecycle.Retired));
        Assert.False(policy.CanUseClosedDependency(ActivityDefinitionVersionLifecycle.Revoked));
    }

    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static async Task<InMemoryReusableActivityStores> StoresAsync(
        string? recommendedVersionId = null,
        bool includeReplacement = false)
    {
        var stores = new InMemoryReusableActivityStores();
        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(
            new ActivityDefinition
            {
                Id = "definition-1",
                ActivityTypeKey = "acme.test",
                Category = "Tests",
                TenantId = "tenant-a",
                CreatedAt = Now,
                LastModifiedAt = Now
            },
            new ActivityDefinitionAuthoringState
            {
                Id = "authoring-1",
                DefinitionId = "definition-1",
                TenantId = "tenant-a",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
                HeadVersionId = includeReplacement ? "version-2" : "version-1",
                RecommendedVersionId = recommendedVersionId,
                CreatedAt = Now,
                LastModifiedAt = Now
            },
            new ActivityDefinitionDraft
            {
                Id = "draft-1",
                DefinitionId = "definition-1",
                TenantId = "tenant-a",
                State = new(new("1", [], [], []), new("test.provider", "1", JsonSerializer.SerializeToElement(new { })), new Dictionary<string, string>()),
                CreatedAt = Now,
                LastModifiedAt = Now
            },
            new ActivityDefinitionDraftLayout
            {
                Id = "layout-draft-1",
                DraftId = "draft-1",
                TenantId = "tenant-a",
                Records = [],
                CreatedAt = Now,
                LastModifiedAt = Now
            }));
        stores.SeedPublication(Publication(), Layout("version-1"));
        if (includeReplacement)
            stores.SeedPublication(Publication("version-2", "2.0.0"), Layout("version-2"));
        return stores;
    }

    private static ActivityDefinitionVersionLayout Layout(string versionId) => new()
    {
        Id = $"layout-{versionId}",
        DefinitionVersionId = versionId,
        TenantId = "tenant-a",
        Records = [],
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    private static ActivityDefinitionVersionPublication Publication(string versionId = "version-1", string version = "1.0.0") => new()
    {
        Id = $"publication-{versionId}",
        DefinitionVersionId = versionId,
        DefinitionId = "definition-1",
        TenantId = "tenant-a",
        Version = version,
        ActivityTypeKey = "acme.test",
        Contract = new("1", [], [], [new("done", "Done", true)]),
        Provider = new("test.provider", "1", JsonSerializer.SerializeToElement(new { })),
        TemplateId = $"template-{versionId}",
        TemplateHash = $"sha256:{versionId}",
        SourceReferenceId = $"source-ref-{versionId}",
        ProviderFingerprint = "test/1",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 0,
        RuntimeRequirements = [],
        Lifecycle = ActivityDefinitionVersionLifecycle.Active,
        PublishedAt = Now,
        CreatedAt = Now,
        LastModifiedAt = Now
    };

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
}
