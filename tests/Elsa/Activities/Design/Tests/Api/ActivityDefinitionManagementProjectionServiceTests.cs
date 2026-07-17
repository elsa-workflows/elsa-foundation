using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Tests.Fixtures;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class ActivityDefinitionManagementProjectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Definition_pages_are_bounded_snapshot_bound_and_exclude_other_tenants()
    {
        var stores = new InMemoryReusableActivityStores();
        for (var index = 0; index < 105; index++)
            await SeedDefinitionAsync(stores, $"a-{index:D3}", "tenant-a");
        for (var index = 0; index < 17; index++)
            await SeedDefinitionAsync(stores, $"hidden-{index:D3}", "tenant-b");
        var service = Service(stores, new("tenant-a", "tenant-a/manage", true));

        var items = new List<ReusableActivityDefinitionManagementView>();
        string? cursor = null;
        string? snapshotId = null;
        DateTimeOffset? asOf = null;
        do
        {
            var page = await service.ListDefinitionsAsync(new(40, cursor), default);
            Assert.InRange(page.Count, 1, 40);
            Assert.Equal(105, page.TotalCount);
            snapshotId ??= page.Snapshot.SnapshotId;
            asOf ??= page.Snapshot.AsOf;
            Assert.Equal(snapshotId, page.Snapshot.SnapshotId);
            Assert.Equal(asOf, page.Snapshot.AsOf);
            Assert.All(page.Items, item => Assert.Equal("tenant-a", item.Definition.TenantId));
            items.AddRange(page.Items);
            cursor = page.Continuation;
            Assert.Equal(cursor is not null, page.HasMore);
        } while (cursor is not null);

        Assert.Equal(105, items.Count);
        Assert.Equal(105, items.Select(x => x.Definition.DefinitionId).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(items, x => x.Definition.DefinitionId.StartsWith("hidden-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Continuations_are_bound_to_limit_filters_tenant_and_authorization_profile()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores, "a-1", "tenant-a");
        await SeedDefinitionAsync(stores, "a-2", "tenant-a");
        var service = Service(stores, new("tenant-a", "tenant-a/manage", true));
        var first = await service.ListDefinitionsAsync(new(1, Search: "Definition", Authority: "Design"), default);
        Assert.NotNull(first.Continuation);

        var changedLimit = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.ListDefinitionsAsync(new(2, first.Continuation, "Definition", "Design"), default));
        var changedFilter = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.ListDefinitionsAsync(new(1, first.Continuation, "different", "Design"), default));
        var changedAuthorization = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            Service(stores, new("tenant-a", "tenant-a/read", false))
                .ListDefinitionsAsync(new(1, first.Continuation, "Definition", "Design"), default));
        var changedTenant = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            Service(stores, new("tenant-b", "tenant-a/manage", true))
                .ListDefinitionsAsync(new(1, first.Continuation, "Definition", "Design"), default));
        var tamperedCursor = first.Continuation![..^1] + (first.Continuation[^1] == 'a' ? 'b' : 'a');
        var tampered = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.ListDefinitionsAsync(new(1, tamperedCursor, "Definition", "Design"), default));

        Assert.All([changedLimit, changedFilter, changedAuthorization, changedTenant, tampered], exception =>
        {
            Assert.Equal(400, exception.StatusCode);
            Assert.Equal("activity.management.cursor-invalid", exception.ErrorCode);
            Assert.Equal("restart-without-cursor", exception.Recovery?.Instruction);
        });
    }

    [Fact]
    public async Task Hidden_details_are_indistinguishable_and_actions_are_explicitly_authorized()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores, "visible", "tenant-a");
        await SeedDefinitionAsync(stores, "hidden", "tenant-b");
        SeedCrossTenantVersion(stores);
        var manager = Service(stores, new("tenant-a", "tenant-a/manage", true));
        var reader = Service(stores, new("tenant-a", "tenant-a/read", false));

        var managed = await manager.GetDefinitionAsync("definition-visible", default);
        var readOnly = await reader.GetDefinitionAsync("definition-visible", default);
        var hidden = await Assert.ThrowsAsync<ActivityAuthoringException>(() => manager.GetDefinitionAsync("definition-hidden", default));
        var absent = await Assert.ThrowsAsync<ActivityAuthoringException>(() => manager.GetDefinitionAsync("missing", default));

        Assert.True(Assert.Single(managed.Actions, x => x.Action == "edit-definition").Allowed);
        Assert.True(Assert.Single(managed.Actions, x => x.Action == "create-draft").Allowed);
        Assert.True(Assert.Single(managed.Actions, x => x.Action == "set-recommendation").Allowed);
        var managedFork = Assert.Single(managed.Actions, x => x.Action == "fork-definition");
        Assert.False(managedFork.Allowed);
        Assert.Equal("activity.definition.design-owned", managedFork.UnavailableCode);
        Assert.Equal(0, managed.Lifecycle.VersionCount);
        Assert.Equal(0, (await manager.ListVersionsAsync(new("definition-visible"), default)).TotalCount);
        Assert.All(readOnly.Actions, action =>
        {
            Assert.False(action.Allowed);
            Assert.Equal("activity.action.forbidden", action.UnavailableCode);
        });
        Assert.Equal(404, hidden.StatusCode);
        Assert.Equal(absent.ErrorCode, hidden.ErrorCode);
        Assert.Equal(absent.Title, hidden.Title);
        Assert.Equal(absent.Message, hidden.Message);
    }

    [Fact]
    public async Task Actions_apply_each_commands_actual_authority_and_provider_guards()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores, "design", "tenant-a");
        await SeedDefinitionAsync(stores, "source", "tenant-a", ActivityContentAuthorityKind.ProviderSource);
        var service = Service(stores, new("tenant-a", "tenant-a/manage-no-provider", true, false));

        var draft = Assert.Single((await service.ListDraftsAsync(new("definition-design"), default)).Items);
        Assert.False(Assert.Single(draft.Actions, x => x.Action == "edit-draft").Allowed);
        Assert.True(Assert.Single(draft.Actions, x => x.Action == "edit-draft-label").Allowed);
        Assert.True(Assert.Single(draft.Actions, x => x.Action == "discard-draft").Allowed);
        Assert.True(Assert.Single(draft.Actions, x => x.Action == "create-conflict-copy").Allowed);

        var source = await service.GetDefinitionAsync("definition-source", default);
        Assert.False(Assert.Single(source.Actions, x => x.Action == "edit-definition").Allowed);
        Assert.True(Assert.Single(source.Actions, x => x.Action == "set-recommendation").Allowed);
        Assert.True(Assert.Single(source.Actions, x => x.Action == "fork-definition").Allowed);
    }

    private static ActivityDefinitionManagementProjectionService Service(
        InMemoryReusableActivityStores stores,
        Context context) => new(
        stores,
        context,
        new Clock(),
        new HmacActivityManagementCursorCodec(Options.Create(new ActivityDependencyCursorOptions
        {
            SigningKey = "activity-management-tests-signing-key"
        })));

    private static async Task SeedDefinitionAsync(
        InMemoryReusableActivityStores stores,
        string suffix,
        string? tenantId,
        ActivityContentAuthorityKind authority = ActivityContentAuthorityKind.Design)
    {
        var definitionId = $"definition-{suffix}";
        var draftId = $"draft-{suffix}";
        var definition = new ActivityDefinition
        {
            Id = definitionId,
            TenantId = tenantId,
            ActivityTypeKey = $"test.{suffix}",
            Category = "Tests",
            DisplayName = $"Definition {suffix}",
            CreatedAt = Now,
            LastModifiedAt = Now
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{suffix}",
            TenantId = tenantId,
            DefinitionId = definitionId,
            ContentAuthority = new(
                authority,
                authority == ActivityContentAuthorityKind.Design ? WellKnownActivityContentAuthorities.Design : "source.provider"),
            CreatedAt = Now,
            LastModifiedAt = Now
        };
        var draft = new ActivityDefinitionDraft
        {
            Id = draftId,
            TenantId = tenantId,
            DefinitionId = definitionId,
            Revision = 1,
            Status = ActivityDefinitionDraftStatus.Active,
            State = new(
                new("1", [], [], []),
                new("test.provider", "1", Json("{}")),
                new Dictionary<string, string>()),
            CreatedAt = Now,
            LastModifiedAt = Now
        };
        var layout = new ActivityDefinitionDraftLayout
        {
            Id = $"layout-{suffix}",
            TenantId = tenantId,
            DraftId = draftId,
            Revision = 1,
            CreatedAt = Now,
            LastModifiedAt = Now
        };
        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(definition, authoring, draft, layout));
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static void SeedCrossTenantVersion(InMemoryReusableActivityStores stores)
    {
        stores.SeedPublication(new()
        {
            Id = "publication-cross-tenant",
            TenantId = "tenant-b",
            DefinitionVersionId = "version-cross-tenant",
            DefinitionId = "definition-visible",
            Version = "1.0.0",
            ActivityTypeKey = "test.visible",
            Contract = new("1", [], [], []),
            Provider = new("test.provider", "1", Json("{}")),
            TemplateId = "template-cross-tenant",
            TemplateHash = "sha256:cross-tenant",
            SourceReferenceId = "source-cross-tenant",
            ProviderFingerprint = "provider/1",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 1,
            RuntimeRequirements = [],
            PublishedAt = Now,
            CreatedAt = Now,
            LastModifiedAt = Now
        }, new()
        {
            Id = "layout-cross-tenant",
            TenantId = "tenant-b",
            DefinitionVersionId = "version-cross-tenant",
            CreatedAt = Now,
            LastModifiedAt = Now
        });
    }

    private sealed class Context(
        string? tenantId,
        string authorizationProfile,
        bool canManage,
        bool canAuthorProvider = true) : IActivityAuthoringContext
    {
        public string? TenantId => tenantId;
        public string AuthorizationProfile => authorizationProfile;
        public bool CanManageActivityDefinitions => canManage;
        public bool CanAuthorProvider(string providerKey) => canAuthorProvider;
        public bool CanReadProviderPayload(string providerKey) => true;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now.AddMinutes(1);
    }
}
