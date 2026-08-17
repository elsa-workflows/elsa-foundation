using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

public sealed class GroundworkActivityManagementMutationProjectionTests
{
    [Fact]
    public async Task Draft_presentation_update_advances_the_projection_atomically()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            createdAt,
            [TemporalProjectionData.DefinitionChange("definition-1", null, "Definition", createdAt)],
            [TemporalProjectionData.Draft("draft-1", "definition-1", "Initial", ActivityDefinitionDraftStatus.Active, "provider-a", createdAt)],
            [])));

        var changedAt = createdAt.AddHours(2);
        var changedDraft = TemporalProjectionData.Draft(
            "draft-1", "definition-1", "Ready for review", ActivityDefinitionDraftStatus.Active, "provider-a", changedAt);
        changedDraft.Revision = 2;
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            changedAt, [], [changedDraft], [])));

        var current = await harness.Reader.ReadDraftsAsync("definition-1", new(null, null, 0, 20));
        var projection = Assert.Single(current.Items);
        Assert.Equal(2, projection.Revision);
        Assert.Equal("Ready for review", projection.PresentationLabel);
        Assert.Equal(2, current.Snapshot.Sequence);
    }

    [Fact]
    public async Task Projection_failure_rolls_back_the_draft_presentation_update()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            createdAt,
            [TemporalProjectionData.DefinitionChange("definition-1", null, "Definition", createdAt)],
            [TemporalProjectionData.Draft("draft-1", "definition-1", "Initial", ActivityDefinitionDraftStatus.Active, "provider-a", createdAt)],
            [])));
        var authoritative = TemporalProjectionData.DefinitionEntity("authoritative-1", null, "Already exists", createdAt);
        var saved = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            authoritative,
            GroundworkActivitiesDesignJson.Options);
        await harness.Store.SaveAsync(saved);

        var changedAt = createdAt.AddHours(2);
        await using var update = await harness.Writer.PrepareAsync(new(
            changedAt,
            [],
            [TemporalProjectionData.Draft("draft-1", "definition-1", "Must roll back", ActivityDefinitionDraftStatus.Active, "provider-a", changedAt)],
            []));
        await Assert.ThrowsAsync<ActivityDesignWriteConflictException>(() => update.CommitAsync(
            [ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind],
            [new ActivityDesignSaveRequest(saved.DocumentKind, saved.Id, saved.SchemaVersion, saved.ContentJson, 0)]));

        var current = await harness.Reader.ReadDraftsAsync("definition-1", new(null, null, 0, 20));
        Assert.Equal("Initial", Assert.Single(current.Items).PresentationLabel);
        Assert.Equal(1, current.Snapshot.Sequence);
    }

    [Fact]
    public async Task Conflict_copy_adds_one_projected_draft_without_revising_the_source()
    {
        using var harness = TemporalActivityDesignV2TestHarness.Create();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(
            createdAt,
            [TemporalProjectionData.DefinitionChange("definition-1", null, "Definition", createdAt)],
            [TemporalProjectionData.Draft("draft-1", "definition-1", "Initial", ActivityDefinitionDraftStatus.Active, "provider-a", createdAt)],
            [])));

        var changedAt = createdAt.AddHours(2);
        var copy = TemporalProjectionData.Draft(
            "draft-copy", "definition-1", "Recovered local work", ActivityDefinitionDraftStatus.Active, "provider-a", changedAt);
        await CommitAsync(harness, await harness.Writer.PrepareAsync(new(changedAt, [], [copy], [])));

        var currentDrafts = await harness.Reader.ReadDraftsAsync("definition-1", new(null, null, 0, 20));
        var definition = Assert.Single((await harness.Reader.ReadDefinitionsAsync(new(null, null, 0, 20))).Items);
        Assert.Equal(2, currentDrafts.Items.Count);
        Assert.Equal(1, Assert.Single(currentDrafts.Items, x => x.DraftId == "draft-1").Revision);
        Assert.Equal("Recovered local work", Assert.Single(currentDrafts.Items, x => x.DraftId == "draft-copy").PresentationLabel);
        Assert.Equal(2, definition.DraftCount);
    }

    private static async Task CommitAsync(
        TemporalActivityDesignV2TestHarness harness,
        GroundworkActivityManagementProjectionUpdate update)
    {
        await using (update)
            await update.CommitAsync([], []);
    }
}
