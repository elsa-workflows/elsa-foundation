using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class ActivityDefinitionManagementProjectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Definition_pages_are_bounded_snapshot_bound_and_exclude_other_tenants()
    {
        var store = new ProjectionStore();
        for (var index = 0; index < 105; index++)
            store.SeedDefinition($"a-{index:D3}", "tenant-a");
        for (var index = 0; index < 17; index++)
            store.SeedDefinition($"hidden-{index:D3}", "tenant-b");
        var service = Service(store, new("tenant-a", "tenant-a/manage", true));

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
        Assert.All(store.DefinitionQueries, query => Assert.Equal(1, query.SnapshotSequence));
    }

    [Fact]
    public async Task Continuations_transport_the_original_sequence_while_a_fresh_traversal_sees_new_state()
    {
        var store = new ProjectionStore();
        store.SeedDefinition("a", "tenant-a");
        store.SeedDefinition("c", "tenant-a");
        var service = Service(store, new("tenant-a", "tenant-a/manage", true));

        var first = await service.ListDefinitionsAsync(new(1), default);
        store.AdvanceWithDefinition("b", "tenant-a");
        var second = await service.ListDefinitionsAsync(new(1, first.Continuation), default);
        var fresh = await service.ListDefinitionsAsync(new(10), default);

        Assert.Equal("definition-a", Assert.Single(first.Items).Definition.DefinitionId);
        Assert.Equal("definition-c", Assert.Single(second.Items).Definition.DefinitionId);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(3, fresh.TotalCount);
        Assert.Equal(1, store.DefinitionQueries[1].SnapshotSequence);
        Assert.Equal(2, store.DefinitionQueries[2].SnapshotSequence);
    }

    [Fact]
    public async Task Continuations_are_bound_to_limit_filters_tenant_and_authorization_profile()
    {
        var store = new ProjectionStore();
        store.SeedDefinition("a-1", "tenant-a");
        store.SeedDefinition("a-2", "tenant-a");
        var service = Service(store, new("tenant-a", "tenant-a/manage", true));
        var first = await service.ListDefinitionsAsync(new(1, Search: "Definition", Authority: "Design"), default);
        Assert.NotNull(first.Continuation);

        var changedLimit = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.ListDefinitionsAsync(new(2, first.Continuation, "Definition", "Design"), default));
        var changedFilter = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.ListDefinitionsAsync(new(1, first.Continuation, "different", "Design"), default));
        var changedAuthorization = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            Service(store, new("tenant-a", "tenant-a/read", false))
                .ListDefinitionsAsync(new(1, first.Continuation, "Definition", "Design"), default));
        var changedTenant = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            Service(store, new("tenant-b", "tenant-a/manage", true))
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
    public async Task A_valid_signed_cursor_for_a_pruned_snapshot_returns_safe_expiry()
    {
        var store = new ProjectionStore();
        store.SeedDefinition("a", "tenant-a");
        store.SeedDefinition("b", "tenant-a");
        var service = Service(store, new("tenant-a", "tenant-a/manage", true));
        var first = await service.ListDefinitionsAsync(new(1), default);
        store.AdvanceWithDefinition("c", "tenant-a");
        store.ExpireBefore(2);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.ListDefinitionsAsync(new(1, first.Continuation), default));

        Assert.Equal(410, exception.StatusCode);
        Assert.Equal("activity.cursor.expired", exception.ErrorCode);
        Assert.Equal("restart-without-cursor", exception.Recovery?.Instruction);
        Assert.DoesNotContain("1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hidden_details_are_indistinguishable_and_actions_are_explicitly_authorized()
    {
        var store = new ProjectionStore();
        store.SeedDefinition("visible", "tenant-a");
        store.SeedDefinition("hidden", "tenant-b");
        var manager = Service(store, new("tenant-a", "tenant-a/manage", true));
        var reader = Service(store, new("tenant-a", "tenant-a/read", false));

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
    public async Task Definition_display_name_falls_back_to_the_activity_type_key()
    {
        var store = new ProjectionStore();
        store.SeedDefinition("source", "tenant-a", ActivityContentAuthorityKind.ProviderSource, omitDisplayName: true);

        var view = await Service(store, new("tenant-a", "tenant-a/read", false))
            .GetDefinitionAsync("definition-source", default);

        Assert.Equal("test.source", view.Definition.DisplayName);
    }

    [Fact]
    public async Task Actions_apply_each_commands_actual_authority_and_provider_guards()
    {
        var store = new ProjectionStore();
        store.SeedDefinition("design", "tenant-a");
        store.SeedDefinition("source", "tenant-a", ActivityContentAuthorityKind.ProviderSource);
        store.SeedVersion("design", "1.0.0", ActivityDefinitionVersionLifecycle.Active);
        var service = Service(store, new("tenant-a", "tenant-a/manage-no-provider", true, false));

        var draft = Assert.Single((await service.ListDraftsAsync(new("definition-design"), default)).Items);
        Assert.False(Assert.Single(draft.Actions, x => x.Action == "edit-draft").Allowed);
        Assert.True(Assert.Single(draft.Actions, x => x.Action == "edit-draft-label").Allowed);
        Assert.True(Assert.Single(draft.Actions, x => x.Action == "discard-draft").Allowed);
        Assert.True(Assert.Single(draft.Actions, x => x.Action == "create-conflict-copy").Allowed);

        var version = Assert.Single((await service.ListVersionsAsync(new("definition-design"), default)).Items);
        Assert.False(Assert.Single(version.Actions, x => x.Action == "clone-draft").Allowed);
        Assert.True(Assert.Single(version.Actions, x => x.Action == "retire-version").Allowed);

        var source = await service.GetDefinitionAsync("definition-source", default);
        Assert.False(Assert.Single(source.Actions, x => x.Action == "edit-definition").Allowed);
        Assert.True(Assert.Single(source.Actions, x => x.Action == "set-recommendation").Allowed);
        Assert.True(Assert.Single(source.Actions, x => x.Action == "fork-definition").Allowed);
    }

    private static ActivityDefinitionManagementProjectionService Service(ProjectionStore store, Context context) => new(
        store,
        context,
        new HmacActivityManagementCursorCodec(Options.Create(new ActivityDependencyCursorOptions
        {
            SigningKey = "activity-management-tests-signing-key"
        })));

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

    private sealed class ProjectionStore : IActivityDefinitionManagementProjectionStore
    {
        private readonly SortedDictionary<long, SnapshotData> _snapshots = new()
        {
            [1] = new()
        };
        private long _sequence = 1;
        private long _retainedFrom = 1;

        public List<ActivityManagementProjectionPageQuery> DefinitionQueries { get; } = [];

        public void SeedDefinition(
            string suffix,
            string? tenantId,
            ActivityContentAuthorityKind authority = ActivityContentAuthorityKind.Design,
            bool omitDisplayName = false)
        {
            var definitionId = $"definition-{suffix}";
            Current.Definitions.Add(Definition(definitionId, suffix, tenantId, authority, _sequence, omitDisplayName: omitDisplayName));
            Current.Drafts.Add(Draft($"draft-{suffix}", definitionId, tenantId, _sequence));
        }

        public void SeedVersion(string definitionSuffix, string version, ActivityDefinitionVersionLifecycle lifecycle)
        {
            var definitionId = $"definition-{definitionSuffix}";
            var versionId = $"version-{definitionSuffix}-{version}";
            Current.Versions.Add(Version(versionId, definitionId, version, lifecycle, _sequence));
            var definition = Current.Definitions.Single(x => x.DefinitionId == definitionId);
            Current.Definitions.Remove(definition);
            Current.Definitions.Add(Definition(
                definitionId,
                definitionSuffix,
                definition.TenantId,
                definition.ContentAuthority.Kind,
                _sequence,
                versionId,
                versionId,
                1));
        }

        public void AdvanceWithDefinition(string suffix, string? tenantId)
        {
            var next = Current.Clone();
            _sequence++;
            _snapshots[_sequence] = next;
            SeedDefinition(suffix, tenantId);
        }

        public void ExpireBefore(long sequence) => _retainedFrom = sequence;

        public Task<ActivityManagementSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot(_sequence));

        public Task<ActivityManagementProjectionPage<ActivityDefinitionManagementProjectionRevision>> ReadDefinitionsAsync(
            ActivityManagementProjectionPageQuery query,
            CancellationToken cancellationToken = default)
        {
            DefinitionQueries.Add(query);
            var (data, snapshot) = Resolve(query.SnapshotSequence);
            var matches = data.Definitions
                .Where(x => Visible(x.TenantId, query.TenantId))
                .Where(x => query.Authority is null || x.ContentAuthority.Kind == query.Authority)
                .Where(x => query.ProviderKey is null || x.HeadProviderKey == query.ProviderKey || x.RecommendationProviderKey == query.ProviderKey)
                .Where(x => Matches(x.SearchText, query.Search))
                .OrderBy(x => x.SortKey, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(Page(matches, query, snapshot));
        }

        public Task<ActivityDefinitionManagementProjectionRevision?> FindDefinitionAsync(
            string definitionId,
            string? tenantId,
            long? snapshotSequence = null,
            CancellationToken cancellationToken = default)
        {
            var (data, _) = Resolve(snapshotSequence);
            return Task.FromResult(data.Definitions.SingleOrDefault(x =>
                x.DefinitionId == definitionId && Visible(x.TenantId, tenantId)));
        }

        public Task<ActivityManagementProjectionPage<ActivityDefinitionDraftManagementProjectionRevision>> ReadDraftsAsync(
            string definitionId,
            ActivityManagementProjectionPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var (data, snapshot) = Resolve(query.SnapshotSequence);
            var matches = data.Drafts
                .Where(x => x.DefinitionId == definitionId && Visible(x.TenantId, query.TenantId))
                .Where(x => query.DraftStatus is null || x.Status == query.DraftStatus)
                .Where(x => query.ProviderKey is null || x.ProviderKey == query.ProviderKey)
                .Where(x => Matches(x.SearchText, query.Search))
                .OrderBy(x => x.SortKey, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(Page(matches, query, snapshot));
        }

        public Task<ActivityManagementProjectionPage<ActivityDefinitionVersionManagementProjectionRevision>> ReadVersionsAsync(
            string definitionId,
            ActivityManagementProjectionPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var (data, snapshot) = Resolve(query.SnapshotSequence);
            var matches = data.Versions
                .Where(x => x.DefinitionId == definitionId && Visible(x.TenantId, query.TenantId))
                .Where(x => query.VersionLifecycle is null || x.Lifecycle == query.VersionLifecycle)
                .Where(x => query.ProviderKey is null || x.ProviderKey == query.ProviderKey)
                .Where(x => Matches(x.SearchText, query.Search))
                .OrderBy(x => x.SortKey, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(Page(matches, query, snapshot));
        }

        private SnapshotData Current => _snapshots[_sequence];

        private (SnapshotData Data, ActivityManagementSnapshot Snapshot) Resolve(long? sequence)
        {
            var resolved = sequence ?? _sequence;
            if (resolved < _retainedFrom || resolved > _sequence || !_snapshots.TryGetValue(resolved, out var data))
                throw new ActivityManagementSnapshotExpiredException(resolved);
            return (data, Snapshot(resolved));
        }

        private static ActivityManagementProjectionPage<T> Page<T>(
            IReadOnlyList<T> matches,
            ActivityManagementProjectionPageQuery query,
            ActivityManagementSnapshot snapshot)
        {
            var items = matches.Skip(query.Offset).Take(query.Limit).ToArray();
            return new(
                items,
                query.Offset + items.Length < matches.Count ? query.Offset + items.Length : null,
                matches.Count,
                snapshot);
        }

        private static ActivityManagementSnapshot Snapshot(long sequence) => new(sequence, Now.AddMinutes(sequence));
        private static bool Visible(string? itemTenantId, string? tenantId) => itemTenantId is null || itemTenantId == tenantId;
        private static bool Matches(string value, string? search) => search is null || value.Contains(search, StringComparison.OrdinalIgnoreCase);

        private static ActivityDefinitionManagementProjectionRevision Definition(
            string definitionId,
            string suffix,
            string? tenantId,
            ActivityContentAuthorityKind authority,
            long sequence,
            string? headVersionId = null,
            string? recommendedVersionId = null,
            long versionCount = 0,
            bool omitDisplayName = false) => new()
        {
            Id = $"definition-revision-{definitionId}-{sequence}",
            TenantId = tenantId,
            ResourceId = definitionId,
            DefinitionId = definitionId,
            ValidFromSequence = sequence,
            ValidToSequenceExclusive = long.MaxValue,
            ValidFromKey = sequence.ToString("D20"),
            ValidToKey = long.MaxValue.ToString("D20"),
            VisibilityKey = tenantId ?? "global",
            SortKey = $"test.{suffix}",
            SearchText = $"definition {suffix} test.{suffix} tests",
            ActivityTypeKey = $"test.{suffix}",
            Category = "Tests",
            DisplayName = omitDisplayName ? null : $"Definition {suffix}",
            ContentAuthority = new(
                authority,
                authority == ActivityContentAuthorityKind.Design ? WellKnownActivityContentAuthorities.Design : "source.provider"),
            HeadVersionId = headVersionId,
            RecommendedVersionId = recommendedVersionId,
            HeadProviderKey = headVersionId is null ? null : "test.provider",
            RecommendationProviderKey = recommendedVersionId is null ? null : "test.provider",
            DraftCount = 1,
            VersionCount = versionCount,
            UpdatedAt = Now
        };

        private static ActivityDefinitionDraftManagementProjectionRevision Draft(
            string draftId,
            string definitionId,
            string? tenantId,
            long sequence) => new()
        {
            Id = $"draft-revision-{draftId}-{sequence}",
            TenantId = tenantId,
            ResourceId = draftId,
            DefinitionId = definitionId,
            DraftId = draftId,
            ValidFromSequence = sequence,
            ValidToSequenceExclusive = long.MaxValue,
            ValidFromKey = sequence.ToString("D20"),
            ValidToKey = long.MaxValue.ToString("D20"),
            VisibilityKey = tenantId ?? "global",
            SortKey = draftId,
            SearchText = draftId,
            Revision = 1,
            Status = ActivityDefinitionDraftStatus.Active,
            ProviderKey = "test.provider",
            ProviderSchemaVersion = "1",
            UpdatedAt = Now
        };

        private static ActivityDefinitionVersionManagementProjectionRevision Version(
            string versionId,
            string definitionId,
            string version,
            ActivityDefinitionVersionLifecycle lifecycle,
            long sequence) => new()
        {
            Id = $"version-revision-{versionId}-{sequence}",
            ResourceId = versionId,
            DefinitionId = definitionId,
            DefinitionVersionId = versionId,
            ValidFromSequence = sequence,
            ValidToSequenceExclusive = long.MaxValue,
            ValidFromKey = sequence.ToString("D20"),
            ValidToKey = long.MaxValue.ToString("D20"),
            VisibilityKey = "tenant-a",
            SortKey = versionId,
            SearchText = $"{versionId} {version}",
            TenantId = "tenant-a",
            Version = version,
            Lifecycle = lifecycle,
            ProviderKey = "test.provider",
            ProviderSchemaVersion = "1",
            PublishedAt = Now
        };

        private sealed class SnapshotData
        {
            public List<ActivityDefinitionManagementProjectionRevision> Definitions { get; } = [];
            public List<ActivityDefinitionDraftManagementProjectionRevision> Drafts { get; } = [];
            public List<ActivityDefinitionVersionManagementProjectionRevision> Versions { get; } = [];

            public SnapshotData Clone()
            {
                var clone = new SnapshotData();
                clone.Definitions.AddRange(Definitions);
                clone.Drafts.AddRange(Drafts);
                clone.Versions.AddRange(Versions);
                return clone;
            }
        }
    }
}
