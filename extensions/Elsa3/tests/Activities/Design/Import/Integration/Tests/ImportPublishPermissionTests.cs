using System.Net;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 246 User Story 5: applying an import publishes, so it needs the publish permission as well as the import's, is
/// refused before anything is written without it, and publishes in the caller's tenant.
/// </summary>
public sealed class ImportPublishPermissionTests
{
    private const string ImportManage = "elsa3-import:manage";
    private const string ImportRead = "elsa3-import:read";

    [Fact]
    public async Task Apply_without_publish_permission_is_forbidden_and_writes_nothing()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        host.Permissions = [ImportManage, ImportRead];
        var handle = await host.UploadCollectionAsync("reusable-rejects");
        var (planId, items) = await host.AnalyzeAsync(handle);
        var before = await host.CountDesignRowsAsync();

        var apply = await host.ApplyAsync(handle, planId, "without-publish", Elsa3ImportHost.Required(Assert.Single(items), "sourceVersionId"));

        Assert.Equal(HttpStatusCode.Forbidden, apply.StatusCode);
        Assert.Equal(before, await host.CountDesignRowsAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetImportStatusAsync("without-publish")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_apply_binds_the_reviewed_publication_choice(bool publish)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var handle = await host.UploadCollectionAsync("reusable-rejects");
        var (plan, items) = await host.AnalyzeAsync(handle);
        var id = Elsa3ImportHost.Required(Assert.Single(items), "sourceVersionId");
        using var response = await host.Send(HttpMethod.Post, $"migration/elsa3/reusable-activities/collections/{handle}/apply-drafts",
            new JsonObject
            {
                ["planId"] = plan,
                ["selectedSourceVersionIds"] = new JsonArray(id),
                ["idempotencyKey"] = "draft-choice",
                ["publishImportedActivities"] = publish
            });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        var receipt = JsonNode.Parse(body)!;
        Assert.Equal(publish, (bool?)receipt["publishImportedActivities"]);
        Assert.NotNull(receipt["sources"]![0]!["activityDraftId"]);
        var expected = publish ? ReusableActivityPublicationStatus.Published : ReusableActivityPublicationStatus.NotPublished;
        Assert.Equal(expected, Assert.Single(PublicationEntry.Of(receipt)).Status);
    }

    [Fact]
    public async Task Later_publication_uses_the_imported_version_even_on_a_new_definition()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("reusable-rejects"))!;
        collection[0]!["version"] = 3;
        collection[0]!["id"] = "rejection-v3";
        var upload = await host.PostAsync("migration/elsa3/reusable-activities/collections", collection);
        var handle = Elsa3ImportHost.Required(upload, "collectionHandle");
        var (plan, _) = await host.AnalyzeAsync(handle);
        var receipt = await host.PostAsync($"migration/elsa3/reusable-activities/collections/{handle}/apply-drafts",
            new JsonObject { ["planId"] = plan, ["selectedSourceVersionIds"] = new JsonArray("rejection-v3"), ["idempotencyKey"] = "later" });
        var source = receipt["sources"]![0]!;
        var draftPath = $"design/activities/drafts/{source["activityDraftId"]}";
        var review = await host.PostAsync($"{draftPath}/publication-preflight", new JsonObject { ["expectedDraftRevision"] = 1 });
        Assert.Equal("3.0.0", (string?)review["reservedVersion"]);
        var publication = await host.PostAsync($"{draftPath}/publish", new JsonObject
        {
            ["expectedDraftRevision"] = 1, ["version"] = "3.0.0", ["idempotencyKey"] = "publish-later",
            ["reviewToken"] = (string?)review["reviewToken"]
        });
        Assert.Equal((string?)source["activityDefinitionVersionId"], (string?)publication["outcome"]!["definitionVersionId"]);
    }

    [Fact]
    public async Task Apply_with_both_permissions_publishes_in_the_callers_tenant()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        host.Permissions = [ImportManage, ImportRead, WorkflowPublishingPermissions.Manage];

        var import = await host.ImportSelectionAsync("with-publish", ["reusable-rejects"]);

        Assert.Equal(HttpStatusCode.Created, import.Apply.StatusCode);
        Assert.Equal(ReusableActivityPublicationStatus.Published, Assert.Single(PublicationEntry.Of(import.Apply.Body)).Status);
        var versionId = import.VersionId("rejection-v1");
        var publication = await host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().FindAsync(versionId));
        Assert.Equal(Elsa3ImportHost.TenantId, publication?.TenantId);
    }
}
