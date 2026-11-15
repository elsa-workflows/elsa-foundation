using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class InMemoryReusableActivityStoresTests
{
    [Fact]
    public async Task Replace_With_Stale_Revision_Leaves_Draft_And_Layout_Unchanged()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores);

        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.ExecuteAsync(
            new ReplaceActivityDraftRequest("draft-1", 9, State("changed"), [Layout("node-2")])));

        var draft = await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-1");
        var layout = await stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal(0, draft!.Revision);
        Assert.Equal("initial", draft.State.Options["label"]);
        Assert.Equal(0, layout!.Revision);
        Assert.Equal("node-1", Assert.Single(layout.Records).NodeId);
    }

    [Fact]
    public async Task Publish_With_Stale_Head_Leaves_Every_Participant_Unchanged()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores);
        var commit = PublicationCommit(expectedHead: "stale-version");

        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.ExecuteAsync(commit));

        var authoring = await stores.FindAsync("definition-1");
        var draft = await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-1");
        var publication = await ((IActivityDefinitionVersionPublicationStore)stores).FindAsync("version-1");
        Assert.Null(authoring!.HeadVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Active, draft!.Status);
        Assert.Empty(stores.CatalogVersions);
        Assert.Null(publication);
        Assert.False(stores.ContainsTemplate("template-1"));
        Assert.False(stores.ContainsSourceReference("source-reference-1"));
    }

    [Fact]
    public async Task Publish_Commits_Immutable_Artifacts_And_Advances_Head_Atomically()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores);

        var result = await stores.ExecuteAsync(PublicationCommit(expectedHead: null));

        var authoring = await stores.FindAsync("definition-1");
        var draft = await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-1");
        var publication = await ((IActivityDefinitionVersionPublicationStore)stores).FindAsync("version-1");
        var layout = await stores.FindVersionLayoutAsync("version-1");
        Assert.Equal("version-1", result.DefinitionVersionId);
        Assert.Equal("version-1", authoring!.HeadVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Published, draft!.Status);
        Assert.Equal("version-1", draft.PublishedVersionId);
        Assert.NotNull(publication);
        Assert.NotNull(layout);
        Assert.Single(stores.CatalogVersions);
        Assert.True(stores.ContainsTemplate("template-1"));
        Assert.True(stores.ContainsSourceReference("source-reference-1"));
    }

    [Fact]
    public async Task ProviderSource_Authority_Rejects_Design_Mutation()
    {
        var stores = new InMemoryReusableActivityStores();
        await SeedDefinitionAsync(stores, ActivityContentAuthorityKind.ProviderSource);

        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.ExecuteAsync(
            new ReplaceActivityDraftRequest("draft-1", 0, State("changed"), [Layout("node-2")])));

        var draft = await ((IActivityDefinitionDraftStore)stores).FindAsync("draft-1");
        Assert.Equal(0, draft!.Revision);
        Assert.Equal("initial", draft.State.Options["label"]);
    }

    private static Task SeedDefinitionAsync(
        InMemoryReusableActivityStores stores,
        ActivityContentAuthorityKind authority = ActivityContentAuthorityKind.Design)
    {
        var now = DateTimeOffset.UtcNow;
        return stores.ExecuteAsync(new CreateActivityDefinitionRequest(
            new ActivityDefinition
            {
                Id = "definition-1",
                ActivityTypeKey = "Acme.Sample",
                Category = "Samples",
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionAuthoringState
            {
                Id = "authoring-1",
                DefinitionId = "definition-1",
                ContentAuthority = new(authority, authority == ActivityContentAuthorityKind.Design ? "design" : "provider"),
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionDraft
            {
                Id = "draft-1",
                DefinitionId = "definition-1",
                Revision = 0,
                State = State("initial"),
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionDraftLayout
            {
                Id = "draft-layout-1",
                DraftId = "draft-1",
                Revision = 0,
                Records = [Layout("node-1")],
                CreatedAt = now,
                LastModifiedAt = now
            }));
    }

    private static ActivityPublicationCommit<object, object, object> PublicationCommit(string? expectedHead)
    {
        var now = DateTimeOffset.UtcNow;
        var contract = Contract();
        var provider = Provider();
        return new(
            null,
            new ActivityPublicationDesignMutation(
                "draft-1",
                0,
                "definition-1",
                expectedHead,
                new ActivityDefinitionVersion("1.0.0", "definition-1")
                {
                    Id = "version-1",
                    DescriptorType = "Acme.SampleDescriptor",
                    DescriptorPayload = Json("{}"),
                    SourceKind = "Designer",
                    SourceId = "draft-1",
                    CreatedAt = now,
                    LastModifiedAt = now
                },
                new ActivityDefinitionVersionPublication
                {
                    Id = "publication-1",
                    DefinitionVersionId = "version-1",
                    DefinitionId = "definition-1",
                    Version = "1.0.0",
                    SourceDraftId = "draft-1",
                    Contract = contract,
                    Provider = provider,
                    TemplateId = "template-1",
                    TemplateHash = "template-hash-1",
                    SourceReferenceId = "source-reference-1",
                    ProviderFingerprint = "provider-fingerprint-1",
                    DirectDependencyCount = 0,
                    ClosedTemplateCount = 1,
                    RuntimeRequirements = [],
                    PublishedAt = now,
                    CreatedAt = now,
                    LastModifiedAt = now
                },
                new ActivityDefinitionVersionLayout
                {
                    Id = "version-layout-1",
                    DefinitionVersionId = "version-1",
                    Records = [Layout("node-1")],
                    CreatedAt = now,
                    LastModifiedAt = now
                },
                []),
            new object(),
            new object(),
            new object());
    }

    private static ActivityDefinitionDraftState State(string label) =>
        new(Contract(), Provider(), new Dictionary<string, string> { ["label"] = label });

    private static ActivityContract Contract() => new("1", [], [], []);

    private static ActivityProviderManifest Provider() => new("workflow", "1", Json("{}"));

    private static ActivityLayoutRecord Layout(string nodeId) => new(nodeId, Json("{}"));

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
