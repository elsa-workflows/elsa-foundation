using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core;
using Groundwork.Kernel;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

public sealed class GroundworkActivityDefinitionTemporalProjectionTests
{
    [Fact]
    public void Manifest_declares_scale_bearing_physical_queries_for_every_temporal_projection()
    {
        var managementUnits = ActivitiesDesignStorageManifest.CreateUnits()
            .Where(unit => unit.Id.Value is
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind)
            .ToArray();

        Assert.Equal(3, managementUnits.Length);
        Assert.All(managementUnits, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal(ActivitiesDesignStorageManifest.StorageSchemaVersion, unit.SchemaVersion);
            Assert.Equal(PortableType.Int64, unit.Columns.Single(column =>
                column.Name == ActivitiesDesignStorageManifest.RevisionField).Type);
            var suffix = unit.Id.Value == ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind
                ? "definitions"
                : unit.Id.Value == ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind
                    ? "drafts"
                    : "versions";
            Assert.Contains(unit.Indexes, index => index.Name == $"management_{suffix}_identity_asc");
        });
    }

    [Fact]
    public async Task Bound_snapshot_keeps_the_original_definition_when_a_later_mutation_changes_it()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var firstAt = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(
            new(firstAt, [TemporalProjectionData.DefinitionChange("definition-1", null, "Original display name", firstAt)], [], [])));
        var firstSnapshot = await harness.Reader.GetCurrentSnapshotAsync();

        var changedAt = firstAt.AddMinutes(1);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(
            new(changedAt, [TemporalProjectionData.DefinitionChange("definition-1", null, "Changed display name", changedAt)], [], [])));

        var originalPage = await harness.Reader.ReadDefinitionsAsync(new(null, firstSnapshot.Sequence, 0, 20));
        var currentPage = await harness.Reader.ReadDefinitionsAsync(new(null, null, 0, 20));

        Assert.Equal("Original display name", Assert.Single(originalPage.Items).DisplayName);
        Assert.Equal("Changed display name", Assert.Single(currentPage.Items).DisplayName);
        Assert.Equal(firstSnapshot, originalPage.Snapshot);
        Assert.True(currentPage.Snapshot.Sequence > firstSnapshot.Sequence);
    }

    [Fact]
    public async Task Pre_watermark_snapshot_remains_a_valid_empty_view_when_the_first_write_races()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var emptySnapshot = await harness.Reader.GetCurrentSnapshotAsync();
        var changedAt = new DateTimeOffset(2026, 7, 17, 8, 30, 0, TimeSpan.Zero);

        await CommitAsync(harness, await harness.Writer.PrepareAsync(
            new(changedAt, [TemporalProjectionData.DefinitionChange("definition-first", null, "First", changedAt)], [], [])));

        var preWatermarkPage = await harness.Reader.ReadDefinitionsAsync(new(null, emptySnapshot.Sequence, 0, 25));
        var freshPage = await harness.Reader.ReadDefinitionsAsync(new(null, null, 0, 25));

        Assert.Equal(0, emptySnapshot.Sequence);
        Assert.Empty(preWatermarkPage.Items);
        Assert.Equal(0, preWatermarkPage.TotalCount);
        Assert.Equal(0, preWatermarkPage.Snapshot.Sequence);
        Assert.Equal("definition-first", Assert.Single(freshPage.Items).DefinitionId);
        Assert.Equal(1, freshPage.Snapshot.Sequence);
    }

    [Fact]
    public async Task Point_lookups_select_the_first_result_operation_declared_by_the_physical_route()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var changedAt = new DateTimeOffset(2026, 7, 17, 8, 45, 0, TimeSpan.Zero);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(
            new(changedAt, [TemporalProjectionData.DefinitionChange("definition-point", null, "Point lookup", changedAt)], [], [])));

        var updatedAt = changedAt.AddMinutes(1);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(
            new(updatedAt, [TemporalProjectionData.DefinitionChange("definition-point", null, "Point lookup updated", updatedAt)], [], [])));

        var found = await harness.Reader.FindDefinitionAsync("definition-point", null);

        Assert.NotNull(found);
        Assert.Equal("Point lookup updated", found.DisplayName);
        Assert.Equal(2, found.ValidFromSequence);
        Assert.Equal(long.MaxValue, found.ValidToSequenceExclusive);
    }

    [Fact]
    public async Task Provider_applies_visibility_search_sort_page_and_exact_count_with_more_than_500_noise_rows()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var reader = harness.Reader;
        var changedAt = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);
        var changes = Enumerable.Range(0, 520)
            .Select(index => TemporalProjectionData.DefinitionChange(
                $"definition-{index:D4}",
                null,
                index < 3 ? $"Needle {index}" : $"Noise {index}",
                changedAt))
            .ToArray();
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(changedAt, changes, [], [])));
        harness.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadDefinitionsAsync(new(
            "tenant-b", null, 0, 2)));

        var first = await reader.ReadDefinitionsAsync(new(
            "tenant-a", null, 0, 2, "needle", ActivityContentAuthorityKind.Design));
        var insertedAt = changedAt.AddMinutes(1);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            insertedAt,
            [TemporalProjectionData.DefinitionChange("definition-new", null, "Needle new", insertedAt)],
            [],
            [])));
        var second = await reader.ReadDefinitionsAsync(new(
            "tenant-a", first.Snapshot.Sequence, first.NextOffset!.Value, 2, "needle", ActivityContentAuthorityKind.Design));
        var fresh = await reader.ReadDefinitionsAsync(new(
            "tenant-a", null, 0, 2, "needle", ActivityContentAuthorityKind.Design));

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(["definition-0000", "definition-0001"], first.Items.Select(x => x.DefinitionId));
        Assert.Equal("definition-0002", Assert.Single(second.Items).DefinitionId);
        Assert.Null(second.NextOffset);
        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.Equal(4, fresh.TotalCount);
        Assert.True(fresh.Snapshot.Sequence > first.Snapshot.Sequence);
    }

    [Fact]
    public async Task Late_projection_conflict_rolls_back_authoritative_rows_revisions_and_watermark()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var old = TemporalProjectionData.DefinitionEntity("definition-atomic", null, "Existing", DateTimeOffset.UtcNow);
        var saved = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            old,
            GroundworkActivitiesDesignJson.Options);
        await harness.Store.SaveAsync(saved);

        var changedAt = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        await using var update = await harness.Writer.PrepareAsync(new(
            changedAt,
            [TemporalProjectionData.DefinitionChange("definition-atomic", null, "Atomic", changedAt)],
            [],
            []));

        var authoritative = new ActivityDesignSaveRequest(
            saved.DocumentKind,
            saved.Id,
            saved.SchemaVersion,
            saved.ContentJson,
            ExpectedVersion: 0);
        await Assert.ThrowsAsync<ActivityDesignWriteConflictException>(() => update.CommitAsync(
            [ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind],
            [authoritative]));

        Assert.Equal("Existing", TemporalProjectionData.Deserialize<ActivityDefinition>(
            (await harness.Store.LoadAsync(saved.DocumentKind, saved.Id))!).DisplayName);
        Assert.Equal(0, (await harness.Reader.GetCurrentSnapshotAsync()).Sequence);
        Assert.Null(await harness.Reader.FindDefinitionAsync("definition-atomic", null));
    }

    [Fact]
    public async Task Writer_rejects_cross_definition_and_cross_tenant_projection_ownership()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var changedAt = new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero);
        var first = TemporalProjectionData.DefinitionChange("definition-a", "tenant-a", "A", changedAt);
        var second = TemporalProjectionData.DefinitionChange("definition-b", null, "B", changedAt);
        var firstVersion = TemporalProjectionData.Version("version-a", "definition-a", "1.0.0", "provider-a", changedAt, "tenant-a");
        var secondVersion = TemporalProjectionData.Version("version-b", "definition-b", "1.0.0", "provider-b", changedAt, null);
        first.Authoring.HeadVersionId = firstVersion.DefinitionVersionId;
        second.Authoring.HeadVersionId = secondVersion.DefinitionVersionId;
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            changedAt, [first, second], [], [firstVersion, secondVersion])));

        var crossDefinition = TemporalProjectionData.DefinitionChange("definition-a", "tenant-a", "A", changedAt.AddMinutes(1));
        crossDefinition.Authoring.HeadVersionId = secondVersion.DefinitionVersionId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Writer.PrepareAsync(new(
            changedAt.AddMinutes(1), [crossDefinition], [], [])));

        var crossTenantDraft = TemporalProjectionData.Draft(
            "draft-cross-tenant", "definition-a", "Cross tenant", ActivityDefinitionDraftStatus.Active, "provider-a", changedAt.AddMinutes(1), "tenant-b");
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Writer.PrepareAsync(new(
            changedAt.AddMinutes(1), [], [crossTenantDraft], [])));

        Assert.Equal(1, (await harness.Reader.GetCurrentSnapshotAsync()).Sequence);
    }

    [Fact]
    public async Task Draft_version_and_definition_provider_filters_use_only_safe_temporal_summary_fields()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var changedAt = new DateTimeOffset(2026, 7, 17, 11, 0, 0, TimeSpan.Zero);
        var definition = TemporalProjectionData.DefinitionChange("definition-filtered", null, "Filtered", changedAt);
        definition.Authoring.HeadVersionId = "version-1";
        definition.Authoring.RecommendedVersionId = "version-2";
        var drafts = new[]
        {
            TemporalProjectionData.Draft("draft-alpha", "definition-filtered", "Alpha draft", ActivityDefinitionDraftStatus.Active, "provider-a", changedAt),
            TemporalProjectionData.Draft("draft-beta", "definition-filtered", "Beta draft", ActivityDefinitionDraftStatus.Discarded, "provider-a", changedAt),
            TemporalProjectionData.Draft("draft-gamma", "definition-filtered", "Gamma draft", ActivityDefinitionDraftStatus.Active, "provider-b", changedAt)
        };
        var versions = new[]
        {
            TemporalProjectionData.Version("version-1", "definition-filtered", "1.0.0", "provider-a", changedAt, null, ActivityDefinitionVersionLifecycle.Active),
            TemporalProjectionData.Version("version-2", "definition-filtered", "2.0.0", "provider-b", changedAt, null, ActivityDefinitionVersionLifecycle.Retired),
            TemporalProjectionData.Version("version-3", "definition-filtered", "3.0.0", "provider-b", changedAt, null, ActivityDefinitionVersionLifecycle.Active)
        };
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(changedAt, [definition], drafts, versions)));

        var definitions = await harness.Reader.ReadDefinitionsAsync(new(null, null, 0, 20, ProviderKey: "provider-b"));
        var draftPage = await harness.Reader.ReadDraftsAsync("definition-filtered", new(
            null, definitions.Snapshot.Sequence, 0, 20, "alpha", ProviderKey: "provider-a", DraftStatus: ActivityDefinitionDraftStatus.Active));
        var versionPage = await harness.Reader.ReadVersionsAsync("definition-filtered", new(
            null, definitions.Snapshot.Sequence, 0, 20, "2.0", ProviderKey: "provider-b", VersionLifecycle: ActivityDefinitionVersionLifecycle.Retired));

        var projectedDefinition = Assert.Single(definitions.Items);
        Assert.Equal("version-1", projectedDefinition.Head!.DefinitionVersionId);
        Assert.Equal("version-2", projectedDefinition.Recommendation!.DefinitionVersionId);
        Assert.Equal("draft-alpha", Assert.Single(draftPage.Items).DraftId);
        Assert.Equal("version-2", Assert.Single(versionPage.Items).DefinitionVersionId);
        Assert.Contains("ALPHA", Assert.Single(draftPage.Items, x => x.DraftId == "draft-alpha").SearchText);
        Assert.Equal(3, projectedDefinition.DraftCount);
        Assert.Equal(3, projectedDefinition.VersionCount);
        Assert.All(
            harness.Store.Query(new ActivityDesignQuery(
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ManagementDraftsQuery,
                [], [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)], Take: 20)).Documents,
            envelope => Assert.DoesNotContain("internalValue", envelope.ContentJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retention_pruning_returns_the_same_safe_expiry_error_for_an_old_snapshot()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var firstAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            firstAt, [TemporalProjectionData.DefinitionChange("definition-retained", null, "First", firstAt)], [], [])));
        var firstSnapshot = await harness.Reader.GetCurrentSnapshotAsync();
        var secondAt = firstAt.AddMinutes(1);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            secondAt, [TemporalProjectionData.DefinitionChange("definition-retained", null, "Second", secondAt)], [], [])));

        var tamperedSequence = firstSnapshot.Sequence + 1000;
        var tamperedFailure = await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() =>
            harness.Reader.ReadDefinitionsAsync(new(null, tamperedSequence, 0, 20)));
        Assert.Equal(tamperedSequence, tamperedFailure.Sequence);

        await new GroundworkActivityManagementProjectionRetention(
            harness.Store, harness.Store, new ImmediateDistributedLockProvider())
            .ExpireBeforeAsync(2, secondAt.AddMinutes(1));

        var firstFailure = await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() =>
            harness.Reader.ReadDefinitionsAsync(new(null, firstSnapshot.Sequence, 0, 20)));
        var replayFailure = await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() =>
            harness.Reader.ReadDefinitionsAsync(new(null, firstSnapshot.Sequence, 0, 20)));
        Assert.Equal(firstSnapshot.Sequence, firstFailure.Sequence);
        Assert.Equal(firstFailure.Message, replayFailure.Message);
        Assert.Equal("Second", Assert.Single((await harness.Reader.ReadDefinitionsAsync(new(null, null, 0, 20))).Items).DisplayName);
    }

    private static async Task CommitAsync(
        TemporalActivityDesignV2TestHarness harness,
        GroundworkActivityManagementProjectionUpdate update)
    {
        await using (update)
            await update.CommitAsync([], []);
    }
}
