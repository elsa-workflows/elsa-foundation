using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

// Spec 1193 FR-021 and SC-008: a keyed tag or view create resolves, by its client operation key, to exactly one resource.
public sealed partial class EfWorkflowDesignPersistenceTests
{
    // Records holding collections compare those by reference; the wire shape is the resource's identity and state.
    private static void AssertSameResource<T>(T expected, T? actual) =>
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual));

    [Fact]
    public async Task Library_keyed_tag_create_returns_the_keyed_tag_refuses_other_input_and_scopes_the_key_by_tenant()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-tag-keys");
        string tagId;
        await using (var host = LibraryHost(database.ConnectionString))
        {
            using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            // The migration chain, not the model, so the new columns and unique index are what the migration creates.
            await db.Database.MigrateAsync();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Null(await library.FindTagByOperationKeyAsync("create-critical"));
            var created = await library.CreateTagAsync(new("Critical"), "alice", "create-critical"); tagId = created.Id;
            // The response was lost: the same key and input return the one tag and write nothing.
            var auditCount = await db.LibraryAudit.CountAsync();
            var repeated = await library.CreateTagAsync(new(" Critical "), "alice", "create-critical");
            AssertSameResource(created, repeated);
            Assert.Equal(auditCount, await db.LibraryAudit.CountAsync());
            AssertSameResource(created, await library.FindTagByOperationKeyAsync("create-critical"));
            // The same key with different input is a conflict; nothing is created.
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Urgent"), "alice", "create-critical"));
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Critical", Description: "Other"), "alice", "create-critical"));
            // A different key cannot create a second tag with the same name; unkeyed and keyed creates coexist.
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Critical"), "alice", "create-critical-again"));
            Assert.Null(await library.FindTagByOperationKeyAsync("create-critical-again"));
            await library.CreateTagAsync(new("Unkeyed one"), "alice"); await library.CreateTagAsync(new("Unkeyed two"), "alice");
            var controlled = await library.CreateTagAsync(new("Region", Mode: "controlled", Values: [new("", "EU"), new("", "US")]), "alice", "create-region");
            AssertSameResource(controlled, await library.CreateTagAsync(new("Region", Mode: "controlled", Values: [new("", "EU"), new("", "US")]), "alice", "create-region"));
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() =>
                library.CreateTagAsync(new("Region", Mode: "controlled", Values: [new("", "EU")]), "alice", "create-region"));
            Assert.Equal(4, (await library.ListTagsAsync()).Items.Count);
            // Only the key's lookup hash is stored, never the client key itself.
            Assert.DoesNotContain(await db.Tags.Select(row => row.OperationKey).ToArrayAsync(), key => key == "create-critical");
            await Assert.ThrowsAsync<ArgumentException>(() => library.CreateTagAsync(new("Bad key"), "alice", new string('k', 257)));
            await Assert.ThrowsAsync<ArgumentException>(() => library.CreateTagAsync(new("Bad key"), "alice", "line\nbreak"));
        }
        await using (var other = LibraryHost(database.ConnectionString, tenant: "tenant-b"))
        {
            using var scope = other.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            // Another tenant's key space is its own: the same key reads nothing and creates that tenant's tag.
            Assert.Null(await library.FindTagByOperationKeyAsync("create-critical"));
            var foreign = await library.CreateTagAsync(new("Critical"), "bob", "create-critical");
            Assert.NotEqual(tagId, foreign.Id);
        }
        await using (var restart = LibraryHost(database.ConnectionString))
        {
            using var scope = restart.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Equal(tagId, (await library.FindTagByOperationKeyAsync("create-critical"))!.Id);
        }
    }

    [Fact]
    public async Task Library_concurrent_keyed_tag_creates_with_one_key_resolve_to_exactly_one_tag()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-tag-key-race");
        await using var host = LibraryHost(database.ConnectionString);
        using (var setup = host.CreateScope()) await setup.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.MigrateAsync();
        async Task<WorkflowTag> CreateAsync()
        {
            using var scope = host.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>().CreateTagAsync(new("Raced"), "alice", "race-key");
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync()));
        Assert.Single(results.Select(tag => tag.Id).Distinct());
        using var read = host.CreateScope();
        Assert.Equal(1, await read.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Tags.CountAsync());
    }

    [Fact]
    public async Task Library_keyed_view_create_returns_the_keyed_view_refuses_other_input_and_scopes_the_key_by_owner()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-view-keys"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.MigrateAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        WorkflowLibraryViewInput Input(string name = "Finance", bool shared = false) => new(name, new(SearchTerm: "pay", Sort: "modified"), shared, ["name", "tags"]);
        Assert.Null(await library.FindViewByOperationKeyAsync("save-finance", "alice"));
        var created = await library.SaveViewAsync(null, Input(), "alice", false, "save-finance");
        var auditCount = await db.LibraryAudit.CountAsync();
        AssertSameResource(created, await library.SaveViewAsync(null, Input(), "alice", false, "save-finance"));
        Assert.Equal(auditCount, await db.LibraryAudit.CountAsync());
        AssertSameResource(created, await library.FindViewByOperationKeyAsync("save-finance", "alice"));
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(null, Input("Other"), "alice", false, "save-finance"));
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(null, Input(shared: true), "alice", true, "save-finance"));
        // The key is scoped by owner as well as tenant: another subject neither reads alice's key nor collides with it.
        Assert.Null(await library.FindViewByOperationKeyAsync("save-finance", "bob"));
        var bobs = await library.SaveViewAsync(null, Input(), "bob", false, "save-finance");
        Assert.NotEqual(created.Id, bobs.Id);
        // Sharing still requires the share permission; an update stays bound by its revision, never by a key.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => library.SaveViewAsync(null, Input("Team", shared: true), "alice", false, "save-team"));
        Assert.Null(await library.FindViewByOperationKeyAsync("save-team", "alice"));
        await Assert.ThrowsAsync<ArgumentException>(() => library.SaveViewAsync(created.Id, Input() with { Revision = created.Revision }, "alice", false, "update-key"));
        Assert.Equal(2, (await library.ListViewsAsync("alice")).Count + (await library.ListViewsAsync("bob")).Count);
        var updated = await library.SaveViewAsync(created.Id, Input("Renamed") with { Revision = created.Revision }, "alice", false);
        // The keyed create remains the view that key made, at its current revision.
        Assert.Equal(updated.Revision, (await library.FindViewByOperationKeyAsync("save-finance", "alice"))!.Revision);
        Assert.Equal(updated.Id, (await library.SaveViewAsync(null, Input(), "alice", false, "save-finance")).Id);
    }
}
