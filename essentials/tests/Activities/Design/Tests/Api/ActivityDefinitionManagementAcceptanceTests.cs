using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Elsa.Mediator.Core.Contracts;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class ActivityDefinitionManagementAcceptanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Management_collections_use_the_bounded_snapshot_page_contract()
    {
        var page = new ActivityManagementPageView<string>(
            ["definition-1"],
            1,
            3,
            true,
            "next-page",
            new("snapshot-7", Now));

        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            "{\"items\":[\"definition-1\"],\"count\":1,\"totalCount\":3,\"hasMore\":true,\"continuation\":\"next-page\",\"snapshot\":{\"snapshotId\":\"snapshot-7\",\"asOf\":\"2026-07-17T10:00:00+00:00\"}}",
            json);
        Assert.IsAssignableFrom<IRequest<ActivityManagementPageView<ReusableActivityDefinitionManagementView>>>(new ListReusableActivityDefinitions());
        Assert.IsAssignableFrom<IRequest<ActivityManagementPageView<ReusableActivityDraftManagementView>>>(new ListReusableActivityDrafts("definition-1"));
        Assert.IsAssignableFrom<IRequest<ActivityManagementPageView<ReusableActivityVersionManagementView>>>(new ListReusableActivityVersions("definition-1"));
    }

    [Fact]
    public void Definition_detail_is_a_bounded_summary_without_embedded_draft_or_version_collections()
    {
        var properties = typeof(ReusableActivityDefinitionManagementView)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name)
            .ToArray();

        Assert.Equal(new[] { "Definition", "Lifecycle", "Actions", "UpdatedAt" }, properties);
        Assert.DoesNotContain("Drafts", properties);
        Assert.DoesNotContain("Versions", properties);
    }

    [Fact]
    public async Task Draft_presentation_labels_are_optional_nonunique_metadata()
    {
        var stores = await SeedAsync("Review candidate");
        var second = Draft("draft-2", "Review candidate", State("second"));

        await stores.ExecuteAsync(new CreateActivityDraftRequest(second, Layout(second.Id, second.Revision, 2), null));

        var drafts = await ((IActivityDefinitionDraftStore)stores).ListByDefinitionAsync("definition-1");
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, draft => Assert.Equal("Review candidate", draft.PresentationLabel));
        Assert.NotEqual(drafts[0].Id, drafts[1].Id);
    }

    [Fact]
    public async Task Presentation_and_full_state_updates_share_one_optimistic_revision_stream()
    {
        var stores = await SeedAsync(null);

        var presented = await stores.ExecuteAsync(new UpdateActivityDraftPresentationRequest(
            "draft-1",
            1,
            "Ready for review",
            Now.AddMinutes(1)));
        var replaced = await stores.ExecuteAsync(new ReplaceActivityDraftRequest(
            "draft-1",
            presented.Revision,
            State("replacement"),
            [new("root", Json("{\"x\":3}"))],
            presented.PresentationLabel));

        Assert.Equal(2, presented.Revision);
        Assert.Equal(3, replaced.Revision);
        Assert.Equal("Ready for review", replaced.PresentationLabel);
        Assert.Equal("replacement", replaced.State.Contract.ContractSchemaVersion);
        var layout = await stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal(3, layout!.Revision);
        Assert.Equal(3, layout.Records.Single().Data.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task Conflict_copy_atomically_preserves_submitted_full_state_and_rejects_a_stale_source_revision()
    {
        var stores = await SeedAsync("Server draft");
        var copy = Draft("draft-copy", "Recovered local work", State("local-state"));
        var copyLayout = Layout(copy.Id, copy.Revision, 42);

        await stores.ExecuteAsync(new CreateActivityDraftConflictCopyRequest("draft-1", 1, copy, copyLayout));

        var source = await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-1");
        var persistedCopy = await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-copy");
        var persistedLayout = await stores.FindDraftLayoutAsync("draft-copy");
        Assert.Equal(1, source!.Revision);
        Assert.Equal("Server draft", source.PresentationLabel);
        Assert.Equal(1, persistedCopy!.Revision);
        Assert.Equal("Recovered local work", persistedCopy.PresentationLabel);
        Assert.Equal("local-state", persistedCopy.State.Contract.ContractSchemaVersion);
        Assert.Equal(42, persistedLayout!.Records.Single().Data.GetProperty("x").GetInt32());

        await stores.ExecuteAsync(new UpdateActivityDraftPresentationRequest(
            "draft-1",
            1,
            "Server changed",
            Now.AddMinutes(2)));
        var staleCopy = Draft("draft-stale-copy", "Must not persist", State("stale-local-state"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.ExecuteAsync(
            new CreateActivityDraftConflictCopyRequest(
                "draft-1",
                1,
                staleCopy,
                Layout(staleCopy.Id, staleCopy.Revision, 99))));

        Assert.Null(await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-stale-copy"));
        Assert.Null(await stores.FindDraftLayoutAsync("draft-stale-copy"));
        Assert.Equal(2, (await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-1"))!.Revision);
    }

    private static async Task<InMemoryReusableActivityStores> SeedAsync(string? presentationLabel)
    {
        var stores = new InMemoryReusableActivityStores();
        var definition = new ActivityDefinition
        {
            Id = "definition-1",
            TenantId = "tenant-a",
            ActivityTypeKey = "test.definition-1",
            Category = "Tests",
            DisplayName = "Test definition",
            CreatedAt = Now,
            LastModifiedAt = Now
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = "authoring-1",
            TenantId = "tenant-a",
            DefinitionId = definition.Id,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            CreatedAt = Now,
            LastModifiedAt = Now
        };
        var initialDraft = Draft("draft-1", presentationLabel, State("initial"));

        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(
            definition,
            authoring,
            initialDraft,
            Layout(initialDraft.Id, initialDraft.Revision, 1)));
        return stores;
    }

    private static ActivityDefinitionDraft Draft(
        string id,
        string? presentationLabel,
        ActivityDefinitionDraftState state) => new()
    {
        Id = id,
        TenantId = "tenant-a",
        DefinitionId = "definition-1",
        Revision = 1,
        PresentationLabel = presentationLabel,
        Status = ActivityDefinitionDraftStatus.Active,
        State = state,
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    private static ActivityDefinitionDraftState State(string contractSchemaVersion) => new(
        new(contractSchemaVersion, [], [], []),
        new("test.provider", "1", Json("{\"behavior\":true}")),
        new Dictionary<string, string>(StringComparer.Ordinal));

    private static ActivityDefinitionDraftLayout Layout(string draftId, long revision, int x) => new()
    {
        Id = $"layout-{draftId}",
        TenantId = "tenant-a",
        DraftId = draftId,
        Revision = revision,
        Records = [new("root", Json($"{{\"x\":{x}}}"))],
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

}
