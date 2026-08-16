using System.Text.Json;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityDependencyQueryTests
{
    [Fact]
    public async Task Direct_outbound_reads_are_authoritative_ordered_and_lifecycle_safe()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "root", tenantId: "tenant-a");
        SeedVersion(stores, "retired", tenantId: null, lifecycle: ActivityDefinitionVersionLifecycle.Retired);
        SeedVersion(stores, "revoked", tenantId: "tenant-a", lifecycle: ActivityDefinitionVersionLifecycle.Revoked);
        SeedEdge(stores, "root", "retired", "z");
        SeedEdge(stores, "root", "revoked", "a");
        var reader = Reader(stores);

        var page = await reader.ReadAsync("root", "outbound", false, "versions", null, 10, default);

        Assert.Equal("AuthoritativeDirect", page.Consistency.Kind);
        Assert.True(page.Consistency.IsAuthoritative);
        Assert.Equal(["a", "z"], page.Items.Select(x => x.Occurrence.OccurrenceId));
        Assert.Equal("Revoked", page.Items[0].Dependency.Lifecycle);
        Assert.Equal("Retired", page.Items[1].Dependency.Lifecycle);
        Assert.All(page.Items, x => Assert.True(x.IsDirect));
    }

    [Fact]
    public async Task Derived_cursor_is_signed_and_bound_to_root_query_tenant_and_authorization_profile()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "root", tenantId: null);
        SeedVersion(stores, "owner-a", tenantId: null);
        SeedVersion(stores, "owner-b", tenantId: null);
        SeedVersion(stores, "other-root", tenantId: null);
        SeedEdge(stores, "owner-a", "root", "a");
        SeedEdge(stores, "owner-b", "root", "b");
        var first = await Reader(stores).ReadAsync("root", "inbound", false, "versions", null, 1, default);
        var cursor = Assert.IsType<string>(first.NextCursor);

        var second = await Reader(stores).ReadAsync("root", "inbound", false, "versions", cursor, 1, default);
        Assert.Equal("b", Assert.Single(second.Items).Occurrence.OccurrenceId);
        Assert.Equal(first.Consistency.AsOfSequence, second.Consistency.AsOfSequence);

        var tampered = (cursor[0] == 'A' ? "B" : "A") + cursor[1..];
        await AssertCursorErrorAsync("activity.cursor.binding-mismatch", () =>
            Reader(stores).ReadAsync("root", "inbound", false, "versions", tampered, 1, default));
        await AssertCursorErrorAsync("activity.cursor.binding-mismatch", () =>
            Reader(stores).ReadAsync("root", "inbound", true, "versions", cursor, 1, default));
        await AssertCursorErrorAsync("activity.cursor.binding-mismatch", () =>
            Reader(stores).ReadAsync("root", "inbound", false, "versions,drafts", cursor, 1, default));
        await AssertCursorErrorAsync("activity.cursor.binding-mismatch", () =>
            Reader(stores).ReadAsync("other-root", "inbound", false, "versions", cursor, 1, default));
        await AssertCursorErrorAsync("activity.cursor.binding-mismatch", () =>
            Reader(stores, profile: "profile-b").ReadAsync("root", "inbound", false, "versions", cursor, 1, default));
        await AssertCursorErrorAsync("activity.cursor.binding-mismatch", () =>
            Reader(stores, tenantId: "tenant-b").ReadAsync("root", "inbound", false, "versions", cursor, 1, default));
    }

    [Fact]
    public async Task Derived_cursor_expires_when_its_projection_watermark_is_unavailable()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "root");
        SeedVersion(stores, "owner-a");
        SeedVersion(stores, "owner-b");
        SeedEdge(stores, "owner-a", "root", "a");
        SeedEdge(stores, "owner-b", "root", "b");
        var first = await Reader(stores).ReadAsync("root", "inbound", false, "versions", null, 1, default);
        var cursor = Assert.IsType<string>(first.NextCursor);
        SeedVersion(stores, "projection-changed");

        await AssertCursorErrorAsync("activity.cursor.expired", () =>
            Reader(stores).ReadAsync("root", "inbound", false, "versions", cursor, 1, default));
    }

    [Fact]
    public async Task Projection_is_cycle_safe_and_returns_complete_exact_paths()
    {
        var stores = new InMemoryReusableActivityStores();
        foreach (var id in new[] { "root", "one", "two" }) SeedVersion(stores, id);
        SeedEdge(stores, "one", "root", "one-root");
        SeedEdge(stores, "two", "one", "two-one");
        SeedEdge(stores, "root", "two", "root-two");

        var page = await Reader(stores).ReadAsync("root", "inbound", true, "versions", null, 10, default);

        Assert.Equal("DerivedProjection", page.Consistency.Kind);
        Assert.False(page.Consistency.IsAuthoritative);
        Assert.Equal(3, page.Items.Count);
        var cycle = Assert.Single(page.Items, x => x.Depth == 3);
        Assert.Equal(["root", "one", "two", "root"], cycle.Path.Select(x => x.VersionId));
    }

    [Fact]
    public async Task Deep_projection_traversal_is_iterative_and_materializes_only_the_bounded_page()
    {
        const int depth = 10_000;
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "v0");
        for (var i = 1; i <= depth; i++)
        {
            SeedVersion(stores, $"v{i}");
            SeedEdge(stores, $"v{i - 1}", $"v{i}", $"node-{i:D5}");
        }

        var slice = await stores.ReadAsync(new(
            "v0",
            new(ActivityDependencyDirection.Outbound, true, new HashSet<string> { "Versions" }),
            "tenant-a",
            null,
            0,
            5));

        Assert.Equal(5, slice.Items.Count);
        Assert.Equal(5, slice.Items[^1].Depth);
        Assert.Equal(6, slice.Items[^1].Path.Count);
        Assert.Equal(5, slice.NextOffset);
    }

    [Fact]
    public async Task Mixed_activity_and_workflow_projection_owners_honor_include_filter()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "root", tenantId: null);
        var root = Reference("ActivityVersion", "root", versionId: "root");
        var dependency = root;
        ActivityDependencyItem[] items =
        [
            Item("activity-version", Reference("ActivityVersion", "av", versionId: "av"), dependency),
            Item("activity-draft", Reference("ActivityDraft", "ad", draftId: "ad", revision: 2), dependency),
            Item("workflow-version", Reference("WorkflowVersion", "wv", versionId: "wv"), dependency),
            Item("workflow-draft", Reference("WorkflowDraft", "wd", draftId: "wd", revision: 3), dependency)
        ];
        var projection = new MixedProjectionStore(root, items);

        var drafts = await Reader(stores, projection: projection).ReadAsync("root", "inbound", true, "drafts", null, 10, default);
        var versions = await Reader(stores, projection: projection).ReadAsync("root", "inbound", true, "versions", null, 10, default);

        Assert.Equal(["ActivityDraft", "WorkflowDraft"], drafts.Items.Select(x => x.Owner.Kind).Order(StringComparer.Ordinal));
        Assert.Equal(["ActivityVersion", "WorkflowVersion"], versions.Items.Select(x => x.Owner.Kind).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Unauthorized_cross_tenant_owners_are_omitted_without_losing_visible_results()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "root", tenantId: "tenant-a");
        SeedVersion(stores, "visible", tenantId: "tenant-a");
        SeedVersion(stores, "hidden", tenantId: "tenant-b");
        SeedEdge(stores, "visible", "root", "visible");
        SeedEdge(stores, "hidden", "root", "hidden");

        var page = await Reader(stores).ReadAsync("root", "inbound", false, "versions", null, 10, default);

        Assert.Equal("visible", Assert.Single(page.Items).Occurrence.OccurrenceId);
    }

    [Fact]
    public void Dependency_view_serialization_contains_only_safe_reference_facts()
    {
        var page = new ActivityDependencyPage(
            Reference("ActivityVersion", "root", versionId: "root"),
            new(ActivityDependencyDirection.Inbound, true, new HashSet<string> { "Versions", "Drafts" }),
            new(ActivityDependencyConsistencyKind.DerivedProjection, false, 42, DateTimeOffset.Parse("2026-07-15T12:00:00Z")),
            [],
            null);

        var json = JsonSerializer.Serialize(page.ToView(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"direction\":\"Inbound\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"DerivedProjection\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("provider", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("descriptor", json, StringComparison.OrdinalIgnoreCase);
    }

    private static ActivityDependencyReader Reader(
        InMemoryReusableActivityStores stores,
        string? tenantId = "tenant-a",
        string profile = "profile-a",
        IActivityDependencyProjectionStore? projection = null) => new(
        stores,
        stores,
        projection ?? stores,
        new HmacActivityDependencyCursorCodec(Options.Create(new ActivityDependencyCursorOptions
        {
            SigningKey = "dependency-cursor-test-key-at-least-32-bytes"
        })),
        new TestAuthorizationContext(tenantId, profile),
        Options.Create(new ActivityDependencyReaderOptions { DefaultPageSize = 2, MaximumPageSize = 20_000 }));

    private static async Task AssertCursorErrorAsync(string code, Func<Task<ActivityDependencyPageView>> action)
    {
        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(action);
        Assert.Equal(code, exception.ErrorCode);
    }

    private static void SeedVersion(
        InMemoryReusableActivityStores stores,
        string id,
        string? tenantId = "tenant-a",
        ActivityDefinitionVersionLifecycle lifecycle = ActivityDefinitionVersionLifecycle.Active)
    {
        var now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        stores.SeedPublication(new()
        {
            Id = $"publication-{id}",
            TenantId = tenantId,
            DefinitionVersionId = id,
            DefinitionId = $"definition-{id}",
            ActivityTypeKey = $"activity.{id}",
            Version = "1.0.0",
            Contract = new("1", [], [], []),
            Provider = new("elsa.activity-graph", "1", Json("{}")),
            TemplateId = $"template-{id}",
            TemplateHash = $"sha256-{id}",
            SourceReferenceId = $"source-{id}",
            ProviderFingerprint = "provider/1",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 1,
            RuntimeRequirements = [],
            ResourceMeasurements = new(0, 0, 0, 0, 0, 0, 0),
            ResumeTargetCount = 0,
            Lifecycle = lifecycle,
            PublishedAt = now,
            CreatedAt = now,
            LastModifiedAt = now
        }, new()
        {
            Id = $"layout-{id}",
            TenantId = tenantId,
            DefinitionVersionId = id,
            CreatedAt = now,
            LastModifiedAt = now
        });
    }

    private static void SeedEdge(InMemoryReusableActivityStores stores, string owner, string dependency, string occurrence) =>
        stores.SeedDependency(new()
        {
            Id = $"edge-{owner}-{occurrence}-{dependency}",
            TenantId = "tenant-a",
            OwnerVersionId = owner,
            OwnerTemplateHash = $"sha256-{owner}",
            DependencyVersionId = dependency,
            DependencyTemplateHash = $"sha256-{dependency}",
            OccurrenceId = occurrence,
            NodeOrigin = [new("AuthoredNode", occurrence)]
        });

    private static ActivityDefinitionReference Reference(
        string kind,
        string id,
        string? versionId = null,
        string? draftId = null,
        long? revision = null) => new(
        kind,
        $"definition-{id}",
        versionId,
        "1.0.0",
        draftId,
        revision,
        versionId is null ? null : $"sha256-{versionId}",
        null,
        ActivityDefinitionVersionLifecycle.Active);

    private static ActivityDependencyItem Item(
        string occurrence,
        ActivityDefinitionReference owner,
        ActivityDefinitionReference dependency) => new(
        $"relationship-{occurrence}",
        owner,
        dependency,
        new(occurrence, [new("AuthoredNode", occurrence)]),
        true,
        1,
        [owner, dependency]);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class TestAuthorizationContext(string? tenantId, string profile) : IActivityDependencyAuthorizationContext, IActivityDependencyContextAsync
    {
        public string? TenantId => tenantId;
        public string AuthorizationProfile => profile;
        public bool CanRead(ActivityDefinitionReference reference) => true;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(profile);
        public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class MixedProjectionStore(
        ActivityDefinitionReference root,
        IReadOnlyList<ActivityDependencyItem> items) : IActivityDependencyProjectionStore
    {
        public Task<ActivityDependencyProjectionSlice> ReadAsync(
            ActivityDependencyProjectionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            const string watermark = "mixed-watermark";
            if (request.Watermark is not null && request.Watermark != watermark)
                throw new ActivityDependencyWatermarkExpiredException(request.Watermark);
            var included = items
                .Where(x => x.Owner.Kind.EndsWith("Version", StringComparison.Ordinal)
                    ? request.Query.Include.Contains("Versions")
                    : request.Query.Include.Contains("Drafts"))
                .OrderBy(x => x.Depth)
                .ThenBy(x => x.Owner.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.Owner.VersionId ?? x.Owner.DraftId, StringComparer.Ordinal)
                .ThenBy(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal)
                .ThenBy(x => x.Dependency.VersionId, StringComparer.Ordinal)
                .ToArray();
            var page = included.Skip(request.Offset).Take(request.Limit).ToArray();
            var next = request.Offset + page.Length;
            return Task.FromResult(new ActivityDependencyProjectionSlice(
                root,
                new(ActivityDependencyConsistencyKind.DerivedProjection, false, 7, DateTimeOffset.Parse("2026-07-15T12:00:00Z")),
                page,
                watermark,
                next < included.Length ? next : null));
        }
    }
}
