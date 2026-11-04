using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

internal static class SourceRevisionNativeWitness
{
    private sealed class Access(string tenant) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope(tenant));
    }

    public static async Task RunAsync(WorkflowsDesignProviderFixture fixture, Func<string, WorkflowsDesignDbContext> create)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider unavailable.");
        var tenant = "source-" + Guid.NewGuid().ToString("N");
        var access = new Access(tenant);
        var scope = new SourceScope("authoring", tenant, "agent", "agent-design");
        var serializer = new NativeProviderSerializer();
        var content = new AgentSourceContent("Write a headline", new("fixture", "connection", "model"), [], [], System.Text.Json.JsonSerializer.SerializeToElement(new { }), new(1, 10000), false);
        string snapshotId;
        await using (var db = create(fixture.ConnectionString))
        {
            await db.Database.EnsureCreatedAsync();
            var agents = new EfAgentSources<WorkflowsDesignDbContext>(db, access);
            await agents.CreateAsync("writer", "draft", "Copywriter", content);
            await agents.CreateAsync("WRITER", "draft", "Other writer", content);
            Assert.Equal(2, (await agents.ListAsync("writer", 0, 1)).Total);
            Assert.Empty((await agents.ListAsync("writer", 9, 1)).Items);
            Assert.Equal(0, (await new EfAgentSources<WorkflowsDesignDbContext>(db, new Access(tenant + "-other")).ListAsync(null, 0, 10)).Total);
            Assert.Equal("identity-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => agents.CreateAsync("writer", "other-draft", "Duplicate", content))).Code);
            var snapshot = await agents.CaptureAsync("writer", "draft", 0, "review");
            snapshotId = snapshot.Id;
            Assert.Null(await new EfSourceRevisions(db, scope).FindSnapshotAsync("writer", "draft", snapshotId + " "));
            await agents.SaveAsync("writer", "draft", 0, "Copywriter", content with { Instructions = "Edited" });
            Assert.Equal(snapshotId, (await agents.CaptureAsync("writer", "draft", 0, "review")).Id);
            Assert.Single(await agents.HistoryAsync("writer", "draft"));
            Assert.Equal("revision-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => agents.CaptureAsync("writer", "draft", 0, "stale"))).Code);
            Assert.Equal("Write a headline", (await agents.HistoryAsync("writer", "draft"))[0].Content.Instructions);

            // A genuine Workflow source and the typed Agent adapter share the exact model and engine.
            db.Definitions.Add(new WorkflowDefinition { Id = "flow", TenantId = tenant, Name = "Flow" });
            db.Drafts.Add(new WorkflowDefinitionDraft { Id = "flow-draft", WorkflowDefinitionId = "flow", TenantId = tenant,
                StateSource = serializer.Serialize(new WorkflowDefinitionState([], null, [], [], null)) });
            await db.SaveChangesAsync();
            var workflows = new WorkflowSourceRevisions(db, access, serializer);
            await workflows.CaptureAsync("FLOW", "flow-draft", 0, "workflow-review");
            Assert.NotNull(await workflows.ReadAsync("FLOW", "flow-draft"));
            Assert.Single(await workflows.HistoryAsync("flow", "flow-draft"));
        }

        // Both authors read revision 1 before either commits; only the database CAS can reject the loser.
        await using (var left = create(fixture.ConnectionString))
        await using (var right = create(fixture.ConnectionString))
        {
            var key = EfSourceRevisions.CopyKey(scope, "writer", "draft");
            var a = await left.Set<SourceWorkingCopy>().SingleAsync(x => x.Key == key);
            var b = await right.Set<SourceWorkingCopy>().SingleAsync(x => x.Key == key);
            EfSourceRevisions.StageSave(left, a, scope, "writer", "draft", 1, "Copywriter", a.Profile, System.Text.Json.JsonSerializer.Serialize(content with { Instructions = "winner" }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            EfSourceRevisions.StageSave(right, b, scope, "writer", "draft", 1, "Copywriter", b.Profile, System.Text.Json.JsonSerializer.Serialize(content with { Instructions = "local loser" }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            await left.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.SaveChangesAsync());
            Assert.Contains("local loser", b.Content);
        }

        // Competing retention and collection may conflict, but may never leave a dangling reference.
        await using (var db = create(fixture.ConnectionString))
        {
            var store = new EfSourceRevisions(db, scope);
            await store.DiscardAsync("writer", "draft", 2);
            await store.UnpinAsync("writer", "draft", snapshotId, "capture:review");
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pin = Compete(true);
        var delete = Compete(false);
        gate.SetResult();
        await Task.WhenAll(pin, delete);
        Assert.True(pin.Result is null || delete.Result is null);
        await using (var check = create(fixture.ConnectionString))
        {
            var exists = await check.Set<SourceSnapshot>().AnyAsync(x => x.Key == snapshotId);
            var pinned = await check.Set<SourceSnapshotPin>().AnyAsync(x => x.SnapshotKey == snapshotId);
            Assert.False(pinned && !exists);
            if (pin.Result is null) { Assert.True(exists); Assert.True(pinned); Assert.NotNull(delete.Result); }
            if (delete.Result is null) { Assert.False(exists); Assert.False(pinned); Assert.NotNull(pin.Result); }
            var retainedCopy = await check.Set<SourceWorkingCopy>().SingleAsync(x => x.Key == EfSourceRevisions.CopyKey(scope, "writer", "draft"));
            Assert.True(retainedCopy.Discarded);
            Assert.Contains("winner", retainedCopy.Content);
        }

        async Task<SourceRevisionException?> Compete(bool retain)
        {
            await using var db = create(fixture.ConnectionString);
            var store = new EfSourceRevisions(db, scope);
            await gate.Task;
            try
            {
                if (retain) await store.PinAsync("writer", "draft", snapshotId, "publication");
                else await store.DeleteSnapshotAsync("writer", "draft", snapshotId);
                return null;
            }
            catch (SourceRevisionException e) { return e; }
        }
    }
}
