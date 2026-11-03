using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

internal static class WorkflowLibraryNativeWitness
{
    public static async Task RunAsync(WorkflowsDesignProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var tenant = "library-" + Guid.NewGuid().ToString("N");
        var folderId = ""; var tagId = ""; var revision = "";
        await using (var host = Host(fixture.ConnectionString, provider, tenant))
        {
            using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            db.Definitions.AddRange(new WorkflowDefinition { Id = "alpha", Name = "Same", TenantId = tenant, CreatedAt = now, LastModifiedAt = now },
                new WorkflowDefinition { Id = "alpha ", Name = "Same", TenantId = tenant, CreatedAt = now, LastModifiedAt = now.AddTicks(1) });
            db.Versions.Add(new WorkflowDefinitionVersion("alpha", "1.0.0", "{}") { Id = "version-alpha", TenantId = tenant, CreatedAt = now, LastModifiedAt = now });
            db.Drafts.AddRange(new WorkflowDefinitionDraft { Id = "draft-z", WorkflowDefinitionId = "alpha", TenantId = tenant,
                    CreatedAt = now.AddMinutes(-1), LastModifiedAt = now, StateSource = "{}" },
                new WorkflowDefinitionDraft { Id = "draft-a", WorkflowDefinitionId = "alpha", TenantId = tenant,
                    CreatedAt = now, LastModifiedAt = now, StateSource = "{}" },
                new WorkflowDefinitionDraft { Id = "draft-z ", WorkflowDefinitionId = "alpha", TenantId = tenant,
                    CreatedAt = now, LastModifiedAt = now, StateSource = "{}", SourceVersionId = "version-alpha" });
            await db.SaveChangesAsync();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            var root = await library.CreateFolderAsync("Finance", null, "alice");
            var folder = await library.CreateFolderAsync("Receivables", root.Id, "alice"); folderId = folder.Id;
            await library.MoveDefinitionsAsync(["alpha", "alpha "], folder.Id, "alice", "place-both");
            var tag = await library.CreateTagAsync(new("Critical"), "alice"); tagId = tag.Id;
            var regions = await library.CreateTagAsync(new("Region", Mode: "controlled", Cardinality: "multiple", Values: [new("", "EU"), new("", "US")]), "alice");
            var organization = (await library.GetOrganizationAsync("alpha"))!;
            organization = await library.ReplaceTagsAsync("alpha", organization.Revision,
                [new(tag.Id), new(regions.Id, regions.Values[0].Id), new(regions.Id, regions.Values[1].Id)], "alice", "tag-alpha"); revision = organization.Revision;
            Assert.Equal("alpha", Assert.Single((await library.ListDefinitionsAsync(new(FolderId: root.Id, IncludeDescendants: true, TagFilters: [new(tag.Id)]), "alice")).Items).Definition.Id);
            Assert.Equal("alpha", Assert.Single((await library.ListDefinitionsAsync(new(TagFilters: [new(regions.Id, "all", regions.Values.Select(value => value.Id).ToArray())]), "alice")).Items).Definition.Id);
            var first = await library.ListDefinitionsAsync(new(FolderId: folder.Id, Sort: "modified", PageSize: 1), "alice");
            Assert.NotNull(first.NextContinuationToken); Assert.Equal(2, first.TotalCount);
            var second = await library.ListDefinitionsAsync(new(FolderId: folder.Id, Sort: "modified", PageSize: 1, ContinuationToken: first.NextContinuationToken), "alice");
            Assert.NotEqual(Assert.Single(first.Items).Definition.Id, Assert.Single(second.Items).Definition.Id);
            Assert.Equal(1, first.Items.Concat(second.Items).Sum(item => item.VersionCount));
            var alpha = first.Items.Concat(second.Items).Single(item => item.Definition.Id == "alpha");
            Assert.Equal("draft-z ", alpha.DraftId); Assert.Equal("1.0.0", alpha.DraftSourceVersion);
            var policy = new WorkflowLibraryReadScope("native-policy-1", ["ALPHA", "alpha "]);
            var policyPage = await library.ListDefinitionsAsync(new(PageSize: 1), "alice", policy);
            Assert.Equal(2, policyPage.TotalCount); Assert.NotNull(policyPage.NextContinuationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(PageSize: 1, ContinuationToken: policyPage.NextContinuationToken), "alice", policy with { Revision = "native-policy-2" }));
            var reduced = await library.ListDefinitionsAsync(new(PageSize: 1), "alice", policy with { DeniedDefinitionIds = ["alpha "] });
            Assert.Equal(1, reduced.TotalCount); Assert.Equal("alpha", Assert.Single(reduced.Items).Definition.Id); Assert.Null(reduced.NextContinuationToken);
            await library.SetFavoriteAsync("alpha", true, "alice");
            var view = await library.SaveViewAsync(null, new("Published finance", new(FolderId: root.Id, IncludeDescendants: true), Shared: true, Columns: ["name", "location"]), "alice", true);
            Assert.Equal(view.Id, Assert.Single(await library.ListViewsAsync("bob")).Id);
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.ReplaceTagsAsync("alpha", "stale", [], "alice", "stale-alpha"));
            var retired = await library.UpdateTagAsync(regions.Id, new(regions.Name, Mode: "controlled", Cardinality: "multiple", Revision: regions.Revision,
                Values: regions.Values.Select((value, index) => value with { Deprecated = index == 0 }).ToArray()), "alice");
            organization = await library.ReplaceTagsAsync("alpha", revision, organization.Tags, "alice", "retain-retired-region");
            var siblingOrganization = (await library.GetOrganizationAsync("alpha "))!;
            await Assert.ThrowsAsync<ArgumentException>(() => library.ReplaceTagsAsync("alpha ", siblingOrganization.Revision,
                [new(retired.Id, retired.Values[0].Id)], "alice", "cannot-add-retired-region"));
            var bulk = new WorkflowTagBulkEdit([new("alpha", organization.Revision), new("alpha ", "stale"), new("missing", "0")], [],
                [new(regions.Id, regions.Values[1].Id)], "bulk-remove-us");
            var outcomes = await library.EditTagsAsync(bulk, "alice");
            Assert.Equal(new[] { "success", "conflict", "not-found" }, outcomes.Select(outcome => outcome.Status));
            revision = outcomes[0].Revision!; Assert.Equal(revision, (await library.EditTagsAsync(bulk, "alice"))[0].Revision);
            await library.RenameFolderAsync(folder.Id, "Customer billing", folder.Revision, "alice");
            var currentFolder = (await library.GetFolderAsync(folder.Id))!.Folder;
            await Assert.ThrowsAsync<ArgumentException>(() => library.MoveFolderAsync(root.Id, folder.Id, root.Revision, "alice"));
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.DeleteEmptyFolderAsync(folder.Id, currentFolder.Revision, "alice"));
            // The existing source-facing metadata command has no organization fields to overwrite.
            await scope.ServiceProvider.GetRequiredService<ISaveWorkflowDefinitionCommand>().Execute(new("source-metadata-update"),
                new WorkflowDefinition { Id = "alpha", Name = "Renamed by source" });
            Assert.Equal(revision, (await library.GetOrganizationAsync("alpha"))!.Revision);
            var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
            var sibling = await definitions.GetAsync("alpha ");
            sibling.DeletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
            Assert.Equal(folder.Id, (await library.GetOrganizationAsync("alpha "))!.FolderId);
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.MoveDefinitionsAsync(["alpha "], null, "alice", "move-deleted-sibling"));
            sibling = await definitions.GetAsync("alpha ");
            sibling.DeletedAt = null; await db.SaveChangesAsync();
            Assert.Equal(folder.Id, (await library.GetOrganizationAsync("alpha "))!.FolderId);
            db.Definitions.Remove(sibling); await db.SaveChangesAsync();
            Assert.Null(await library.GetOrganizationAsync("alpha "));
        }
        await using (var restart = Host(fixture.ConnectionString, provider, tenant))
        {
            using var scope = restart.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            var organization = (await library.GetOrganizationAsync("alpha"))!;
            Assert.Equal(folderId, organization.FolderId); Assert.Equal(revision, organization.Revision); Assert.Contains(organization.Tags, assignment => assignment.TagId == tagId);
            Assert.Equal("alpha", Assert.Single((await library.ListDefinitionsAsync(new(FavoritesOnly: true), "alice")).Items).Definition.Id);
            Assert.Empty((await library.ListDefinitionsAsync(new(FavoritesOnly: true), "bob")).Items);
        }
        await using (var foreign = Host(fixture.ConnectionString, provider, tenant + "-foreign"))
        {
            using var scope = foreign.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Null(await library.GetOrganizationAsync("alpha")); Assert.Null(await library.GetFolderAsync(folderId));
            Assert.Empty((await library.ListTagsAsync()).Items); Assert.Empty(await library.ListViewsAsync("alice"));
        }
        foreach (var deleteWorkflows in new[] { false, true })
            await RunDeletionAsync(fixture.ConnectionString, provider, tenant + "-delete-" + deleteWorkflows, deleteWorkflows);
    }

    private static async Task RunDeletionAsync(string connection, string provider, string tenant, bool deleteWorkflows)
    {
        string childId;
        await using (var host = Host(connection, provider, tenant))
        {
            using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
            var folder = await library.CreateFolderAsync("Demo", null, "alice");
            var child = await library.CreateFolderAsync("Demo", folder.Id, "alice"); childId = child.Id;
            db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "One", TenantId = tenant },
                new WorkflowDefinition { Id = "deleted", Name = "Deleted", TenantId = tenant });
            await db.SaveChangesAsync();
            await library.MoveDefinitionsAsync(["one", "deleted"], folder.Id, "alice", "place");
            var deleted = await db.Definitions.SingleAsync(row => row.TenantId == tenant && row.Id == "deleted");
            deleted.DeletedAt = DateTimeOffset.UnixEpoch; await db.SaveChangesAsync();
            var preview = await deletion.PreviewFolderDeletionAsync(folder.Id);
            await library.CreateFolderAsync("Grandchild", child.Id, "alice");
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => deletion.DeleteFolderAsync(folder.Id,
                preview.Revision, deleteWorkflows, "alice", "stale-delete"));
            preview = await deletion.PreviewFolderDeletionAsync(folder.Id);
            await deletion.DeleteFolderAsync(folder.Id, preview.Revision, deleteWorkflows, "alice", "delete");
        }
        await using var restart = Host(connection, provider, tenant);
        using var verify = restart.CreateScope();
        var read = verify.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var readDb = verify.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        Assert.Equal(childId, Assert.Single((await read.ListFoldersAsync(null)).Items).Id);
        Assert.Null((await read.GetOrganizationAsync("one"))!.FolderId);
        Assert.Null((await read.GetOrganizationAsync("deleted"))!.FolderId);
        Assert.Equal(deleteWorkflows, (await readDb.Definitions.SingleAsync(row => row.TenantId == tenant && row.Id == "one")).DeletedAt is not null);
        Assert.Equal(DateTimeOffset.UnixEpoch, (await readDb.Definitions.SingleAsync(row => row.TenantId == tenant && row.Id == "deleted")).DeletedAt);
    }

    /// <summary>
    /// Spec 1193 FR-021 on a native engine: the unique operation-key index admits many unkeyed rows, scopes a key by tenant
    /// (and by owner for views), and a repeated or concurrent keyed create resolves to exactly one tag or view.
    /// </summary>
    public static async Task RunOperationKeysAsync(WorkflowsDesignProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var tenant = "library-keys-" + Guid.NewGuid().ToString("N");
        string tagId; string viewId;
        await using (var host = Host(fixture.ConnectionString, provider, tenant))
        {
            using (var setup = host.CreateScope()) await setup.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.EnsureCreatedAsync();
            using var scope = host.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            await library.CreateTagAsync(new("Unkeyed one"), "alice"); await library.CreateTagAsync(new("Unkeyed two"), "alice");
            await library.SaveViewAsync(null, new("Unkeyed one", new()), "alice", false); await library.SaveViewAsync(null, new("Unkeyed two", new()), "alice", false);
            async Task<WorkflowTag> CreateAsync()
            {
                using var racer = host.CreateScope();
                return await racer.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>().CreateTagAsync(new("Keyed"), "alice", "keyed-tag");
            }
            var raced = await Task.WhenAll(CreateAsync(), CreateAsync(), CreateAsync());
            tagId = Assert.Single(raced.Select(tag => tag.Id).Distinct());
            Assert.Equal(tagId, (await library.CreateTagAsync(new("Keyed"), "alice", "keyed-tag")).Id);
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Other"), "alice", "keyed-tag"));
            Assert.Equal(3, (await library.ListTagsAsync()).Items.Count);
            var view = await library.SaveViewAsync(null, new("Keyed", new(SearchTerm: "pay")), "alice", false, "keyed-view"); viewId = view.Id;
            Assert.Equal(viewId, (await library.SaveViewAsync(null, new("Keyed", new(SearchTerm: "pay")), "alice", false, "keyed-view")).Id);
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(null, new("Other", new()), "alice", false, "keyed-view"));
            Assert.NotEqual(viewId, (await library.SaveViewAsync(null, new("Keyed", new(SearchTerm: "pay")), "bob", false, "keyed-view")).Id);
            Assert.Equal(3, (await library.ListViewsAsync("alice")).Count);
        }
        await using (var foreign = Host(fixture.ConnectionString, provider, tenant + "-foreign"))
        {
            using var scope = foreign.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Null(await library.FindTagByOperationKeyAsync("keyed-tag")); Assert.Null(await library.FindViewByOperationKeyAsync("keyed-view", "alice"));
            Assert.NotEqual(tagId, (await library.CreateTagAsync(new("Keyed"), "alice", "keyed-tag")).Id);
        }
        await using (var restart = Host(fixture.ConnectionString, provider, tenant))
        {
            using var scope = restart.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Equal(tagId, (await library.FindTagByOperationKeyAsync("keyed-tag"))!.Id);
            Assert.Equal(viewId, (await library.FindViewByOperationKeyAsync("keyed-view", "alice"))!.Id);
        }
    }

    private static ServiceProvider Host(string connection, string provider, string tenant)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, NativeProviderSerializer>();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new WitnessAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = provider, ConnectionString = connection });
        return services.BuildServiceProvider();
    }

    private sealed class WitnessAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
}
