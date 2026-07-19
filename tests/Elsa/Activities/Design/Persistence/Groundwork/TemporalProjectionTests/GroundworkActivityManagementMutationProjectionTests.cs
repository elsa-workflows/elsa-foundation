using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Primitives.Contracts;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

public sealed class GroundworkActivityManagementMutationProjectionTests
{
    [Fact]
    public async Task Draft_presentation_update_advances_the_projection_atomically()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());

        var updated = await harness.Stores.ExecuteAsync(new UpdateActivityDraftPresentationRequest(
            "draft-1",
            0,
            "Ready for review",
            new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        var current = CurrentProjection<ActivityDefinitionDraftManagementProjectionRevision>(
            harness.Documents,
            ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
            "draft-1");
        Assert.Equal(1, updated.Revision);
        Assert.Equal("Ready for review", updated.PresentationLabel);
        Assert.Equal(updated.Revision, current.Revision);
        Assert.Equal(updated.PresentationLabel, current.PresentationLabel);
    }

    [Fact]
    public async Task Projection_failure_rolls_back_the_draft_presentation_update()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());
        harness.Documents.FailOnDocumentKind = ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind;

        await Assert.ThrowsAsync<DocumentAtomicWriteException>(() => harness.Stores.ExecuteAsync(
            new UpdateActivityDraftPresentationRequest(
                "draft-1",
                0,
                "Must roll back",
                new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero))));

        var persisted = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        var current = CurrentProjection<ActivityDefinitionDraftManagementProjectionRevision>(
            harness.Documents,
            ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
            "draft-1");
        Assert.Equal(0, persisted!.Revision);
        Assert.Null(persisted.PresentationLabel);
        Assert.Equal(0, current.Revision);
        Assert.Null(current.PresentationLabel);
    }

    [Fact]
    public async Task Conflict_copy_adds_one_projected_draft_without_revising_the_source()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());
        var copy = Draft("draft-copy", "definition-1", new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero));
        copy.PresentationLabel = "Recovered local work";

        await harness.Stores.ExecuteAsync(new CreateActivityDraftConflictCopyRequest(
            "draft-1",
            0,
            copy,
            DraftLayout("draft-layout-copy", "draft-copy", copy.LastModifiedAt)));

        var currentDrafts = CurrentProjections<ActivityDefinitionDraftManagementProjectionRevision>(
            harness.Documents,
            ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind);
        var definition = CurrentProjection<ActivityDefinitionManagementProjectionRevision>(
            harness.Documents,
            ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
            "definition-1");
        Assert.Equal(2, currentDrafts.Count);
        Assert.Single(currentDrafts, x => x.DraftId == "draft-1" && x.Revision == 0);
        Assert.Equal("Recovered local work", Assert.Single(currentDrafts, x => x.DraftId == "draft-copy").PresentationLabel);
        Assert.Equal(2, definition.DraftCount);
    }

    private static CreateActivityDefinitionRequest CreateRequest()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new(
            new()
            {
                Id = "definition-1",
                ActivityTypeKey = "Acme.Sample",
                Category = "Samples",
                CreatedAt = now,
                LastModifiedAt = now
            },
            new()
            {
                Id = "authoring-1",
                DefinitionId = "definition-1",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, "design"),
                CreatedAt = now,
                LastModifiedAt = now
            },
            Draft("draft-1", "definition-1", now),
            DraftLayout("draft-layout-1", "draft-1", now));
    }

    private static ActivityDefinitionDraft Draft(string id, string definitionId, DateTimeOffset now) => new()
    {
        Id = id,
        DefinitionId = definitionId,
        Revision = 0,
        State = new(
            new("1", [], [], []),
            new("workflow", "1", Json("{}")),
            new Dictionary<string, string> { ["label"] = "initial" }),
        CreatedAt = now,
        LastModifiedAt = now
    };

    private static ActivityDefinitionDraftLayout DraftLayout(string id, string draftId, DateTimeOffset now) => new()
    {
        Id = id,
        DraftId = draftId,
        Revision = 0,
        Records = [new("node-1", Json("{}"))],
        CreatedAt = now,
        LastModifiedAt = now
    };

    private static TEntity CurrentProjection<TEntity>(
        TemporalProjectionDocumentStore documents,
        string kind,
        string resourceId)
        where TEntity : ActivityManagementProjectionRevision =>
        Assert.Single(CurrentProjections<TEntity>(documents, kind), x => x.ResourceId == resourceId);

    private static IReadOnlyList<TEntity> CurrentProjections<TEntity>(
        TemporalProjectionDocumentStore documents,
        string kind)
        where TEntity : ActivityManagementProjectionRevision =>
        documents.Documents(kind)
            .Select(Deserialize<TEntity>)
            .Where(x => x.ValidToSequenceExclusive == long.MaxValue)
            .ToArray();

    private static TEntity Deserialize<TEntity>(DocumentEnvelope envelope) where TEntity : Elsa.Primitives.Entities.Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, GroundworkActivitiesDesignJson.Options)!.Entity;

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed record Harness(TemporalProjectionDocumentStore Documents, GroundworkReusableActivityStores Stores)
    {
        public static Harness Create()
        {
            var documents = new TemporalProjectionDocumentStore();
            var locks = new ImmediateDistributedLockProvider();
            return new(
                documents,
                new(
                    documents,
                    new FakeClock(),
                    locks,
                    documents,
                    new GroundworkActivityManagementProjectionWriter(documents, locks)));
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
    }
}
