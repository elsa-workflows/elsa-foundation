using System.Net;
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
